using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.ServiceProcess;

namespace Insidash.TallyConnector
{
    public class TrayApplication : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private SyncEngine _engine;
        private ConnectorConfig _config;
        private System.Windows.Forms.Timer _syncTimer;
        private readonly SynchronizationContext _uiContext;
        private DateTime _lastSyncTime = DateTime.MinValue;
        private string _apiBase; // Promoted to field for re-activation/settings reloads

        public TrayApplication()
        {
            // Capture the UI thread's synchronization context for cross-thread status updates
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // Ensure a local config file exists on disk
            if (!LocalConfig.Exists())
            {
                LocalConfig.Save(new ConnectorConfig());
            }

            _config = LocalConfig.Load();
            _apiBase = System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"];

            // ── FIRST-RUN SETUP WIZARD ──
            // If there are no configured profiles, run the Setup Wizard immediately.
            if (_config.Profiles == null || _config.Profiles.Count == 0)
            {
                if (!PerformFirstRunSetup())
                {
                    // User canceled first-run setup, exit application
                    Application.Exit();
                    return;
                }

                // Reload config after successful setup
                _config = LocalConfig.Load();
            }

            _engine = new SyncEngine(_config, _apiBase);
            _engine.OnStatusChanged += UpdateTooltip;

            // Build tray icon
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application, // Standard icon fallback (can be custom later)
                Visible = true,
                Text = "Insidash Tally Connector"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Sync Now", null, OnSyncNow);
            menu.Items.Add("Select Tally Company", null, OnSelectCompany);
            menu.Items.Add("Open Settings", null, OnOpenSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Switch Insidash Account / Re-activate", null, OnReactivate);
            menu.Items.Add("Exit Connector", null, OnExit);
            _trayIcon.ContextMenuStrip = menu;

            // Initialize the menu text with current active status
            UpdateMenuTexts();

            // Start periodic sync timer polling every 10 seconds
            _syncTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _syncTimer.Tick += async (s, e) => await PollSyncTimerTickAsync();
            _syncTimer.Start();

            // Run first sync immediately in background
            Task.Run(async () =>
            {
                await _engine.RunFullSyncAsync();
                _lastSyncTime = DateTime.Now;
            });

            // Check for updates on startup
            Task.Run(async () =>
            {
                var updater = new AutoUpdater(_apiBase.TrimEnd('/') + "/api/connector/version");
                await updater.CheckAndUpdateAsync();
            });
        }

        private bool PerformFirstRunSetup()
        {
            MessageBox.Show(
                "Welcome to TalkWithTally!\n\n" +
                "To begin, we will connect to your running Tally Prime instance and select which company you want to sync.",
                "TalkWithTally Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Fetch company list from Tally
            System.Collections.Generic.List<string> companyNames;
            try
            {
                // Create a temporary engine just to query Tally (active token doesn't matter yet)
                var tempEngine = new SyncEngine(_config, _apiBase);
                companyNames = Task.Run(async () => await tempEngine.GetTallyCompanyNamesAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not connect to Tally Prime.\n\n" +
                    "Please ensure Tally Prime is running with its gateway server (HTTP port) enabled.\n\n" +
                    "Error: " + ex.Message,
                    "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Show company select dialog
            using (var dialog = new CompanySelectWindow(_config, companyNames))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return false;
                }
            }

            // Reload config after Selection dialog saved TallyCompanyName (but it has no profile yet)
            _config = LocalConfig.Load();
            string selectedCompany = _config.TallyCompanyName;

            if (string.IsNullOrWhiteSpace(selectedCompany))
            {
                return false;
            }

            // Step B: Prompt for Activation Key for this selected company
            using (var activation = new ActivationWindow(selectedCompany))
            {
                if (activation.ShowDialog() != DialogResult.OK)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateTooltip(string message)
        {
            // Marshal text updates back to the WinForms UI thread securely
            _uiContext.Post(_ =>
            {
                if (_trayIcon != null)
                {
                    string text = $"Insidash | {message}";
                    // NotifyIcon tooltip has a hard limit of 63 characters in .NET Framework
                    if (text.Length > 63)
                    {
                        text = text.Substring(0, 60) + "...";
                    }
                    _trayIcon.Text = text;
                }
            }, null);
        }

        private void UpdateMenuTexts()
        {
            if (_trayIcon == null || _trayIcon.ContextMenuStrip == null) return;

            string activeComp = string.IsNullOrWhiteSpace(_config.TallyCompanyName)
                ? "None (Autodetect)"
                : _config.TallyCompanyName;

            foreach (ToolStripItem item in _trayIcon.ContextMenuStrip.Items)
            {
                if (item.Text.StartsWith("Select Tally Company"))
                {
                    item.Text = $"Select Tally Company (Active: {activeComp})";
                    break;
                }
            }
        }

        private async Task PollSyncTimerTickAsync()
        {
            bool shouldSync = false;

            if ((DateTime.Now - _lastSyncTime).TotalMilliseconds >= _config.SyncIntervalMs)
            {
                shouldSync = true;
            }
            else
            {
                string requestId = await _engine.CheckForManualSyncRequestAsync();
                if (requestId != null)
                {
                    shouldSync = true;
                    _trayIcon.ShowBalloonTip(2000, "Insidash", "Sync requested from dashboard...", ToolTipIcon.Info);
                    await _engine.MarkManualSyncProcessedAsync(requestId);
                }
            }

            if (shouldSync)
            {
                await _engine.RunFullSyncAsync();
                _lastSyncTime = DateTime.Now;
            }
        }

        private async void OnSyncNow(object sender, EventArgs e)
        {
            _trayIcon.ShowBalloonTip(2000, "Insidash", "Syncing Tally data...", ToolTipIcon.Info);
            var result = await _engine.RunFullSyncAsync();
            if (result.IsFullSuccess)
            {
                _lastSyncTime = DateTime.Now;
                _trayIcon.ShowBalloonTip(2000, "Insidash", "Sync complete ✓", ToolTipIcon.Info);
            }
            else
            {
                _trayIcon.ShowBalloonTip(3000, "Insidash", $"Sync finished with {result.Errors.Count} errors.", ToolTipIcon.Warning);
            }
        }

        private void OnSelectCompany(object sender, EventArgs e)
        {
            _trayIcon.ShowBalloonTip(1500, "Insidash", "Fetching companies from Tally...", ToolTipIcon.Info);

            System.Collections.Generic.List<string> companyNames;
            try
            {
                companyNames = Task.Run(async () => await _engine.GetTallyCompanyNamesAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not fetch company list from Tally.\n\n" +
                    "Make sure Tally Prime is open and the gateway server is enabled.\n\n" +
                    "Error: " + ex.Message,
                    "Insidash Connector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new CompanySelectWindow(_config, companyNames))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _config = LocalConfig.Load(); // reload with new TallyCompanyName

                    // Verify if this switched company is already activated
                    if (!_config.Profiles.ContainsKey(_config.TallyCompanyName))
                    {
                        // Switch Company context, but it is unactivated — trigger activation popup
                        _trayIcon.ShowBalloonTip(3000, "Insidash", $"'{_config.TallyCompanyName}' is not activated. Opening activation window...", ToolTipIcon.Warning);
                        using (var activation = new ActivationWindow(_config.TallyCompanyName))
                        {
                            if (activation.ShowDialog() != DialogResult.OK)
                            {
                                // User canceled activation, switch back to previous valid config
                                return;
                            }
                        }
                    }

                    _config = LocalConfig.Load(); // reload config again after activation
                    _engine = new SyncEngine(_config, _apiBase);
                    _engine.OnStatusChanged += UpdateTooltip;
                    UpdateMenuTexts();
                    RestartBackgroundService();

                    // Trigger sync immediately for the newly selected company
                    Task.Run(async () => await _engine.RunFullSyncAsync());
                }
            }
        }

        private void OnOpenSettings(object sender, EventArgs e)
        {
            using (var settings = new SettingsWindow(_config))
            {
                settings.ShowDialog();
            }
            _config = LocalConfig.Load();
            _engine = new SyncEngine(_config, _apiBase);
            _engine.OnStatusChanged += UpdateTooltip;
            UpdateMenuTexts();
        }

        private void OnReactivate(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_config.TallyCompanyName))
            {
                MessageBox.Show("Please select a Tally company first before activating.", "Insidash", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"This will overwrite or link your active Tally company '{_config.TallyCompanyName}' to a different Insidash account.\n\n" +
                "Syncing will pause until a new activation key is entered.\n\n" +
                "Continue?",
                "Switch Insidash Account",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            _syncTimer.Stop();

            using (var activation = new ActivationWindow(_config.TallyCompanyName))
            {
                if (activation.ShowDialog() == DialogResult.OK)
                {
                    _config = LocalConfig.Load();
                    _engine = new SyncEngine(_config, _apiBase);
                    _engine.OnStatusChanged += UpdateTooltip;
                    UpdateMenuTexts();

                    _trayIcon.ShowBalloonTip(2000, "Insidash",
                        "Reconnected successfully ✓",
                        ToolTipIcon.Info);

                    RestartBackgroundService();

                    Task.Run(async () => await _engine.RunFullSyncAsync());
                }
            }

            _syncTimer.Start();
        }

        private void RestartBackgroundService()
        {
            try
            {
                using (var sc = new ServiceController("InsidashTallyConnector"))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        sc.Start();
                    }
                }
            }
            catch
            {
                // Service might not be installed (running local Dev/Debug exe) — ignore safely
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            if (_syncTimer != null)
            {
                _syncTimer.Stop();
                _syncTimer.Dispose();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            Application.Exit();
        }
    }
}