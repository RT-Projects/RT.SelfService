using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;

// TODO:
// - start/stop/pause
// - specify exe args
// - comments

namespace RT.SelfService
{
    public abstract class SelfService : ServiceBase
    {
        public string ServiceDisplayName { get; set; }
        public string ServiceDescription { get; set; }
        public ServiceStartMode ServiceStartMode { get; set; }
        public IList<string> ServicesDependedOn { get; set; }
    }

    public abstract class SelfServiceProcess
    {
        public ServiceAccount Account { get; set; }
        public IList<SelfService> Services { get; set; }

        public void ExecuteServices()
        {
            ServiceBase.Run(Services.ToArray());
        }

        public void Install()
        {
            if (Services.Count == 0)
                throw new InvalidOperationException("There are no services defined.");

            string user = null;
            string password = null; // currently will always stay at zero, but is here in case support for ServiceAccount.User is implemented.
            switch (Account)
            {
                case ServiceAccount.LocalService:
                    user = @"NT AUTHORITY\LocalService";
                    break;

                case ServiceAccount.NetworkService:
                    user = @"NT AUTHORITY\NetworkService";
                    break;

                case ServiceAccount.User:
                    throw new NotSupportedException("SelfService does not currently support installing as a user.");
            }
            string binaryPath = Assembly.GetEntryAssembly().Location;
            if (binaryPath == null || binaryPath.Length == 0)
                throw new InvalidOperationException("Could not retrieve entry assembly file name.");
            binaryPath = "\"" + binaryPath + "\"";

            IntPtr databaseHandle = ServiceUtil.OpenServiceDatabase();
            try
            {
                foreach (var service in Services)
                    ServiceUtil.InstallService(databaseHandle, Services.Count, service.ServiceName, service.ServiceDisplayName, service.ServiceDescription, service.ServiceStartMode, service.ServicesDependedOn, binaryPath, user, password);
            }
            finally
            {
                ServiceUtil.CloseServiceDatabase(databaseHandle);
            }
        }

        public void Uninstall()
        {
            var databaseHandle = ServiceUtil.OpenServiceDatabase();
            try
            {
                foreach (var service in Services)
                {
                    ServiceUtil.DeleteService(databaseHandle, service.ServiceName);
                    ServiceUtil.StopService(service.ServiceName); // Not sure why this comes after the deletion, but .NET ServiceInstaller does this, so perhaps there's a good reason
                }
            }
            finally
            {
                ServiceUtil.CloseServiceDatabase(databaseHandle);
            }
            Thread.Sleep(5000);
        }
    }
}
