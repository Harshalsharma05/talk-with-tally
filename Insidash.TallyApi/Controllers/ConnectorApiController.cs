using System;
using System.Linq;
using System.Web.Http;
using Insidash.DAL.Context;
using Insidash.DAL.Entities;

namespace Insidash.TallyApi.Controllers
{
    [RoutePrefix("api/connector")]
    public class ConnectorApiController : ApiController
    {
        // ─── ENDPOINT 1: Activation ─────────────────────────────────────────────
        // Called once by the agent on first run.
        // Validates the activation key and returns the sync token + company config.

        [HttpPost, Route("activate")]
        public IHttpActionResult Activate([FromBody] ActivateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ActivationKey))
                return BadRequest("Activation key is required.");

            using (var db = new InsidashTallyContext())
            {
                var key = db.TallyActivationKeys
                    .FirstOrDefault(k =>
                        k.ActivationKey == request.ActivationKey.Trim().ToUpper()
                        && k.IsActive
                        && !k.IsActivated);

                if (key == null)
                    return Unauthorized(); // key not found, already used, or inactive

                // Check expiry if set
                if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.Now)
                    return Unauthorized(); // key expired

                // Mark as activated
                key.IsActivated  = true;
                key.ActivatedAt  = DateTime.Now;
                key.MachineID    = request.MachineID;
                key.AgentVersion = request.AgentVersion;
                db.SaveChanges();

                // Fetch company Tally config from the Company table
                // Company already has TallyPort, TallyHost, TallyUrl
                var company = db.Database
                    .SqlQuery<CompanyTallyConfig>(
                        "SELECT CompanyID, TallyHost, TallyPort, TallyUrl FROM Company WHERE CompanyID = @p0",
                        key.CompanyID)
                    .FirstOrDefault();

                return Ok(new
                {
                    syncToken      = key.SyncToken,
                    companyId      = key.CompanyID,
                    tallyHost      = company?.TallyHost ?? "localhost",
                    tallyPort      = company?.TallyPort ?? "9000",
                    syncIntervalMs = 300000  // 5 minutes, can be made per-company later
                });
            }
        }

        // ─── ENDPOINT 2: Version Check ───────────────────────────────────────────
        // Called by the agent on every startup to check if an update is available.
        // Reuses the existing PatchUpdate table.

        [HttpGet, Route("version")]
        public IHttpActionResult GetLatestVersion()
        {
            using (var db = new InsidashTallyContext())
            {
                var latest = db.Database
                    .SqlQuery<VersionInfo>(
                        "SELECT TOP 1 PatchUpdateNo AS Version, PatchUpdateDate AS ReleasedAt " +
                        "FROM PatchUpdate WHERE IsActive = '1' AND IsDelete = '0' " +
                        "ORDER BY PatchUpdateDate DESC")
                    .FirstOrDefault();

                if (latest == null)
                    return Ok(new { version = "1.0.0", downloadUrl = (string)null });

                return Ok(new
                {
                    version     = latest.Version,
                    releasedAt  = latest.ReleasedAt,
                    downloadUrl = $"https://your-cloud-server.com/downloads/InsidashTallyConnector_{latest.Version}.exe"
                });
            }
        }

        // ─── ENDPOINT 3: Check Sync Request ──────────────────────────────────────────
        // Called by the agent in its polling loop to check if the user clicked "Sync Now" in the UI.

        [HttpGet, Route("check-sync-request")]
        public IHttpActionResult CheckSyncRequest()
        {
            if (!Request.Headers.Contains("X-Sync-Token"))
                return BadRequest("Sync token is required.");

            string token = Request.Headers.GetValues("X-Sync-Token").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
                return Unauthorized();

            using (var db = new InsidashTallyContext())
            {
                var key = db.TallyActivationKeys
                    .FirstOrDefault(k => k.SyncToken == token && k.IsActive);

                if (key == null)
                    return Unauthorized();

                var pendingRequest = db.Database.SqlQuery<string>(@"
                    SELECT TOP 1 RequestID FROM TallySyncRequest
                    WHERE CompanyID = @p0 AND IsProcessed = 0
                    ORDER BY RequestedAt DESC", key.CompanyID).FirstOrDefault();

                if (pendingRequest != null)
                {
                    return Ok(new { syncRequested = true, requestId = pendingRequest });
                }

                return Ok(new { syncRequested = false });
            }
        }

        // ─── ENDPOINT 4: Mark Sync Processed ─────────────────────────────────────────
        // Called by the agent after it successfully runs the manual sync to clear the request.

        [HttpPost, Route("mark-sync-processed")]
        public IHttpActionResult MarkSyncProcessed([FromBody] MarkProcessedRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestID))
                return BadRequest("RequestID is required.");

            if (!Request.Headers.Contains("X-Sync-Token"))
                return BadRequest("Sync token is required.");

            string token = Request.Headers.GetValues("X-Sync-Token").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
                return Unauthorized();

            using (var db = new InsidashTallyContext())
            {
                var key = db.TallyActivationKeys
                    .FirstOrDefault(k => k.SyncToken == token && k.IsActive);

                if (key == null)
                    return Unauthorized();

                db.Database.ExecuteSqlCommand(@"
                    UPDATE TallySyncRequest
                    SET IsProcessed = 1, ProcessedAt = GETDATE()
                    WHERE RequestID = @p0 AND CompanyID = @p1",
                    request.RequestID, key.CompanyID);

                return Ok(new { success = true });
            }
        }
    }

    // ─── Request / Response DTOs ─────────────────────────────────────────────────

    public class MarkProcessedRequest
    {
        public string RequestID { get; set; }
    }

    public class ActivateRequest
    {
        public string ActivationKey { get; set; }
        public string MachineID     { get; set; }  // hardware fingerprint
        public string AgentVersion  { get; set; }  // e.g. "1.0.0"
    }

    public class CompanyTallyConfig
    {
        public int    CompanyID { get; set; }
        public string TallyHost { get; set; }
        public string TallyPort { get; set; }
        public string TallyUrl  { get; set; }
    }

    public class VersionInfo
    {
        public string   Version    { get; set; }
        public DateTime ReleasedAt { get; set; }
    }
}
