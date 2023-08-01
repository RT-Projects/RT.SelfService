using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace RT.Services;

/// <summary>Specifies a service's security context, which defines its logon type.</summary>
public enum ServiceAccount
{
    /// <summary>
    ///     An account that acts as a non-privileged user on the local computer, and presents anonymous credentials to any
    ///     remote server.</summary>
    LocalService = 0,
    /// <summary>
    ///     An account that provides extensive local privileges, and presents the computer's credentials to any remote
    ///     server.</summary>
    NetworkService = 1,
    /// <summary>
    ///     An account, used by the service control manager, that has extensive privileges on the local computer and acts
    ///     as the computer on the network.</summary>
    LocalSystem = 2,
    /// <summary>An account defined by a specific user on the network.</summary>
    User = 3,
}

/// <summary>
///     Provides utility methods for creating executables which can install and uninstall themselves as a service, and run
///     both as a service and as a standard process. This class does not support processes which host multiple services.</summary>
public static class SingleSelfService
{
    /// <summary>
    ///     Registers the running executable as an own-process Windows Service using the specified options. Throws a <see
    ///     cref="ServiceAlreadyExistsException"/> if a service with the same name or display name already exists. Throws
    ///     a <c>Win32Exception</c> for other errors.</summary>
    /// <param name="name">
    ///     Name of the service, used to uniquely identify it.</param>
    /// <param name="displayName">
    ///     Service name as it should be shown to the administrator.</param>
    /// <param name="description">
    ///     A description of the service, displayed in the sidebar in services.msc.</param>
    /// <param name="exeArgs">
    ///     Command-line arguments to be passed to the executable when started by the service manager as a service. When
    ///     run with this command line, the executable must call <see cref="ExecuteAsService"/> at some point during
    ///     initialisation.</param>
    /// <param name="start">
    ///     Specifies whether the service should start automatically on boot.</param>
    /// <param name="account">
    ///     Specifies one of the built-in accounts under which the service process should execute.</param>
    /// <param name="username">
    ///     If using ServiceAccount.User, specifies the name of the user to run the service as.</param>
    /// <param name="password">
    ///     The password of the user account specified by <paramref name="username"/>, if any.</param>
    /// <param name="servicesDependedOn">
    ///     A list of service names that must be started before this service can be started, or null if none.</param>
    public static void Install(string name, string displayName, string description, string exeArgs, ServiceStartMode start = ServiceStartMode.Automatic, ServiceAccount account = ServiceAccount.LocalService, string username = null, string password = null, IList<string> servicesDependedOn = null)
    {
        if (account == ServiceAccount.User)
        {
            if (username == null)
                throw new ArgumentNullException("username");
        }
        else
        {
            if (username != null)
                throw new ArgumentException("Username must be null.", "username");
            if (password != null)
                throw new ArgumentException("Password must be null.", "username");

            switch (account)
            {
                case ServiceAccount.LocalService: username = @"NT AUTHORITY\LocalService"; password = null; break;
                case ServiceAccount.NetworkService: username = @"NT AUTHORITY\NetworkService"; break;
                case ServiceAccount.LocalSystem: username = null; break;
                default: throw new InvalidOperationException();
            }
        }

        if (servicesDependedOn == null)
            servicesDependedOn = new string[0];

        // username is null for the local system account
        if (username != null && !username.Contains('\\'))
            username = ".\\" + username;

        string binaryPathAndArgs = Process.GetCurrentProcess().MainModule.FileName;
        if (binaryPathAndArgs == null || binaryPathAndArgs.Length == 0)
            throw new InvalidOperationException("Could not retrieve entry assembly file name.");
        binaryPathAndArgs = "\"" + binaryPathAndArgs + "\"";
        if (!string.IsNullOrEmpty(exeArgs))
            binaryPathAndArgs += " " + exeArgs;

        IntPtr databaseHandle = ServiceUtil.OpenServiceDatabase();
        try
        {
            ServiceUtil.InstallService(databaseHandle, 1, name, displayName, description, start, servicesDependedOn, binaryPathAndArgs, username, password);
        }
        finally
        {
            ServiceUtil.CloseServiceDatabase(databaseHandle);
        }
    }

    /// <summary>
    ///     Un-registers a service with the specified name. Note that this method can uninstall any service, not only
    ///     those registered with <see cref="Install"/>. Throws a <see cref="ServiceNotFoundException"/> if no such
    ///     service exists, or a <c>Win32Exception</c> for other errors.</summary>
    /// <param name="name">
    ///     Name of the service to un-register.</param>
    public static void Uninstall(string name)
    {
        IntPtr databaseHandle = ServiceUtil.OpenServiceDatabase();
        try
        {
            ServiceUtil.DeleteService(databaseHandle, name);
            ServiceUtil.StopService(name); // Not sure why this comes after the deletion, but .NET ServiceInstaller does this, so perhaps there's a good reason
        }
        finally
        {
            ServiceUtil.CloseServiceDatabase(databaseHandle);
        }
    }

    /// <summary>
    ///     Passes control to the service manager, and configures callbacks to be used by the service manager in response
    ///     to various events. This method must be called when the process is invoked with command line arguments
    ///     specified in <see cref="Install"/>. Does not return (unless called outside of SCM control, in which case it
    ///     shows a generic error message and terminates the process).</summary>
    /// <param name="onStart">
    ///     Invoked when the service is started.</param>
    /// <param name="onStop">
    ///     Invoked when the service is stopped. Optional.</param>
    /// <param name="onShutdown">
    ///     Invoked when the system is shutting down. Optional.</param>
    /// <param name="onPowerEvent">
    ///     Invoked on a power status change event. Optional. For more information, see ServiceBase.OnPowerEvent.</param>
    public static void ExecuteAsService(Action onStart, Action onStop = null, Action onShutdown = null, Func<PowerBroadcastStatus, bool> onPowerEvent = null)
    {
        var svc = new service();
        svc.StartEvent = onStart;
        svc.StopEvent = onStop;
        svc.ShutdownEvent = onShutdown;
        svc.PowerEvent = onPowerEvent;
        svc.Run();
    }

    private class service : ServiceBase
    {
        public Action StartEvent;
        public Action StopEvent;
        public Action ShutdownEvent;
        public Func<PowerBroadcastStatus, bool> PowerEvent;

        public void Run()
        {
            if (StartEvent == null)
                throw new NullReferenceException();
            CanStop = StopEvent != null;
            CanShutdown = ShutdownEvent != null;
            CanHandlePowerEvent = PowerEvent != null;
            ServiceBase.Run(this);
        }

        protected override void OnStart(string[] args)
        {
            StartEvent();
        }

        protected override void OnStop()
        {
            if (StopEvent != null)
                StopEvent();
        }

        protected override void OnShutdown()
        {
            if (ShutdownEvent != null)
                ShutdownEvent();
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
        {
            if (PowerEvent != null)
                return PowerEvent(powerStatus);
            return true;
        }
    }
}
