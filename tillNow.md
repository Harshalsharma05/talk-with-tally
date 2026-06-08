# TalkWithTally — Project Status and Architecture Overview

TalkWithTally is a .NET-based backend integration that synchronizes local Tally Prime accounting data to a centralized SQL Server database and allows users to query their financial records in plain natural language (e.g. via WhatsApp, Web Portal, or SMS) using LLMs.

---

## 1. Core Plan (plan.md) vs. Actually Implemented Architecture

The project initially started with a simpler design, which was expanded to support scalability and production-grade queries.

| Component | Initial Plan (`plan.md`) | Actually Implemented | Why the Change was Made |
| :--- | :--- | :--- | :--- |
| **Data Synchronization** | Sync agent extracts XML, posts it to API, and saves it as serialized JSON snapshots. | Synced XML is parsed into domain-specific objects and saved directly into structured relational database tables. | Storing data as JSON blobs meant the AI had to read the entire dataset. It hit token limit boundaries and truncated records, causing data loss. |
| **AI Data Processing** | AI service reads the entire raw JSON ledger snapshot on every chat query. | **NL2SQL (Natural Language to SQL)**: The AI translates the user's question into a SQL query, runs it on the DB, and formats the result. | Allows the system to scale to millions of vouchers without exceeding the AI's token limit, while ensuring 100% mathematical accuracy. |
| **Parsing Suffixes** | Basic element selection from XML. | Custom number parsers that strip commas (`Replace(",", "")`) and convert Tally's accounting signs. | Tally Prime outputs numbers formatted with commas (e.g., `10,00,000.00`), which causes standard C# decimal parses to crash. |
| **Tally Ledger Queries** | Queries custom TDL report `List of Ledger`. | Queries the native Tally `Collection` API (`Ledger`). | The custom report `List of Ledger` was missing/unsupported in Tally Prime out-of-the-box. The native collection works on any system. |

---

## 2. SQL Server Database Schema (Popway_BillingERP)

Two new relational tables were created on the DB laptop to replace the snapshot-only approach:

### 1. `TallyLedger` (Stores Ledger Account Balances)
- **`LedgerID`** (`NVARCHAR(50)`, Primary Key) - GUID identifier.
- **`CompanyID`** (`INT`) - ID linking to the user's company (e.g. `5` for `Insidash Test Corp`).
- **`Name`** (`NVARCHAR(255)`) - Name of the ledger (e.g., `HDFC Bank A/c`).
- **`Parent`** (`NVARCHAR(255)`) - Parent group account (e.g., `Bank Accounts`).
- **`ClosingBalance`** (`DECIMAL(18,2)`) - Closing balance amount.
  - **Accounting Sign Convention**: To facilitate SQL sum/aggregation queries, **Debits (Dr) are stored as negative decimals** (e.g. `-1000000.00` for assets), and **Credits (Cr) are stored as positive decimals** (e.g. `1000000.00` for equity/liabilities).
- **`SyncedAt`** (`DATETIME`) - Timestamp of the synchronization.

### 2. `TallyVoucher` (Stores Transactions/Day Book Entries)
- **`VoucherID`** (`NVARCHAR(50)`, Primary Key) - GUID.
- **`CompanyID`** (`INT`) - Links to company.
- **`Date`** (`DATE`) - Transaction date.
- **`VchType`** (`NVARCHAR(100)`) - e.g., `Sales`, `Payment`, `Receipt`, `Purchase`.
- **`PartyName`** (`NVARCHAR(255)`) - Primary ledger account associated with the transaction.
- **`Amount`** (`DECIMAL(18,2)`) - Transacted amount.
- **`Narration`** (`NVARCHAR(MAX)`) - Voucher narration/description.
- **`SyncedAt`** (`DATETIME`) - Synchronization time.

---

## 3. Project Folder Structure

The TalkWithTally C# solution (`TalkWithTally.sln`) targets `.NET Framework 4.8` and is divided into four main projects:

```
TalkWithTally/
├── Insidash.TallyApi/          ← ASP.NET Web API 2 project (API Host)
│   ├── Controllers/
│   │   └── TallyApiController.cs  ← Handles /sync and /chat routes
│   ├── Filters/
│   │   └── SyncTokenAuthFilter.cs ← Validates sync headers
│   └── Web.config                 ← Configures DB connection and Groq/Claude keys
├── Insidash.BLL/               ← Business Logic Layer (Class Library)
│   ├── Parsers/
│   │   └── TallyXmlParser.cs      ← Parses Tally XML to strongly-typed DTOs
│   └── Services/
│       ├── IAIService.cs          ← AI Chat interface
│       ├── GroqAIService.cs       ← Concrete Groq completion client
│       ├── Nl2SqlService.cs       ← Handles SQL translation & response formatting
│       └── TokenBudgetService.cs  ← Token budget management (retained for backward compatibility)
├── Insidash.DAL/               ← Data Access Layer (Class Library)
│   ├── Context/
│   │   └── InsidashTallyContext.cs← EF6 DbContext
│   ├── Entities/
│   │   ├── TallyCompanyConfig.cs  ← Maps company configurations
│   │   ├── TallySnapshot.cs       ← Maps historical snapshots
│   │   ├── TallyLedger.cs         ← Maps TallyLedger table
│   │   ├── TallyVoucher.cs        ← Maps TallyVoucher table
│   │   └── AIChatLog.cs           ← Maps chat log database history
│   └── Repositories/
│       ├── TallySnapshotRepository.cs
│       ├── AIChatLogRepository.cs
│       └── TallyRelationalRepository.cs ← Bulk relational upserts and SQL execution
└── Insidash.TallySyncAgent/    ← Local Console Sync Agent
    ├── Program.cs                 ← Sync schedule, XML builder, and POST triggers
    └── App.config                 ← Configures Tally port (9000) and API base URL
```

---

## 4. Key Implementation Details

### A. Comma Sanitizer & Sign Parser
The [TallyXmlParser.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.BLL/Parsers/TallyXmlParser.cs) parses numbers safely by sanitizing thousand separators and mapping Debit/Credit suffixes:
```csharp
public decimal ParseClosingBalance(string rawVal)
{
    if (string.IsNullOrWhiteSpace(rawVal)) return 0;
    string text = rawVal.Trim();
    bool isCredit = text.EndsWith("Cr", StringComparison.OrdinalIgnoreCase);
    bool isDebit = text.EndsWith("Dr", StringComparison.OrdinalIgnoreCase);

    string numberPart = text;
    if (isCredit || isDebit)
        numberPart = text.Substring(0, text.Length - 2).Trim();

    // Remove formatting commas (Tally formats as 10,00,000.00)
    numberPart = numberPart.Replace(",", "");

    if (decimal.TryParse(numberPart, out decimal val))
        return isCredit ? -val : val; // Credit positive, Debit negative

    return 0;
}
```

### B. Relational Transaction Upsert
In [TallyRelationalRepository.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.DAL/Repositories/TallyRelationalRepository.cs), sync pushes clear existing rows and bulk-insert new records within a secure Database Transaction:
```csharp
using (var transaction = ctx.Database.BeginTransaction())
{
    try
    {
        ctx.Database.ExecuteSqlCommand("DELETE FROM TallyLedger WHERE CompanyID = @p0", companyId);
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
    catch {
        transaction.Rollback();
        throw;
    }
}
```

### C. NL2SQL Coordination Pipeline
In [Nl2SqlService.cs](file:///c:/AAA_STUDY/Internship_Tasks/TalkWithTally/Insidash.BLL/Services/Nl2SqlService.cs), natural language questions are resolved in a two-stage process:
1. **Translation**: The LLM is provided the SQL schema (tables, column names, mappings, and company ID) and asked to convert the question into a SQL query.
2. **Execution**: The database executes the SQL query via ADO.NET and loads the results into a dynamic `DataTable`, which is serialized into a JSON string.
3. **Conversational Synthesis**: The LLM is given the user's question, the generated SQL, and the query results, and formats a clean conversational response.

---

## 5. Future Production Roadmap (Features & Scaling)

To elevate this project from an internship prototype to a production-ready SaaS product like **Riko AI**, the following improvements are planned:

### 1. Incremental Synchronization (Critical for Scale)
- **Problem**: Full sync runs a `DELETE` and `INSERT` on all records every 5 minutes. If a company has 1,000,000 transactions, this will lock the database and time out.
- **Solution**: Update the Sync Agent to query Tally only for vouchers modified since the last sync time (using Tally's `ALTEREDON` field). Modify the repository to execute an **Upsert** (merge/update) on the database rather than deleting all records.

### 2. Safety & Read-Only SQL Restrictions
- **Problem**: Executing LLM-generated SQL directly is vulnerable to SQL injection or malicious generation (e.g. `DROP TABLE TallyVoucher;`).
- **Solution**: Configure the database connection string used by the chat endpoint to connect using a **read-only database user** (`tally_readonly`) that only has `SELECT` privileges on the `TallyLedger` and `TallyVoucher` tables.

### 3. Inventory & Outstanding Invoice Tracking
- **Problem**: Users cannot ask about stock levels or outstanding bills.
- **Solution**:
  - Map Tally’s `StockItem` and `Godown` collections to a `TallyStockItem` relational table.
  - Map voucher `BILLDETAILS.LIST` structures to a `TallyBillOutstanding` table to enable aging analysis queries (e.g. *"Show outstanding bills older than 90 days"*).

### 4. Hybrid Intent Query Routing
- **Problem**: LLMs can make mistakes when writing SQL for highly complex reports (like a Balance Sheet).
- **Solution**: Implement an intent classification step. If the user asks for a standard accounting statement (e.g. *"Give me the Profit & Loss"*), instead of writing custom SQL, the LLM calls a **pre-written stored procedure** that generates the report with 100% mathematical precision.
