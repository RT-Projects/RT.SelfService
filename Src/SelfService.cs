using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceProcess;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

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
            // LOG: InstallingService

            if (Services.Count == 0)
                throw new InvalidOperationException("There are no services defined.");
            int serviceType = Services.Count == 1 ? 0x10 : 0x20;

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
            if ((binaryPath == null) || (binaryPath.Length == 0))
                throw new InvalidOperationException("FILE NAME");

            binaryPath = "\"" + binaryPath + "\"";

            foreach (var service in Services)
            {
                if (!ValidateServiceName(service.ServiceName))
                    throw new InvalidOperationException("SERVICE NAME BAD");
                if (service.ServiceDisplayName.Length > 0xFF)
                    throw new ArgumentException("DisplayNameTooLong");
                string dependencies = null;
                if (service.ServicesDependedOn.Count > 0)
                {
                    StringBuilder builder = new StringBuilder();
                    for (int k = 0; k < service.ServicesDependedOn.Count; k++)
                    {
                        string name = service.ServicesDependedOn[k];
                        try
                        {
                            ServiceController controller = new ServiceController(name, ".");
                            name = controller.ServiceName;
                        }
                        catch
                        {
                        }
                        builder.Append(name);
                        builder.Append('\0');
                    }
                    builder.Append('\0');
                    dependencies = builder.ToString();
                }
                IntPtr databaseHandle = SafeNativeMethods.OpenSCManager(null, null, 0xF003F);
                IntPtr zero = IntPtr.Zero;
                if (databaseHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("OpenSC", new Win32Exception());
                }
                try
                {
                    zero = NativeMethods.CreateService(databaseHandle, service.ServiceName, service.ServiceDisplayName, 0xF01FF, serviceType, (int) service.ServiceStartMode, 1, binaryPath, null, IntPtr.Zero, dependencies, user, password);
                    if (zero == IntPtr.Zero)
                    {
                        throw new Win32Exception();
                    }
                    if (service.ServiceDescription.Length != 0)
                    {
                        NativeMethods.SERVICE_DESCRIPTION serviceDesc = new NativeMethods.SERVICE_DESCRIPTION();
                        serviceDesc.description = Marshal.StringToHGlobalUni(service.ServiceDescription);
                        bool flag = NativeMethods.ChangeServiceConfig2(zero, 1, ref serviceDesc);
                        Marshal.FreeHGlobal(serviceDesc.description);
                        if (!flag)
                        {
                            throw new Win32Exception();
                        }
                    }
                }
                finally
                {
                    if (zero != IntPtr.Zero)
                    {
                        SafeNativeMethods.CloseServiceHandle(zero);
                    }
                    SafeNativeMethods.CloseServiceHandle(databaseHandle);
                }
                // LOG: InstallOK
            }
        }

        private static bool ValidateServiceName(string name)
        {
            if (((name == null) || (name.Length == 0)) || (name.Length > 80))
            {
                return false;
            }
            char[] chArray = name.ToCharArray();
            for (int i = 0; i < chArray.Length; i++)
            {
                if (((chArray[i] < ' ') || (chArray[i] == '/')) || (chArray[i] == '\\'))
                {
                    return false;
                }
            }
            return true;
        }

        public void Uninstall()
        {
            foreach (var service in Services)
            {
                // LOG: ServiceRemoving
                IntPtr databaseHandle = SafeNativeMethods.OpenSCManager(null, null, 0xf003f);
                if (databaseHandle == IntPtr.Zero)
                {
                    throw new Win32Exception();
                }
                IntPtr zero = IntPtr.Zero;
                try
                {
                    zero = NativeMethods.OpenService(databaseHandle, service.ServiceName, 0x10000);
                    if (zero == IntPtr.Zero)
                        throw new Win32Exception();
                    NativeMethods.DeleteService(zero);
                }
                finally
                {
                    if (zero != IntPtr.Zero)
                        SafeNativeMethods.CloseServiceHandle(zero);
                    SafeNativeMethods.CloseServiceHandle(databaseHandle);
                }
                // LOG: ServiceRemoved
                try
                {
                    using (ServiceController controller = new ServiceController(service.ServiceName))
                    {
                        if (controller.Status != ServiceControllerStatus.Stopped)
                        {
                            // LOG: TryToStop
                            controller.Stop();
                            int num = 10;
                            controller.Refresh();
                            while ((controller.Status != ServiceControllerStatus.Stopped) && (num > 0))
                            {
                                Thread.Sleep(1000);
                                controller.Refresh();
                                num--;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            Thread.Sleep(5000);
        }
    }
}
