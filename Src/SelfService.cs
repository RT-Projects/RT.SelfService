using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;

namespace RT.Services
{
    /// <summary>
    /// Encapsulates a Windows service hosted in a process that can install and uninstall its own services.
    /// Descendants should override <see cref="ServiceBase.OnStart"/> and other similar methods.
    /// They should also set <see cref="ServiceBase.ServiceName"/>, display name, description etc.
    /// in the constructor.
    /// </summary>
    public abstract class SelfService : ServiceBase
    {
        /// <summary>Service name as displayed in the service manager. Can usually be used just like <see cref="ServiceBase.ServiceName"/> to refer to the service.</summary>
        public string ServiceDisplayName { get; protected set; }
        /// <summary>Describes what the service does. This is displayed by the service manager.</summary>
        public string ServiceDescription { get; protected set; }
        /// <summary>Initial service startup type. The user can change this through the service manager.</summary>
        public ServiceStartMode ServiceStartMode { get; protected set; }
        /// <summary>List of service names that this service depends on. Display names are acceptable but discouraged, since they can be localised.</summary>
        public IList<string> ServicesDependedOn { get; protected set; }

        /// <summary>
        /// Initialises some parameters to their defaults, such as the name of the event log used by the service,
        /// </summary>
        public SelfService()
        {
            EventLog.Log = "Application";
            ServicesDependedOn = new List<string>().AsReadOnly();
        }

        /// <summary>
        /// Starts this service. Returns true if the service state has been verified as running, or false otherwise.
        /// Will wait up to 5 seconds for the service to start. Does not throw any exceptions.
        /// </summary>
        public bool Start()
        {
            return ServiceUtil.StartService(ServiceName);
        }

        /// <summary>
        /// Stops this service. Returns true if the service state has been verified as stopped, or false otherwise.
        /// Will wait up to 5 seconds for the service to stop. Does not throw any exceptions. Note: if the service
        /// is running under the service host, in "service mode", use <see cref="StopSelf"/> instead.
        /// </summary>
        public new bool Stop()
        {
            return ServiceUtil.StopService(ServiceName);
        }

        /// <summary>
        /// Used by the service to stop itself when running in service mode, under the service host.
        /// </summary>
        public void StopSelf()
        {
            base.Stop();
        }
    }

    /// <summary>
    /// Encapsulates the process (i.e. executable) that contains one or more services.
    /// </summary>
    /// <remarks>
    /// The actual process must call <see cref="ExecuteServices"/> when run with the same arguments as when the service
    /// was registered using <see cref="Install(string, string, string)"/>. If this condition is not met, the registered services will be unable to start.
    /// </remarks>
    public abstract class SelfServiceProcess
    {
        /// <summary>A list of all services contained in this process.</summary>
        public IList<SelfService> Services { get; set; }

        /// <summary>
        /// Runs this process in "serivce mode". See Remarks on <see cref="SelfServiceProcess"/>.
        /// </summary>
        public void ExecuteServices()
        {
            ServiceBase.Run(Services.ToArray());
        }

        /// <summary>
        /// Starts all the services in this process.
        /// </summary>
        public void StartAll()
        {
            foreach (var service in Services)
                service.Start();
        }

        /// <summary>
        /// Stops all the services in this process.
        /// </summary>
        public void StopAll()
        {
            foreach (var service in Services)
                service.Stop();
        }

        /// <summary>
        /// Installs all services in this process (i.e. registers them with the service manager). All services are initially stopped.
        /// </summary>
        /// <param name="account">Computer account to use for this service process.</param>
        /// <param name="exeArgs">Optional arguments to be used when started by the service manager. May be null. See Remarks on <see cref="SelfServiceProcess"/>.</param>
        public void Install(ServiceAccount account, string exeArgs)
        {
            string user = null;
            switch (account)
            {
                case ServiceAccount.LocalService:
                    user = @"NT AUTHORITY\LocalService";
                    break;

                case ServiceAccount.NetworkService:
                    user = @"NT AUTHORITY\NetworkService";
                    break;

                case ServiceAccount.User:
                    throw new NotSupportedException("Call the other overload of Install to install as a specific user.");
            }
            install(user, null, exeArgs);
        }

        /// <summary>
        /// Installs all services in this process (i.e. registers them with the service manager). All services are initially stopped.
        /// </summary>
        /// <param name="user">Computer account to use for this service process. This user must have the "Log on as a service" permission (though the call succeeds regardless).</param>
        /// <param name="password">Account password for the specified user. Note that its validity is not verified.</param>
        /// <param name="exeArgs">Optional arguments to be used when started by the service manager. May be null. See Remarks on <see cref="SelfServiceProcess"/>.</param>
        public void Install(string user, string password, string exeArgs)
        {
            install(user, password, exeArgs);
        }

        private void install(string user, string password, string exeArgs)
        {
            if (Services.Count == 0)
                throw new InvalidOperationException("There are no services defined.");

            // user is null for the local system account
            if (user != null && !user.Contains('\\'))
                user = ".\\" + user;

            string binaryPathAndArgs = Assembly.GetEntryAssembly().Location;
            if (binaryPathAndArgs == null || binaryPathAndArgs.Length == 0)
                throw new InvalidOperationException("Could not retrieve entry assembly file name.");
            binaryPathAndArgs = "\"" + binaryPathAndArgs + "\"";
            if (!string.IsNullOrEmpty(exeArgs))
                binaryPathAndArgs += " " + exeArgs;

            IntPtr databaseHandle = ServiceUtil.OpenServiceDatabase();
            try
            {
                foreach (var service in Services)
                    ServiceUtil.InstallService(databaseHandle, Services.Count, service.ServiceName, service.ServiceDisplayName, service.ServiceDescription, service.ServiceStartMode, service.ServicesDependedOn, binaryPathAndArgs, user, password);
            }
            finally
            {
                ServiceUtil.CloseServiceDatabase(databaseHandle);
            }
        }

        /// <summary>
        /// Uninstalls all services in this process (i.e. unregisters them from the service manager). The current
        /// state of the services doesn't matter for this method - running services will be stopped first.
        /// </summary>
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
        }
    }

    /// <summary>
    /// Encapsulates the process (i.e. executable) that contains exactly one service of type <typeparamref name="T"/>.
    /// <c>SingleSelfServiceProcess&lt;T&gt;</c> is not abstract, so it can be used without deriving from it.
    /// </summary>
    public sealed class SingleSelfServiceProcess<T> : SelfServiceProcess where T : SelfService, new()
    {
        /// <summary>Gets the single service that runs in this process.</summary>
        public T Service { get; private set; }

        /// <summary>Constructor.</summary>
        public SingleSelfServiceProcess()
        {
            Service = new T();
            Services = new List<SelfService> { Service }.AsReadOnly();
        }
    }
}
