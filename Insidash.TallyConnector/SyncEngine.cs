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
        // Selective Timeout Tuning. 
        // We give Tally up to 60 seconds to compile its data dumps, and the API 60 seconds to process them.
        private static readonly HttpClient _tallyClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private static readonly HttpClient _apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        public event Action<string> OnStatusChanged;  // raised to update tray tooltip

        private readonly ConnectorConfig _config;
        private readonly string _apiBase;

        public SyncEngine(ConnectorConfig config, string apiBase)
        {
            _config = config;
            _apiBase = apiBase;
        }

        private string GetActiveSyncToken()
        {
            if (string.IsNullOrWhiteSpace(_config.TallyCompanyName)) return string.Empty;
            
            if (_config.Profiles != null && _config.Profiles.TryGetValue(_config.TallyCompanyName, out var profile))
            {
                return profile.SyncToken;
            }
            return string.Empty;
        }

        private CompanyProfile GetActiveProfile()
        {
            if (string.IsNullOrWhiteSpace(_config.TallyCompanyName)) return null;
            if (_config.Profiles != null && _config.Profiles.TryGetValue(_config.TallyCompanyName, out var profile))
            {
                return profile;
            }
            return null;
        }

        public async Task<SyncResult> RunFullSyncAsync()
        {
            var result = new SyncResult { StartedAt = DateTime.Now };

            // STOPS DATA POLLUTION: Abort if no Tally company has been chosen yet
            if (string.IsNullOrWhiteSpace(_config.TallyCompanyName))
            {
                string errorMsg = "Sync paused — Right-click tray and 'Select Tally Company' first.";
                OnStatusChanged?.Invoke(errorMsg);
                result.Errors.Add(errorMsg);
                return result;
            }

            // Verify if the active company profile is actually activated
            var profile = GetActiveProfile();
            if (profile == null || string.IsNullOrWhiteSpace(profile.SyncToken))
            {
                string errorMsg = $"Sync paused — '{_config.TallyCompanyName}' is not activated.";
                OnStatusChanged?.Invoke(errorMsg);
                result.Errors.Add(errorMsg);
                return result;
            }

            // ── CRITICAL FIX: PRE-FLIGHT SAFETY CHECK ──
            // Ask Tally what companies are currently open. If our target isn't open, 
            // DO NOT send data requests, or Tally will crash with a Memory Access Violation.
            try
            {
                var openCompanies = await GetTallyCompanyNamesAsync();
                bool isCompanyOpen = false;

                foreach (var c in openCompanies)
                {
                    if (c.Equals(_config.TallyCompanyName, StringComparison.OrdinalIgnoreCase))
                    {
                        isCompanyOpen = true;
                        break;
                    }
                }

                if (!isCompanyOpen)
                {
                    string msg = $"Paused: '{_config.TallyCompanyName}' is not open in Tally.";
                    OnStatusChanged?.Invoke(msg);
                    result.Errors.Add(msg);
                    return result; // Silently abort sync and wait for next tick
                }
            }
            catch
            {
                // If Tally is still booting up or unreachable, pause gracefully.
                string msg = "Paused: Waiting for Tally to start...";
                OnStatusChanged?.Invoke(msg);
                result.Errors.Add(msg);
                return result;
            }
            // ───────────────────────────────────────────

            // AUTO-DISCOVERY ON-THE-FLY TRIGGER
            if (!profile.LastVoucherSyncAt.HasValue || string.IsNullOrWhiteSpace(profile.HistoricalStartDate))
            {
                OnStatusChanged?.Invoke("Discovering company timeline...");
                string discoveredDate = await DiscoverCompanyStartDateAsync();
                if (!string.IsNullOrWhiteSpace(discoveredDate))
                {
                    profile.HistoricalStartDate = discoveredDate;
                    LocalConfig.Save(_config);
                }
            }

            // Sync all major data types
            int initialMasterId = profile.LastMasterAlterId;
            int highestMasterIdFound = initialMasterId;

            // Updated to the unified enum array we built earlier
            var dataTypes = new[] { TallyDataType.Group, TallyDataType.Ledger, TallyDataType.StockItem };

            foreach (var dataType in dataTypes)
            {
                try
                {
                    OnStatusChanged?.Invoke($"Syncing {dataType}...");
                    int maxIdReturned = await SyncDataTypeAsync(profile, dataType, initialMasterId);

                    if (maxIdReturned > highestMasterIdFound)
                    {
                        highestMasterIdFound = maxIdReturned;
                    }
                    result.SyncedTypes.Add(dataType.ToString());
                }
                catch (Exception ex)
                {
                    LogError(dataType.ToString(), ex);
                    OnStatusChanged?.Invoke($"{dataType} sync failed");
                }
            }

            if (highestMasterIdFound > profile.LastMasterAlterId)
            {
                profile.LastMasterAlterId = highestMasterIdFound;
                LocalConfig.Save(_config);
            }

            try
            {
                OnStatusChanged?.Invoke("Processing Vouchers...");
                await SyncVouchersSmartAsync(profile);
                result.SyncedTypes.Add("Voucher");
            }
            catch (Exception ex)
            {
                LogError("Voucher", ex);
                OnStatusChanged?.Invoke("Voucher sync failed");
            }
            try
            {
                OnStatusChanged?.Invoke("Processing Outstanding Bills...");
                await SyncOutstandingsSmartAsync(profile);
                result.SyncedTypes.Add("BillOutstanding");
            }
            catch (Exception ex)
            {
                LogError("BillOutstanding", ex);
                OnStatusChanged?.Invoke("Outstanding Bills sync failed");
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

        private async Task SyncVouchersSmartAsync(CompanyProfile profile)
        {
            int startId = profile.LastVoucherAlterId;
            OnStatusChanged?.Invoke(startId == 0 ? "Starting Voucher Backfill..." : "Syncing recent vouchers...");

            string rawXml = await ExecuteVoucherSyncRangeAsync(profile, startId);
            int maxReturnedId = GetMaxAlterIdFromXml(rawXml);

            // Update the high-water mark so the next sync only pulls new records
            if (maxReturnedId > startId)
            {
                profile.LastVoucherAlterId = maxReturnedId;
                LocalConfig.Save(_config);
            }
        }

        private async Task<string> ExecuteVoucherSyncRangeAsync(CompanyProfile profile, int startId)
        {
            string tallyUrl = $"http://{_config.TallyHost}:{_config.TallyPort}";

            // Call the factory with only the startId
            string envelope = TallyEnvelopeFactory.Build(TallyDataType.Voucher, _config.TallyCompanyName, startId, null);

            var tallyContent = new StringContent(envelope, System.Text.Encoding.UTF8, "text/xml");
            var tallyResponse = await _tallyClient.PostAsync(tallyUrl, tallyContent);
            tallyResponse.EnsureSuccessStatusCode();
            string rawXml = await tallyResponse.Content.ReadAsStringAsync();

            var payload = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                DataType = "Vouchers",
                RawXml = rawXml
            });

            using (var req = new HttpRequestMessage(HttpMethod.Post, _apiBase.TrimEnd('/') + "/api/tally/sync"))
            {
                req.Headers.Add("X-Sync-Token", profile.SyncToken);
                req.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var apiResp = await _apiClient.SendAsync(req);
                if (!apiResp.IsSuccessStatusCode)
                {
                    string body = await apiResp.Content.ReadAsStringAsync();
                    throw new Exception($"API Error {(int)apiResp.StatusCode}: {body}");
                }
            }

            return rawXml;
        }

        private async Task<int> SyncDataTypeAsync(CompanyProfile profile, TallyDataType dataType, int startAlterId)
        {
            string tallyUrl = $"http://{_config.TallyHost}:{_config.TallyPort}";

            // Now uses the enum!
            string envelope = TallyEnvelopeFactory.Build(dataType, _config.TallyCompanyName, startAlterId);


            var tallyContent = new StringContent(envelope, Encoding.UTF8, "text/xml");
            var tallyResponse = await _tallyClient.PostAsync(tallyUrl, tallyContent);
            tallyResponse.EnsureSuccessStatusCode();
            string rawXml = await tallyResponse.Content.ReadAsStringAsync();

            //if (dataType.ToString().StartsWith("Ledger") || dataType == TallyDataType.BillOutstanding)
            //{
            //    try
            //    {
            //        string debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Tally_RAW_{dataType}.txt");
            //        System.IO.File.WriteAllText(debugPath, rawXml);
            //        System.Diagnostics.Debug.WriteLine($"[Connector Debug] Dumped {dataType} raw response to: {debugPath}");

            //         // Pause execution for 2 seconds so you can visibly see it hit this stage in the UI DEBUG ONLY
            //         await Task.Delay(2000);
            //    }
            //    catch { }
            //}

            string apiDataType = "";
            if (dataType == TallyDataType.Ledger) apiDataType = "Ledgers";
            else if (dataType == TallyDataType.StockItem) apiDataType = "StockItems";
            else if (dataType == TallyDataType.BillOutstanding) apiDataType = "BillOutstandings";
            else if (dataType == TallyDataType.Group) apiDataType = "Groups";

            var payload = JsonConvert.SerializeObject(new { DataType = apiDataType, RawXml = rawXml });
            using (var req = new HttpRequestMessage(HttpMethod.Post, _apiBase.TrimEnd('/') + "/api/tally/sync"))
            {
                req.Headers.Add("X-Sync-Token", profile.SyncToken);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                var apiResp = await _apiClient.SendAsync(req);
                if (!apiResp.IsSuccessStatusCode)
                {
                    string body = await apiResp.Content.ReadAsStringAsync();
                    throw new Exception($"API Error {(int)apiResp.StatusCode}: {body}");
                }
            }

            return GetMaxAlterIdFromXml(rawXml);
        }

        private string BuildVoucherDateRangeEnvelope(string fromDate, string toDate)
        {
            string companyTag = string.IsNullOrWhiteSpace(_config.TallyCompanyName)
            ? ""
            : $"<SVCURRENTCOMPANY>{System.Security.SecurityElement.Escape(_config.TallyCompanyName)}</SVCURRENTCOMPANY>";

            return $@"<ENVELOPE>
            <HEADER>
            <VERSION>1</VERSION>
            <TALLYREQUEST>Export</TALLYREQUEST>
            <TYPE>Collection</TYPE>
            <ID>MyVoucherCollection</ID>
            </HEADER>
            <BODY>
            <DESC>
                <STATICVARIABLES>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                <SVFROMDATE TYPE=""Date"">{fromDate}</SVFROMDATE>
                <SVTODATE TYPE=""Date"">{toDate}</SVTODATE>
                {companyTag}
                </STATICVARIABLES>
                <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""MyVoucherCollection"">
                    <TYPE>Voucher</TYPE>
                    <NATIVEMETHOD>DATE</NATIVEMETHOD>
                    <NATIVEMETHOD>VOUCHERTYPENAME</NATIVEMETHOD>
                    <NATIVEMETHOD>PARTYLEDGERNAME</NATIVEMETHOD>
                    <NATIVEMETHOD>AMOUNT</NATIVEMETHOD>
                    <NATIVEMETHOD>NARRATION</NATIVEMETHOD>
                    <NATIVEMETHOD>GUID</NATIVEMETHOD>
                    </COLLECTION>
                </TDLMESSAGE>
                </TDL>
            </DESC>
            </BODY>
            </ENVELOPE>";
        }

        private async Task SyncOutstandingsSmartAsync(CompanyProfile profile)
        {
            OnStatusChanged?.Invoke("Syncing Outstanding Bills...");
            await ExecuteOutstandingSyncRangeAsync(profile);

            profile.LastOutstandingSyncAt = DateTime.Now;
            LocalConfig.Save(_config);
        }

        private async Task ExecuteOutstandingSyncRangeAsync(CompanyProfile profile)
        {
            string tallyUrl = $"http://{_config.TallyHost}:{_config.TallyPort}";

            // Outstanding Bills have no AlterId, so we pass 0
            string envelope = TallyEnvelopeFactory.Build(TallyDataType.BillOutstanding, _config.TallyCompanyName, 0, null);

            var tallyContent = new StringContent(envelope, System.Text.Encoding.UTF8, "text/xml");
            var tallyResponse = await _tallyClient.PostAsync(tallyUrl, tallyContent);
            tallyResponse.EnsureSuccessStatusCode();
            string rawXml = await tallyResponse.Content.ReadAsStringAsync();

            // ─── INJECT THIS DIAGNOSTIC DUMP Debugging Only 
            //try
            //{
            //    string debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Tally_RAW_BillOutstanding.txt");
            //    System.IO.File.WriteAllText(debugPath, rawXml);
            //    System.Diagnostics.Debug.WriteLine($"[Connector Debug] Dumped raw Outstanding Bills XML to: {debugPath}");
            //}
            //catch (Exception ex)
            //{
            //    System.Diagnostics.Debug.WriteLine($"[Connector Debug] Failed writing debug file: {ex.Message}");
            //}
            // ───────────────────────────────────

            var payload = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                DataType = "BillOutstandings",
                RawXml = rawXml
            });

            using (var req = new HttpRequestMessage(HttpMethod.Post, _apiBase.TrimEnd('/') + "/api/tally/sync"))
            {
                req.Headers.Add("X-Sync-Token", profile.SyncToken);
                req.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var apiResp = await _apiClient.SendAsync(req);
                if (!apiResp.IsSuccessStatusCode)
                {
                    string body = await apiResp.Content.ReadAsStringAsync();
                    throw new Exception($"API Error {(int)apiResp.StatusCode}: {body}");
                }
            }
        }

        private string BuildOutstandingDateRangeEnvelope(string fromDate, string toDate)
        {
            string companyTag = string.IsNullOrWhiteSpace(_config.TallyCompanyName)
                ? ""
                : $"<SVCURRENTCOMPANY>{System.Security.SecurityElement.Escape(_config.TallyCompanyName)}</SVCURRENTCOMPANY>";

            return $@"<ENVELOPE>
                <HEADER>
                <VERSION>1</VERSION>
                <TALLYREQUEST>Export</TALLYREQUEST>
                <TYPE>Collection</TYPE>
                <ID>MyOutstandingCollection</ID>
                </HEADER>
                <BODY>
                <DESC>
                    <STATICVARIABLES>
                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                    <SVFROMDATE TYPE=""Date"">{fromDate}</SVFROMDATE>
                    <SVTODATE TYPE=""Date"">{toDate}</SVTODATE>
                    {companyTag}
                    </STATICVARIABLES>
                    <TDL>
                    <TDLMESSAGE>
                        <COLLECTION NAME=""MyOutstandingCollection"">
                        <TYPE>Ledger</TYPE>
                        <FILTER>IsADebtors</FILTER>
                        <NATIVEMETHOD>NAME</NATIVEMETHOD>
                        <NATIVEMETHOD>CLOSINGBALANCE</NATIVEMETHOD>
                        <NATIVEMETHOD>BILLDETAILS.LIST</NATIVEMETHOD>
                        </COLLECTION>
                        <SYSTEM TYPE=""Formulae"" NAME=""IsADebtors"">
                        $$IsDebtors:$PARENT
                        </SYSTEM>
                    </TDLMESSAGE>
                    </TDL>
                </DESC>
                </BODY>
            </ENVELOPE>";
        }

        private void LogError(string dataType, Exception ex)
        {
            string logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "InsidashTallyConnector"
            );

            try { System.IO.Directory.CreateDirectory(logDir); } catch { }

            string logPath = System.IO.Path.Combine(logDir, "sync-errors.log");
            try
            {
                System.IO.File.AppendAllText(
                    logPath,
                    $"[{DateTime.Now}] {dataType}\r\n{ex}\r\n\r\n"
                );
            }
            catch { }
        }

        public async Task<string> CheckForManualSyncRequestAsync()
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, _apiBase.TrimEnd('/') + "/api/connector/check-sync-request"))
                {
                    req.Headers.Add("X-Sync-Token", GetActiveSyncToken());
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
            catch { }
            return null;
        }

        public async Task MarkManualSyncProcessedAsync(string requestId)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { RequestID = requestId });
                using (var req = new HttpRequestMessage(HttpMethod.Post, _apiBase.TrimEnd('/') + "/api/connector/mark-sync-processed"))
                {
                    req.Headers.Add("X-Sync-Token", GetActiveSyncToken());
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    var resp = await _apiClient.SendAsync(req);
                    resp.EnsureSuccessStatusCode();
                }
            }
            catch { }
        }

        public async Task<List<string>> GetTallyCompanyNamesAsync()
        {
            string tallyUrl = $"http://{_config.TallyHost}:{_config.TallyPort}";
            string envelope = TallyEnvelopeFactory.BuildCompanyListEnvelope();

            var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            var response = await _tallyClient.PostAsync(tallyUrl, content);
            response.EnsureSuccessStatusCode();
            string rawXml = await response.Content.ReadAsStringAsync();

            var names = new List<string>();
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(SanitizeXmlForParsing(rawXml));
                foreach (var company in doc.Descendants("COMPANY"))
                {
                    string name = (string)company.Attribute("NAME")
                                ?? (string)company.Element("NAME");
                    if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                        names.Add(name);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to parse company list from Tally: " + ex.Message);
            }

            return names;
        }

        private async Task<string> DiscoverCompanyStartDateAsync()
        {
            try
            {
                string tallyUrl = $"http://{_config.TallyHost}:{_config.TallyPort}";
                string envelope = TallyEnvelopeFactory.Build(TallyDataType.CompanyMetadata, _config.TallyCompanyName);

                var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
                var response = await _tallyClient.PostAsync(tallyUrl, content);
                response.EnsureSuccessStatusCode();
                string rawXml = await response.Content.ReadAsStringAsync();

                var doc = System.Xml.Linq.XDocument.Parse(SanitizeXmlForParsing(rawXml));
                
                // Tally TDL returns native elements inside the collection payload
                var startingFromElement = doc.Descendants("STARTINGFROM").GetEnumerator();
                if (startingFromElement.MoveNext())
                {
                    string dateVal = startingFromElement.Current.Value?.Trim();
                    // Validate it's an 8-digit string matching standard yyyyMMdd
                    if (!string.IsNullOrWhiteSpace(dateVal) && dateVal.Length == 8 && long.TryParse(dateVal, out _))
                    {
                        return dateVal;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Discovery", new Exception("Failed auto-discovering company start date from Tally", ex));
            }

            return null; // Fallback to let the caller handle defaults
        }

        private string SanitizeXmlForParsing(string xml)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                xml, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
        }

        private int GetMaxAlterIdFromXml(string rawXml)
        {
            if (string.IsNullOrWhiteSpace(rawXml)) return 0; // Guard against empty
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(SanitizeXmlForParsing(rawXml));
                int maxId = 0;
                foreach (var element in doc.Descendants("ALTERID"))
                {
                    if (int.TryParse(element.Value, out int currentId))
                    {
                        if (currentId > maxId) maxId = currentId;
                    }
                }
                return maxId;
            }
            catch { return 0; }
        }

        private string ExtractLatestDateFromChunk(string rawXml)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(SanitizeXmlForParsing(rawXml));
                DateTime maxDate = DateTime.MinValue;

                // Scan the <DATE> tags in the chunk
                foreach (var dateNode in doc.Descendants("DATE"))
                {
                    if (DateTime.TryParseExact(dateNode.Value.Trim(), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                    {
                        if (parsedDate > maxDate) maxDate = parsedDate;
                    }
                }

                if (maxDate != DateTime.MinValue)
                    return maxDate.ToString("MMM-yyyy"); // e.g., "Apr-2021"
            }
            catch { }

            return "Current Batch"; // Fallback if no dates found
        }
    }

    public class SyncResult
    {
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public List<string> SyncedTypes { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public bool IsFullSuccess => Errors.Count == 0;
    }
}