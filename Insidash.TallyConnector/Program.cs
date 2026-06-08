using System;
using System.ServiceProcess;
using System.Windows.Forms;

namespace Insidash.TallyConnector
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Detect launch context:
            // - Environment.UserInteractive is FALSE when called by Windows Service Control Manager (SCM).
            // - Environment.UserInteractive is TRUE when run by a user (double-click, command line, startup folder).
            // LocalConfig.Save(new ConnectorConfig());
            if (!Environment.UserInteractive)
            {
                // Run as a background Windows Service
                ServiceBase.Run(new ConnectorService());
            }
            else
            {
                // Run as a taskbar system tray GUI application
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplication());
            }
        }
    }
}
