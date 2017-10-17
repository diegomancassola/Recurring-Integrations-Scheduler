using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.SqlServer.Dts.Runtime;

namespace Job.SQL.Import.SSIS.Templates.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            string error = "";
            string pkSSIS = @"C:\Integrations\Projects\Recurring-Integrations-Scheduler\Job.SQL.Import.SSIS.Templates\ODBCQueryToEntity.dtsx";
            Console.WriteLine("The package is executing...");
            Package pkg = null;
            Microsoft.SqlServer.Dts.Runtime.Application app;
            DTSExecResult result;
            try
            {
                app = new Microsoft.SqlServer.Dts.Runtime.Application();
                pkg = app.LoadPackage(pkSSIS, null);
                pkg.Parameters["OUTPUTPATH"].Value = @"C:\Integrations\Outbound\Temp";
                result = pkg.Execute();
                if (result == Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure)
                {
                    foreach (Microsoft.SqlServer.Dts.Runtime.DtsError dt_error in pkg.Errors)
                    {
                        error += dt_error.Description.ToString();
                    }
                    Console.WriteLine(error);
                }
                if (result == Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success)
                {
                    Console.WriteLine("The package executed successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            Console.ReadKey();
        }
    }
}
