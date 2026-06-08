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
            if (updateStarted) return; // Batch script will restart us after update

            DateTime lastSyncTime = DateTime.MinValue;
            DateTime lastUpdateCheck = DateTime.Now;

            while (!ct.IsCancellationRequested)
            {
                bool shouldSync = false;

                // 1. Check if it's time for a scheduled sync
                if ((DateTime.Now - lastSyncTime).TotalMilliseconds >= _config.SyncIntervalMs)
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
                        // Clear the request flag on the server immediately to prevent double runs
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
                        // Log to Windows Event Log
                        try
                        {
                            EventLog.WriteEntry(SERVICE_NAME,
                                $"Sync error: {ex.Message}", EventLogEntryType.Warning);
                        }
                        catch
                        {
                            // Ignore EventLog write failures during development
                        }
                    }
                }

                // Check for updates once every 24 hours
                if ((DateTime.Now - lastUpdateCheck).TotalHours >= 24)
                {
                    bool needsRestart = await _updater.CheckAndUpdateAsync();
                    if (needsRestart) return;
                    lastUpdateCheck = DateTime.Now;
                }

                // Wait 10 seconds before polling again
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
