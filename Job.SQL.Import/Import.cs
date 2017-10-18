/* Copyright (c) Microsoft Corporation. All rights reserved.
   Licensed under the MIT License. */

using Common.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Quartz;
using RecurringIntegrationsScheduler.Common.Contracts;
using RecurringIntegrationsScheduler.Common.Helpers;
using RecurringIntegrationsScheduler.Common.JobSettings;
using RecurringIntegrationsScheduler.Job.Properties;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Xml;

//Start - DManc - 2017/10/13
using RecurringIntegrationsScheduler.Common_M.JobSettings;
using Microsoft.SqlServer.Dts.Runtime;
//End - DManc - 2017/10/13

namespace RecurringIntegrationsScheduler.Job
{
    /// <summary>
    /// Job that uploads data packages using new method introduced in platform update 5
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    [DisallowConcurrentExecution]
    public class SQLImport_M : IJob
    {
        /// <summary>
        /// The log
        /// </summary>
        private static readonly ILog Log = LogManager.GetLogger(typeof(SQLImport_M));

        /// <summary>
        /// The settings
        /// </summary>
        private readonly SQLImportJobSettings_M _settings = new SQLImportJobSettings_M();

        /// <summary>
        /// The HTTP client helper
        /// </summary>
        private HttpClientHelper _httpClientHelper;

        /// <summary>
        /// Job execution context
        /// </summary>
        private IJobExecutionContext _context;
        
        /// <summary>
        /// Gets or sets the input queue.
        /// </summary>
        /// <value>
        /// The input queue.
        /// </value>
        private ConcurrentQueue<DataMessage> InputQueue { get; set; }

        /// <summary>
        /// Called by the <see cref="T:Quartz.IScheduler" /> when a <see cref="T:Quartz.ITrigger" />
        /// fires that is associated with the <see cref="T:Quartz.IJob" />.
        /// </summary>
        /// <param name="context">The execution context.</param>
        /// <exception cref="Quartz.JobExecutionException">false</exception>
        /// <remarks>
        /// The implementation may wish to set a  result object on the
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to
        /// <see cref="T:Quartz.IJobListener" />s or
        /// <see cref="T:Quartz.ITriggerListener" />s that are watching the job's
        /// execution.
        /// </remarks>
        public void Execute(IJobExecutionContext context)
        {
            try
            {
                _context = context;
                _settings.Initialize(context);

                Log.DebugFormat(CultureInfo.InvariantCulture,
                    string.Format(Resources.Job_0_starting, _context.JobDetail.Key));

                var t = System.Threading.Tasks.Task.Run(this.Process);
                t.Wait();

                Log.DebugFormat(CultureInfo.InvariantCulture,
                    string.Format(Resources.Job_0_ended, _context.JobDetail.Key));
            }
            catch (Exception ex)
            {
                //Pause this job
                context.Scheduler.PauseJob(context.JobDetail.Key);
                Log.WarnFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_was_paused_because_of_error, _context.JobDetail.Key));

                if (!string.IsNullOrEmpty(ex.Message))
                    Log.Error(ex.Message, ex);

                while (ex.InnerException != null)
                {
                    if (!string.IsNullOrEmpty(ex.InnerException.Message))
                        Log.Error(ex.InnerException.Message, ex.InnerException);

                    ex = ex.InnerException;
                }
                if (context.Scheduler.SchedulerName != "Private")
                    throw new JobExecutionException(
                        string.Format(Resources.Import_job_0_failed, _context.JobDetail.Key), ex, false);
            }
        }

        /// <summary>
        /// Processes this instance.
        /// </summary>
        /// <returns></returns>
        private async System.Threading.Tasks.Task Process()
        {
            Guid tempGuid = Guid.NewGuid();
            InputQueue = new ConcurrentQueue<DataMessage>();

            //Start - DManc - 2017/10/13
            //Create temporary folder
            DirectoryInfo tempDir = new DirectoryInfo(_settings.TempDir + "\\" + tempGuid.ToString());
            tempDir.Create();

            //Execute SSIS package
            string ssisExecutionError = "";
            string ssisPackageName = _settings.SSISPackage;
            Log.Info(string.Format(Resources.The_SSIS_package_0_is_executing, ssisPackageName));
            Package pkg = null;
            Microsoft.SqlServer.Dts.Runtime.Application app;
            DTSExecResult result;
            try
            {
                app = new Microsoft.SqlServer.Dts.Runtime.Application();
                pkg = app.LoadPackage(ssisPackageName, null);
                pkg.Parameters[_settings.SSISOutputPathParmName].Value = tempDir.FullName;
                result = pkg.Execute();
                if (result == Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure)
                {
                    foreach (Microsoft.SqlServer.Dts.Runtime.DtsError dt_error in pkg.Errors)
                        ssisExecutionError += dt_error.Description.ToString();
                    throw new Exception(ssisExecutionError);
                }
                else if (result == Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success)
                {
                    Log.Info(string.Format(Resources.The_SSIS_package_0_executed_successfully, ssisPackageName));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                throw new Exception(ex.ToString());
            }

            if (tempDir.GetFiles().Length > 0)
            {
                //Create ZIP package
                FileInfo packageTemplate = new FileInfo(_settings.PackageTemplate);
                FileInfo zipPackage = new FileInfo(tempDir.FullName + "\\" + tempGuid.ToString() + ".zip");
                FileStream zipToOpen = null;
                using (zipToOpen = new FileStream(packageTemplate.FullName, FileMode.Open))
                {
                    ZipArchive archive = null;

                    FileOperationsHelper.Create(zipToOpen, zipPackage.FullName);
                    var tempZipStream = FileOperationsHelper.Read(zipPackage.FullName);
                    using (archive = new ZipArchive(tempZipStream, ZipArchiveMode.Update))
                    {
                        foreach (FileInfo entityFile in tempDir.GetFiles())
                        {
                            if (entityFile.Name == zipPackage.Name)
                                continue;

                            var sourceStream = FileOperationsHelper.Read(entityFile.FullName);
                            if (sourceStream == null)
                                continue;

                            var importedFile = archive.CreateEntry(entityFile.Name, CompressionLevel.Fastest);
                            using (var entryStream = importedFile.Open())
                            {
                                sourceStream.CopyTo(entryStream);
                                sourceStream.Close();
                                sourceStream.Dispose();
                            }
                        }
                    }
                }

                //Move to input directory
                zipPackage.CopyTo(_settings.InputDir + "\\" + zipPackage.Name);
            }

            //Delete temporary directory
            tempDir.Delete(true);

            //End - DManc - 2017/10/13

            foreach (
                var dataMessage in FileOperationsHelper.GetFiles(MessageStatus.Input, _settings.InputDir, _settings.SearchPattern, SearchOption.AllDirectories, _settings.OrderBy, _settings.ReverseOrder))
            {
                Log.DebugFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_File_1_found_in_input_location, _context.JobDetail.Key, dataMessage.FullPath.Replace(@"{", @"{{").Replace(@"}", @"}}")));
                InputQueue.Enqueue(dataMessage);
            }

            if (!InputQueue.IsEmpty)
            {
                Log.InfoFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_Found_1_file_s_in_input_folder, _context.JobDetail.Key, InputQueue.Count));
                await ProcessInputQueue();
            }
        }

        /// <summary>
        /// Processes input queue
        /// </summary>
        /// <returns>
        /// Task object for continuation
        /// </returns>
        ///  //Start - DManc - 2017/10/13  
        //private async Task ProcessInputQueue()
        protected async System.Threading.Tasks.Task ProcessInputQueue()
        //End - DManc - 2017/10/13
        {
            using (_httpClientHelper = new HttpClientHelper(_settings))
            {
                var firstFile = true;
                //string fileNameInPackage = "";
                FileStream zipToOpen = null;
                ZipArchive archive = null;

                //if (!String.IsNullOrEmpty(_settings.PackageTemplate))
                //{
                //    fileNameInPackage = GetFileNameInPackage();
                //}

                while (InputQueue.TryDequeue(out DataMessage dataMessage))
                {
                    try
                    {
                        //string tempFileName = "";
                        if (!firstFile)
                        {
                            System.Threading.Thread.Sleep(_settings.Interval);
                        }
                        else
                        {
                            firstFile = false;
                        }
                        var sourceStream = FileOperationsHelper.Read(dataMessage.FullPath);
                        if (sourceStream == null) continue;//Nothing to do here

                        //If we need to "wrap" file in package envelope
                        //if (!String.IsNullOrEmpty(_settings.PackageTemplate))
                        //{
                        //    using (zipToOpen = new FileStream(_settings.PackageTemplate, FileMode.Open))
                        //    {
                        //        tempFileName = Path.GetTempFileName();
                        //        FileOperationsHelper.Create(zipToOpen, tempFileName);
                        //        var tempZipStream = FileOperationsHelper.Read(tempFileName);
                        //        using (archive = new ZipArchive(tempZipStream, ZipArchiveMode.Update))
                        //        {
                        //            var importedFile = archive.CreateEntry(fileNameInPackage, CompressionLevel.Fastest);
                        //            using (var entryStream = importedFile.Open())
                        //            {
                        //                sourceStream.CopyTo(entryStream);
                        //                sourceStream.Close();
                        //                sourceStream.Dispose();
                        //            }
                        //        }
                        //        sourceStream = FileOperationsHelper.Read(tempFileName);
                        //    }
                        //}

                        Log.DebugFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_Uploading_file_1_File_size_2_bytes, _context.JobDetail.Key, dataMessage.FullPath.Replace(@"{", @"{{").Replace(@"}", @"}}"), sourceStream.Length));

                        // Get blob url and id. Returns in json format
                        var response = await _httpClientHelper.GetAzureWriteUrl();

                        var blobInfo = (JObject)JsonConvert.DeserializeObject(response);
                        var blobId = blobInfo["BlobId"].ToString();
                        var blobUrl = blobInfo["BlobUrl"].ToString();

                        var blobUri = new Uri(blobUrl);

                        //Upload package to blob storage
                        var uploadResponse = await _httpClientHelper.UploadContentsToBlob(blobUri, sourceStream);
                        if (sourceStream != null)
                        {
                            sourceStream.Close();
                            sourceStream.Dispose();
                            //if (!String.IsNullOrEmpty(_settings.PackageTemplate))
                            //{
                            //    FileOperationsHelper.Delete(tempFileName);
                            //}
                        }
                        if (uploadResponse.IsSuccessStatusCode)
                        {
                            //Now send import request
                            var importResponse = await _httpClientHelper.ImportFromPackage(blobUri.AbsoluteUri, _settings.DataProject, CreateExecutionId(_settings.DataProject), _settings.ExecuteImport, _settings.OverwriteDataProject, _settings.Company);

                            if (importResponse.IsSuccessStatusCode)
                            {
                                var result = importResponse.Content.ReadAsStringAsync().Result;
                                var jsonResponse = (JObject)JsonConvert.DeserializeObject(result);
                                string executionId = jsonResponse["value"].ToString();

                                var targetDataMessage = new DataMessage(dataMessage)
                                {
                                    MessageId = executionId,
                                    FullPath = dataMessage.FullPath.Replace(_settings.InputDir, _settings.UploadSuccessDir),
                                    MessageStatus = MessageStatus.Enqueued
                                };

                                // Move to inprocess/success location
                                FileOperationsHelper.Move(dataMessage.FullPath, targetDataMessage.FullPath);

                                if (_settings.ExecutionJobPresent)
                                    FileOperationsHelper.WriteStatusFile(targetDataMessage, _settings.StatusFileExtension);

                                Log.DebugFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_File_1_uploaded_successfully, _context.JobDetail.Key, dataMessage.FullPath.Replace(@"{", @"{{").Replace(@"}", @"}}")));
                            }
                            else
                            {
                                // import request failed. Move message to error location.
                                Log.ErrorFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_Upload_failed_for_file_1_Failure_response_Status_2_Reason_3, _context.JobDetail.Key, dataMessage.FullPath.Replace(@"{", @"{{").Replace(@"}", @"}}"), importResponse.StatusCode, importResponse.ReasonPhrase));

                                var targetDataMessage = new DataMessage(dataMessage)
                                {
                                    FullPath = dataMessage.FullPath.Replace(_settings.InputDir, _settings.UploadErrorsDir),
                                    MessageStatus = MessageStatus.Failed
                                };

                                // Move data to error location
                                FileOperationsHelper.Move(dataMessage.FullPath, targetDataMessage.FullPath);

                                // Save the log with import failure details
                                FileOperationsHelper.WriteStatusLogFile(targetDataMessage, importResponse, _settings.StatusFileExtension);
                            }
                        }
                        else
                        {
                            // upload failed. Move message to error location.
                            Log.ErrorFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_Upload_failed_for_file_1_Failure_response_Status_2_Reason_3, _context.JobDetail.Key, dataMessage.FullPath.Replace(@"{", @"{{").Replace(@"}", @"}}"), uploadResponse.StatusCode, uploadResponse.ReasonPhrase));

                            var targetDataMessage = new DataMessage(dataMessage)
                            {
                                FullPath = dataMessage.FullPath.Replace(_settings.InputDir, _settings.UploadErrorsDir),
                                MessageStatus = MessageStatus.Failed
                            };

                            // Move data to error location
                            FileOperationsHelper.Move(dataMessage.FullPath, targetDataMessage.FullPath);

                            // Save the log with import failure details
                            FileOperationsHelper.WriteStatusLogFile(targetDataMessage, uploadResponse, _settings.StatusFileExtension);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_Failure_processing_file_1_Exception_2, _context.JobDetail.Key, dataMessage.FullPath.Replace(@"{", @"{{").Replace(@"}", @"}}"), ex.Message), ex);
                        throw;
                    }
                    finally
                    {
                        if (zipToOpen != null)
                        {
                            zipToOpen.Close();
                            zipToOpen.Dispose();
                        }
                        if (archive != null)
                        {
                            archive.Dispose();
                        }
                    }
                }
            }
        }

        private string CreateExecutionId(string dataProject)
        {
            return $"{dataProject}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{Guid.NewGuid().ToString()}";
        }
    }
}