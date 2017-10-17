using Quartz;
using RecurringIntegrationsScheduler.Common.Contracts;
using RecurringIntegrationsScheduler.Common.Properties;
using System;
using System.Globalization;
using System.IO;

namespace RecurringIntegrationsScheduler.Common.JobSettings
{
    /// <summary>
    /// Serialize/deserialize DMImport job settings
    /// </summary>
    /// <seealso cref="RecurringIntegrationsScheduler.Common.Configuration.Settings" />
    public class SQLImportJobSettings_M : ImportJobSettings
    {
        /// <summary>
        /// Initialize and verify settings for job
        /// </summary>
        /// <param name="context"></param>
        /// <exception cref="Quartz.JobExecutionException">
        /// </exception>
        public override void Initialize(IJobExecutionContext context)
        {
            var dataMap = context.JobDetail.JobDataMap;

            base.Initialize(context);

            //Start - DManc - 2017/10/13
            SSISPackage = dataMap.GetString(SettingsConstants_M.SSISPackage);
            if (string.IsNullOrEmpty(SSISPackage))
            {
                throw new JobExecutionException(string.Format(CultureInfo.InvariantCulture,
                    string.Format(Resources_M.Missing_SSIS_package,
                        context.JobDetail.Key)));
            }
            else
            {
                try
                {
                    if (!File.Exists(SSISPackage))
                    {
                        throw new JobExecutionException(
                            string.Format(CultureInfo.InvariantCulture,
                                string.Format(Resources_M.SSIS_package_not_found, PackageTemplate,
                                    context.JobDetail.Key)));
                    }
                }
                catch (Exception ex)
                {
                    throw new JobExecutionException(
                        string.Format(CultureInfo.InvariantCulture,
                            string.Format(Resources_M.Verification_of_SSIS_package_location_failed_0, PackageTemplate,
                                context.JobDetail.Key)), ex);
                }
            }

            TempDir = dataMap.GetString(SettingsConstants_M.TempDir);
            if (!string.IsNullOrEmpty(TempDir))
            {
                try
                {
                    Directory.CreateDirectory(TempDir);
                }
                catch (Exception ex)
                {
                    throw new JobExecutionException(
                        string.Format(CultureInfo.InvariantCulture,
                            string.Format(Resources_M.Temp_directory_does_not_exist_or_cannot_be_accessed,
                                context.JobDetail.Key)), ex);
                }
            }
            else
            {
                throw new JobExecutionException(string.Format(CultureInfo.InvariantCulture,
                    string.Format(Resources_M.Temp_directory_is_missing_in_job_configuration,
                        context.JobDetail.Key)));
            }
            //End - DManc - 2017/10/13 
        }

        #region Members

        //Start - DManc - 2017/10/13
        /// <summary>
        /// SSIS Package Name
        /// </summary>
        /// <value>
        /// SSIS Package Name
        /// </value>
        public string SSISPackage { get; private set; }
        //End - DManc - 2017/10/13 

        //Start - DManc - 2017/10/13
        /// <summary>
        /// Temporary directory for SSIS output
        /// </summary>
        /// <value>
        /// Temporary directory for SSIS output
        /// </value>
        public string TempDir { get; private set; }
        //End - DManc - 2017/10/13 

        #endregion
    }
}