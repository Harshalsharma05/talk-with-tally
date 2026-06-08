using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Insidash.TallyConnector
{
    public class SyncEngine
    {
        private static readonly HttpClient _tallyClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly HttpClient _apiClient   = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public event Action<string> OnStatusChanged;  // raised to update tray tooltip

        private readonly ConnectorConfig _config;
        private readonly string          _apiBase;

        public SyncEngine(ConnectorConfig config, string apiBase)
        {
            _config  = config;
            _apiBase = apiBase;
        }

        public async Task<SyncResult> RunFullSyncAsync()
        {
            var result = new SyncResult { StartedAt = DateTime.Now };

            // Sync all four major data types required by the AI chat engine
            var dataTypes = new[] { "Ledger", "Voucher", "StockItem", "BillOutstanding" };
            foreach (var dataType in dataTypes)
            {
                try
                {
                    OnStatusChanged?.Invoke($"Syncing {dataType}...");
                    await SyncDataTypeAsync(dataType);
                    result.SyncedTypes.Add(dataType);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{dataType}: {ex}");

                    string logPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "InsidashTallyConnector",
                        "sync-errors.log"
                    );

                    System.IO.File.AppendAllText(
                        logPath,
                        $"[{DateTime.Now}] {dataType}\r\n{ex}\r\n\r\n"
                    );

                    OnStatusChanged?.Invoke($"{dataType} sync failed");
                }
            }

            result.CompletedAt = DateTime.Now;
            if (result.IsFullSuccess)
            {
                OnStatusChanged?.Invoke($"Last synced: {DateTime.Now:HH:mm}");
            }
            else
            {
                OnStatusChanged?.Invoke($"Sync finished with {result.Errors.Count} errors");
            }
            return result;
        }

        private async Task SyncDataTypeAsync(string dataType)
        {
            string tallyUrl = $"http://{_config.TallyHost}:{_config.TallyPort}";
            string envelope = TallyEnvelopeFactory.Build(dataType);

            var tallyContent  = new StringContent(envelope, Encoding.UTF8, "text/xml");
            var tallyResponse = await _tallyClient.PostAsync(tallyUrl, tallyContent);
            tallyResponse.EnsureSuccessStatusCode();
            string rawXml = await tallyResponse.Content.ReadAsStringAsync();

            // Map singular connector types to plural API endpoint payloads to avoid routing errors
            string apiDataType = dataType;
            if (dataType == "Ledger") apiDataType = "Ledgers";
            else if (dataType == "Voucher") apiDataType = "Vouchers";
            else if (dataType == "StockItem") apiDataType = "StockItems";
            else if (dataType == "BillOutstanding") apiDataType = "BillOutstandings";

            var payload = JsonConvert.SerializeObject(new { DataType = apiDataType, RawXml = rawXml });
            using (var req = new HttpRequestMessage(HttpMethod.Post, _apiBase.TrimEnd('/') + "/api/tally/sync"))
            {
                Console.WriteLine($"[DEBUG] Sending SyncToken: {_config.SyncToken}");
                req.Headers.Add("X-Sync-Token", _config.SyncToken);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                var apiResp = await _apiClient.SendAsync(req);
                if (!apiResp.IsSuccessStatusCode)
                {
                    string body = await apiResp.Content.ReadAsStringAsync();

                    throw new Exception(
                        $"API Error {(int)apiResp.StatusCode}: {body}"
                    );
                }
            }
        }

        public async Task<string> CheckForManualSyncRequestAsync()
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, _apiBase.TrimEnd('/') + "/api/connector/check-sync-request"))
                {
                    req.Headers.Add("X-Sync-Token", _config.SyncToken);
                    var resp = await _apiClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        dynamic data = JsonConvert.DeserializeObject(json);
                        if (data != null && data.syncRequested != null && (bool)data.syncRequested)
                        {
                            return (string)data.requestId;
                        }
                    }
                }
            }
            catch
            {
                // Ignore API/connection errors for manual check to prevent crashing the agent
            }
            return null;
        }

        public async Task MarkManualSyncProcessedAsync(string requestId)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { RequestID = requestId });
                using (var req = new HttpRequestMessage(HttpMethod.Post, _apiBase.TrimEnd('/') + "/api/connector/mark-sync-processed"))
                {
                    req.Headers.Add("X-Sync-Token", _config.SyncToken);
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    var resp = await _apiClient.SendAsync(req);
                    resp.EnsureSuccessStatusCode();
                }
            }
            catch
            {
                // Ignore API/connection errors
            }
        }
    }

    public class SyncResult
    {
        public DateTime StartedAt   { get; set; }
        public DateTime CompletedAt { get; set; }
        public List<string> SyncedTypes { get; set; } = new List<string>();
        public List<string> Errors      { get; set; } = new List<string>();
        public bool IsFullSuccess => Errors.Count == 0;
    }
}
