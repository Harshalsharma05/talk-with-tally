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
            // 1. Ensure the AppData directory exists safely
            string appDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "InsidashTallyConnector"
            );
            try
            {
                System.IO.Directory.CreateDirectory(appDataPath);
            }
            catch 
            { 
                // Prevent crash if folder permissions are restrictive
            }

            // 2. Load config safely (with fallback if missing or corrupted)
            ConnectorConfig config = null;
            try
            {
                config = LocalConfig.Load();
            }
            catch 
            { 
                // Ignore loading errors to prevent silent crashes
            }

            if (config == null)
            {
                config = new ConnectorConfig();
                try
                {
                    // Create and save a clean, default config file on first run
                    LocalConfig.Save(config); 
                }
                catch 
                {
                }
            }

            // 3. Write debug text safely
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(appDataPath, "debug.txt"),
                    "TOKEN: " + (config.SyncToken ?? "") + "\r\nCOMPANY: " + config.CompanyID
                );
            }
            catch 
            { 
                // Ignore writing errors
            }

            // 4. Boot SCM vs GUI
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