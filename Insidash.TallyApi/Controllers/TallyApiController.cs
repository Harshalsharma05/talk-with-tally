using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Insidash.BLL.Parsers;
using Insidash.BLL.Services;
using Insidash.DAL.Context;
using Insidash.DAL.Entities;
using Insidash.DAL.Repositories;
using Insidash.TallyApi.Filters;

namespace Insidash.TallyApi.Controllers
{
    [RoutePrefix("api/tally")]
    public class TallyApiController : ApiController
    {
        private readonly TallyXmlParser _parser = new TallyXmlParser();
        private readonly TallySnapshotRepository _snapshotRepository = new TallySnapshotRepository();
        private readonly AIChatLogRepository _chatLogRepository = new AIChatLogRepository();
        private readonly TallyRelationalRepository _relationalRepo = new TallyRelationalRepository();

        [HttpPost]
        [Route("sync")]
        [SyncTokenAuthFilter]
        public IHttpActionResult Sync([FromBody] SyncPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.RawXml))
            {
                return BadRequest("Payload data or RawXml is missing.");
            }

            string token = Request.Headers.GetValues("X-Sync-Token").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                return Unauthorized();
            }

            int companyId;
            using (var db = new InsidashTallyContext())
            {
                var company = db.TallyCompanyConfigs
                    .Where(c => c.SyncToken == token && c.IsActive)
                    .Select(c => new { c.CompanyID })
                    .FirstOrDefault();

                if (company == null)
                {
                    return Unauthorized();
                }

                companyId = company.CompanyID;
            }

            if (string.Equals(payload.DataType, "vouchers", StringComparison.OrdinalIgnoreCase))
            {
                try { System.IO.File.WriteAllText(@"c:\AAA_STUDY\Internship_Tasks\TalkWithTally\vouchers_raw.xml", payload.RawXml); } catch { }

                // 1. Parse raw XML to our extended DTOs (which now extract nested item blocks)
                var rawDtos = _parser.ParseVouchersToDto(payload.RawXml);

                // 2. Map DTO models over to the parent domain entities while structuralizing child lists
                var dbVouchers = rawDtos.Select(dto => new TallyVoucher
                {
                    VoucherID = dto.VoucherID,
                    Date = dto.Date,
                    VchType = dto.VchType,
                    PartyName = dto.PartyName,
                    Amount = dto.Amount,
                    Narration = dto.Narration,

                    // Map child inventory line items over to the parent TallyVoucher entity
                    InventoryItems = dto.InventoryItems != null
                        ? dto.InventoryItems.Select(itemDto => new TallyVoucherInventoryItem
                        {
                            VoucherID = itemDto.VoucherID,
                            StockItemName = itemDto.StockItemName,
                            Quantity = itemDto.Quantity,
                            Rate = itemDto.Rate,
                            Amount = itemDto.Amount
                        }).ToList()
                        : new List<TallyVoucherInventoryItem>(),

                    // ── NEW STEP: Map child ledger entries over to the parent TallyVoucher entity ──
                    LedgerEntries = dto.LedgerEntries != null
                        ? dto.LedgerEntries.Select(ledgerDto => new TallyVoucherLedgerItem
                        {
                            LedgerName = ledgerDto.LedgerName,
                            Amount = ledgerDto.Amount,
                            IsDeemedPositive = ledgerDto.IsDeemedPositive
                        }).ToList()
                        : new List<TallyVoucherLedgerItem>()
                }).ToList();

                // 3. Pass the structural records downstream (The updated repo will process both headers and lines atomically)
                _relationalRepo.SaveVouchers(companyId, dbVouchers);

                UpsertSyncState(companyId, "Vouchers", dbVouchers.Count);

                return Ok(new
                {
                    status = "synced",
                    companyId,
                    dataType = "Vouchers",
                    recordsProcessed = dbVouchers.Count
                });
            }
            else if (string.Equals(payload.DataType, "stockitems", StringComparison.OrdinalIgnoreCase))
            {
                try { System.IO.File.WriteAllText(@"c:\AAA_STUDY\Internship_Tasks\TalkWithTally\stock_items_raw.xml", payload.RawXml); } catch { }
                var rawDtos = _parser.ParseStockItemsToDto(payload.RawXml);
                var dbItems = rawDtos.Select(dto => new TallyStockItem
                {
                    Name = dto.Name,
                    Parent = dto.Parent,
                    Unit = dto.Unit,
                    ClosingQty = dto.ClosingQty,
                    ClosingValue = dto.ClosingValue
                }).ToList();

                _relationalRepo.SaveStockItems(companyId, dbItems);
                UpsertSyncState(companyId, "StockItems", dbItems.Count);

                return Ok(new
                {
                    status = "synced",
                    companyId,
                    dataType = "StockItems",
                    recordsProcessed = dbItems.Count
                });
            }
            else if (string.Equals(payload.DataType, "billoutstandings", StringComparison.OrdinalIgnoreCase))
            {
                try { System.IO.File.WriteAllText(@"c:\AAA_STUDY\Internship_Tasks\TalkWithTally\bill_outstandings_raw.xml", payload.RawXml); } catch { }
                var rawDtos = _parser.ParseBillOutstandingsToDto(payload.RawXml);
                var dbBills = rawDtos.Select(dto => new TallyBillOutstanding
                {
                    PartyName = dto.PartyName,
                    BillDate = dto.BillDate,
                    BillRef = dto.BillRef,
                    Amount = dto.Amount,
                    DueDate = dto.DueDate
                }).ToList();

                _relationalRepo.SaveBillOutstandings(companyId, dbBills);
                UpsertSyncState(companyId, "BillOutstandings", dbBills.Count);

                return Ok(new
                {
                    status = "synced",
                    companyId,
                    dataType = "BillOutstandings",
                    recordsProcessed = dbBills.Count
                });
            }
            else if (string.Equals(payload.DataType, "groups", StringComparison.OrdinalIgnoreCase))
            {
                var rawDtos = _parser.ParseGroupsToDto(payload.RawXml);
                var dbGroups = rawDtos.Select(dto => new TallyGroup
                {
                    CompanyID = companyId,
                    Name = dto.Name,
                    Parent = dto.Parent,
                    SyncedAt = DateTime.Now
                }).ToList();

                _relationalRepo.SaveGroups(companyId, dbGroups);
                UpsertSyncState(companyId, "Groups", dbGroups.Count);

                return Ok(new
                {
                    status = "synced",
                    companyId,
                    dataType = "Groups",
                    recordsProcessed = dbGroups.Count
                });
            }
            else
            {
                var rawDtos = _parser.ParseLedgersToDto(payload.RawXml);
                var dbLedgers = rawDtos.Select(dto => new TallyLedger
                {
                    Name = dto.Name,
                    Parent = dto.Parent,
                    ClosingBalance = dto.ClosingBalance
                }).ToList();

                _relationalRepo.SaveLedgers(companyId, dbLedgers);
                UpsertSyncState(companyId, "Ledgers", dbLedgers.Count);

                return Ok(new
                {
                    status = "synced",
                    companyId,
                    dataType = "Ledgers",
                    recordsProcessed = dbLedgers.Count
                });
            }
        }

        [HttpPost]
        [Route("chat")]
        public async Task<IHttpActionResult> Chat([FromBody] ChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("Message is required.");
            }

            if (!ValidateCompanyAccess(request.CompanyId))
            {
                return Unauthorized();
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // ── UPDATED: Map directly to the new TallyAIChatLog entity ──
            var log = new TallyAIChatLog
            {
                CompanyID = request.CompanyId,
                UserQuestion = request.Message,
                CreatedDate = DateTime.Now,
                IsSuccess = false,
                ResponseType = "AI_Exception"
            };

            try
            {
                // 1. Initialize AI Service (from Web.config)
                string apiKey = ConfigurationManager.AppSettings["GROQ_API_KEY"];
                if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PUT_GROQ_KEY_HERE"))
                {
                    // Fall back to claude key if needed
                    apiKey = ConfigurationManager.AppSettings["CLAUDE_API_KEY"];
                }

                var aiService = new GroqAIService(apiKey);
                var nl2sql = new Nl2SqlService(aiService);
                var router = new IntentRouterService();

                string generatedSql;
                string templateName;
                string matchedTemplateSql = router.TryMatchTemplate(request.Message, out templateName);

                if (matchedTemplateSql != null)
                {
                    // Use pre-written SQL template
                    generatedSql = matchedTemplateSql.Replace("@CompanyID", request.CompanyId.ToString());
                    log.ResponseType = "Fast";
                }
                else
                {
                    // Generate SQL from user question
                    generatedSql = await nl2sql.GenerateSqlAsync(request.Message, request.CompanyId);
                }

                log.SQLQuery = generatedSql;

                // --- Safety check ---
                if (!nl2sql.IsSafeSelect(generatedSql))
                {
                    log.ResponseType = "AI_UnsafeSQL";
                    log.AIResponse = "⚠️ Unable to process safely.";
                    log.ErrorMessage = "Blocked: non-SELECT or missing CompanyID filter";
                    return Ok(new
                    {
                        response = log.AIResponse,
                        sql = generatedSql,
                        success = false
                    });
                }

                // 3. Execute SQL query dynamically
                string sqlResultJson = _relationalRepo.ExecuteQueryToDynamicJson(generatedSql);

                if (string.IsNullOrWhiteSpace(sqlResultJson) || sqlResultJson.Trim() == "[]")
                {
                    string smartMsg = GetSmartNoDataMessage(request.CompanyId, request.Message);
                    log.AIResponse = smartMsg;
                    log.ResponseType = "AI_NoData";
                    log.IsSuccess = true;

                    return Ok(new
                    {
                        response = smartMsg,
                        sql = generatedSql,
                        success = true
                    });
                }

                // 4. Formulate Natural Language Answer using query results
                string finalResponse = await nl2sql.FormulateResponseAsync(request.Message, generatedSql, sqlResultJson);
                log.AIResponse = finalResponse;
                if (log.ResponseType != "Fast")
                {
                    log.ResponseType = "AI_Success";
                }
                log.IsSuccess = true;

                return Ok(new
                {
                    response = finalResponse,
                    sql = generatedSql,
                    success = true
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("AI_NoSQL: "))
            {
                string aiText = ex.Message.Substring("AI_NoSQL: ".Length);
                log.ResponseType = "AI_NoSQL";
                log.AIResponse = "🤔 I couldn't understand that as a financial query. Try asking like:\n" +
                                 "• \"Show pending payments\"\n" +
                                 "• \"What are today's sales?\"\n" +
                                 "• \"Total sales for December 2025\"\n" +
                                 "• \"Show me top customers by revenue\"";
                log.ErrorMessage = "Conversational response returned by AI: " + aiText;
                log.IsSuccess = true;

                return Ok(new
                {
                    response = log.AIResponse,
                    success = true
                });
            }
            catch (System.Data.SqlClient.SqlException ex) when (ex.Number == -2)
            {
                log.ResponseType = "AI_Timeout";
                log.AIResponse = "⏱️ Query took too long. Try asking for a smaller date range.";
                log.ErrorMessage = "SQL execution timeout (>15s): " + ex.Message;
                log.IsSuccess = false;

                return Ok(new
                {
                    response = log.AIResponse,
                    success = false
                });
            }
            catch (Exception ex)
            {
                log.ErrorMessage = ex.Message;
                log.AIResponse = "An error occurred while processing your request.";
                return InternalServerError(ex);
            }
            finally
            {
                stopwatch.Stop();
                log.ExecutionTime = (int)stopwatch.ElapsedMilliseconds;

                // ── Saves automatically to the new TallyAIChatLog table ──
                _chatLogRepository.InsertLog(log);
            }
        }

        [HttpGet]
        [Route("history/{companyId:int}")]
        public IHttpActionResult History(int companyId, int page = 1, int pageSize = 20)
        {
            if (!ValidateCompanyAccess(companyId))
            {
                return Unauthorized();
            }

            // ── Fetches automatically from the new TallyAIChatLog table ──
            var logs = _chatLogRepository.GetByCompany(companyId, page, pageSize);
            return Ok(logs);
        }

        [HttpGet]
        [Route("status/{companyId:int}")]
        public IHttpActionResult Status(int companyId)
        {
            if (!ValidateCompanyAccess(companyId))
            {
                return Unauthorized();
            }

            // Fetch sync stats using DbContext directly for speed
            using (var ctx = new InsidashTallyContext())
            {
                var ledgerCount = ctx.TallyLedgers.Count(l => l.CompanyID == companyId);
                var ledgerLatest = ctx.TallyLedgers.Where(l => l.CompanyID == companyId)
                    .Select(l => (DateTime?)l.SyncedAt).FirstOrDefault();

                var voucherCount = ctx.TallyVouchers.Count(v => v.CompanyID == companyId);
                var voucherLatest = ctx.TallyVouchers.Where(v => v.CompanyID == companyId)
                    .Select(v => (DateTime?)v.SyncedAt).FirstOrDefault();

                return Ok(new
                {
                    ledgersSyncedAt = ledgerLatest,
                    ledgerCount = ledgerCount,
                    vouchersSyncedAt = voucherLatest,
                    voucherCount = voucherCount
                });
            }
        }

        [HttpGet]
        [Route("sync-status")]
        public IHttpActionResult GetSyncStatus()
        {
            int companyId = GetAuthenticatedCompanyId();

            using (var db = new InsidashTallyContext())
            {
                var snapshots = db.TallySyncStates
                    .Where(s => s.CompanyID == companyId)
                    .OrderByDescending(s => s.LastSyncedAt)
                    .ToList();

                bool isActivated = db.TallyActivationKeys
                    .Any(k => k.CompanyID == companyId && k.IsActivated && k.IsActive);

                if (!isActivated || !snapshots.Any())
                {
                    return Ok(new
                    {
                        status = "not_connected",
                        isActivated = isActivated,
                        lastSyncedAt = (string)null,
                        totalLedgers = 0,
                        totalVouchers = 0,
                        downloadUrl = "https://your-server.com/downloads/InsidashTallyConnector_Setup.exe"
                    });
                }

                var lastSync = snapshots.First();
                int ledgerCount = db.TallyLedgers.Count(l => l.CompanyID == companyId);
                int voucherCount = db.TallyVouchers.Count(v => v.CompanyID == companyId);

                return Ok(new
                {
                    status = "connected",
                    isActivated = true,
                    lastSyncedAt = lastSync.LastSyncedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                    totalLedgers = ledgerCount,
                    totalVouchers = voucherCount,
                    syncStatus = lastSync.LastSyncStatus
                });
            }
        }

        [HttpPost]
        [Route("sync-now")]
        public IHttpActionResult RequestSync()
        {
            int companyId = GetAuthenticatedCompanyId();

            using (var db = new InsidashTallyContext())
            {
                // Clear stale requests so Sync Now is not blocked forever if the agent stopped
                db.Database.ExecuteSqlCommand(@"
                    UPDATE TallySyncRequest
                    SET IsProcessed = 1, ProcessedAt = GETDATE()
                    WHERE CompanyID = @p0 AND IsProcessed = 0
                      AND RequestedAt < DATEADD(minute, -10, GETDATE())", companyId);

                bool alreadyPending = db.Database.SqlQuery<int>(@"
                    SELECT COUNT(*) FROM TallySyncRequest
                    WHERE CompanyID = @p0 AND IsProcessed = 0", companyId).First() > 0;

                if (alreadyPending)
                    return Ok(new { queued = true, alreadyPending = true, message = "Sync already in progress." });

                db.Database.ExecuteSqlCommand(@"
                    INSERT INTO TallySyncRequest (RequestID, CompanyID, RequestedAt, IsProcessed)
                    VALUES (NEWID(), @p0, GETDATE(), 0)", companyId);

                return Ok(new { queued = true, message = "Sync request sent to connector." });
            }
        }

        [HttpGet]
        [Route("suggestions")]
        public IHttpActionResult GetSuggestions()
        {
            using (var db = new InsidashTallyContext())
            {
                var suggestions = db.Database.SqlQuery<SuggestionDto>(@"
                    SELECT s.SuggestionID, s.Text
                    FROM AISuggestion s
                    INNER JOIN AIDomain d ON d.DomainID = s.DomainID
                    WHERE d.Name = 'Tally' AND s.IsActive = 1
                    ORDER BY s.SuggestionID")
                    .ToList();

                return Ok(suggestions);
            }
        }

        [HttpGet]
        [Route("my-activation-key")]
        public IHttpActionResult GetOrCreateActivationKey()
        {
            int companyId = GetAuthenticatedCompanyId();

            using (var db = new InsidashTallyContext())
            {
                // Return existing key if one already exists for this company
                var existing = db.TallyActivationKeys
                    .FirstOrDefault(k => k.CompanyID == companyId && k.IsActive);

                if (existing != null)
                {
                    return Ok(new
                    {
                        activationKey = existing.ActivationKey,
                        isActivated = existing.IsActivated,
                        activatedAt = existing.ActivatedAt
                    });
                }

                // No key exists yet — auto-generate one via stored procedure
                var result = db.Database.SqlQuery<ActivationKeyResult>(
                    "EXEC sp_GenerateTallyActivationKey @p0", companyId)
                    .FirstOrDefault();

                if (result == null)
                    return InternalServerError(new Exception("Failed to generate activation key."));

                return Ok(new
                {
                    activationKey = result.ActivationKey,
                    isActivated = false,
                    activatedAt = (DateTime?)null
                });
            }
        }

        private void UpsertSyncState(int companyId, string apiDataType, int recordCount, string status = "Success")
        {
            string dataType;
            if (string.Equals(apiDataType, "Vouchers", StringComparison.OrdinalIgnoreCase))
                dataType = "Voucher";
            else if (string.Equals(apiDataType, "StockItems", StringComparison.OrdinalIgnoreCase))
                dataType = "StockItem";
            else if (string.Equals(apiDataType, "BillOutstandings", StringComparison.OrdinalIgnoreCase))
                dataType = "BillOutstanding";
            else
                dataType = "Ledger";

            using (var db = new InsidashTallyContext())
            {
                var state = db.TallySyncStates
                    .FirstOrDefault(s => s.CompanyID == companyId && s.DataType == dataType);

                if (state != null)
                {
                    state.LastSyncedAt = DateTime.Now;
                    state.LastSyncStatus = status;
                    state.RecordsSynced = recordCount;
                    state.UpdatedAt = DateTime.Now;
                }
                else
                {
                    db.TallySyncStates.Add(new TallySyncState
                    {
                        CompanyID = companyId,
                        DataType = dataType,
                        LastSyncedAt = DateTime.Now,
                        LastSyncStatus = status,
                        RecordsSynced = recordCount,
                        UpdatedAt = DateTime.Now
                    });
                }

                db.SaveChanges();
            }
        }

        private string GetSmartNoDataMessage(int companyId, string userQuestion)
        {
            using (var db = new InsidashTallyContext())
            {
                var lastSync = db.TallySyncStates
                    .Where(s => s.CompanyID == companyId && s.LastSyncStatus == "Success")
                    .Select(s => (DateTime?)s.LastSyncedAt)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                if (lastSync == null)
                    return "📡 Tally data hasn't been synced yet for your company. Please ensure the Tally Sync Agent is running.";

                if ((DateTime.Now - lastSync.Value).TotalMinutes > 30)
                    return $"📡 Last Tally sync was {(int)(DateTime.Now - lastSync.Value).TotalHours} hours ago. Data may be outdated. Ensure the Sync Agent is running.";
            }

            string fuzzySuggestion = TrySuggestSimilarName(userQuestion, companyId);
            if (fuzzySuggestion != null)
                return fuzzySuggestion;

            return "📭 No matching records found. Try adjusting your date range or search terms.";
        }

        private string TrySuggestSimilarName(string originalName, int companyId)
        {
            return null; // CRM Tables removed from Tally Scope
        }

        private bool IsCommonKeyword(string word)
        {
            var commons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "show", "list", "view", "find", "get", "what", "is", "our", "my", "the", "for", "payment",
                "receipt", "invoice", "sale", "sales", "bill", "bills", "ledger", "ledgers", "voucher", "vouchers",
                "customer", "customers", "product", "products", "item", "items", "pending", "outstanding",
                "amount", "date", "today", "yesterday", "monthly", "yearly", "total", "sum", "balance"
            };
            return commons.Contains(word);
        }

        private bool ValidateCompanyAccess(int requestedCompanyId)
        {
            if (!Request.Headers.Contains("X-Sync-Token"))
                return false;

            string token = Request.Headers.GetValues("X-Sync-Token").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
                return false;

            using (var db = new InsidashTallyContext())
            {
                var company = db.TallyCompanyConfigs
                    .Where(c => c.SyncToken == token && c.IsActive)
                    .Select(c => new { c.CompanyID })
                    .FirstOrDefault();

                if (company == null)
                    return false;

                return company.CompanyID == requestedCompanyId;
            }
        }

        private int GetAuthenticatedCompanyId()
        {
            var sessionCompanyId = System.Web.HttpContext.Current?.Session?["CompanyID"];
            if (sessionCompanyId != null && int.TryParse(sessionCompanyId.ToString(), out int cid))
            {
                return cid;
            }

            var queryParams = Request.GetQueryNameValuePairs();
            var cidParam = queryParams.FirstOrDefault(q => string.Equals(q.Key, "companyId", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(cidParam.Value) && int.TryParse(cidParam.Value, out int paramCid))
            {
                return paramCid;
            }

            if (Request.Headers.Contains("X-Company-ID"))
            {
                var headerVal = Request.Headers.GetValues("X-Company-ID").FirstOrDefault();
                if (int.TryParse(headerVal, out int headerCid))
                {
                    return headerCid;
                }
            }

            if (Request.Headers.Contains("X-Sync-Token"))
            {
                string token = Request.Headers.GetValues("X-Sync-Token").FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    using (var db = new InsidashTallyContext())
                    {
                        int tokenCompanyId = db.TallyCompanyConfigs
                            .Where(c => c.SyncToken == token && c.IsActive)
                            .Select(c => c.CompanyID)
                            .FirstOrDefault();

                        if (tokenCompanyId != 0)
                            return tokenCompanyId;
                    }
                }
            }

            return 10892; // for debugging and testing only, comment this in production
        }
    }

    public class SuggestionDto
    {
        public int SuggestionID { get; set; }
        public string Text { get; set; }
    }

    public class SyncPayload
    {
        public string DataType { get; set; }
        public string RawXml { get; set; }
    }

    public class ChatRequest
    {
        public int CompanyId { get; set; }
        public string Message { get; set; }
    }

    public class ActivationKeyResult
    {
        public string ActivationKey { get; set; }
        public int CompanyID { get; set; }
    }
}