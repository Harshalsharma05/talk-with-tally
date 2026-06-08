# TalkWithTally — Natural Language to SQL (NL2SQL) Implementation Plan

Migrate the AI Chat feature from reading whole raw JSON snapshots to a structured, relational SQL database query model. Instead of truncating and sending thousands of raw JSON lines to the AI, the AI will translate the user's natural language question into a SQL query, execute it against the relational tables, and format the final answer using the query results.

---

## User Review Required

> [!IMPORTANT]
> **Database Host & Schema Control**: You must run the SQL scripts on your DB laptop (managing the `Popway_BillingERP` database) to create the new tables.
> **Security Configuration**: Executing raw AI-generated SQL is a potential security risk. We strongly recommend configuring a read-only database user (e.g., `tally_readonly`) that only has `SELECT` access to the Tally tables.
> **Sign Convention for Balances**: In accounting, Debits and Credits represent opposing balances. For simplicity in SQL aggregations (like `SUM`), we propose representing **Debit (Dr)** balances as **positive** numbers and **Credit (Cr)** balances as **negative** numbers.

---

## Open Questions

> [!WARNING]
> Do we want to drop/delete the existing `TallySnapshot` table? 
> **Recommendation**: Do not drop it. Keep it as an archive/audit trail of original XML payloads for troubleshooting, but do not use it for chat context.

---

## Proposed Changes

### Component 1 — SQL Server Database (to be executed on DB Laptop)

Create the relational tables `TallyLedger` and `TallyVoucher` in your database.

#### [NEW] `Database DDL Script` (Run in SSMS)
```sql
USE Popway_BillingERP;
GO

-- 1. Create TallyLedger table
CREATE TABLE TallyLedger (
    LedgerID       NVARCHAR(50)   NOT NULL PRIMARY KEY,
    CompanyID      INT            NOT NULL,             -- FK to Company
    Name           NVARCHAR(255)  NOT NULL,             -- Ledger Account Name (e.g., 'HDFC Bank')
    Parent         NVARCHAR(255)  NULL,                 -- Group Parent (e.g., 'Bank Accounts')
    ClosingBalance DECIMAL(18, 2) NOT NULL,             -- Debit (+) / Credit (-)
    SyncedAt       DATETIME       NOT NULL DEFAULT GETDATE()
);

-- Index on CompanyID for fast lookup
CREATE INDEX IX_TallyLedger_CompanyID ON TallyLedger (CompanyID);

-- 2. Create TallyVoucher table
CREATE TABLE TallyVoucher (
    VoucherID      NVARCHAR(50)   NOT NULL PRIMARY KEY,
    CompanyID      INT            NOT NULL,             -- FK to Company
    Date           DATE           NOT NULL,             -- Voucher Transaction Date
    VchType        NVARCHAR(100)  NOT NULL,             -- e.g., 'Receipt', 'Payment', 'Sales', 'Purchase'
    PartyName      NVARCHAR(255)  NULL,                 -- Account Involved
    Amount         DECIMAL(18, 2) NOT NULL,             -- Transaction Amount
    Narration      NVARCHAR(MAX)  NULL,                 -- Transaction description
    SyncedAt       DATETIME       NOT NULL DEFAULT GETDATE()
);

-- Indexes for filtering transactions
CREATE INDEX IX_TallyVoucher_CompanyID ON TallyVoucher (CompanyID);
CREATE INDEX IX_TallyVoucher_Date ON TallyVoucher (Date);
```

---

### Component 2 — DAL (Insidash.DAL)

Add the EF6 entity classes for the new tables and map them in the DbContext.

#### [NEW] [TallyLedger.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.DAL/Entities/TallyLedger.cs)
Create the Entity Framework model matching the `TallyLedger` table.
```csharp
using System;

namespace Insidash.DAL.Entities
{
    public class TallyLedger
    {
        public string LedgerID { get; set; }
        public int CompanyID { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public decimal ClosingBalance { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}
```

#### [NEW] [TallyVoucher.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.DAL/Entities/TallyVoucher.cs)
Create the Entity Framework model matching the `TallyVoucher` table.
```csharp
using System;

namespace Insidash.DAL.Entities
{
    public class TallyVoucher
    {
        public string VoucherID { get; set; }
        public int CompanyID { get; set; }
        public DateTime Date { get; set; }
        public string VchType { get; set; }
        public string PartyName { get; set; }
        public decimal Amount { get; set; }
        public string Narration { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}
```

#### [MODIFY] [InsidashTallyContext.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.DAL/Context/InsidashTallyContext.cs)
Register the new models and map them to their SQL Server tables.
```csharp
public DbSet<TallyLedger> TallyLedgers { get; set; }
public DbSet<TallyVoucher> TallyVouchers { get; set; }

protected override void OnModelCreating(DbModelBuilder modelBuilder)
{
    modelBuilder.Entity<TallyLedger>().ToTable("TallyLedger");
    modelBuilder.Entity<TallyVoucher>().ToTable("TallyVoucher");
    // Existing mappings ...
}
```

#### [NEW] [ITallyRelationalRepository.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.DAL/Repositories/ITallyRelationalRepository.cs)
Define methods for relational data upserts.
```csharp
using System.Collections.Generic;
using Insidash.DAL.Entities;

namespace Insidash.DAL.Repositories
{
    public interface ITallyRelationalRepository
    {
        void SaveLedgers(int companyId, List<TallyLedger> ledgers);
        void SaveVouchers(int companyId, List<TallyVoucher> vouchers);
        string ExecuteQueryToDynamicJson(string sqlQuery);
    }
}
```

#### [NEW] [TallyRelationalRepository.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.DAL/Repositories/TallyRelationalRepository.cs)
Implement repository database transaction operations.
* **SaveLedgers**: Clear existing company ledgers and bulk save incoming ledgers.
* **SaveVouchers**: Clear existing company vouchers and bulk save incoming vouchers.
* **ExecuteQueryToDynamicJson**: Execute the LLM-generated SQL query using ADO.NET and serialize results dynamically (supporting aggregates, lists, and tables) into a JSON string.
```csharp
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Insidash.DAL.Context;
using Insidash.DAL.Entities;
using Newtonsoft.Json;

namespace Insidash.DAL.Repositories
{
    public class TallyRelationalRepository : ITallyRelationalRepository
    {
        public void SaveLedgers(int companyId, List<TallyLedger> ledgers)
        {
            using (var ctx = new InsidashTallyContext())
            {
                using (var transaction = ctx.Database.BeginTransaction())
                {
                    try
                    {
                        // Delete old ledgers for this company
                        ctx.Database.ExecuteSqlCommand("DELETE FROM TallyLedger WHERE CompanyID = @p0", companyId);

                        // Insert new ledgers
                        foreach (var ledger in ledgers)
                        {
                            ledger.LedgerID = Guid.NewGuid().ToString();
                            ledger.CompanyID = companyId;
                            ledger.SyncedAt = DateTime.Now;
                            ctx.TallyLedgers.Add(ledger);
                        }

                        ctx.SaveChanges();
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public void SaveVouchers(int companyId, List<TallyVoucher> vouchers)
        {
            using (var ctx = new InsidashTallyContext())
            {
                using (var transaction = ctx.Database.BeginTransaction())
                {
                    try
                    {
                        // Delete old vouchers for this company
                        ctx.Database.ExecuteSqlCommand("DELETE FROM TallyVoucher WHERE CompanyID = @p0", companyId);

                        // Insert new vouchers
                        foreach (var voucher in vouchers)
                        {
                            voucher.VoucherID = Guid.NewGuid().ToString();
                            voucher.CompanyID = companyId;
                            voucher.SyncedAt = DateTime.Now;
                            ctx.TallyVouchers.Add(voucher);
                        }

                        ctx.SaveChanges();
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public string ExecuteQueryToDynamicJson(string sqlQuery)
        {
            using (var ctx = new InsidashTallyContext())
            {
                var connection = ctx.Database.Connection;
                if (connection.State != ConnectionState.Open)
                    connection.Open();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = sqlQuery;
                    using (var reader = cmd.ExecuteReader())
                    {
                        var dataTable = new DataTable();
                        dataTable.Load(reader);
                        return JsonConvert.SerializeObject(dataTable);
                    }
                }
            }
        }
    }
}
```

---

### Component 3 — BLL (Insidash.BLL)

Update the XML parser to return structured records, and add the NL2SQL AI coordination service.

#### [MODIFY] [TallyXmlParser.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.BLL/Parsers/TallyXmlParser.cs)
Modify parser outputs to support mapped domain objects and add currency/numeric balance parsing.
```csharp
// Helper method to parse Dr/Cr balances
public decimal ParseClosingBalance(string rawVal)
{
    if (string.IsNullOrWhiteSpace(rawVal)) return 0;
    string text = rawVal.Trim();

    bool isCredit = text.EndsWith("Cr", StringComparison.OrdinalIgnoreCase);
    bool isDebit = text.EndsWith("Dr", StringComparison.OrdinalIgnoreCase);

    string numberPart = text;
    if (isCredit || isDebit)
    {
        numberPart = text.Substring(0, text.Length - 2).Trim();
    }

    if (decimal.TryParse(numberPart, out decimal val))
    {
        // Debits represent positive, Credits represent negative
        return isCredit ? -val : val;
    }
    return 0;
}

// Helper method to parse transaction amounts
public decimal ParseAmount(string rawVal)
{
    if (string.IsNullOrWhiteSpace(rawVal)) return 0;
    string text = rawVal.Trim();
    
    // Remove negative prefix or formatting if present
    bool isNegative = text.StartsWith("-");
    if (isNegative) text = text.Substring(1).Trim();

    if (decimal.TryParse(text, out decimal val))
    {
        return isNegative ? -val : val;
    }
    return 0;
}
```

Change parser signatures to return rich lists:
```csharp
public List<TallyLedgerDto> ParseLedgersToDto(string rawXml)
{
    string cleanXml = SanitizeXml(rawXml);
    XDocument document = XDocument.Parse(cleanXml);
    return document.Descendants("LEDGER")
        .Select(l => new TallyLedgerDto
        {
            Name = (string)l.Attribute("NAME"),
            Parent = (string)l.Element("PARENT"),
            ClosingBalance = ParseClosingBalance((string)l.Element("CLOSINGBALANCE"))
        })
        .ToList();
}

public List<TallyVoucherDto> ParseVouchersToDto(string rawXml)
{
    string cleanXml = SanitizeXml(rawXml);
    XDocument document = XDocument.Parse(cleanXml);
    return document.Descendants("VOUCHER")
        .Select(v => new TallyVoucherDto
        {
            Date = DateTime.TryParse((string)v.Element("DATE"), out DateTime d) ? d : DateTime.Today,
            VchType = (string)v.Element("VOUCHERTYPENAME"),
            PartyName = (string)v.Element("PARTYNAME"),
            Amount = ParseAmount((string)v.Element("AMOUNT")),
            Narration = (string)v.Element("NARRATION")
        })
        .ToList();
}

public class TallyLedgerDto { public string Name { get; set; } public string Parent { get; set; } public decimal ClosingBalance { get; set; } }
public class TallyVoucherDto { public DateTime Date { get; set; } public string VchType { get; set; } public string PartyName { get; set; } public decimal Amount { get; set; } public string Narration { get; set; } }
```

#### [NEW] [Nl2SqlService.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.BLL/Services/Nl2SqlService.cs)
Create a service that orchestrates the Natural Language to SQL workflow:
1. Feeds the database schema (tables, columns, signs) + user question to the LLM.
2. Extracts raw SQL from the LLM response.
3. Executes it against SQL Server using `TallyRelationalRepository`.
4. Feeds the SQL + execution results back to the LLM to write the final conversational answer.
```csharp
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Insidash.BLL.Services
{
    public class Nl2SqlService
    {
        private readonly IAIService _aiService;

        public Nl2SqlService(IAIService aiService)
        {
            _aiService = aiService;
        }

        public async Task<string> GenerateSqlAsync(string userQuestion, int companyId)
        {
            string systemPrompt = $@"You are a translation assistant that converts natural language questions into raw MS SQL Server queries.
We have a SQL Server database 'Popway_BillingERP' with the following two tables:

1. Table 'TallyLedger':
   - LedgerID (NVARCHAR(50), PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - Name (NVARCHAR(255)) - Name of ledger account
   - Parent (NVARCHAR(255)) - Parent group name (e.g. Bank Accounts, Indirect Expenses)
   - ClosingBalance (DECIMAL(18,2)) - Positive indicates Debit (Dr), Negative indicates Credit (Cr)

2. Table 'TallyVoucher':
   - VoucherID (NVARCHAR(50), PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - Date (DATE) - Transaction date
   - VchType (NVARCHAR(100)) - Voucher type (e.g. Sales, Payment, Receipt)
   - PartyName (NVARCHAR(255)) - Associated ledger/party name
   - Amount (DECIMAL(18,2)) - Transaction Amount
   - Narration (NVARCHAR(MAX)) - Remarks

RULES:
- Respond ONLY with the executable SQL Query inside a markdown code block: ```sql <sql query here> ```. Do not add explanations.
- ALWAYS filter the queries by CompanyID = {companyId}.
- Be careful with the balance signs: Debit balances (positive) vs. Credit balances (negative).
- Keep queries simple, optimized, and read-only. Only use SELECT statements.";

            var result = await _aiService.ChatAsync(systemPrompt, userQuestion);
            if (!result.Success)
            {
                throw new Exception("LLM SQL Generation failed: " + result.ErrorMessage);
            }

            // Extract SQL query from code blocks
            var match = Regex.Match(result.Content, @"```sql\s*([\s\S]+?)\s*```", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : result.Content.Trim();
        }

        public async Task<string> FormulateResponseAsync(string userQuestion, string sqlQuery, string sqlResultJson)
        {
            string systemPrompt = @"You are TalkWithTally, an AI accounting assistant.
Answer the user's question by formatting the SQL query results into a clear, helpful, and natural financial response.
Always format currency values in INR (₹). Highlight details and summarize findings if multiple records are returned.";

            string userMessage = $@"User Question: {userQuestion}
Generated SQL Query: {sqlQuery}
Query Results (JSON): {sqlResultJson}";

            var result = await _aiService.ChatAsync(systemPrompt, userMessage);
            return result.Success ? result.Content : "Failed to generate financial answer: " + result.ErrorMessage;
        }
    }
}
```

---

### Component 4 — Presentation Layer (Insidash.TallyApi)

Modify the api controller actions to route sync payloads into relational tables and invoke the NL2SQL service during chats.

#### [MODIFY] [TallyApiController.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.TallyApi/Controllers/TallyApiController.cs)
Change the Sync and Chat controller endpoints.
```csharp
using Insidash.DAL.Entities;
// Add other references ...

private readonly TallyRelationalRepository _relationalRepo = new TallyRelationalRepository();

[HttpPost]
[Route("sync")]
[SyncTokenAuthFilter]
public IHttpActionResult Sync([FromBody] SyncPayload payload)
{
    // ... validation logic and resolve companyId ...

    if (string.Equals(payload.DataType, "vouchers", StringComparison.OrdinalIgnoreCase))
    {
        var rawDtos = _parser.ParseVouchersToDto(payload.RawXml);
        var dbVouchers = rawDtos.Select(dto => new TallyVoucher
        {
            Date = dto.Date,
            VchType = dto.VchType,
            PartyName = dto.PartyName,
            Amount = dto.Amount,
            Narration = dto.Narration
        }).ToList();
        
        _relationalRepo.SaveVouchers(companyId, dbVouchers);
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
    }

    return Ok(new { status = "synced", companyId, dataType = payload.DataType });
}

[HttpPost]
[Route("chat")]
public async Task<IHttpActionResult> Chat([FromBody] ChatRequest request)
{
    if (request == null || string.IsNullOrWhiteSpace(request.Message))
    {
        return BadRequest("Message is required.");
    }

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        // 1. Initialize services (Groq or Claude)
        string apiKey = System.Configuration.ConfigurationManager.AppSettings["GROQ_API_KEY"];
        var aiService = new GroqAIService(apiKey);
        var nl2sql = new Nl2SqlService(aiService);

        // 2. Generate SQL
        string generatedSql = await nl2sql.GenerateSqlAsync(request.Message, request.CompanyId);

        // 3. Execute SQL
        string sqlResultJson = _relationalRepo.ExecuteQueryToDynamicJson(generatedSql);

        // 4. Formulate Natural Language Answer
        string finalResponse = await nl2sql.FormulateResponseAsync(request.Message, generatedSql, sqlResultJson);

        stopwatch.Stop();

        // 5. Log the interaction
        _chatLogRepository.InsertLog(new AIChatLog
        {
            CompanyID = request.CompanyId,
            UserQuestion = request.Message,
            AIResponse = finalResponse,
            ResponseType = "TallySQL",
            SQLQuery = generatedSql,
            IsSuccess = true,
            ExecutionTime = (int)stopwatch.ElapsedMilliseconds,
            CreatedDate = DateTime.Now
        });

        return Ok(new
        {
            response = finalResponse,
            sql = generatedSql,
            success = true
        });
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        
        // Log Error
        _chatLogRepository.InsertLog(new AIChatLog
        {
            CompanyID = request.CompanyId,
            UserQuestion = request.Message,
            AIResponse = "Error processing request.",
            ResponseType = "TallySQL",
            IsSuccess = false,
            ErrorMessage = ex.Message,
            ExecutionTime = (int)stopwatch.ElapsedMilliseconds,
            CreatedDate = DateTime.Now
        });

        return InternalServerError(ex);
    }
}
```

---

## Verification Plan

### Manual Verification
1. **Database Table Creation**: Connect to the DB laptop using SSMS and execute the DDL script to generate `TallyLedger` and `TallyVoucher` tables.
2. **Synchronize Raw XML Data**: Start the `Insidash.TallySyncAgent` console app to run a full sync cycle of Ledgers and Vouchers. Verify using SSMS that rows are populated in both `TallyLedger` and `TallyVoucher` with parsed decimals.
3. **Execute Chat Queries**: Call the `POST /api/tally/chat` endpoint using Postman or a client, testing questions like:
   - *"What is our current balance in HDFC Bank?"*
   - *"List our top 3 ledger balances."*
   - *"Show me all transactions of VchType Sales on 2026-06-05."*
4. **Audit Generated SQL**: Verify in the response output (and in `AIChatLog`) that the generated `sql` property is clean, targeting the proper table, column names, and restricted correctly to the input `CompanyID`.
