# TalkWithTally — Production Roadmap Implementation Plan

> **Purpose:** This document details every planned production feature ordered from highest to lowest priority. Each phase includes the full reasoning behind the priority, what specifically needs to be built, exact implementation steps, all edge cases, and verification criteria. Written for a coding agent — follow phases in strict order.

---

## Priority Rationale — Based on Live Usage Data

Before the phases, here is why the priority order was chosen, grounded in actual log data:

| Signal | Observation | Implication |
|---|---|---|
| Top query = "Pending Payments" (34/248 runs) | Outstanding invoice tracking is the #1 use case | Tally outstanding data (bills receivable) is urgently needed |
| "Today's Payment" returns `AI_NoData` (2 confirmed cases) | Users querying payment data that exists in Tally but not in Insidash DB | Incremental sync is blocking real usage |
| 3 companies active, 106/49/37 query distribution | Already multi-tenant, one company dominates | Read-only DB user is a security gap right now, not a future concern |
| Full DELETE+INSERT on every sync | As company data grows, sync will time out | Incremental sync + upsert is the unlock for scale |
| 110 "Fast" route hits vs 66 AI-route hits | Pre-written queries are preferred and more reliable | Hybrid intent routing will absorb more of the remaining AI-route failures |

---

## Phase 1 — Read-Only Database User for Chat Endpoint

> **Priority: CRITICAL — Security Gap Active in Production**
> 
> The chat endpoint currently executes LLM-generated SQL using the same database connection as the sync endpoint, which has full read-write access. A single malformed LLM output that passes the safety check could modify or corrupt financial data across all companies. This must be fixed before any other feature is added.

### Step 1.1 — Create the read-only SQL Server login

Run this in SSMS on the DB laptop. Connect using the `sa` account or any account with `ALTER ANY LOGIN` permission:

```sql
-- Step A: Create the server-level login
CREATE LOGIN tally_readonly_user
WITH PASSWORD = 'TallyRead@2024!',
     CHECK_EXPIRATION = OFF,
     CHECK_POLICY = ON;

-- Step B: Switch to your working database
USE Popway_BillingERP;
GO

-- Step C: Create a database user mapped to that login
CREATE USER tally_readonly_user
FOR LOGIN tally_readonly_user;

-- Step D: Grant SELECT only on the two Tally tables
GRANT SELECT ON dbo.TallyLedger   TO tally_readonly_user;
GRANT SELECT ON dbo.TallyVoucher  TO tally_readonly_user;

-- Step E: Grant SELECT on Insidash tables the chat endpoint queries
-- Add every table referenced in the NL2SQL system prompt
GRANT SELECT ON dbo.Customer        TO tally_readonly_user;
GRANT SELECT ON dbo.Invoice         TO tally_readonly_user;
GRANT SELECT ON dbo.InvoiceDetail   TO tally_readonly_user;
GRANT SELECT ON dbo.Product         TO tally_readonly_user;
GRANT SELECT ON dbo.Payment         TO tally_readonly_user;
GRANT SELECT ON dbo.PaymentMap      TO tally_readonly_user;
GRANT SELECT ON dbo.Unit            TO tally_readonly_user;
GRANT SELECT ON dbo.Category        TO tally_readonly_user;
GRANT SELECT ON dbo.PaymentMethod   TO tally_readonly_user;
GRANT SELECT ON dbo.DefineData      TO tally_readonly_user;
GRANT SELECT ON dbo.InvoiceView     TO tally_readonly_user;
GRANT SELECT ON dbo.PaymentView     TO tally_readonly_user;

-- Step F: Explicitly DENY any write operations, just to be certain
DENY INSERT, UPDATE, DELETE ON dbo.TallyLedger  TO tally_readonly_user;
DENY INSERT, UPDATE, DELETE ON dbo.TallyVoucher TO tally_readonly_user;
DENY INSERT, UPDATE, DELETE ON dbo.Customer     TO tally_readonly_user;
DENY INSERT, UPDATE, DELETE ON dbo.Invoice      TO tally_readonly_user;
```

### Step 1.2 — Add a second connection string in `Web.config`

The API project's `Web.config` currently has one connection string for everything. Add a second one exclusively for the chat endpoint:

```xml
<connectionStrings>
  <!-- Existing: used for sync writes, AIChatLog inserts, config reads -->
  <add name="InsidashTallyConnection"
       connectionString="Data Source=...;Initial Catalog=Popway_BillingERP;User ID=sa;Password=...;"
       providerName="System.Data.SqlClient" />

  <!-- NEW: used exclusively by the chat endpoint SQL execution -->
  <add name="InsidashTallyReadOnly"
       connectionString="Data Source=...;Initial Catalog=Popway_BillingERP;User ID=tally_readonly_user;Password=TallyRead@2024!;"
       providerName="System.Data.SqlClient" />
</connectionStrings>
```

### Step 1.3 — Wire the read-only connection into SQL execution

In `TallyRelationalRepository.cs` (or wherever `ExecuteSql` runs the LLM-generated query), locate the connection string being used. Change it to use `InsidashTallyReadOnly`:

```csharp
public DataTable ExecuteUserQuery(string sql, int companyId)
{
    // Use the READ-ONLY connection string — never the write connection
    string connStr = ConfigurationManager
        .ConnectionStrings["InsidashTallyReadOnly"].ConnectionString;

    using (var conn = new SqlConnection(connStr))
    using (var cmd = new SqlCommand(sql, conn))
    {
        cmd.Parameters.AddWithValue("@CompanyID", companyId);
        cmd.CommandTimeout = 15; // 15-second hard limit on LLM-generated queries
        conn.Open();

        var table = new DataTable();
        new SqlDataAdapter(cmd).Fill(table);
        return table;
    }
}
```

All other repository methods (sync upserts, AIChatLog inserts) continue using `InsidashTallyConnection`. Only the user-query execution path uses `InsidashTallyReadOnly`.

### Step 1.4 — Add a command timeout

Notice `cmd.CommandTimeout = 15` in step 1.3. This is new. LLM-generated SQL can produce accidental cross-join queries that scan millions of rows. Without a timeout, these will block the thread indefinitely. 15 seconds is generous for any legitimate financial query on an SME dataset.

In `Nl2SqlService.cs`, handle the timeout exception specifically:

```csharp
catch (SqlException ex) when (ex.Number == -2) // -2 = timeout
{
    log.ResponseType  = "AI_Timeout";
    log.AIResponse    = "⏱️ Query took too long. Try asking for a smaller date range.";
    log.ErrorMessage  = "SQL execution timeout (>15s)";
}
```

### Step 1.5 — Verify

```sql
-- Connect to SSMS using tally_readonly_user credentials (NOT sa)
-- This should work:
SELECT TOP 1 * FROM TallyLedger;

-- This must fail with "The SELECT permission was denied":
INSERT INTO TallyLedger (LedgerID, CompanyID, Name, Parent, ClosingBalance, SyncedAt)
VALUES ('test', 1, 'test', 'test', 0, GETDATE());

-- This must fail with "The UPDATE permission was denied":
UPDATE TallyLedger SET Name = 'hacked' WHERE CompanyID = 5;
```

---

## Phase 2 — Incremental Synchronization

> **Priority: HIGH — Current Full-Sync is Blocking Real Usage**
>
> The current sync deletes all records and re-inserts everything every 5 minutes. Live logs show "Today's Payment" returning `AI_NoData` even when payments exist — this is because if a sync fails mid-way (network drop, Tally timeout), the DELETE has already run but the INSERT hasn't completed, leaving the table empty. Incremental sync eliminates this data loss window entirely and scales to large companies.

### Step 2.1 — Add a `LastSyncedAt` tracker table

The sync agent needs to remember when it last successfully synced each data type per company. Add this table in SSMS:

```sql
USE Popway_BillingERP;
GO

CREATE TABLE TallySyncState (
    SyncStateID  INT IDENTITY(1,1) PRIMARY KEY,
    CompanyID    INT NOT NULL,
    DataType     NVARCHAR(50) NOT NULL,    -- 'Ledger' or 'Voucher'
    LastSyncedAt DATETIME NOT NULL DEFAULT '2000-01-01',
    LastSyncStatus NVARCHAR(20) NOT NULL DEFAULT 'Never',
    RecordsSynced  INT NOT NULL DEFAULT 0,
    UpdatedAt    DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_TallySyncState UNIQUE (CompanyID, DataType)
);

-- Seed with one row per data type per company so the agent has a starting point
-- Replace 5 with your actual CompanyID(s)
INSERT INTO TallySyncState (CompanyID, DataType, LastSyncedAt, LastSyncStatus)
VALUES (5, 'Ledger',  '2000-01-01', 'Never'),
       (5, 'Voucher', '2000-01-01', 'Never');
```

### Step 2.2 — Understand Tally's incremental query capabilities

**For Ledgers:** Ledgers are master data (account heads). They change infrequently. Tally exposes an `ALTEREDON` metadata field on masters that records when a master was last modified. Incremental ledger sync can use this.

**For Vouchers:** Vouchers are transaction data. Tally does NOT reliably expose `ALTEREDON` on vouchers. The correct approach is to query vouchers by `DATE >= lastSyncDate`. This means on the first sync you get everything; on subsequent syncs you only get vouchers dated on or after the last sync timestamp.

**Important caveat:** Users can back-date vouchers in Tally (e.g., enter a December voucher in January). A date-based incremental sync will miss back-dated entries. Document this as a known limitation. Full re-sync can be triggered manually to correct it.

### Step 2.3 — Update the Tally XML envelope for incremental voucher query

In `TallyEnvelopeFactory.cs` (or `Program.cs` where envelopes are built), add a date parameter to the voucher envelope:

```csharp
public static string BuildVoucherEnvelope(DateTime fromDate)
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
```

For ledgers, keep the full-collection envelope (ledger counts are small, full re-fetch is fine). The incremental optimization is most critical for vouchers which can number in the hundreds of thousands.

### Step 2.4 — Refactor the upsert to MERGE instead of DELETE+INSERT

For vouchers, replace the destructive DELETE with an upsert that only touches modified records. In `TallyRelationalRepository.cs`:

```csharp
public void UpsertVouchersIncremental(List<TallyVoucher> vouchers, int companyId)
{
    string connStr = ConfigurationManager
        .ConnectionStrings["InsidashTallyConnection"].ConnectionString;

    using (var conn = new SqlConnection(connStr))
    {
        conn.Open();

        // Step 1: Bulk-insert new vouchers into a temp staging table
        var stagingTable = BuildVoucherDataTable(vouchers, companyId);

        using (var bulk = new SqlBulkCopy(conn))
        {
            bulk.DestinationTableName = "#TallyVoucherStaging";
            // Create the temp table first
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE #TallyVoucherStaging (
                        VoucherID  NVARCHAR(50),
                        CompanyID  INT,
                        Date       DATE,
                        VchType    NVARCHAR(100),
                        PartyName  NVARCHAR(255),
                        Amount     DECIMAL(18,2),
                        Narration  NVARCHAR(MAX),
                        SyncedAt   DATETIME
                    )";
                cmd.ExecuteNonQuery();
            }
            bulk.WriteToServer(stagingTable);
        }

        // Step 2: MERGE from staging into real table
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                MERGE TallyVoucher AS target
                USING #TallyVoucherStaging AS source
                    ON target.VoucherID = source.VoucherID
                        AND target.CompanyID = source.CompanyID
                WHEN MATCHED THEN
                    UPDATE SET
                        target.Date      = source.Date,
                        target.VchType   = source.VchType,
                        target.PartyName = source.PartyName,
                        target.Amount    = source.Amount,
                        target.Narration = source.Narration,
                        target.SyncedAt  = source.SyncedAt
                WHEN NOT MATCHED THEN
                    INSERT (VoucherID, CompanyID, Date, VchType, PartyName, Amount, Narration, SyncedAt)
                    VALUES (source.VoucherID, source.CompanyID, source.Date,
                            source.VchType, source.PartyName, source.Amount,
                            source.Narration, source.SyncedAt);

                DROP TABLE #TallyVoucherStaging;";
            cmd.ExecuteNonQuery();
        }
    }
}
```

For ledgers, keep the full DELETE+INSERT with `SqlBulkCopy` (from the Phase 6 fix) since ledger counts are manageable and accuracy requires full replacement.

### Step 2.5 — Update sync state on success

In `Program.cs` of the sync agent, after a successful sync cycle:

```csharp
private static void UpdateSyncState(int companyId, string dataType, int recordCount)
{
    string connStr = ConfigurationManager.AppSettings["ApiDbConnection"];
    using (var conn = new SqlConnection(connStr))
    using (var cmd = new SqlCommand(@"
        MERGE TallySyncState AS target
        USING (SELECT @CompanyID AS CompanyID, @DataType AS DataType) AS source
            ON target.CompanyID = source.CompanyID AND target.DataType = source.DataType
        WHEN MATCHED THEN
            UPDATE SET LastSyncedAt = GETDATE(), LastSyncStatus = 'Success',
                       RecordsSynced = @RecordCount, UpdatedAt = GETDATE()
        WHEN NOT MATCHED THEN
            INSERT (CompanyID, DataType, LastSyncedAt, LastSyncStatus, RecordsSynced)
            VALUES (@CompanyID, @DataType, GETDATE(), 'Success', @RecordCount);
    ", conn))
    {
        cmd.Parameters.AddWithValue("@CompanyID", companyId);
        cmd.Parameters.AddWithValue("@DataType", dataType);
        cmd.Parameters.AddWithValue("@RecordCount", recordCount);
        conn.Open();
        cmd.ExecuteNonQuery();
    }
}
```

### Step 2.6 — Read `LastSyncedAt` at the start of each sync cycle

```csharp
private static DateTime GetLastSyncedAt(int companyId, string dataType)
{
    string connStr = ConfigurationManager.AppSettings["ApiDbConnection"];
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
```

Then in the main sync loop:

```csharp
DateTime lastVoucherSync = GetLastSyncedAt(companyId, "Voucher");
string envelope = TallyEnvelopeFactory.BuildVoucherEnvelope(lastVoucherSync);
// ... sync ...
UpdateSyncState(companyId, "Voucher", insertedCount);
```

### Step 2.7 — Verify

After deploying:
1. Trigger a sync. Confirm `TallySyncState` has a row with `LastSyncedAt` close to `GETDATE()`.
2. Add a new voucher in Tally. Wait for next sync cycle.
3. Query `TallyVoucher` and confirm the new voucher appears without all old vouchers being deleted and re-inserted.
4. Check that "Today's Payment" no longer returns `AI_NoData` when same-day payments exist.

---

## Phase 3 — Hybrid Intent Routing (Pre-Written Query Templates)

> **Priority: HIGH — Directly Fixes the Highest-Failure Query Patterns**
>
> Live data shows 110 "Fast" route hits (pre-written SQL, 100% success) vs 66 AI-route hits (variable quality). The "Fast" route already exists for some queries. This phase formalizes it into a proper intent routing system that can be extended without code changes, and adds the missing accounting report templates that the LLM currently gets wrong.

### Step 3.1 — Understand what "Fast" route already does

Looking at the live logs, "Fast" route fires for: "Pending Payments", "Today's Sales", "Monthly Sales", "customer list", "product list", "Top Customers", "today payment". These are exact or near-exact keyword matches routing to pre-written SQL. The mechanism is working — it just needs to be formalized and expanded.

### Step 3.2 — Create a `QueryTemplate` table in the database

This lets you add new pre-written templates without deploying code:

```sql
USE Popway_BillingERP;
GO

CREATE TABLE TallyQueryTemplate (
    TemplateID   INT IDENTITY(1,1) PRIMARY KEY,
    Name         NVARCHAR(100) NOT NULL,
    Keywords     NVARCHAR(500) NOT NULL,  -- comma-separated trigger words
    SqlQuery     NVARCHAR(MAX) NOT NULL,
    Description  NVARCHAR(255) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAt    DATETIME NOT NULL DEFAULT GETDATE()
);

-- Seed: Profit & Loss report
INSERT INTO TallyQueryTemplate (Name, Keywords, SqlQuery, Description) VALUES (
'Profit and Loss',
'profit,loss,p&l,pl,income,revenue,expense,net profit,gross profit',
'SELECT
    SUM(CASE WHEN Parent IN (''Sales Accounts'',''Direct Income'',''Indirect Income'') THEN ABS(ClosingBalance) ELSE 0 END) AS TotalRevenue,
    SUM(CASE WHEN Parent IN (''Direct Expenses'',''Indirect Expenses'',''Purchase Accounts'') THEN ABS(ClosingBalance) ELSE 0 END) AS TotalExpenses,
    SUM(CASE WHEN Parent IN (''Sales Accounts'',''Direct Income'',''Indirect Income'') THEN ABS(ClosingBalance) ELSE 0 END)
    - SUM(CASE WHEN Parent IN (''Direct Expenses'',''Indirect Expenses'',''Purchase Accounts'') THEN ABS(ClosingBalance) ELSE 0 END) AS NetProfit
FROM TallyLedger
WHERE CompanyID = @CompanyID',
'Profit and Loss statement from Tally ledger data');

-- Seed: Balance Sheet totals
INSERT INTO TallyQueryTemplate (Name, Keywords, SqlQuery, Description) VALUES (
'Balance Sheet',
'balance sheet,total assets,total liabilities,capital,equity',
'SELECT
    SUM(CASE WHEN Parent IN (''Current Assets'',''Fixed Assets'',''Bank Accounts'',''Cash-in-Hand'',''Sundry Debtors'') THEN ABS(ClosingBalance) ELSE 0 END) AS TotalAssets,
    SUM(CASE WHEN Parent IN (''Current Liabilities'',''Loans (Liability)'',''Sundry Creditors'') THEN ABS(ClosingBalance) ELSE 0 END) AS TotalLiabilities,
    SUM(CASE WHEN Parent IN (''Capital Account'',''Reserves & Surplus'') THEN ABS(ClosingBalance) ELSE 0 END) AS CapitalEquity
FROM TallyLedger
WHERE CompanyID = @CompanyID',
'Balance Sheet summary from Tally ledger data');

-- Seed: Outstanding receivables aging
INSERT INTO TallyQueryTemplate (Name, Keywords, SqlQuery, Description) VALUES (
'Outstanding Receivables',
'outstanding,receivable,debtor,baki,due,overdue,aging',
'SELECT TOP 20
    v.PartyName,
    SUM(v.Amount) AS TotalOutstanding,
    COUNT(*) AS VoucherCount,
    MIN(v.Date) AS OldestEntry,
    DATEDIFF(DAY, MIN(v.Date), GETDATE()) AS DaysOustanding
FROM TallyVoucher v
WHERE v.CompanyID = @CompanyID
    AND v.VchType IN (''Sales'', ''Journal'')
    AND v.Amount > 0
GROUP BY v.PartyName
HAVING SUM(v.Amount) > 0
ORDER BY TotalOutstanding DESC',
'Aged outstanding receivables from Tally vouchers');
```

### Step 3.3 — Build the `IntentRouter` service

In `Insidash.BLL/Services/`, create `IntentRouterService.cs`:

```csharp
public class IntentRouterService
{
    private readonly List<QueryTemplateDto> _templates;

    public IntentRouterService(List<QueryTemplateDto> templates)
    {
        _templates = templates;
    }

    /// <summary>
    /// Returns a matched template SQL if a pre-written query covers the intent.
    /// Returns null if the question should go to the LLM (NL2SQL path).
    /// </summary>
    public string TryMatchTemplate(string userQuestion)
    {
        string lower = userQuestion.ToLowerInvariant().Trim();

        foreach (var template in _templates.Where(t => t.IsActive))
        {
            var keywords = template.Keywords
                .Split(',')
                .Select(k => k.Trim().ToLowerInvariant())
                .Where(k => k.Length > 0);

            if (keywords.Any(k => lower.Contains(k)))
                return template.SqlQuery;
        }

        return null; // no match → go to NL2SQL
    }
}

public class QueryTemplateDto
{
    public int    TemplateID  { get; set; }
    public string Name        { get; set; }
    public string Keywords    { get; set; }
    public string SqlQuery    { get; set; }
    public bool   IsActive    { get; set; }
}
```

### Step 3.4 — Load templates and wire into the chat pipeline

In `Nl2SqlService.cs`, load templates once at startup (or cache with a 10-minute TTL):

```csharp
private List<QueryTemplateDto> LoadTemplates()
{
    using (var db = new InsidashTallyContext())
    {
        return db.Database.SqlQuery<QueryTemplateDto>(
            "SELECT TemplateID, Name, Keywords, SqlQuery, IsActive FROM TallyQueryTemplate WHERE IsActive = 1"
        ).ToList();
    }
}
```

Then in `ProcessQueryAsync`, check the template router **before** calling the LLM:

```csharp
public async Task<string> ProcessQueryAsync(string userQuestion, int companyId)
{
    // ... log setup, stopwatch ...

    // Stage 0: Intent routing — check pre-written templates first
    string templateSql = _intentRouter.TryMatchTemplate(userQuestion);

    if (templateSql != null)
    {
        // Use verified pre-written SQL directly — no LLM call needed
        log.SQLQuery     = templateSql;
        log.ResponseType = "Fast";

        var resultTable = _repository.ExecuteUserQuery(templateSql, companyId);
        // ... format and return ...
    }
    else
    {
        // Fall through to NL2SQL LLM path
        // ... existing AI translation logic ...
    }
}
```

### Step 3.5 — Add an EF6 entity and DbSet for the new table

In `Insidash.DAL/Entities/`, create `TallyQueryTemplate.cs`:

```csharp
[Table("TallyQueryTemplate")]
public class TallyQueryTemplate
{
    [Key]
    public int    TemplateID  { get; set; }
    public string Name        { get; set; }
    public string Keywords    { get; set; }
    public string SqlQuery    { get; set; }
    public string Description { get; set; }
    public bool   IsActive    { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

Add to `InsidashTallyContext.cs`:
```csharp
public DbSet<TallyQueryTemplate> QueryTemplates { get; set; }
```

### Step 3.6 — Verify

1. Ask "what is our profit and loss" — `ResponseType` should be `Fast`, SQL should be the P&L template.
2. Ask "show me balance sheet" — `ResponseType` should be `Fast`.
3. Ask a unique question not in any template (e.g., "show me invoices with discount > 10%") — should fall through to the LLM NL2SQL path.
4. Add a new row to `TallyQueryTemplate` in SSMS and test it fires within 10 minutes (or immediately if you clear the cache).

---

## Phase 4 — Inventory & Outstanding Invoice Tracking (New Data Tables)

> **Priority: MEDIUM-HIGH — Expands Queryable Data Surface**
>
> Currently the chatbot can only answer questions about Ledgers and Vouchers. Users are already asking about "outstanding bills" (confirmed in live logs: "Pending Payments" is the #1 query). This phase adds `TallyStockItem` for inventory queries and `TallyBillOutstanding` for receivables aging — both queried via NL2SQL.

### Step 4.1 — Create the two new tables in SSMS

```sql
USE Popway_BillingERP;
GO

-- Stock inventory from Tally
CREATE TABLE TallyStockItem (
    StockItemID  NVARCHAR(50)   NOT NULL PRIMARY KEY,
    CompanyID    INT            NOT NULL,
    Name         NVARCHAR(255)  NOT NULL,
    Parent       NVARCHAR(255)  NULL,   -- stock group
    Unit         NVARCHAR(50)   NULL,
    ClosingQty   DECIMAL(18,4)  NOT NULL DEFAULT 0,
    ClosingValue DECIMAL(18,2)  NOT NULL DEFAULT 0,
    SyncedAt     DATETIME       NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_TallyStockItem_Company FOREIGN KEY (CompanyID)
        REFERENCES Company(CompanyID) ON DELETE CASCADE
);

-- Receivables aging from Tally voucher bill details
CREATE TABLE TallyBillOutstanding (
    BillID       NVARCHAR(50)   NOT NULL PRIMARY KEY,
    CompanyID    INT            NOT NULL,
    PartyName    NVARCHAR(255)  NOT NULL,
    BillDate     DATE           NOT NULL,
    BillRef      NVARCHAR(100)  NULL,   -- Tally bill reference number
    Amount       DECIMAL(18,2)  NOT NULL,
    DueDate      DATE           NULL,
    SyncedAt     DATETIME       NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_TallyBillOutstanding_Company FOREIGN KEY (CompanyID)
        REFERENCES Company(CompanyID) ON DELETE CASCADE
);

-- Indexes for the most common query patterns
CREATE INDEX IX_TallyBillOutstanding_CompanyParty
    ON TallyBillOutstanding (CompanyID, PartyName);

CREATE INDEX IX_TallyBillOutstanding_BillDate
    ON TallyBillOutstanding (CompanyID, BillDate);

CREATE INDEX IX_TallyStockItem_CompanyName
    ON TallyStockItem (CompanyID, Name);
```

### Step 4.2 — Add Tally XML envelopes for the new data types

In `TallyEnvelopeFactory.cs`:

```csharp
public static string BuildStockItemEnvelope()
{
    return @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>List of Stock Items</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME=""List of Stock Items"" ISMODIFY=""No"">
            <TYPE>Stock Item</TYPE>
            <FETCH>NAME, PARENT, BASEUNITS, CLOSINGBALANCE, CLOSINGVALUE</FETCH>
          </COLLECTION>
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";
}

public static string BuildBillOutstandingEnvelope()
{
    // Queries the outstanding bills from Tally's ledger bill details
    return @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>Outstanding Receivables</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME=""Outstanding Receivables"" ISMODIFY=""No"">
            <TYPE>Ledger</TYPE>
            <FILTER>IsADebtors</FILTER>
            <FETCH>NAME, CLOSINGBALANCE, BILLDETAILS.LIST</FETCH>
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
```

### Step 4.3 — Add parsers in `TallyXmlParser.cs`

Add two new parse methods alongside the existing `ParseLedger` and `ParseVoucher`:

```csharp
public List<TallyStockItem> ParseStockItems(string rawXml)
{
    var doc = new XmlDocument();
    doc.LoadXml(rawXml);
    var items = new List<TallyStockItem>();

    foreach (XmlNode node in doc.SelectNodes("//STOCKITEM"))
    {
        try
        {
            items.Add(new TallyStockItem
            {
                StockItemID  = Guid.NewGuid().ToString(),
                Name         = node["NAME"]?.InnerText?.Trim() ?? "",
                Parent       = node["PARENT"]?.InnerText?.Trim(),
                Unit         = node["BASEUNITS"]?.InnerText?.Trim(),
                ClosingQty   = ParseDecimalSafe(node["CLOSINGBALANCE"]?.InnerText),
                ClosingValue = ParseDecimalSafe(node["CLOSINGVALUE"]?.InnerText)
            });
        }
        catch { /* skip malformed nodes */ }
    }
    return items;
}

private decimal ParseDecimalSafe(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return 0;
    raw = raw.Replace(",", "").Trim();
    return decimal.TryParse(raw, NumberStyles.Any,
        CultureInfo.InvariantCulture, out decimal val) ? val : 0;
}
```

### Step 4.4 — Add EF6 entities and sync repo methods

Create `TallyStockItem.cs` and `TallyBillOutstanding.cs` entities in `Insidash.DAL/Entities/`. Add `DbSet<>` entries to `InsidashTallyContext`. Add `UpsertStockItems` and `UpsertBillOutstanding` methods to `TallyRelationalRepository` using the same `SqlBulkCopy` pattern from Phase 6 of the fixes.

### Step 4.5 — Add the new data types to the sync agent loop

In `Program.cs` of the sync agent, add the new types to the sync cycle after Ledger and Voucher:

```csharp
var dataTypes = new[]
{
    TallyDataType.Ledger,
    TallyDataType.Voucher,
    TallyDataType.StockItem,       // NEW
    TallyDataType.BillOutstanding  // NEW
};
```

### Step 4.6 — Expand the NL2SQL system prompt schema

Add the two new tables to the system prompt in `Nl2SqlService.cs`:

```
TallyStockItem( StockItemID PK, CompanyID, Name, Parent, Unit, ClosingQty, ClosingValue, SyncedAt )
TallyBillOutstanding( BillID PK, CompanyID, PartyName, BillDate, BillRef, Amount, DueDate, SyncedAt )

RULES for Tally tables:
- Stock queries → TallyStockItem
- Inventory value → SUM(ClosingValue) FROM TallyStockItem
- Outstanding bills older than N days → DATEDIFF(DAY, BillDate, GETDATE()) > N FROM TallyBillOutstanding
- ALWAYS filter by CompanyID on Tally tables too
```

### Step 4.7 — Verify

1. Check `TallyStockItem` is populated after next sync: `SELECT COUNT(*) FROM TallyStockItem WHERE CompanyID = 5`
2. Ask "show me stock inventory" — should return item names and quantities.
3. Ask "outstanding bills older than 90 days" — should query `TallyBillOutstanding` with a date filter.

---

## Phase 5 — Response Quality Improvements

> **Priority: MEDIUM — Reduces User Confusion on Failures**
>
> Three patterns in live logs create poor user experience: (1) "📭 No data found" with no guidance on why, (2) "🤖 I'm unable to process" with no retry suggestion, (3) `AI_NoData` returned for "Today's Payment" when the issue was a sync lag, not truly empty data. This phase makes failure responses informative rather than generic.

### Step 5.1 — Distinguish "no data" from "sync not yet done"

Before returning `AI_NoData`, check if a sync has been completed at all for this company. In `Nl2SqlService.cs`:

```csharp
private string GetSmartNoDataMessage(int companyId, string sql)
{
    using (var db = new InsidashTallyContext())
    {
        // Check if Tally sync has ever run for this company
        var lastSync = db.Database.SqlQuery<DateTime?>(
            "SELECT MAX(LastSyncedAt) FROM TallySyncState WHERE CompanyID = @p0 AND LastSyncStatus = 'Success'",
            companyId).FirstOrDefault();

        if (lastSync == null)
            return "📡 Tally data hasn't been synced yet for your company. Please ensure the Tally Sync Agent is running.";

        if ((DateTime.Now - lastSync.Value).TotalMinutes > 30)
            return $"📡 Last Tally sync was {(int)(DateTime.Now - lastSync.Value).TotalHours} hours ago. Data may be outdated. Ensure the Sync Agent is running.";

        return "📭 No matching records found. Try adjusting your date range or search terms.";
    }
}
```

### Step 5.2 — Add a clarification suggestion to `AI_NoSQL` responses

When the LLM returns no SQL (meaning the question was out of scope or ambiguous), return a helpful redirect rather than a generic error. In the `AI_NoSQL` catch:

```csharp
case "AI_NoSQL":
    log.AIResponse = "🤔 I couldn't understand that as a financial query. Try asking like:\n" +
                     "• \"Show pending payments\"\n" +
                     "• \"What are today's sales?\"\n" +
                     "• \"Total sales for December 2025\"\n" +
                     "• \"Show me top customers by revenue\"";
    break;
```

### Step 5.3 — Add a fuzzy name-match suggestion for `AI_NoData` on customer/party queries

From the logs: "hiren customr sales" and "Jyoti Kanna paymnt" both returned `AI_NoData`. The names may exist in the DB under slightly different spellings. When a customer/party name query returns no data, run a fuzzy suggestion query:

```csharp
private string TrySuggestSimilarName(string originalName, int companyId)
{
    // Simple LIKE-based suggestion from the Customer table
    string connStr = ConfigurationManager
        .ConnectionStrings["InsidashTallyReadOnly"].ConnectionString;

    using (var conn = new SqlConnection(connStr))
    using (var cmd = new SqlCommand(@"
        SELECT TOP 3 FirstName FROM Customer
        WHERE CompanyID = @cid AND IsDelete = 0
          AND FirstName LIKE @pattern", conn))
    {
        // Take first 4 chars of the name for a broad LIKE
        string pattern = "%" + originalName.Substring(0, Math.Min(4, originalName.Length)) + "%";
        cmd.Parameters.AddWithValue("@cid", companyId);
        cmd.Parameters.AddWithValue("@pattern", pattern);
        conn.Open();

        var names = new List<string>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) names.Add(reader.GetString(0));

        return names.Any()
            ? $"📭 No data found. Did you mean: {string.Join(", ", names)}?"
            : "📭 No matching records found.";
    }
}
```

Call this in the `AI_NoData` handler when the original question contains a proper noun (capitalized word or a name-like token).

### Step 5.4 — Verify

1. Query "show payment for hiren" when "Hiren" doesn't exist → should see "Did you mean: Hiren Shah?" or similar.
2. Query any financial question with no sync ever run → should see sync agent message, not generic "no data".
3. Query something out of scope like "what is the weather" → should see the redirect suggestion list.

---

## Phase 6 — Stored Procedures for Standard Accounting Reports

> **Priority: MEDIUM — Enables 100% Accurate Financial Statements**
>
> The roadmap correctly identifies that LLMs make mistakes on complex accounting reports like Balance Sheet and P&L. Phase 3 added keyword-matched templates as a first layer. This phase replaces those inline SQL strings with proper stored procedures, which are easier to maintain, can be tested independently, and can be updated by an accountant without touching application code.

### Step 6.1 — Create stored procedures in SSMS

```sql
USE Popway_BillingERP;
GO

-- Profit and Loss from Tally Ledgers
CREATE PROCEDURE sp_TallyProfitAndLoss
    @CompanyID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        'Revenue' AS Section,
        Name,
        Parent,
        ABS(ClosingBalance) AS Amount
    FROM TallyLedger
    WHERE CompanyID = @CompanyID
      AND Parent IN ('Sales Accounts', 'Direct Income', 'Indirect Income')
      AND ClosingBalance <> 0

    UNION ALL

    SELECT
        'Expenses' AS Section,
        Name,
        Parent,
        ABS(ClosingBalance) AS Amount
    FROM TallyLedger
    WHERE CompanyID = @CompanyID
      AND Parent IN ('Direct Expenses', 'Indirect Expenses', 'Purchase Accounts')
      AND ClosingBalance <> 0

    ORDER BY Section, Amount DESC;
END
GO

-- Balance Sheet from Tally Ledgers
CREATE PROCEDURE sp_TallyBalanceSheet
    @CompanyID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        CASE
            WHEN Parent IN ('Current Assets','Fixed Assets','Bank Accounts',
                            'Cash-in-Hand','Sundry Debtors','Loans & Advances (Asset)')
                THEN 'Assets'
            WHEN Parent IN ('Current Liabilities','Loans (Liability)',
                            'Sundry Creditors','Provisions')
                THEN 'Liabilities'
            WHEN Parent IN ('Capital Account','Reserves & Surplus')
                THEN 'Capital'
            ELSE 'Other'
        END AS Section,
        Name,
        Parent,
        ClosingBalance
    FROM TallyLedger
    WHERE CompanyID = @CompanyID
      AND ClosingBalance <> 0
    ORDER BY Section, ABS(ClosingBalance) DESC;
END
GO

-- Receivables Aging
CREATE PROCEDURE sp_TallyReceivablesAging
    @CompanyID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        PartyName,
        SUM(Amount)                                    AS TotalOutstanding,
        SUM(CASE WHEN DATEDIFF(DAY,BillDate,GETDATE()) <= 30  THEN Amount ELSE 0 END) AS Within30Days,
        SUM(CASE WHEN DATEDIFF(DAY,BillDate,GETDATE()) BETWEEN 31 AND 60  THEN Amount ELSE 0 END) AS Days31To60,
        SUM(CASE WHEN DATEDIFF(DAY,BillDate,GETDATE()) BETWEEN 61 AND 90  THEN Amount ELSE 0 END) AS Days61To90,
        SUM(CASE WHEN DATEDIFF(DAY,BillDate,GETDATE()) > 90  THEN Amount ELSE 0 END)  AS Over90Days,
        MIN(BillDate)                                  AS OldestBill
    FROM TallyBillOutstanding
    WHERE CompanyID = @CompanyID
      AND Amount > 0
    GROUP BY PartyName
    ORDER BY TotalOutstanding DESC;
END
GO
```

### Step 6.2 — Grant execute permission to the read-only user

```sql
GRANT EXECUTE ON sp_TallyProfitAndLoss    TO tally_readonly_user;
GRANT EXECUTE ON sp_TallyBalanceSheet     TO tally_readonly_user;
GRANT EXECUTE ON sp_TallyReceivablesAging TO tally_readonly_user;
```

### Step 6.3 — Update the `TallyQueryTemplate` table rows to call stored procs

Update the P&L and Balance Sheet templates seeded in Phase 3 to use `EXEC` calls:

```sql
UPDATE TallyQueryTemplate
SET SqlQuery = 'EXEC sp_TallyProfitAndLoss @CompanyID'
WHERE Name = 'Profit and Loss';

UPDATE TallyQueryTemplate
SET SqlQuery = 'EXEC sp_TallyBalanceSheet @CompanyID'
WHERE Name = 'Balance Sheet';

UPDATE TallyQueryTemplate
SET SqlQuery = 'EXEC sp_TallyReceivablesAging @CompanyID'
WHERE Name = 'Outstanding Receivables';
```

### Step 6.4 — Update the SQL executor to handle `EXEC` calls

The `IsSafeSelect` check from the fixes must be updated to allow `EXEC sp_Tally*` pattern alongside `SELECT`:

```csharp
private bool IsSafeQuery(string sql)
{
    string upper = sql.Trim().ToUpperInvariant();

    // Allow SELECT statements (existing check)
    if (upper.StartsWith("SELECT") && !ContainsForbiddenKeywords(upper))
        return true;

    // Allow EXEC of only our whitelisted stored procedures
    if (upper.StartsWith("EXEC SP_TALLY"))
        return true;

    return false;
}
```

### Step 6.5 — Verify

Run in SSMS:
```sql
EXEC sp_TallyProfitAndLoss @CompanyID = 5;
EXEC sp_TallyBalanceSheet  @CompanyID = 5;
```

Confirm numbers match what Tally shows in its own P&L/Balance Sheet reports.

---

## Phase 7 — Multi-Company & Tenant Isolation Hardening

> **Priority: LOW-MEDIUM — Foundation for SaaS Scaling**
>
> Currently 3 companies are active. The system works. But `CompanyID` is passed in request bodies without server-side verification that the requesting user is authorized for that company. This is a cross-tenant data leak waiting to happen as more companies are onboarded.

### Step 7.1 — Add a company authorization check to the chat endpoint

In `TallyApiController.cs`, the `/chat` route accepts `CompanyId` from the request body and passes it directly to SQL. Add a server-side check:

```csharp
[HttpPost, Route("chat")]
public async Task<IHttpActionResult> Chat([FromBody] ChatRequest request)
{
    // Extract authenticated user's company from session/token
    // (Adjust this based on how Insidash handles authentication)
    int authenticatedCompanyId = GetAuthenticatedCompanyId();

    // Block if the requested CompanyID doesn't match the authenticated one
    if (request.CompanyId != authenticatedCompanyId)
        return Unauthorized();

    // ... rest of handler ...
}
```

### Step 7.2 — Add a `CompanyID` constraint index to TallyLedger and TallyVoucher

These indexes already filter by `CompanyID` in every query. Confirm the indexes exist:

```sql
-- These should already exist from the schema; create if missing
CREATE INDEX IX_TallyLedger_CompanyID  ON TallyLedger  (CompanyID);
CREATE INDEX IX_TallyVoucher_CompanyID ON TallyVoucher (CompanyID);
CREATE INDEX IX_TallyVoucher_Date      ON TallyVoucher (CompanyID, Date);
```

### Step 7.3 — Verify

Attempt a chat request with a `CompanyId` that does not match the authenticated session. The server should return `401 Unauthorized`, not financial data from a different company.

---

## Implementation Order Summary

```
Phase 1 → Read-only DB user                [Do immediately — security gap]
Phase 2 → Incremental sync                 [Do next — fixes live data loss]
Phase 3 → Hybrid intent routing            [Do after sync — expands reliability]
Phase 4 → Inventory + outstanding tables   [Do after routing — expands data]
Phase 5 → Response quality                 [Do after data tables — UX polish]
Phase 6 → Stored procedures                [Do after Phase 3 templates work]
Phase 7 → Tenant isolation                 [Do before going to more companies]
```

---

## Live Data Signals — Tracked Gaps This Roadmap Closes

| Current gap (observed in logs) | Closed by |
|---|---|
| "Today's Payment" returns `AI_NoData` despite payments existing | Phase 2 (incremental sync eliminates empty-table window) |
| "Pending Payments" is #1 query — works but uses naive SQL | Phase 6 (stored proc for receivables aging) |
| No way to query inventory or outstanding bills | Phase 4 |
| "hiren customr sales" returns "no data found" with no help | Phase 5 |
| 3 companies in prod, no auth check on CompanyID | Phase 7 |
| P&L and Balance Sheet will fail when attempted via LLM | Phase 3 + Phase 6 |
| Sync failure leaves tables empty for 5 min window | Phase 2 |
| LLM-generated queries run on full read-write connection | Phase 1 |

> **Note to coding agent:** Do not begin Phase 2 until Phase 1's read-only user is verified in SSMS. Do not begin Phase 4 until Phase 2's incremental sync is confirmed working (otherwise the new tables will have the same full-delete race condition). Phases 5, 6, and 7 are independent of each other and can be parallelized after Phase 4.
