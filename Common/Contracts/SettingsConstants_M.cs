/* Copyright (c) Microsoft Corporation. All rights reserved.
   Licensed under the MIT License. */

namespace RecurringIntegrationsScheduler.Common.Contracts
{
    /// <summary>
    /// Constants strings used in job map object
    /// </summary>
    public static class SettingsConstants_M
    {
        #region Common settings

        //Start - DManc - 2017/10/13
        /// <summary>
        /// The SQL import job
        /// </summary>
        public const string SQLImportJob_M = "RecurringIntegrationsScheduler.Job.SQLImport_M";

        /// <summary>
        /// The SQL export job
        /// </summary>
        public const string SQLExportJob_M = "RecurringIntegrationsScheduler.Job.SQLExport_M";
        //End - DManc - 2017/10/13

        #endregion

        //Start - DManc - 2017/10/13
        #region SQL Import Job settings
        
        /// <summary>
        /// Package template
        /// </summary>
        public const string SQLStatement_M = "SQLStatement";

        #endregion
        //End - DManc - 2017/10/13
    }
}