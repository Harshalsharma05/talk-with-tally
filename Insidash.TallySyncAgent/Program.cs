using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Insidash.TallySyncAgent
{
    internal static class Program
    {
        private static readonly HttpClient _tallyClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly HttpClient _apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly string _tallyUrl = ConfigurationManager.AppSettings["TallyBaseUrl"];
        private static readonly string _apiUrl = (ConfigurationManager.AppSettings["ApiBaseUrl"] ?? "").TrimEnd('/');
        private static readonly string _syncToken = ConfigurationManager.AppSettings["SyncToken"];
        private static readonly int _interval = int.Parse(ConfigurationManager.AppSettings["SyncIntervalMs"] ?? "300000");

        private static int Main(string[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TalkWithTally Sync Agent started.");

            if (args != null && args.Length > 0 && args[0].Equals("--test-db", StringComparison.OrdinalIgnoreCase))
            {
                return TestDatabaseConnection();
            }

            if (args != null && args.Length > 0 && args[0].Equals("--test-sync", StringComparison.OrdinalIgnoreCase))
            {
                return TestSyncPost();
            }

            // Normal agent startup — continuous sync loop
            _apiClient.DefaultRequestHeaders.Add("X-Sync-Token", _syncToken);
            RunAsync().GetAwaiter().GetResult();
            return 0;
        }

        private static async Task RunAsync()
        {
            int companyId = GetCompanyIdByToken(_syncToken);
            if (companyId == -1)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: Could not resolve CompanyID for SyncToken. Defaulting to CompanyID = 5.");
                companyId = 5;
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Resolved CompanyID = {companyId} from SyncToken.");
            }

            while (true)
            {
                // Run a full sync cycle
                await RunFullSyncCycle(companyId);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cycle complete. Next sync in {_interval / 60000} min (or on-demand).");

                int elapsed = 0;
                bool manualRequested = false;
                while (elapsed < _interval)
                {
                    await Task.Delay(5000);
                    elapsed += 5000;
                    if (CheckForSyncRequest(companyId))
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] On-demand sync request detected!");
                        manualRequested = true;
                        break;
                    }
                }

                if (manualRequested)
                {
                    MarkSyncRequestProcessed(companyId);
                }
            }
        }

        private static async Task RunFullSyncCycle(int companyId)
        {
            // Ledgers (Full replacement)
            await SyncDataType(companyId, "Ledgers", BuildLedgerXml());

            // Vouchers (Incremental)
            DateTime lastVoucherSync = GetLastSyncedAt(companyId, "Voucher");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Incremental Voucher sync starting from: {lastVoucherSync:yyyy-MM-dd HH:mm:ss}");
            await SyncDataType(companyId, "Vouchers", BuildVoucherXml(lastVoucherSync));

            // StockItems (Full replacement)
            await SyncDataType(companyId, "StockItems", BuildStockItemXml());

            // BillOutstandings (Full replacement)
            await SyncDataType(companyId, "BillOutstandings", BuildBillOutstandingXml());
        }

        private static async Task SyncDataType(int companyId, string dataType, string xmlEnvelope)
        {
            string dbDataType;
            if (dataType == "Vouchers") dbDataType = "Voucher";
            else if (dataType == "StockItems") dbDataType = "StockItem";
            else if (dataType == "BillOutstandings") dbDataType = "BillOutstanding";
            else dbDataType = "Ledger";
            try
            {
                // Step 1: Pull from Tally
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Pulling {dataType} from Tally at {_tallyUrl}...");
                var tallyResponse = await _tallyClient.PostAsync(
                    _tallyUrl,
                    new StringContent(xmlEnvelope, Encoding.UTF8, "text/xml"));

                if (!tallyResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tally error for {dataType}: {tallyResponse.StatusCode}");
                    UpdateSyncState(companyId, dbDataType, 0, "TallyError");
                    return;
                }

                string rawXml = await tallyResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Got {rawXml.Length} chars from Tally for {dataType}. Pushing to API...");

                // Step 2: Push to API
                var payload = new { DataType = dataType, RawXml = rawXml };
                var apiResponse = await _apiClient.PostAsync(
                    $"{_apiUrl}/api/tally/sync",
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

                string apiResult = await apiResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {dataType} sync HTTP {(int)apiResponse.StatusCode}: {apiResult}");

                if (apiResponse.IsSuccessStatusCode)
                {
                    var resData = JsonConvert.DeserializeObject<SyncResponseDto>(apiResult);
                    int processed = resData != null ? resData.recordsProcessed : 0;
                    UpdateSyncState(companyId, dbDataType, processed, "Success");
                }
                else
                {
                    UpdateSyncState(companyId, dbDataType, 0, "ApiError");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error syncing {dataType}: {ex.Message}");
                UpdateSyncState(companyId, dbDataType, 0, "Error");
            }
        }

        private static string BuildLedgerXml()
        {
            return @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>Ledger</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";
        }

        private static string BuildVoucherXml(DateTime fromDate)
        {
            string fromStr = fromDate.ToString("yyyyMMdd");
            string toStr   = DateTime.Now.ToString("yyyyMMdd");

            return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Data</TYPE>
    <ID>Day Book</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        <SVFROMDATE>{fromStr}</SVFROMDATE>
        <SVTODATE>{toStr}</SVTODATE>
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";
        }

        private static string BuildStockItemXml()
{
    return @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>StockItem</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>

      <FETCHLIST>
        <FETCH>Name</FETCH>
        <FETCH>Parent</FETCH>
        <FETCH>BaseUnits</FETCH>
        <FETCH>ClosingBalance</FETCH>
        <FETCH>ClosingValue</FETCH>
      </FETCHLIST>

    </DESC>
  </BODY>
</ENVELOPE>";
}

        private static string BuildBillOutstandingXml()
        {
            return @"<ENVELOPE>
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

        private static bool CheckForSyncRequest(int companyId)
        {
            try
            {
                string connStr = ConfigurationManager.ConnectionStrings["InsidashTallyDb"].ConnectionString;
                using (var conn = new SqlConnection(connStr))
                using (var cmd = new SqlCommand(@"
                    SELECT COUNT(*) FROM TallySyncRequest
                    WHERE CompanyID = @cid AND IsProcessed = 0", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", companyId);
                    conn.Open();
                    int count = (int)cmd.ExecuteScalar();
                    return count > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error checking for sync request: {ex.Message}");
                return false;
            }
        }

        private static void MarkSyncRequestProcessed(int companyId)
        {
            try
            {
                string connStr = ConfigurationManager.ConnectionStrings["InsidashTallyDb"].ConnectionString;
                using (var conn = new SqlConnection(connStr))
                using (var cmd = new SqlCommand(@"
                    UPDATE TallySyncRequest
                    SET IsProcessed = 1, ProcessedAt = GETDATE()
                    WHERE CompanyID = @cid AND IsProcessed = 0", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", companyId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error marking sync request processed: {ex.Message}");
            }
        }

        private static int GetCompanyIdByToken(string token)
        {
            string connStr = ConfigurationManager.ConnectionStrings["InsidashTallyDb"].ConnectionString;
            using (var conn = new SqlConnection(connStr))
            using (var cmd = new SqlCommand("SELECT CompanyID FROM TallyCompanyConfig WHERE SyncToken = @token AND IsActive = 1", conn))
            {
                cmd.Parameters.AddWithValue("@token", token);
                conn.Open();
                var res = cmd.ExecuteScalar();
                return res != null ? (int)res : -1;
            }
        }

        private static DateTime GetLastSyncedAt(int companyId, string dataType)
        {
            string connStr = ConfigurationManager.ConnectionStrings["InsidashTallyDb"].ConnectionString;
            using (var conn = new SqlConnection(connStr))
            using (var cmd = new SqlCommand(
                "SELECT LastSyncedAt FROM TallySyncState WHERE CompanyID=@cid AND DataType=@dt",
                conn))
            {
                cmd.Parameters.AddWithValue("@cid", companyId);
                cmd.Parameters.AddWithValue("@dt", dataType);
                conn.Open();
                var result = cmd.ExecuteScalar();
                return result == null ? new DateTime(2000, 1, 1) : (DateTime)result;
            }
        }

        private static void UpdateSyncState(int companyId, string dataType, int recordCount, string status = "Success")
        {
            string connStr = ConfigurationManager.ConnectionStrings["InsidashTallyDb"].ConnectionString;
            using (var conn = new SqlConnection(connStr))
            using (var cmd = new SqlCommand(@"
                MERGE TallySyncState AS target
                USING (SELECT @CompanyID AS CompanyID, @DataType AS DataType) AS source
                    ON target.CompanyID = source.CompanyID AND target.DataType = source.DataType
                WHEN MATCHED THEN
                    UPDATE SET LastSyncedAt = GETDATE(), LastSyncStatus = @Status,
                               RecordsSynced = @RecordCount, UpdatedAt = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT (CompanyID, DataType, LastSyncedAt, LastSyncStatus, RecordsSynced)
                    VALUES (@CompanyID, @DataType, GETDATE(), @Status, @RecordCount);
            ", conn))
            {
                cmd.Parameters.AddWithValue("@CompanyID", companyId);
                cmd.Parameters.AddWithValue("@DataType", dataType);
                cmd.Parameters.AddWithValue("@RecordCount", recordCount);
                cmd.Parameters.AddWithValue("@Status", status);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        internal class SyncResponseDto
        {
            public string status { get; set; }
            public int companyId { get; set; }
            public string dataType { get; set; }
            public int recordsProcessed { get; set; }
        }

        // ---- Existing test harnesses below ----

        private static int TestSyncPost()
        {
            try
            {
                string apiBaseUrl = ConfigurationManager.AppSettings["ApiBaseUrl"];
                string syncToken = ConfigurationManager.AppSettings["SyncToken"];

                if (string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    Console.WriteLine("AppSetting 'ApiBaseUrl' is missing in App.config.");
                    return 5;
                }

                if (string.IsNullOrWhiteSpace(syncToken))
                {
                    Console.WriteLine("AppSetting 'SyncToken' is missing in App.config.");
                    return 6;
                }

                apiBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
                string url = apiBaseUrl + "/api/tally/sync";

                const string sampleXml = "<ENVELOPE><BODY><DATA><TALLYMESSAGE><LEDGER NAME=\"Cash\"><PARENT>Cash-in-hand</PARENT><CLOSINGBALANCE>50000.00 Dr</CLOSINGBALANCE></LEDGER></TALLYMESSAGE></DATA></BODY></ENVELOPE>";
                string payloadJson = "{\"DataType\":\"Ledgers\",\"RawXml\":\"" + EscapeJson(sampleXml) + "\"}";

                Console.WriteLine("POST " + url);
                Console.WriteLine("Using X-Sync-Token from App.config.");

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("X-Sync-Token", syncToken);

                    using (var content = new StringContent(payloadJson, Encoding.UTF8, "application/json"))
                    {
                        var response = client.PostAsync(url, content).GetAwaiter().GetResult();
                        string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                        Console.WriteLine("HTTP Status: " + (int)response.StatusCode + " " + response.StatusCode);
                        Console.WriteLine("Response Body: " + responseBody);

                        return response.IsSuccessStatusCode ? 0 : 7;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while testing sync POST: " + ex.Message);
                return 8;
            }
        }

        private static string EscapeJson(string value)
        {
            if (value == null) return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static int TestDatabaseConnection()
        {
            try
            {
                var csSetting = ConfigurationManager.ConnectionStrings["InsidashTallyDb"];
                if (csSetting == null || string.IsNullOrWhiteSpace(csSetting.ConnectionString))
                {
                    Console.WriteLine("Connection string 'InsidashTallyDb' not found in App.config.");
                    return 2;
                }

                var connString = csSetting.ConnectionString;
                using (var conn = new SqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM dbo.TallySnapshot;";
                        var result = cmd.ExecuteScalar();
                        Console.WriteLine($"Connection OK. TallySnapshot row count: {result}");
                    }
                    conn.Close();
                }

                return 0;
            }
            catch (SqlException ex)
            {
                Console.WriteLine("SQL Exception: " + ex.Message);
                return 3;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return 4;
            }
        }
    }
}
