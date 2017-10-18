/* Copyright (c) Microsoft Corporation. All rights reserved.
   Licensed under the MIT License. */

using RecurringIntegrationsScheduler.Common.Contracts;
using RecurringIntegrationsScheduler.Common.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace RecurringIntegrationsScheduler.Common_M.Helpers
{
    public static class FileOperationsHelper_M
    {
        /// <summary>
        /// Extracts content of data package zip archive
        /// </summary>
        /// <param name="filePath">File path of data package</param>
        /// <param name="deletePackage">Flag whether to delete zip file</param>
        /// <param name="addTimestamp">Flag whether to add timestamp to extracted file name</param>
        /// <returns>Boolean with operation result</returns>
        public static List<string> UnzipPackageToList(string filePath, bool deletePackage, bool addTimestamp = false)
        {
            List<string> outputList = new List<string>();
            if (File.Exists(filePath))
            {
                using (var zip = ZipFile.OpenRead(filePath))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if ((entry.Length == 0) || (entry.FullName == "Manifest.xml") ||
                            (entry.FullName == "PackageHeader.xml"))
                            continue;

                        string fileName;

                        if (addTimestamp)
                            fileName =
                                Path.Combine(Path.GetDirectoryName(filePath),
                                    Path.GetFileNameWithoutExtension(filePath)) + "-" + entry.FullName;
                        else
                            fileName = Path.Combine(Path.GetDirectoryName(filePath), entry.FullName);

                        entry.ExtractToFile(fileName, !addTimestamp);
                        outputList.Add(fileName);
                    }
                }
                if (deletePackage)
                    File.Delete(filePath);
            }
            return outputList;
        }
    }
}