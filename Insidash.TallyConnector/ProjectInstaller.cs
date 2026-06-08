using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace Insidash.TallyConnector
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            var processInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem  // Runs as SYSTEM to enable network access
            };

            var serviceInstaller = new ServiceInstaller
            {
                ServiceName  = ConnectorService.SERVICE_NAME,
                DisplayName  = ConnectorService.DISPLAY_NAME,
                Description  = "Syncs Tally Prime accounting data to Insidash cloud for AI-powered queries.",
                StartType    = ServiceStartMode.Automatic  // Starts automatically on system boot
            };

            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}
