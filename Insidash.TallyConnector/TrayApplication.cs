using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Insidash.TallyConnector
{
    public class TrayApplication : ApplicationContext
    {
        private NotifyIcon   _trayIcon;
        private SyncEngine   _engine;
        private ConnectorConfig _config;
        private System.Windows.Forms.Timer _syncTimer;
        private readonly SynchronizationContext _uiContext;
        private DateTime _lastSyncTime = DateTime.MinValue;

        public TrayApplication()
        {
            // Capture the UI thread's synchronization context for cross-thread status updates
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // Check activation
            if (!LocalConfig.Exists())
            {
                var activation = new ActivationWindow();
                if (activation.ShowDialog() != DialogResult.OK)
                {
                    Application.Exit();
                    return;
                }
            }

            _config = LocalConfig.Load();
            _engine = new SyncEngine(_config, System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"]);
            _engine.OnStatusChanged += UpdateTooltip;

            // Build tray icon
            _trayIcon = new NotifyIcon
            {
                Icon    = SystemIcons.Application, // Standard icon fallback (can be custom later)
                Visible = true,
                Text    = "Insidash Tally Connector"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Sync Now",        null, OnSyncNow);
            menu.Items.Add("Open Settings",   null, OnOpenSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit Connector",  null, OnExit);
            _trayIcon.ContextMenuStrip = menu;

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
                var updater = new AutoUpdater(
                    System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"].TrimEnd('/') + "/api/connector/version");
                await updater.CheckAndUpdateAsync();
            });
        }

        private void UpdateTooltip(string message)
        {
            // Marshal text updates back to the WinForms UI thread securely
            _uiContext.Post(_ => {
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

        private async Task PollSyncTimerTickAsync()
        {
            bool shouldSync = false;

            // 1. Check if it is time for a scheduled sync
            if ((DateTime.Now - _lastSyncTime).TotalMilliseconds >= _config.SyncIntervalMs)
            {
                shouldSync = true;
            }
            else
            {
                // 2. Poll the server for any user-initiated "Sync Now" requests
                string requestId = await _engine.CheckForManualSyncRequestAsync();
                if (requestId != null)
                {
                    shouldSync = true;
                    // Balloon tip notification to alert the user that sync is starting
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

        private void OnOpenSettings(object sender, EventArgs e)
        {
            using (var settings = new SettingsWindow(_config))
            {
                settings.ShowDialog();
            }
            // Reload config in case user changed Tally host/port
            _config = LocalConfig.Load();
            _engine = new SyncEngine(_config, System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"]);
            _engine.OnStatusChanged += UpdateTooltip;
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
