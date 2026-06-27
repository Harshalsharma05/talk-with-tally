using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.ServiceProcess;
using System.IO;

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
        private string _apiBase;

        public TrayApplication()
        {
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            if (!LocalConfig.Exists())
            {
                LocalConfig.Save(new ConnectorConfig());
            }

            _config = LocalConfig.Load();
            _apiBase = System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"];

            if (_config.Profiles == null || _config.Profiles.Count == 0)
            {
                if (!PerformFirstRunSetup())
                {
                    Application.Exit();
                    return;
                }
                _config = LocalConfig.Load();
            }

            _engine = new SyncEngine(_config, _apiBase);
            _engine.OnStatusChanged += UpdateTooltip;

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");

            _trayIcon = new NotifyIcon
            {
                // 2. Load the icon from the path, fallback to default if file is missing
                Icon = System.IO.File.Exists(iconPath)
               ? new Icon(iconPath)
               : SystemIcons.Application,
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

            UpdateMenuTexts();

            // Only run a local timer if the Windows Service is NOT installed/running
            _syncTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _syncTimer.Tick += async (s, e) => await PollSyncTimerTickAsync();
            _syncTimer.Start();

            Task.Run(async () =>
            {
                var updater = new AutoUpdater(_apiBase.TrimEnd('/') + "/api/connector/version");
                await updater.CheckAndUpdateAsync();
            });
        }

        // ── THE MUTEX LOCKS: Prevents Tally Concurrency Crashes ──
        private static Mutex _tallyMutex = new Mutex(false, "Global\\InsidashTallyMutex");

        private void SuspendSync()
        {
            if (_syncTimer != null) _syncTimer.Stop();
            // This blocks the Service's RunLoop
            _tallyMutex.WaitOne(TimeSpan.FromSeconds(15));
        }

        private void ResumeSync()
        {
            try { _tallyMutex.ReleaseMutex(); } catch { }
            if (_syncTimer != null) _syncTimer.Start();
        }
        // ────────────────────────────────────────────────────────

        private bool PerformFirstRunSetup()
        {
            MessageBox.Show(
                "Welcome to TalkWithTally!\n\n" +
                "To begin, we will connect to your running Tally Prime instance and select which company you want to sync.",
                "TalkWithTally Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

            SuspendSync(); // Lock Tally

            try
            {
                System.Collections.Generic.List<string> companyNames;
                try
                {
                    var tempEngine = new SyncEngine(_config, _apiBase);
                    companyNames = Task.Run(async () => await tempEngine.GetTallyCompanyNamesAsync()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not connect to Tally Prime. Error: " + ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                using (var dialog = new CompanySelectWindow(_config, companyNames))
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return false;
                }

                _config = LocalConfig.Load();
                if (string.IsNullOrWhiteSpace(_config.TallyCompanyName)) return false;

                using (var activation = new ActivationWindow(_config.TallyCompanyName))
                {
                    if (activation.ShowDialog() != DialogResult.OK) return false;
                }

                return true;
            }
            finally
            {
                ResumeSync(); // Unlock Tally
            }
        }

        private void UpdateTooltip(string message)
        {
            _uiContext.Post(_ =>
            {
                if (_trayIcon != null)
                {
                    string text = $"Insidash | {message}";
                    if (text.Length > 63) text = text.Substring(0, 60) + "...";
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
            // Only poll locally if the Windows Service isn't running to prevent double-hammering
            bool isServiceRunning = false;
            try
            {
                using (var sc = new ServiceController("InsidashTallyConnector"))
                {
                    isServiceRunning = (sc.Status == ServiceControllerStatus.Running);
                }
            }
            catch { }

            if (isServiceRunning) return; // Let the service handle background syncs!

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
            _trayIcon.ShowBalloonTip(2000, "Insidash", "Pausing background service for manual sync...", ToolTipIcon.Info);

            SuspendSync(); // Lock Tally
            try
            {
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
            finally
            {
                ResumeSync(); // Unlock Tally
            }
        }

        private void OnSelectCompany(object sender, EventArgs e)
        {
            _trayIcon.ShowBalloonTip(1500, "Insidash", "Pausing sync to fetch companies safely...", ToolTipIcon.Info);

            SuspendSync(); // Lock Tally from background interference

            try
            {
                System.Collections.Generic.List<string> companyNames;
                try
                {
                    companyNames = Task.Run(async () => await _engine.GetTallyCompanyNamesAsync()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not fetch company list from Tally.\n\nError: " + ex.Message, "Insidash Connector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using (var dialog = new CompanySelectWindow(_config, companyNames))
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        _config = LocalConfig.Load();

                        if (!_config.Profiles.ContainsKey(_config.TallyCompanyName))
                        {
                            _trayIcon.ShowBalloonTip(3000, "Insidash", $"'{_config.TallyCompanyName}' is not activated. Opening activation window...", ToolTipIcon.Warning);
                            using (var activation = new ActivationWindow(_config.TallyCompanyName))
                            {
                                if (activation.ShowDialog() != DialogResult.OK) return;
                            }
                        }

                        _config = LocalConfig.Load();
                        _engine = new SyncEngine(_config, _apiBase);
                        _engine.OnStatusChanged += UpdateTooltip;
                        UpdateMenuTexts();
                    }
                }
            }
            finally
            {
                ResumeSync(); // Unlock Tally and restart service
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
                "Continue?", "Switch Insidash Account", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SuspendSync(); // Lock Tally

            try
            {
                using (var activation = new ActivationWindow(_config.TallyCompanyName))
                {
                    if (activation.ShowDialog() == DialogResult.OK)
                    {
                        _config = LocalConfig.Load();
                        _engine = new SyncEngine(_config, _apiBase);
                        _engine.OnStatusChanged += UpdateTooltip;
                        UpdateMenuTexts();

                        _trayIcon.ShowBalloonTip(2000, "Insidash", "Reconnected successfully ✓", ToolTipIcon.Info);
                    }
                }
            }
            finally
            {
                ResumeSync(); // Unlock Tally
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