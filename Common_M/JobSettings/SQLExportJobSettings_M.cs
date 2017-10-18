/* Copyright (c) Microsoft Corporation. All rights reserved.
   Licensed under the MIT License. */

using Quartz;
using RecurringIntegrationsScheduler.Common.Contracts;
using System;
using System.Globalization;
using System.IO;

using RecurringIntegrationsScheduler.Common.JobSettings;
using Common_M.Properties;

namespace RecurringIntegrationsScheduler.Common_M.JobSettings
{
    /// <summary>
    /// Serialize/deserialize download job settings
    /// </summary>
    /// <seealso cref="RecurringIntegrationsScheduler.Common.Configuration.Settings" />
    public class SQLExportJobSettings_M : ExportJobSettings
    {
        /// <summary>
        /// Initialize and verify settings for job
        /// </summary>
        /// <param name="context">The context.</param>
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
                    string.Format(Resources.Missing_SSIS_package,
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
                                string.Format(Resources.SSIS_package_not_found, SSISPackage,
                                    context.JobDetail.Key)));
                    }
                }
                catch (Exception ex)
                {
                    throw new JobExecutionException(
                        string.Format(CultureInfo.InvariantCulture,
                            string.Format(Resources.Verification_of_SSIS_package_location_failed_0, SSISPackage,
                                context.JobDetail.Key)), ex);
                }
            }

            SSISInputFilePathParmName = dataMap.GetString(SettingsConstants_M.SSISInputFilePathParmName);
            if (string.IsNullOrEmpty(SSISInputFilePathParmName))
                throw new JobExecutionException(string.Format(CultureInfo.InvariantCulture,
                    string.Format(Resources.Name_of_input_file_path_parameter_missing,
                        context.JobDetail.Key))); 

            //End - DManc - 2017/10/13 
        }

        #region Members

        #endregion

        //Start - DManc - 2017/10/13
        /// <summary>
        /// SSIS Package Name
        /// </summary>
        /// <value>
        /// SSIS Package Name
        /// </value>
        public string SSISPackage { get; private set; }

        /// <summary>
        /// SSIS Package name of output path parameter
        /// </summary>
        /// <value>
        /// SSIS Package name of output path parameter
        /// </value>
        public string SSISInputFilePathParmName { get; private set; }
        //End - DManc - 2017/10/13 
    }
}