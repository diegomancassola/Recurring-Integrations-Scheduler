/* Copyright (c) Microsoft Corporation. All rights reserved.
   Licensed under the MIT License. */

using Common.Logging;
using Quartz;
using RecurringIntegrationsScheduler.Common.Contracts;
using RecurringIntegrationsScheduler.Common.Helpers;
using RecurringIntegrationsScheduler.Common.JobSettings;
using RecurringIntegrationsScheduler.Job.Properties;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

//Start - DManc - 2017/10/13
using System.Collections.Generic;
using RecurringIntegrationsScheduler.Common_M.JobSettings;
using RecurringIntegrationsScheduler.Common_M.Helpers;
using Microsoft.SqlServer.Dts.Runtime;
//End - DManc - 2017/10/13

namespace RecurringIntegrationsScheduler.Job
{
    /// <summary>
    /// Job that is used to request export of data using new mothod introduced in platform update 5
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    [DisallowConcurrentExecution]
    public class SQLExport_M : IJob
    {
        /// <summary>
        /// The log
        /// </summary>
        private static readonly ILog Log = LogManager.GetLogger(typeof(SQLExport_M));

        /// <summary>
        /// The settings
        /// </summary>
        private readonly SQLExportJobSettings_M _settings = new SQLExportJobSettings_M();

        /// <summary>
        /// The HTTP client helper
        /// </summary>
        private HttpClientHelper _httpClientHelper;

        /// <summary>
        /// Job execution context
        /// </summary>
        private IJobExecutionContext _context;

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

                Log.DebugFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_starting, _context.JobDetail.Key));

                var t = System.Threading.Tasks.Task.Run(Process);
                t.Wait();

                Log.DebugFormat(CultureInfo.InvariantCulture, string.Format(Resources.Job_0_ended, _context.JobDetail.Key));
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
                    throw new JobExecutionException(string.Format(Resources.Download_job_0_failed, _context.JobDetail.Key), ex, false);
            }
        }

        /// <summary>
        /// Processes this instance.
        /// </summary>
        /// <returns></returns>
        private async System.Threading.Tasks.Task Process()
        {
            using (_httpClientHelper = new HttpClientHelper(_settings))
            {
                var executionId = CreateExecutionId(_settings.DataProject);
                var responseExportToPackage = await _httpClientHelper.ExportToPackage(_settings.DataProject, executionId, executionId, _settings.Company);

                if (!responseExportToPackage.IsSuccessStatusCode)
                    throw new Exception(string.Format(Resources.Job_0_Download_failure_1, _context.JobDetail.Key, responseExportToPackage.StatusCode));

                var executionStatus = "";
                const int RETRIES = 10;
                var i = 0;
                do
                {
                    executionStatus = await _httpClientHelper.GetExecutionSummaryStatus(executionId);
                    if (executionStatus == "NotRun" || executionStatus == "Executing")
                    {
                        System.Threading.Thread.Sleep(_settings.Interval);
                    }
                    i++;
                    Log.Debug(string.Format(Resources.Job_0_Checking_if_export_is_completed_Try_1, _context.JobDetail.Key, i));
                }
                while ((executionStatus == "NotRun" || executionStatus == "Executing") && i <= RETRIES);

                if (executionStatus == "Succeeded" || executionStatus == "PartiallySucceeded")
                {
                    Uri packageUrl = await _httpClientHelper.GetExportedPackageUrl(executionId);

                    var response = await _httpClientHelper.GetRequestAsync(new UriBuilder(packageUrl).Uri, false);
                    if (!response.IsSuccessStatusCode)
                        throw new Exception(string.Format(Resources.Job_0_Download_failure_1, _context.JobDetail.Key, string.Format($"Status: {response.StatusCode}. Message: {response.Content}")));

                    using (Stream downloadedStream = await response.Content.ReadAsStreamAsync())
                    {
                        Guid downloadGuid = Guid.NewGuid();
                        var fileName = downloadGuid.ToString() + ".zip";
                        var successPath = Path.Combine(_settings.DownloadSuccessDir, fileName);
                        var dataMessage = new DataMessage()
                        {
                            FullPath = successPath,
                            Name = fileName,
                            MessageStatus = MessageStatus.Succeeded
                        };
                        FileOperationsHelper.Create(downloadedStream, dataMessage.FullPath);

                        if (_settings.UnzipPackage)
                        {
                            List<string> inputList = FileOperationsHelper_M.UnzipPackageToList(dataMessage.FullPath, _settings.DeletePackage, _settings.AddTimestamp);

                            //Only one file per package
                            if (inputList.Count > 1)
                                throw new Exception(Resources.Composite_entity_is_not_supported_on_this_type_of_package);

                            //Start - DManc - 2017/10/13
                            //Execute package for each file downloaded
                            foreach (string inputFullFileName in inputList)
                            {
                                FileInfo inputFile = new FileInfo(inputFullFileName);
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
                                    pkg.Parameters[_settings.SSISInputFilePathParmName].Value = inputFile.FullName;
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

                                        //Delete input file
                                        inputFile.Delete();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex.Message);
                                    throw new Exception(ex.ToString());
                                }
                            }
                            //End - DManc - 2017/10/13
                        }
                    }
                }
                else if (executionStatus == "Unknown" || executionStatus == "Failed" || executionStatus == "Canceled")
                {
                    throw new Exception(string.Format(Resources.Export_execution_failed_for_job_0_Status_1, _context.JobDetail.Key, executionStatus));
                }
                else
                {
                    Log.Error(string.Format(Resources.Job_0_Execution_status_1_Execution_Id_2, _context.JobDetail.Key, executionStatus, executionId));
                }
            }
        }

        private string CreateExecutionId(string dataProject)
        {
            return $"{dataProject}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{Guid.NewGuid().ToString()}";
        }
    }
}