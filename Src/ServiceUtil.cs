using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;

namespace RT.SelfService
{
    /// <summary>
    /// Contains some utility methods for managing windows services.
    /// </summary>
    static class ServiceUtil
    {
        /// <summary>
        /// Validates the specified string as being acceptable for a service name. Returns true if this is the case, false otherwise.
        /// </summary>
        public static bool ValidateServiceName(string name)
        {
            if (name == null || name.Length == 0 || name.Length > 80)
                return false;

            char[] chArray = name.ToCharArray();
            for (int i = 0; i < chArray.Length; i++)
                if (chArray[i] < ' ' || chArray[i] == '/' || chArray[i] == '\\')
                    return false;

            return true;
        }

        /// <summary>
        /// Opens a handle to the service manager. This handle is required by some other service utility methods.
        /// The handle must always be closed after use using <see cref=CloseServiceDatabase"/>.
        /// </summary>
        public static IntPtr OpenServiceDatabase()
        {
            var handle = SafeNativeMethods.OpenSCManager(null, null, 0xF003F);
            if (handle == IntPtr.Zero)
                throw new Win32Exception();
            return handle;
        }

        public static void CloseServiceDatabase(IntPtr databaseHandle)
        {
            SafeNativeMethods.CloseServiceHandle(databaseHandle); // this function closes both service and database handles...
        }

        public static void DeleteService(string serviceName)
        {
            var databaseHandle = ServiceUtil.OpenServiceDatabase();
            try { ServiceUtil.DeleteService(databaseHandle, serviceName); }
            finally { SafeNativeMethods.CloseServiceHandle(databaseHandle); }

            // Not sure why this comes after the deletion, but .NET ServiceInstaller does this, so perhaps there's a good reason
            ServiceUtil.StopService(serviceName);
        }

        public static void DeleteService(IntPtr databaseHandle, string serviceName)
        {
            IntPtr serviceHandle = IntPtr.Zero;
            try
            {
                serviceHandle = NativeMethods.OpenService(databaseHandle, serviceName, 0x10000);
                if (serviceHandle == IntPtr.Zero)
                    throw new Win32Exception();
                NativeMethods.DeleteService(serviceHandle);
            }
            finally
            {
                if (serviceHandle != IntPtr.Zero)
                    SafeNativeMethods.CloseServiceHandle(serviceHandle);
            }
        }

        public static bool StartService(string serviceName)
        {
            try
            {
                using (var controller = new ServiceController(serviceName))
                {
                    if (controller.Status != ServiceControllerStatus.StartPending && controller.Status != ServiceControllerStatus.Running)
                    {
                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    }
                    controller.Refresh();
                    return controller.Status == ServiceControllerStatus.Running;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool StopService(string serviceName)
        {
            try
            {
                using (var controller = new ServiceController(serviceName))
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                    controller.Refresh();
                    return controller.Status == ServiceControllerStatus.Stopped;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string MakeDependenciesString(IList<string> servicesDependedOn)
        {
            if (servicesDependedOn.Count == 0)
                return null;

            var result = new StringBuilder();
            for (int k = 0; k < servicesDependedOn.Count; k++)
            {
                string name = servicesDependedOn[k];
                try
                {
                    ServiceController controller = new ServiceController(name, ".");
                    name = controller.ServiceName;
                }
                catch { }
                result.Append(name);
                result.Append('\0');
            }
            result.Append('\0');
            return result.ToString();
        }

        public static void InstallService(IntPtr databaseHandle, int totalServices, string serviceName, string serviceDisplayName, string serviceDescription, ServiceStartMode serviceStartMode, IList<string> servicesDependedOn, string binaryPathAndArgs, string user, string password)
        {
            if (!ServiceUtil.ValidateServiceName(serviceName))
                throw new ArgumentException(string.Format("The service name \"{0}\" is not acceptable.", serviceName), "serviceName");
            if (serviceDisplayName.Length > 0xFF)
                throw new ArgumentException("The service display name is too long; maximum length is 255 characters", "serviceDisplayName");

            IntPtr serviceHandle = IntPtr.Zero;
            try
            {
                serviceHandle = NativeMethods.CreateService(databaseHandle, serviceName, serviceDisplayName, 0xF01FF, totalServices == 1 ? 0x10 : 0x20, (int) serviceStartMode, 1, binaryPathAndArgs, null, IntPtr.Zero, ServiceUtil.MakeDependenciesString(servicesDependedOn), user, password);
                if (serviceHandle == IntPtr.Zero)
                    throw new Win32Exception();

                if (!string.IsNullOrEmpty(serviceDescription))
                {
                    NativeMethods.SERVICE_DESCRIPTION serviceDesc = new NativeMethods.SERVICE_DESCRIPTION();
                    serviceDesc.description = Marshal.StringToHGlobalUni(serviceDescription);
                    bool flag = NativeMethods.ChangeServiceConfig2(serviceHandle, 1, ref serviceDesc);
                    Marshal.FreeHGlobal(serviceDesc.description);
                    if (!flag)
                        throw new Win32Exception();
                }
            }
            finally
            {
                if (serviceHandle != IntPtr.Zero)
                    SafeNativeMethods.CloseServiceHandle(serviceHandle);
            }
        }
    }
}
