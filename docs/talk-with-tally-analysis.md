# Deep Technical Analysis: talk-with-tally (Insidash TalkWithTally)

> **Repository**: `Harshalsharma05/talk-with-tally`
> **Purpose**: AI-powered natural language interface to Tally Prime accounting data
> **Stack**: C# / ASP.NET Web API (.NET Framework), Entity Framework 6, MS SQL Server, Groq (llama-3.3-70b), Vanilla JS + jQuery
> **Author context**: Internship / sandbox project for the Insidash platform

---

## Phase 1: Repository Tree — Annotated

```
talk-with-tally/
│
├── TalkWithTally.sln                   ← Visual Studio solution file; links all 5 projects
├── plan.md                             ← Developer's running architecture notes / intentions
├── sqlPlan.md                          ← Database schema planning notes
├── tillNow.md                          ← Progress log (what was built so far)
├── FIXES_README.md                     ← Bugs found and fixed, documented
├── FRONTEND_README.md                  ← Detailed frontend integration guide
├── PRODUCTION_CONNECTOR_README.md      ← Production deployment guide for the Windows agent
├── ROADMAP_README.md                   ← Future feature plans
├── InsidashTallyConnector.iss          ← Inno Setup script (creates the .exe installer)
├── .gitignore                          ← Ignores Web.config, bin/, obj/, secrets
│
├── Insidash.TallyApi/                  ← [PROJECT 1] ASP.NET Web API — the cloud backend
│   ├── Controllers/
│   │   ├── TallyApiController.cs       ← Main AI chat + data sync endpoints
│   │   └── ConnectorApiController.cs  ← Agent lifecycle (activate, version, sync-request)
│   ├── Filters/
│   │   └── SyncTokenAuthFilter.cs     ← Action filter that validates X-Sync-Token header
│   ├── App_Start/                      ← (WebApiConfig.cs, RouteConfig.cs) — routing setup
│   ├── CorsHandler.cs                  ← Custom CORS middleware (allows cross-origin JS)
│   ├── Global.asax / Global.asax.cs   ← Application startup — calls WebApiConfig.Register
│   ├── Program.cs                      ← OWIN self-host entry point (for local testing)
│   └── Web.config.example             ← Template for connection strings & API keys
│
├── Insidash.BLL/                       ← [PROJECT 2] Business Logic Layer
│   ├── Parsers/
│   │   ├── TallyXmlParser.cs          ← Parses Tally Prime's XML export into C# DTOs
│   │   └── ParsedTallyData.cs         ← Container class for parsed results
│   └── Services/
│       ├── IAIService.cs              ← Interface: ChatAsync(systemPrompt, userMessage)
│       ├── GroqAIService.cs           ← Calls Groq Cloud API (llama-3.3-70b)
│       ├── AIManager.cs               ← Orchestrates provider fallback chain
│       ├── Nl2SqlService.cs           ← Core: prompts LLM to generate SQL; validates it
│       └── TokenBudgetService.cs      ← Trims oversized JSON context before AI calls
│
├── Insidash.DAL/                       ← [PROJECT 3] Data Access Layer
│   ├── Context/
│   │   └── InsidashTallyContext.cs    ← EF6 DbContext — maps all entities to SQL tables
│   ├── Entities/                       ← C# POCOs mapped to DB tables
│   │   ├── TallyCompanyConfig.cs      ← Company ↔ SyncToken mapping
│   │   ├── TallySnapshot.cs           ← Raw JSON blobs (legacy approach)
│   │   ├── TallyLedger.cs             ← Synced ledger accounts
│   │   ├── TallyVoucher.cs            ← Synced financial transactions
│   │   ├── TallyStockItem.cs          ← Synced inventory items
│   │   ├── TallyBillOutstanding.cs    ← Synced receivables/bills
│   │   ├── TallySyncState.cs          ← Tracks last sync time per data type
│   │   ├── TallyQueryTemplate.cs      ← Pre-written SQL shortcuts
│   │   ├── TallyActivationKey.cs      ← Agent licensing keys
│   │   └── AIChatLog.cs               ← Audit log of every chat interaction
│   └── Repositories/
│       ├── ITallyRelationalRepository.cs  ← Interface for data write operations
│       ├── TallyRelationalRepository.cs   ← Bulk-copy insert + MERGE logic
│       ├── ITallySnapshotRepository.cs    ← Interface for snapshot reads
│       ├── TallySnapshotRepository.cs     ← Reads legacy JSON snapshots
│       ├── IAIChatLogRepository.cs        ← Interface for chat log writes
│       ├── AIChatLogRepository.cs         ← Inserts/reads AIChatLog rows
│       └── IntentRouterService.cs         ← Keyword-based template matcher
│
├── Insidash.TallyConnector/            ← [PROJECT 4] Windows tray application (agent)
│   ├── Program.cs                      ← Entry point; chooses tray vs. service mode
│   ├── TrayApplication.cs             ← WinForms ApplicationContext; tray icon + timer
│   ├── SyncEngine.cs                  ← Fetches from Tally, POSTs to API
│   ├── TallyEnvelopeFactory.cs        ← Builds TDL XML request envelopes for Tally
│   ├── ConnectorService.cs            ← Windows Service wrapper (alternate run mode)
│   ├── ActivationWindow.cs            ← First-run activation key entry UI
│   ├── SettingsWindow.cs              ← Change Tally host/port UI
│   ├── LocalConfig.cs                 ← Reads/writes connector config from disk
│   ├── MachineID.cs                   ← Generates hardware fingerprint for licensing
│   ├── AutoUpdater.cs                 ← Checks API for new version, downloads .exe
│   ├── ProjectInstaller.cs            ← Windows Service install metadata
│   ├── install.bat                    ← Registers as Windows Service via sc.exe
│   └── uninstall.bat                  ← Removes the Windows Service
│
├── Insidash.TallySyncAgent/            ← [PROJECT 5] Headless background worker (alternate)
│   └── (mirrors TallyConnector but runs without a tray icon)
│
├── InsidashTallyConnector.iss          ← Inno Setup: packages the agent into a .exe installer
│
└── frontend/                           ← [PROJECT 6] Embeddable chat widget (vanilla JS)
    ├── index.html                      ← Demo/test harness for the widget
    ├── css/
    │   └── talkwithtally.css          ← All widget styles (BEM-style scoped to .twt-*)
    └── js/
        ├── talkwithtally.js           ← Complete widget logic (IIFE module)
        └── vendor/                    ← jQuery + Bootstrap (bundled copies)
```

---

## Phase 2: Architecture Analysis

### Overall Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  USER'S BROWSER                                                 │
│  frontend/js/talkwithtally.js  (jQuery widget, embedded in     │
│  the existing Insidash web app)                                 │
└──────────────────────────────┬──────────────────────────────────┘
                               │ HTTP (AJAX with X-Sync-Token header)
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│  CLOUD SERVER (IIS / OWIN self-host)                            │
│  Insidash.TallyApi  —  ASP.NET Web API (.NET Framework 4.x)    │
│                                                                 │
│  TallyApiController    ConnectorApiController                   │
│   /api/tally/*          /api/connector/*                        │
│                                                                 │
│  ┌─────────────┐   ┌──────────────────────────────────────────┐ │
│  │  Insidash   │   │  Insidash.BLL                            │ │
│  │  .DAL       │   │  TallyXmlParser ← parses Tally XML       │ │
│  │  (EF 6 +   │   │  Nl2SqlService  ← builds LLM prompt      │ │
│  │   ADO.NET)  │   │  GroqAIService  ← calls Groq Cloud API   │ │
│  │             │   │  IntentRouter   ← template fast-path     │ │
│  └──────┬──────┘   └──────────────────────────────────────────┘ │
│         │                                                        │
└─────────┼────────────────────────────────────────────────────────┘
          │ SQL (SqlBulkCopy + EF6 + raw ADO)
          ▼
┌─────────────────────────────────────────────────────────────────┐
│  SQL SERVER  (Popway_BillingERP database)                       │
│  TallyLedger  TallyVoucher  TallyStockItem  TallyBillOut...     │
│  Customer  Invoice  Payment  PaymentMap  (existing ERP tables)  │
│  TallyCompanyConfig  TallyActivationKey  TallySyncState         │
│  AIChatLog  TallyQueryTemplate  TallySyncRequest  AISuggestion  │
└─────────────────────────────────────────────────────────────────┘
          ▲
          │ HTTP POST (raw TDL XML → JSON payload)
          │
┌─────────────────────────────────────────────────────────────────┐
│  USER'S WINDOWS MACHINE                                         │
│  Insidash.TallyConnector  (system tray agent / Windows Service) │
│  SyncEngine  ←→  Tally Prime (HTTP port 9000, TDL protocol)    │
└─────────────────────────────────────────────────────────────────┘
          ▲
          │ TDL XML over HTTP
          ▼
┌─────────────────────────────────────────────────────────────────┐
│  TALLY PRIME (running on the same or LAN machine)               │
│  Exposes an HTTP XML server on port 9000 (configurable)         │
└─────────────────────────────────────────────────────────────────┘

External call (from TallyApi backend):
TallyApiController  →  GroqAIService  →  Groq Cloud (api.groq.com)
                                         llama-3.3-70b-versatile
```

### Why this architecture?

The core challenge is that **Tally Prime runs on Windows, on-premise, at the business's site**. It cannot be accessed directly from the cloud. The connector agent solves this by acting as a local bridge: it pulls data from Tally and pushes it to the cloud SQL database. The AI then queries that SQL database — not Tally directly.

This is a **pull-and-store** (ETL-style) architecture rather than a live-proxy approach. Pros: queries are fast (SQL not XML); data survives if Tally goes offline. Cons: data is not real-time; full sync replaces old data.

---

## Phase 3: Tech Stack Deep Dive

| Technology | Role | Why chosen | Alternatives |
|---|---|---|---|
| **ASP.NET Web API (.NET Framework 4.x)** | Cloud API backend | Existing Insidash codebase is .NET Framework; no migration cost | ASP.NET Core, Node.js, Flask |
| **Entity Framework 6** | ORM for reads + schema management | Same reason — matches existing project conventions | Dapper, raw ADO.NET |
| **ADO.NET SqlBulkCopy** | High-speed bulk inserts during sync | EF's `AddRange` is extremely slow for 1000+ rows; SqlBulkCopy bypasses row-by-row inserts | EF bulk extensions, MERGE via TVP |
| **SQL Server (Popway_BillingERP)** | Single shared database | Already hosts the parent ERP system; Tally tables are additive | PostgreSQL, MySQL |
| **Groq Cloud API (llama-3.3-70b-versatile)** | LLM for NL→SQL and response formatting | Free tier, extremely fast inference (~300 tokens/sec); OpenAI-compatible API | OpenAI GPT-4o, Claude, Ollama |
| **WinForms** | System tray connector UI | .NET Framework WinForms is the simplest way to create a tray icon + balloon tips on Windows | WPF, Electron, console-only |
| **Windows Service** | Alternate headless deployment | Allows the connector to run without a logged-in user (background service) | Scheduled Task |
| **Inno Setup (.iss)** | Installer packaging | Simple, free, well-documented Windows installer builder | WiX, NSIS, ClickOnce |
| **jQuery + Bootstrap 5** | Frontend widget | Insidash already uses jQuery; no new dependencies needed | React, Vue, plain fetch API |
| **Newtonsoft.Json** | JSON serialisation | Ubiquitous in .NET Framework projects | System.Text.Json (only in .NET Core+) |
| **LINQ to XML (XDocument)** | Tally XML parsing | Simple, built-in, schema-agnostic | XmlSerializer, XmlDocument, SAX |

---

## Phase 4: End-to-End Workflow

### Path A: Sync (Data from Tally → SQL)

```
1. TrayApplication timer fires every N ms (default: 5 min = 300,000ms)
   OR user clicks "Sync Now" in dashboard (creates TallySyncRequest row)
   OR user right-clicks tray → "Sync Now"

2. TrayApplication calls: engine.RunFullSyncAsync()

3. SyncEngine loops over ["Ledger", "Voucher", "StockItem", "BillOutstanding"]

4. For each dataType:
   a. TallyEnvelopeFactory.Build(dataType)
      → Returns a TDL XML string (Tally Definition Language)
      e.g. for Ledger:
        <ENVELOPE><HEADER><TYPE>Collection</TYPE><ID>Ledger</ID>...

   b. SyncEngine POSTs that XML to Tally's HTTP server
      URL: http://localhost:9000  (configurable in LocalConfig)
      Tally responds with XML like:
        <ENVELOPE><BODY><DATA><COLLECTION>
          <LEDGER NAME="HDFC Bank">
            <PARENT>Bank Accounts</PARENT>
            <CLOSINGBALANCE>150000.00 Dr</CLOSINGBALANCE>
          </LEDGER>
          ...

   c. SyncEngine reads rawXml from response

   d. Wraps in JSON payload: { DataType: "Ledgers", RawXml: "<ENVELOPE>..." }

   e. POSTs to cloud API:
      POST https://your-server.com/api/tally/sync
      Header: X-Sync-Token: <token from LocalConfig>

5. Cloud API: TallyApiController.Sync()

   a. SyncTokenAuthFilter validates X-Sync-Token against TallyCompanyConfig table
      → Identifies which CompanyID this token belongs to

   b. Routes on payload.DataType:
      "ledgers"         → _parser.ParseLedgersToDto(rawXml)
      "vouchers"        → _parser.ParseVouchersToDto(rawXml)
      "stockitems"      → _parser.ParseStockItemsToDto(rawXml)
      "billoutstandings"→ _parser.ParseBillOutstandingsToDto(rawXml)

6. TallyXmlParser:
   a. SanitizeXml() — strips invalid XML characters (common in Tally exports)
   b. XDocument.Parse(cleanXml)
   c. LINQ queries on XDocument elements/attributes
   d. For ClosingBalance: parses "150000.00 Dr" → -150000.00 (negative = debit)
   e. Returns List<TallyXxxDto>

7. TallyApiController maps DTOs to EF entity objects

8. TallyRelationalRepository.SaveXxx():
   - For Ledgers/StockItems/BillOutstandings:
       DELETE FROM table WHERE CompanyID = @cid  (full replace)
       SqlBulkCopy into table (batch size 500)
   - For Vouchers:
       CREATE TABLE #TallyVoucherStaging (...)
       SqlBulkCopy into staging
       MERGE TallyVoucher USING staging ON VoucherID + CompanyID
         → UPDATE existing, INSERT new
       DROP TABLE #TallyVoucherStaging

9. UpsertSyncState() updates TallySyncState row
   (CompanyID + DataType → LastSyncedAt, RecordsSynced)

10. API returns: { status: "synced", companyId, dataType, recordsProcessed }
```

### Path B: Chat (User query → AI answer)

```
1. User types question in widget, clicks Send (or presses Enter)
   e.g. "Show me all pending payments from December 2025"

2. talkwithtally.js: onSend()
   - Renders user bubble in chat area
   - Shows typing indicator (animated dots)
   - POSTs to API:
     POST /api/tally/chat
     Header: X-Sync-Token: <token>
     Body: { companyId: 5, message: "Show me all pending payments..." }

3. TallyApiController.Chat()

4. ValidateCompanyAccess() — validates token + confirms companyId matches

5. Stopwatch starts (for ExecutionTime logging)

6. IntentRouterService.TryMatchTemplate(message)
   - Loads all active TallyQueryTemplate rows from DB
   - For each template: splits Keywords by comma
   - Checks if any keyword appears in lowercased question
   - Example: question has "outstanding" → matches "Outstanding Receivables" template
   - If matched: returns pre-written SQL string with @CompanyID placeholder
     → replaces @CompanyID with actual companyId integer
     → sets log.ResponseType = "Fast"

7. If NO template matched → Nl2SqlService.GenerateSqlAsync(question, companyId)

   a. Builds a detailed system prompt (inlined into code):
      - Lists all 8 tables with every column name, type, and comment
      - States rules: SELECT only, always filter CompanyID, no invented columns
      - Lists schema gotchas: "Invoice uses Date2 not InvoiceDate", etc.
      - Instructs: respond with ```sql <query> ``` ONLY

   b. Calls GroqAIService.ChatAsync(systemPrompt, userQuestion)
      → POST https://api.groq.com/openai/v1/chat/completions
        {
          model: "llama-3.3-70b-versatile",
          temperature: 0.3,    ← low = more deterministic SQL
          max_tokens: 1024,
          messages: [
            { role: "system", content: "<schema + rules>" },
            { role: "user",   content: "Show me all pending payments..." }
          ]
        }

   c. Groq returns: choices[0].message.content
      = "```sql\nSELECT v.PartyName, SUM(v.Amount)...\n```"

   d. ExtractSqlFromJson() strips markdown fences
   e. Returns raw SQL string

8. IsSafeSelect(generatedSql) — security gate:
   - Must start with SELECT (or EXEC sp_Tally*)
   - Must NOT contain: INSERT, UPDATE, DELETE, DROP, EXEC, XP_, OPENROWSET...
   - Must contain "COMPANYID" (prevents cross-tenant data leaks)
   - If unsafe → returns error response immediately (never executes SQL)

9. TallyRelationalRepository.ExecuteQueryToDynamicJson(sql)
   - Opens read-only connection (InsidashTallyDbReadOnly connection string)
   - cmd.CommandTimeout = 15  (hard 15-second limit)
   - Executes the SQL
   - Loads results into DataTable
   - JsonConvert.SerializeObject(dataTable)
   - Returns: "[{\"PartyName\":\"Raj Traders\",\"SUM\":15000}...]"

10. If empty result → GetSmartNoDataMessage()
    - Checks TallySyncState: was last sync > 30 min ago? → warn about stale data
    - TrySuggestSimilarName(): fuzzy-matches words from user question against Customer table
    - Returns helpful message like "Did you mean: Raj Traders, Rajesh & Co.?"

11. Nl2SqlService.FormulateResponseAsync(question, sql, jsonResults)
    - Second Groq call with a different system prompt:
      "You are TalkWithTally, an AI accounting assistant. Format results in INR (₹)."
    - User message = question + generated SQL + JSON results
    - LLM formats the answer in natural language:
      "Here are the pending payments for December 2025:
       • Raj Traders: ₹15,000
       • Sharma Enterprises: ₹8,500
       Total outstanding: ₹23,500"

12. AIChatLogRepository.InsertLog():
    - Records: CompanyID, UserQuestion, AIResponse, SQLQuery,
               ResponseType (Fast/AI_Success/AI_NoData/AI_Timeout/etc.),
               ExecutionTime (ms), IsSuccess, ErrorMessage

13. Returns to frontend:
    { response: "Here are the pending...", sql: "SELECT...", success: true }

14. talkwithtally.js: $typing.remove()
    renderAIResponse(res.response)
    - looksLikeTable() heuristic: if 3+ "Key: Value" lines → renders as HTML table
    - Otherwise: renders as text bubble with <br> newlines
```

---

## Phase 5: Code-Level Explanation

### `TallyApiController.cs` — The Central Hub

This is the most important file in the project. It handles every interaction the frontend has with the backend. Key design decisions:

**Authentication pattern**: Rather than session cookies or JWT, it uses a static `X-Sync-Token` header. This token lives in `TallyCompanyConfig.SyncToken` and is issued once during activation. It acts as both authentication and company identification — a single token implicitly identifies a company.

**Hardcoded CompanyID fallback** (line near end of `GetAuthenticatedCompanyId()`):
```csharp
return 5; // for debugging and testing only, comment this in production
```
This is a development artifact. In production, this must be removed — it would expose company 5's data to any unauthenticated request reaching that code path.

**Hardcoded file paths** in the Sync endpoint:
```csharp
System.IO.File.WriteAllText(@"c:\AAA_STUDY\Internship_Tasks\TalkWithTally\stock_items_raw.xml", payload.RawXml);
```
These are debug artifacts from the developer's local machine path. They would fail silently on any other server (the try/catch swallows the error). Remove before production.

### `Nl2SqlService.cs` — The AI Brain

This is where natural language becomes SQL. The two-stage AI pipeline is elegant:

**Stage 1 prompt** is carefully engineered:
- Lists every table, every column, every type — the LLM has full schema context
- States explicit anti-hallucination rules ("NEVER use a column name not in the above list")
- Documents schema quirks that would trip up a naive LLM ("Invoice has Date2, not InvoiceDate")
- Sets temperature to 0.3 to make SQL generation deterministic
- Defines a sentinel escape hatch: if the query is impossible, return `{"sql":"UNSUPPORTED"}`

**Stage 2 prompt** is simpler:
- Gives the LLM the question, the SQL, and the results
- Asks it to format a human-readable financial answer in INR

**IsSafeSelect()** is the security firewall. It uses word-boundary regex (`\bEXEC\b`) to catch injection attempts. However, it has a notable limitation: it only checks for `COMPANYID` as a string in the query — it does not verify the value is correct, just that the column name appears. A malicious prompt could include `WHERE CompanyID = 5 -- correct company` but also query another company's data through a JOIN. This is a known tradeoff for MVP simplicity.

### `TallyXmlParser.cs` — The XML Bridge

The `SanitizeXml()` method is important and non-trivial. Tally Prime's XML export sometimes contains control characters (like `&#4;`) that are technically invalid XML. If you try to parse raw Tally XML without sanitization, `XDocument.Parse()` throws an exception. The sanitizer:
1. Uses regex to find all `&#nnn;` and `&#xHH;` entities
2. Checks if each code point is in the valid XML character range (per the XML 1.0 spec)
3. Strips any that aren't valid

`ParseClosingBalance()` handles Tally's unusual number format where sign is a suffix:
- `"150000.00 Cr"` → positive decimal (+150000.00) — credit balance
- `"75000.00 Dr"` → negative decimal (-75000.00) — debit balance

This sign convention is maintained throughout: in `TallyLedger`, negative ClosingBalance = the account has a debit balance (asset or expense-side).

### `TallyRelationalRepository.cs` — Data Persistence

The `SaveLedgers()`, `SaveStockItems()`, and `SaveBillOutstandings()` methods all use the same pattern: **delete-then-bulk-insert** within a transaction. This is a full replace strategy — every sync wipes the company's old data and replaces it fresh. This is fast and simple but means there is no historical ledger data; only the current snapshot from Tally is kept.

`SaveVouchers()` is different — it uses a **staging table + MERGE** pattern. This is significantly more sophisticated:
1. Creates a temp table `#TallyVoucherStaging`
2. Bulk-copies new vouchers into the staging table
3. Executes a `MERGE` statement that updates existing vouchers (matched by VoucherID + CompanyID) and inserts new ones
4. Drops the staging table

Why the difference? Vouchers represent individual financial transactions. Tally assigns each one a unique GUID (`GUID` element). If you deleted and re-inserted all vouchers on every sync, you'd break any foreign key relationships or analytics tables that reference VoucherIDs. The MERGE preserves existing rows while adding new ones.

### `SyncEngine.cs` — The Agent Pump

The two static `HttpClient` instances (`_tallyClient` and `_apiClient`) deserve explanation. In .NET, `HttpClient` should be **reused as a singleton** — creating a new one per request causes socket exhaustion (a known .NET bug). Using `static readonly` ensures one instance per client type for the entire application lifetime.

The `CheckForManualSyncRequestAsync()` method implements a **polling pattern** for the "Sync Now" feature. The frontend creates a row in `TallySyncRequest`. The agent, on every 10-second timer tick, polls `/api/connector/check-sync-request`. If a pending row exists, the agent triggers an immediate full sync. This avoids the need for WebSockets or push notifications — the agent always initiates contact.

### `TallyEnvelopeFactory.cs` — Talking TDL

Tally Prime exposes its data through a proprietary XML protocol called TDL (Tally Definition Language). To request data, you POST a TDL "envelope" to Tally's HTTP server. The factory builds four envelope types:

- **Ledger**: Requests the built-in `Ledger` collection
- **Voucher**: Requests the `Day Book` with a date range from 2000-01-01 to tomorrow (captures full history)
- **StockItem**: Requests `StockItem` collection with a `FETCHLIST` specifying exactly which fields to return
- **BillOutstanding**: Uses TDL's custom collection syntax with a filter (`$$IsDebtors:$PARENT`) to return only sundry debtor ledgers with their bill details

The BillOutstanding envelope is the most complex — it defines an inline TDL custom collection (`MyOutstandingCollection`) that filters ledgers to only debtors and fetches their nested bill details. This requires knowledge of Tally's internal data model.

### `TrayApplication.cs` — The Windows Agent UI

The 10-second timer (`Interval = 10000`) runs `PollSyncTimerTickAsync()` which makes two decisions:
1. Has `SyncIntervalMs` elapsed since last sync? → trigger scheduled sync
2. Is there a pending manual sync request in the DB? → trigger immediate sync

Both paths call the same `RunFullSyncAsync()`. The `_lastSyncTime` field ensures the scheduled sync doesn't fire twice in a row.

The `UpdateTooltip()` method marshals to the UI thread via `SynchronizationContext.Post()`. WinForms controls can only be updated from the thread that created them — `SynchronizationContext` is the .NET way to post work back to the UI thread from background async code.

### `talkwithtally.js` — The Frontend Widget

The entire widget is a **revealing module pattern** (IIFE — Immediately Invoked Function Expression):
```javascript
const TWT = (function ($) { ... })(jQuery);
```
This creates a private scope. Only `TWT.init` is exported. All internal variables (`_isOpen`, `_isSending`, etc.) are hidden from the global scope. This prevents conflicts when the widget is embedded in an existing web application that also uses jQuery.

The widget has three "screens" inside a single div: `splash` (first-time welcome), `not-connected` (connector not installed), and `chat` (main interface). Screen transitions are driven by adding/removing the `twt-hidden` CSS class.

The `looksLikeTable()` heuristic is a simple but effective check: if 3 or more lines in the AI response contain a colon with exactly 2 parts, it's probably a key-value list (ledger name: balance). In that case, `buildTableBubble()` renders it as an HTML table instead of plain text.

---

## Phase 6: Database and Data Model

### Full Schema (Entity-Relationship Overview)

```
Company (existing ERP table)
   │  PK: CompanyID (INT)
   │
   ├─── TallyCompanyConfig   (1:1 per company)
   │       ConfigID (NVARCHAR PK, GUID)
   │       CompanyID (INT, FK → Company)
   │       SyncToken (NVARCHAR 255, UNIQUE) ← token used by connector + API calls
   │       IsActive (BIT)
   │
   ├─── TallyActivationKey   (1:many per company)
   │       KeyID (NVARCHAR PK)
   │       CompanyID (INT, FK)
   │       ActivationKey (NVARCHAR 32, UNIQUE) ← shown to admin (e.g. "5038112A19534996")
   │       SyncToken (NVARCHAR 64, UNIQUE) ← internal secret (given to connector on activation)
   │       IsActivated (BIT), ActivatedAt, MachineID, AgentVersion
   │       IsActive, ExpiresAt
   │
   ├─── TallyLedger           (1:many per company, full-replace on sync)
   │       LedgerID (NVARCHAR 50, PK, GUID)
   │       CompanyID (INT)
   │       Name (NVARCHAR 255)       ← "HDFC Bank", "Sales", "Raj Traders"
   │       Parent (NVARCHAR 255)     ← "Bank Accounts", "Sales Accounts"
   │       ClosingBalance (DECIMAL)  ← negative = Dr, positive = Cr
   │       SyncedAt (DATETIME)
   │
   ├─── TallyVoucher          (1:many per company, MERGE on sync)
   │       VoucherID (NVARCHAR 50, PK, Tally GUID)
   │       CompanyID (INT)
   │       Date (DATE)         ← transaction date
   │       VchType (NVARCHAR)  ← "Sales", "Payment", "Receipt", "Journal"
   │       PartyName (NVARCHAR)← the counter-party ledger
   │       Amount (DECIMAL 18,2)
   │       Narration (NVARCHAR MAX)
   │       SyncedAt (DATETIME)
   │
   ├─── TallyStockItem        (1:many per company, full-replace on sync)
   │       StockItemID (NVARCHAR 50, PK, GUID)
   │       CompanyID (INT)
   │       Name (NVARCHAR 255)       ← product name
   │       Parent (NVARCHAR 255)     ← stock group
   │       Unit (NVARCHAR 50)        ← "Nos", "Kgs", "Pcs"
   │       ClosingQty (DECIMAL 18,4) ← current quantity in stock
   │       ClosingValue (DECIMAL 18,2)← valuation
   │
   ├─── TallyBillOutstanding  (1:many per company, full-replace on sync)
   │       BillID (NVARCHAR 50, PK, GUID)
   │       CompanyID (INT)
   │       PartyName (NVARCHAR 255)  ← customer/debtor name
   │       BillDate (DATE)
   │       BillRef (NVARCHAR 100)    ← Tally bill reference number
   │       Amount (DECIMAL 18,2)     ← outstanding amount
   │       DueDate (DATE, nullable)
   │
   ├─── TallySyncState        (1 row per CompanyID + DataType combination)
   │       SyncStateID (INT, IDENTITY, PK)
   │       CompanyID (INT)
   │       DataType (NVARCHAR 50) ← "Ledger", "Voucher", "StockItem", "BillOutstanding"
   │       LastSyncedAt (DATETIME)
   │       LastSyncStatus (NVARCHAR)← "Success", "Failed", "Never"
   │       RecordsSynced (INT)
   │       UNIQUE(CompanyID, DataType)
   │
   ├─── AIChatLog             (1 row per chat message sent)
   │       LogID (NVARCHAR 50, PK, GUID)
   │       CompanyID (INT)
   │       UserQuestion (NVARCHAR MAX)
   │       AIResponse (NVARCHAR MAX)
   │       ResponseType (NVARCHAR 50) ← "Fast", "AI_Success", "AI_NoData", "AI_Timeout"...
   │       SQLQuery (NVARCHAR MAX)
   │       ExecutionTime (INT, ms)
   │       IsSuccess (BIT)
   │       ErrorMessage (NVARCHAR MAX)
   │       CreatedDate (DATETIME)
   │
   └─── TallySyncRequest      (transient: created by frontend, deleted by agent)
           RequestID (NVARCHAR 50, PK, GUID)
           CompanyID (INT)
           RequestedAt (DATETIME)
           IsProcessed (BIT)
           ProcessedAt (DATETIME)

TallyQueryTemplate  (global, not per-company)
    TemplateID (INT IDENTITY, PK)
    Name (NVARCHAR 100)
    Keywords (NVARCHAR 500)  ← comma-separated trigger words
    SqlQuery (NVARCHAR MAX)  ← pre-written SQL with @CompanyID placeholder
    IsActive (BIT)

AIDomain + AISuggestion  (existing Insidash tables, extended for Tally)
    Domain: Name='Tally', DomainID=4
    Suggestions: "Ledger Balance", "Trial Balance", "Top Debtors", etc.
```

### Key Schema Design Decisions

**Why GUIDs as primary keys for Tally tables?** Tally assigns its own GUIDs to objects (Ledgers, Vouchers). Using those as PKs allows the MERGE operation to correctly identify existing vouchers. For Ledgers and StockItems (which use full-replace), new GUIDs are generated each sync (`Guid.NewGuid()`), which is wasteful but harmless since those PKs are never referenced by other tables.

**Why `NVARCHAR(50)` for numeric-looking PKs?** This accommodates Tally's GUID format which is a string like `"{7a5c...}"`. Tally GUIDs include curly braces.

**Why `TallyCompanyConfig` AND `TallyActivationKey` both have `SyncToken`?** These serve different purposes. `TallyActivationKey.SyncToken` is the token issued at activation (the connector stores this). After activation, the connector uses this token for all subsequent API calls. `TallyCompanyConfig.SyncToken` is a simpler lookup table that maps token → companyID. In practice the tokens are the same value — `TallyActivationKey.SyncToken` is what gets stored in `TallyCompanyConfig.SyncToken` (this linkage is not explicitly shown in code but is implied by the architecture).

---

## Phase 7: API Analysis

### `TallyApiController` — Route prefix: `api/tally`

| Route | Method | Auth | Purpose |
|---|---|---|---|
| `POST /api/tally/sync` | POST | `SyncTokenAuthFilter` | Receives XML from connector, parses, saves to SQL |
| `POST /api/tally/chat` | POST | `ValidateCompanyAccess()` | Processes NL question → SQL → LLM answer |
| `GET /api/tally/history/{companyId}` | GET | `ValidateCompanyAccess()` | Paginated chat history |
| `GET /api/tally/status/{companyId}` | GET | `ValidateCompanyAccess()` | Ledger/voucher counts + last sync timestamps |
| `GET /api/tally/sync-status` | GET | `GetAuthenticatedCompanyId()` | Connected/not-connected status for frontend |
| `POST /api/tally/sync-now` | POST | `GetAuthenticatedCompanyId()` | Inserts `TallySyncRequest` row to trigger agent |
| `GET /api/tally/suggestions` | GET | None | Returns `AISuggestion` chip list for chat UI |

### `ConnectorApiController` — Route prefix: `api/connector`

| Route | Method | Auth | Purpose |
|---|---|---|---|
| `POST /api/connector/activate` | POST | None (key itself is auth) | First-run: validates activation key, returns sync token |
| `GET /api/connector/version` | GET | None | Returns latest agent version from PatchUpdate table |
| `GET /api/connector/check-sync-request` | GET | X-Sync-Token | Agent polling: is there a pending manual sync? |
| `POST /api/connector/mark-sync-processed` | POST | X-Sync-Token | Agent confirms it processed the sync request |

### Full lifecycle of a `POST /api/tally/chat` call

```
1. Widget JS sends:
   POST /api/tally/chat
   Headers: X-Sync-Token: <token>, Content-Type: application/json
   Body: { "companyId": 5, "message": "Show pending bills over 90 days" }

2. Web API routing matches TallyApiController.Chat(ChatRequest request)

3. ModelBinding deserializes body → ChatRequest { CompanyId=5, Message="..." }

4. ValidateCompanyAccess(5):
   - Reads X-Sync-Token header
   - Queries: SELECT CompanyID FROM TallyCompanyConfig WHERE SyncToken=@token AND IsActive=1
   - Checks result.CompanyID == 5 → true → proceed

5. Stopwatch.Start()

6. AIChatLog created with defaults (IsSuccess=false, ResponseType="AI_Exception")

7. Read GROQ_API_KEY from Web.config <appSettings>

8. Instantiate: GroqAIService(apiKey), Nl2SqlService(groqService), IntentRouterService()

9. IntentRouterService.TryMatchTemplate("Show pending bills over 90 days"):
   - Loads TallyQueryTemplate WHERE IsActive=1
   - Splits keywords: "outstanding" matches "pending,receivable,due,overdue,aging"
   - If matched → returns stored SQL template
   - In this case: "Show pending bills over 90 days" → matches "Outstanding Receivables"?
     Actually: "pending bills" contains "pending" → matches template
   - Returns: "EXEC sp_TallyReceivablesAging @CompanyID"
     after replace: "EXEC sp_TallyReceivablesAging 5"

10. IsSafeSelect("EXEC sp_TallyReceivablesAging 5"):
    - Starts with "EXEC SP_TALLY" → enters whitelist check
    - "EXEC SP_TALLYRECEIVABLESAGING" is in whitelist → PASS
    - No semicolons, no UNION/SELECT inside → PASS

11. ExecuteQueryToDynamicJson("EXEC sp_TallyReceivablesAging 5"):
    - Uses read-only connection string
    - Executes stored procedure with 15s timeout
    - Returns JSON array of party/amount/aging buckets

12. JSON is non-empty → proceed to formulation

13. FormulateResponseAsync():
    - Second Groq call with formatted results
    - Returns natural language summary

14. finally block: AIChatLogRepository.InsertLog(log)

15. Return: { response: "...", sql: "EXEC sp_TallyReceivablesAging 5", success: true }
```

---

## Phase 8: AI and Tally Integration Deep Dive

### The Two AI Calls

Every successful non-template chat query makes **two sequential LLM calls**:

**Call 1 — SQL Generation** (Nl2SqlService.GenerateSqlAsync):
- Temperature: 0.3 — low randomness for deterministic SQL
- System prompt: ~1500 tokens of schema + rules
- User message: the user's question
- Expected output: a single SQL query in a markdown code block
- The LLM plays the role of a "SQL translator" with full schema awareness

**Call 2 — Response Formulation** (Nl2SqlService.FormulateResponseAsync):
- Temperature: not explicitly set (defaults to Groq's default, ~1.0)
- System prompt: TalkWithTally persona + "format in INR"
- User message: original question + SQL + JSON results
- Expected output: a human-friendly financial summary
- The LLM plays the role of a "financial advisor interpreter"

### The Template Fast-Path

The `IntentRouterService` is a simple keyword router that short-circuits the AI for common queries. It loads `TallyQueryTemplate` from the database. When a query matches, the pre-written stored procedure call is used directly — no LLM call for SQL generation. This provides:
- Faster responses (no Groq latency for SQL generation)
- Guaranteed correct SQL for complex reports (P&L, Balance Sheet)
- Lower API costs

The templates are seeded in the DB and can be modified by admins without redeploying code — a powerful configuration point.

### Why Groq Instead of OpenAI or Claude?

Three reasons visible in the code:
1. Groq's free tier is generous for a prototype
2. Speed: llama-3.3-70b on Groq is extremely fast (~0.3s for 1024 tokens)
3. The API is OpenAI-compatible — switching to OpenAI or another provider requires only changing the URL and model name in `GroqAIService.cs`

The `AIManager.cs` was designed with a fallback chain (first Claude, then Groq). Claude support is stubbed out (`// ClaudeAIService will be implemented in a future phase`). The `IAIService` interface makes adding new providers trivial.

---

## Phase 9: Development Environment

### Required Software

| Software | Purpose | Version Notes |
|---|---|---|
| Visual Studio 2022 | .NET Framework development | The .csproj files are old-style SDK format |
| .NET Framework 4.x | Target runtime for all projects | Check each .csproj's `<TargetFrameworkVersion>` |
| SQL Server (Express OK) | Database | Installs alongside the existing Insidash ERP |
| Tally Prime | Accounting software (required for sync testing) | Must have HTTP server enabled |
| Inno Setup | Building the installer | Only needed for deployment packaging |

### Environment Variables / Configuration

In `Insidash.TallyApi/Web.config` (not in repo — use `Web.config.example` as template):

```xml
<connectionStrings>
  <add name="InsidashTallyDb"
       connectionString="Server=.;Database=Popway_BillingERP;User Id=sa;Password=xxx;" />
  <add name="InsidashTallyDbReadOnly"
       connectionString="Server=.;Database=Popway_BillingERP;User Id=tally_readonly_user;Password=TallyRead@2024!;" />
</connectionStrings>
<appSettings>
  <add key="GROQ_API_KEY" value="gsk_xxx" />
  <add key="CLAUDE_API_KEY" value="PUT_CLAUDE_KEY_HERE" />  <!-- stub, unused -->
</appSettings>
```

In `Insidash.TallyConnector/App.config` (stored on the Windows machine):
- `ApiBaseUrl` — the cloud API URL
- `SyncToken` — populated by `LocalConfig.cs` after activation
- `TallyHost` / `TallyPort` — set by `SettingsWindow`

### Installation Steps (Development)

1. Clone the repository
2. Copy `Web.config.example` → `Web.config`, fill in connection strings and Groq key
3. Open `TalkWithTally.sln` in Visual Studio
4. Run the SQL scripts from the reference document (in order: 5 → 9 → 20 → 21 → 23 → 28 → 29 → 30 → 36 → 37)
5. Run `EXEC sp_GenerateTallyActivationKey @CompanyID = 5` to generate a test activation key
6. Set `Insidash.TallyApi` as startup project, press F5 (runs on localhost:8081 via OWIN)
7. In the connector project, set `ApiBaseUrl = http://localhost:8081`, run it
8. Enter the activation key in the popup
9. Open `frontend/index.html` in a browser, set `TWT_SYNC_TOKEN` and `TWT_COMPANY_ID` in the script
10. Click the chat bubble

---

## Phase 10: Design Patterns and Engineering Decisions

### Patterns Used

**Repository Pattern** (`TallyRelationalRepository`, `AIChatLogRepository`, `TallySnapshotRepository`): Data access is abstracted behind interfaces. Controllers and services depend on the interface, not the concrete class. This enables testing (mock the repository) and future database changes.

**Service Layer** (`Nl2SqlService`, `GroqAIService`, `AIManager`): Business logic is separated from the API layer. The controller orchestrates but doesn't contain logic.

**Strategy / Polymorphism** (`IAIService`): `GroqAIService` implements `IAIService`. `AIManager` holds a `List<IAIService>` and iterates with fallback. Adding a new AI provider means implementing the interface and registering in `AIManager` — no existing code changes needed.

**Filter Attribute** (`SyncTokenAuthFilter`): Cross-cutting authentication concern extracted into a reusable attribute. Applied to endpoints that require connector-level auth.

**Revealing Module** (`talkwithtally.js`): Frontend JS uses an IIFE to create a private scope, preventing global namespace pollution. Only the `init` method is public.

**Factory Method** (`TallyEnvelopeFactory`): Static factory that produces the appropriate TDL XML string based on a `dataType` string. Centralizes Tally XML knowledge in one place.

**Template Method / Seeded Templates**: `TallyQueryTemplate` table stores named SQL queries. `IntentRouterService` applies keyword matching to select the right template. This is a database-driven strategy pattern — logic is in data, not code.

### Potential Code Smells

1. **Hardcoded `return 5`** in `GetAuthenticatedCompanyId()` — development debug artifact, must be removed for production
2. **Hardcoded file paths** (`c:\AAA_STUDY\...`) in `TallyApiController.Sync()` — dev artifacts, should be removed
3. **New instances per request**: `new TallyXmlParser()`, `new TallySnapshotRepository()`, etc. are created in the controller constructor. These are stateless, but it's a pattern that doesn't scale to DI containers well
4. **No dependency injection**: The entire project uses `new` for creating services. This makes unit testing hard (can't inject mocks) and violates the Dependency Inversion principle
5. **`IntentRouterService` is in DAL**: The intent router loads DB templates and does string matching — this is business logic, and should be in BLL, not DAL
6. **Tenant isolation relies on `COMPANYID` string check only**: `IsSafeSelect()` confirms the word "CompanyID" appears in the SQL but cannot verify the value is correct. A crafted prompt might include `CompanyID = 5 UNION SELECT ... FROM TallyLedger WHERE 1=1` and bypass isolation

### Scalability Considerations

The current architecture is designed for single-tenant (one company) or low-concurrency multi-tenant use. For scale:
- The delete-then-bulk-insert sync strategy blocks reads on the table during sync for large datasets
- The MERGE for vouchers is much better — it's an atomic upsert
- The LLM calls (2 per query) add 1-3 seconds of latency — acceptable for interactive chat
- SQL queries have a 15-second hard timeout — appropriate for complex aggregations
- `TokenBudgetService` trims JSON context to prevent token limit overflows — important for companies with large datasets

---

## Phase 11: SQL Reference Document — Deep Analysis

### Script 1: `CREATE DATABASE InsidashTallySandbox`
Creates a scratch database for development. **Why**: Allows testing DB scripts without touching the production `Popway_BillingERP`. **If removed**: Development would require always working against production.

### Script 2: `SELECT SYSTEM_USER, IS_SRVROLEMEMBER('sysadmin')`
Diagnostic check. **Why**: Confirms the executing login has `sysadmin` role, which is required to create tables and users. **If removed**: Scripts would fail silently with permission errors.

### Script 3: `INFORMATION_SCHEMA.COLUMNS` inspection
Confirms `Company.CompanyID` column exists and its data type. **Why**: The FK constraint `FK_TallyConfig_Company` requires `CompanyID` to be INT. If it's VARCHAR in the target DB, the FK would fail to create. **Performance**: Zero impact — metadata query.

### Script 4: `SELECT TOP 10 CompanyID, Name FROM Company`
Lists available companies to find a real `CompanyID` for seeding. **Why**: You need to know what CompanyID values exist before you can seed `TallyCompanyConfig`. **Expected output**: Table of company IDs and names.

### Script 5: Create `TallyCompanyConfig`, `TallySnapshot`, `AIChatLog` — Sandbox
The foundational table creation script. Uses `IF OBJECT_ID(...) IS NULL` guards so it's idempotent — safe to re-run. Creates all three with FK constraints to `Company`.

**TallyCompanyConfig**: The authentication table. Every API call is traced back to a company through this table's `SyncToken` column.

**TallySnapshot**: The original (legacy) storage model — stores parsed Tally data as a JSON blob in `JsonContent`. This was the first approach before the relational model was built. It still exists in the codebase but is effectively replaced by the individual `TallyLedger`, `TallyVoucher`, etc. tables.

**AIChatLog**: The full audit trail of every AI interaction. `ResponseType` is an internal classification system:
- `Fast` — served from template, no LLM SQL generation
- `AI_Success` — LLM generated SQL, data found, response formulated
- `AI_NoData` — LLM SQL worked but returned 0 rows
- `AI_NoSQL` — LLM couldn't produce SQL (conversational/unclear question)
- `AI_Timeout` — SQL execution exceeded 15 seconds
- `AI_UnsafeSQL` — LLM generated a non-SELECT query (safety blocked)
- `AI_Exception` — unexpected error

### Script 9: Create `TallyLedger` and `TallyVoucher`
Creates the two most important Tally data tables.

`TallyLedger.ClosingBalance` is `DECIMAL(18,2)`. The sign convention: negative = debit (Dr), positive = credit (Cr). This matches standard accounting where assets (Dr) are positive in the balance sheet but negative in this representation — a subtlety that the AI prompt explicitly explains to the LLM.

Indexes on `CompanyID` are critical — almost every query filters by CompanyID. Without these indexes, every chat query would cause a full table scan.

### Script 18: Create Read-Only Login `tally_readonly_user`
This is the **database security boundary** for the AI. The `InsidashTallyDbReadOnly` connection string (used in `ExecuteQueryToDynamicJson`) maps to this login. It has:
- SELECT on Tally tables and ERP tables
- Explicit DENY on INSERT/UPDATE/DELETE

This means even if the LLM somehow generates a write query that passes `IsSafeSelect()` (which it can't, since that check is separate), the database itself would reject it. **Two-layer defence**.

### Script 20: Create `TallySyncState` + seed rows
One row per (CompanyID, DataType) combination. The `UNIQUE(CompanyID, DataType)` constraint prevents duplicate state rows. `UpsertSyncState()` in the API controller relies on this uniqueness to do an update-if-exists, insert-if-not pattern.

**Why this matters**: The frontend's "sync status" display is driven entirely by this table. When the agent syncs successfully, `TallySyncState.LastSyncedAt` is updated. The frontend polls `/api/tally/sync-status` which reads this table to decide whether to show the green "connected" dot or the amber "stale data" warning.

### Script 21: Create `TallyQueryTemplate` + seed P&L, Balance Sheet, Receivables
The three seeded templates are the "power queries" that would be very hard for an LLM to generate correctly on its own:

**P&L template** uses `CASE WHEN Parent IN (...)` to classify ledger accounts into Revenue and Expenses buckets. This requires knowledge of Tally's default group names ("Sales Accounts", "Direct Expenses", etc.).

**Balance Sheet template** similarly classifies by parent groups into Assets, Liabilities, and Capital.

**Outstanding Receivables template** uses `TallyBillOutstanding` (which has precise per-bill data) rather than TallyVoucher (which has transactional data).

Later in Script 28, these templates are **upgraded to call stored procedures** instead of inline SQL. This is a maintenance improvement — the stored procedure SQL can be modified independently.

### Script 23: Create `TallyStockItem` and `TallyBillOutstanding`
Adds the inventory and receivables tables. Uses `ON DELETE CASCADE` on the FK — if a Company row is deleted, all its Tally data is automatically cleaned up. The indexes on `(CompanyID, PartyName)` and `(CompanyID, BillDate)` are specifically optimized for the receivables aging queries that filter and group by party and date.

### Script 28: Create Stored Procedures
Three stored procedures (`sp_TallyProfitAndLoss`, `sp_TallyBalanceSheet`, `sp_TallyReceivablesAging`) are created and granted to the read-only user.

`sp_TallyReceivablesAging` is the most sophisticated — it uses `CASE WHEN DATEDIFF(DAY, BillDate, GETDATE())` to bucket outstanding bills into aging brackets (0-30, 31-60, 61-90, >90 days). This is the "aged receivables" report used by accountants to prioritise collections.

**Critical**: After creating the SPs, the templates in `TallyQueryTemplate` are updated to call `EXEC sp_TallyProfitAndLoss @CompanyID` instead of inline SQL. This change must happen — otherwise the template SQL would still be the old inline version.

### Script 30: Create `TallyActivationKey`
The licensing table for the connector agent. When a new customer installs the connector, they enter a 16-character alphanumeric key. The `activate` endpoint validates this key, marks it as used, records the machine's hardware fingerprint, and returns the `SyncToken`.

The `MachineID` field (from `MachineID.cs` in the connector) is a hardware fingerprint derived from processor and BIOS serial numbers. This prevents the same activation key from being used on multiple machines.

### Script 32: `sp_GenerateTallyActivationKey`
An admin-facing stored procedure for generating new activation keys. Generates:
- A 16-char uppercase key (e.g. `5038112A19534996`) — shown to the customer
- A 64-char lowercase sync token — stored internally, never shown

The key is generated by stripping hyphens from two GUIDs and concatenating them. This is not cryptographically strong, but is sufficient for a business B2B product where keys are distributed by the vendor.

### Script 35: Version management (`PatchUpdate` table)
Used by `AutoUpdater.cs` in the connector. On startup, the connector calls `GET /api/connector/version`, gets the latest version number, and compares to its own. If an update exists, it downloads and re-launches. The update script marks old versions inactive and inserts a new row.

### Script 36: Create `TallySyncRequest`
This table is the **signal channel** between the frontend ("Sync Now" button) and the agent (polling loop). The frontend inserts a row; the agent polls and processes it. The API has a cleanup clause that auto-expires requests older than 10 minutes — preventing the queue from growing if the agent is offline.

### Script 37/38: `AIDomain` + `AISuggestion` for Tally
The chat widget displays "suggestion chips" at the bottom — quick-tap queries like "Ledger Balance", "Top Debtors". These come from the `AISuggestion` table filtered by `AIDomain.Name = 'Tally'`. Script 38 is the production-safe version (uses `SCOPE_IDENTITY()` for auto-incremented domain ID instead of hardcoding `4`).

Note the multilingual keywords: `"baki"` (Hindi for "remaining/balance"), `"naqdh"` (Hindi for "cash"), `"vikri"` (Hindi for "sales"). This reflects that the target users are Indian accountants who may type in Hinglish.

---

## Summary: What This System Does and How

TalkWithTally is a **natural-language accounting assistant** that bridges Tally Prime (a dominant Indian accounting software) with an AI chat interface. The system works in two independent loops:

**Loop 1 — Sync**: A Windows agent installed at the customer's office communicates with Tally Prime using its built-in TDL XML API. Every few minutes (or on demand), it pulls ledgers, vouchers, stock items, and outstanding bills. It packages this as JSON and posts it to a cloud API, which parses the XML, validates it, and writes structured rows into SQL Server using bulk-copy operations.

**Loop 2 — Chat**: When a user types a question into the chat widget, the backend first tries to match it against pre-written SQL templates (fast, accurate). If no template matches, it calls Groq's LLaMA 3.3 70B model with a carefully engineered schema-aware prompt to generate SQL. The SQL passes a safety filter (must be SELECT-only, must filter by CompanyID). It executes against a read-only database connection with a 15-second timeout. The results are fed to a second LLM call that formats the answer in natural language, with currency in INR (₹).

Every interaction is logged. Every sync is tracked. The architecture is built on top of an existing ERP system (Popway_BillingERP) and adds the Tally integration as additive tables — a smart design that avoids disrupting existing functionality.
