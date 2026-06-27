using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;
using System.Diagnostics;

namespace Insidash.TallyConnector
{
    public class ConnectorService : ServiceBase
    {
        public const string SERVICE_NAME = "InsidashTallyConnector";
        public const string DISPLAY_NAME = "Insidash Tally Connector";

        private CancellationTokenSource _cts;
        private Task                    _syncTask;
        private SyncEngine              _engine;
        private AutoUpdater             _updater;
        private ConnectorConfig         _config;
        private string                  _apiBase;

        private static Mutex _tallyMutex = new Mutex(false, "Global\\InsidashTallyMutex");

        public ConnectorService()
        {
            ServiceName = SERVICE_NAME;
            CanStop     = true;
            CanPauseAndContinue = false;
        }

        protected override void OnStart(string[] args)
        {
            _apiBase = ConfigurationManager.AppSettings["ApiBaseUrl"];
            _config  = LocalConfig.Load();  // Throws if not activated — service won't start
            _updater = new AutoUpdater(_apiBase.TrimEnd('/') + "/api/connector/version");
            _engine  = new SyncEngine(_config, _apiBase);
            _cts     = new CancellationTokenSource();

            _syncTask = Task.Run(() => RunLoop(_cts.Token));
        }

        protected override void OnStop()
        {
            _cts?.Cancel();
            try
            {
                _syncTask?.Wait(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Ignore task wait exceptions on SCM stop
            }
        }

        private async Task RunLoop(CancellationToken ct)
        {
            // Update check on startup
            bool updateStarted = await _updater.CheckAndUpdateAsync();
            if (updateStarted) return;

            DateTime lastSyncTime = DateTime.MinValue;
            DateTime lastUpdateCheck = DateTime.Now;

            // Define the Mutex (Must match the name in TrayApplication.cs)
            using (var tallyMutex = new Mutex(false, "Global\\InsidashTallyMutex"))
            {
                while (!ct.IsCancellationRequested)
                {
                    // 1. CHECK FOR MUTEX LOCK
                    // If Tray App is holding the lock, WaitOne(0) returns false.
                    if (!tallyMutex.WaitOne(0))
                    {
                        // Tray App is busy (e.g., Select Company / Sync Now). 
                        // Skip this cycle and wait briefly.
                        await Task.Delay(2000, ct);
                        continue;
                    }

                    try
                    {
                        bool shouldSync = false;

                        // 2. Check if it's time for a scheduled sync
                        if ((DateTime.Now - lastSyncTime).TotalMilliseconds >= _config.SyncIntervalMs)
                        {
                            shouldSync = true;
                        }
                        else
                        {
                            // 3. Poll for manual sync requests
                            string requestId = await _engine.CheckForManualSyncRequestAsync();
                            if (requestId != null)
                            {
                                shouldSync = true;
                                await _engine.MarkManualSyncProcessedAsync(requestId);
                            }
                        }

                        if (shouldSync)
                        {
                            try
                            {
                                await _engine.RunFullSyncAsync();
                                lastSyncTime = DateTime.Now;
                            }
                            catch (Exception ex)
                            {
                                try { EventLog.WriteEntry(SERVICE_NAME, $"Sync error: {ex.Message}", EventLogEntryType.Warning); } catch { }
                            }
                        }

                        // 4. Update check (every 24 hours)
                        if ((DateTime.Now - lastUpdateCheck).TotalHours >= 24)
                        {
                            bool needsRestart = await _updater.CheckAndUpdateAsync();
                            if (needsRestart) return;
                            lastUpdateCheck = DateTime.Now;
                        }
                    }
                    finally
                    {
                        // ALWAYS release the mutex so the Tray App or next cycle can run
                        tallyMutex.ReleaseMutex();
                    }

                    // 5. Wait 10 seconds before polling again
                    try
                    {
                        await Task.Delay(10000, ct);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }
}
