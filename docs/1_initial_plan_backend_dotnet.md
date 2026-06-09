## Revised Plan — Phase by Phase

---

### Phase 0 — Environment Setup & Prerequisites

**0.1 — Developer laptop (your main machine)**

Install in this order:
- VS Code with extensions: C# (ms-dotnettools.csharp), NuGet Package Manager, REST Client (for testing without Postman), XML Tools
- .NET Framework 4.8 Developer Pack from Microsoft (enables targeting `.NET Framework 4.8` in projects)
- Git (you'll version-control everything from day one)
- Postman (for structured API testing later)
- Tally Prime in Educational Mode — set port to 9000 in `Gateway of Tally → F12 → Advanced Configuration → TDL & Addons → Tally.ERP 9/Tally Prime port: 9000`

**0.2 — Database laptop (second machine with SSMS)**

This is where SQL Server lives and where you'll run SSMS. Steps:

1. Install SQL Server Express 2019 (compatible with SQL Server 2012 syntax, no licensing needed for dev). Download from Microsoft's site.
2. During install, note the instance name (default: `SQLEXPRESS` or `MSSQLSERVER`).
3. Install SSMS (SQL Server Management Studio) 18 or 19.
4. After install, open SQL Server Configuration Manager → SQL Server Network Configuration → Protocols for your instance → enable TCP/IP.
5. In TCP/IP properties → IP Addresses tab → scroll to IPALL → set TCP Port to `1433`, clear TCP Dynamic Ports.
6. Enable SQL Server Browser service: SQL Server Configuration Manager → SQL Server Services → SQL Server Browser → right-click → Start → set to Automatic.
7. Windows Firewall: add inbound rule for port 1433 (TCP).
8. In SSMS: connect locally, then go to Security → Logins → create a new SQL login (e.g. `insidash_dev`) with SQL Server Authentication, strong password, and grant `sysadmin` role for now (you can tighten this later).
9. Right-click the server → Properties → Security → change to "SQL Server and Windows Authentication mode" → restart SQL Server service.

**0.3 — Connect your dev laptop to the DB laptop**

On your dev laptop, test connection with SSMS (install SSMS there too or just use the connection string). The connection string format:

```
Server=<DB_LAPTOP_IP>,1433;Database=InsidashTallySandbox;User Id=insidash_dev;Password=<your_pass>;
```

Find the DB laptop's IP with `ipconfig` on that machine. Both machines must be on the same network (use your home WiFi/LAN or a hotspot).

**0.4 — Project folder structure**

Create this on your dev laptop:

```
TalkWithTally/
├── Insidash.TallyApi/          ← ASP.NET Web API 2 project
│   ├── Controllers/
│   ├── Models/
│   └── Web.config
├── Insidash.BLL/               ← Class library
│   ├── Services/
│   └── Parsers/
├── Insidash.DAL/               ← Class library
│   ├── Context/
│   ├── Entities/
│   └── Repositories/
├── Insidash.TallySyncAgent/    ← Console app
│   ├── Program.cs
│   └── App.config
└── TalkWithTally.sln
```

Create projects via terminal (since you're on VS Code without Visual Studio):

```bash
# You'll need the old-style project scaffolding
# Easiest approach: use dotnet new with full framework targeting
dotnet new webapi -n Insidash.TallyApi --framework net48
dotnet new classlib -n Insidash.BLL --framework net48
dotnet new classlib -n Insidash.DAL --framework net48
dotnet new console -n Insidash.TallySyncAgent --framework net48
dotnet new sln -n TalkWithTally
dotnet sln add **/*.csproj
```

---

### Phase 1 — Database Layer (SQL Server 2012 Compatible DDL)

Run all of this in SSMS on the DB laptop.

**1.1 — Create sandbox database**

```sql
CREATE DATABASE InsidashTallySandbox;
GO
USE InsidashTallySandbox;
GO
```

**1.2 — Tally-specific tables (FK to existing Company table)**

The `CompanyID` type must be `NVARCHAR(50)` to match the existing `Company.CompanyID` (TEXT in SQLite backup = NVARCHAR in SQL Server). Do not use `INT IDENTITY` here.

```sql
-- Stores per-company Tally connection config
-- (supplements existing Company table which already has TallyPort/TallyHost/TallyUrl)
CREATE TABLE TallyCompanyConfig (
    ConfigID      NVARCHAR(50)  NOT NULL PRIMARY KEY DEFAULT NEWID(),
    CompanyID     NVARCHAR(50)  NOT NULL,   -- FK → Company.CompanyID
    SyncToken     NVARCHAR(255) NOT NULL UNIQUE,
    IsActive      BIT           NOT NULL DEFAULT 1,
    CreatedOn     DATETIME      NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_TallyConfig_Company FOREIGN KEY (CompanyID)
        REFERENCES Company(CompanyID)
);

-- Raw Tally data snapshots (ledgers, vouchers, trial balance etc.)
CREATE TABLE TallySnapshot (
    SnapshotID    NVARCHAR(50)  NOT NULL PRIMARY KEY DEFAULT NEWID(),
    CompanyID     NVARCHAR(50)  NOT NULL,
    DataType      NVARCHAR(100) NOT NULL,      -- 'Ledgers' | 'Vouchers' | 'TrialBalance'
    JsonContent   NVARCHAR(MAX) NOT NULL,      -- parsed & serialized XML → JSON
    RawXml        NVARCHAR(MAX) NULL,          -- optional: store original XML for debugging
    SyncedAt      DATETIME      NOT NULL DEFAULT GETDATE(),
    RecordCount   INT           NOT NULL DEFAULT 0,
    CONSTRAINT FK_TallySnapshot_Company FOREIGN KEY (CompanyID)
        REFERENCES Company(CompanyID)
);

-- Chat session tracking (links to existing AIChatLog structure)
-- Use the existing AIChatLog table if it's in the same DB;
-- if your sandbox is separate, replicate the schema:
CREATE TABLE AIChatLog (
    LogID         NVARCHAR(50)  NOT NULL PRIMARY KEY DEFAULT NEWID(),
    CompanyID     NVARCHAR(50)  NOT NULL,
    UserQuestion  NVARCHAR(MAX) NOT NULL,
    AIResponse    NVARCHAR(MAX) NULL,
    ResponseType  NVARCHAR(50)  NULL,          -- 'Tally' | 'CRM' | 'Combined'
    SQLQuery      NVARCHAR(MAX) NULL,
    ExecutionTime NVARCHAR(50)  NULL,
    IsSuccess     BIT           NOT NULL DEFAULT 1,
    ErrorMessage  NVARCHAR(MAX) NULL,
    CreatedDate   DATETIME      NOT NULL DEFAULT GETDATE()
);

-- Seed: insert a test company (use an existing CompanyID from your Company table)
-- First check: SELECT TOP 1 CompanyID, Name FROM Company WHERE IsDelete = '0'
-- Then seed TallyCompanyConfig with that CompanyID
INSERT INTO TallyCompanyConfig (CompanyID, SyncToken)
VALUES ('<YOUR_REAL_COMPANY_ID>', 'twt_dev_token_' + CAST(NEWID() AS NVARCHAR(50)));
```

Key design decisions here: `NEWID()` for PKs matches the existing schema's TEXT-based IDs. `RawXml` is nullable so you don't have to store it in production (only useful for debugging). `RecordCount` lets the chat service quickly know how much data was synced without parsing the JSON.

---

### Phase 2 — DAL (Entity Framework 6 + Repositories)

**2.1 — NuGet packages (install via terminal in each project)**

```bash
cd Insidash.DAL
nuget install EntityFramework -Version 6.4.4
nuget install Newtonsoft.Json -Version 13.0.3

cd ../Insidash.BLL
nuget install Newtonsoft.Json -Version 13.0.3

cd ../Insidash.TallyApi
nuget install Microsoft.AspNet.WebApi.Core -Version 5.3.0
nuget install Microsoft.AspNet.WebApi.WebHost -Version 5.3.0
nuget install EntityFramework -Version 6.4.4
nuget install Newtonsoft.Json -Version 13.0.3
```

**2.2 — Entity classes in `Insidash.DAL/Entities/`**

```csharp
// TallyCompanyConfig.cs
public class TallyCompanyConfig
{
    public string ConfigID   { get; set; }
    public string CompanyID  { get; set; }
    public string SyncToken  { get; set; }
    public bool   IsActive   { get; set; }
    public DateTime CreatedOn { get; set; }
}

// TallySnapshot.cs
public class TallySnapshot
{
    public string   SnapshotID   { get; set; }
    public string   CompanyID    { get; set; }
    public string   DataType     { get; set; }
    public string   JsonContent  { get; set; }
    public string   RawXml       { get; set; }
    public DateTime SyncedAt     { get; set; }
    public int      RecordCount  { get; set; }
}

// AIChatLog.cs
public class AIChatLog
{
    public string   LogID         { get; set; }
    public string   CompanyID     { get; set; }
    public string   UserQuestion  { get; set; }
    public string   AIResponse    { get; set; }
    public string   ResponseType  { get; set; }
    public string   SQLQuery      { get; set; }
    public string   ExecutionTime { get; set; }
    public bool     IsSuccess     { get; set; }
    public string   ErrorMessage  { get; set; }
    public DateTime CreatedDate   { get; set; }
}
```

**2.3 — DbContext in `Insidash.DAL/Context/InsidashTallyContext.cs`**

```csharp
using System.Data.Entity;

public class InsidashTallyContext : DbContext
{
    public InsidashTallyContext()
        : base("name=InsidashTallyDb") { }

    public DbSet<TallyCompanyConfig> TallyCompanyConfigs { get; set; }
    public DbSet<TallySnapshot>      TallySnapshots      { get; set; }
    public DbSet<AIChatLog>          AIChatLogs          { get; set; }

    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TallyCompanyConfig>().ToTable("TallyCompanyConfig");
        modelBuilder.Entity<TallySnapshot>().ToTable("TallySnapshot");
        modelBuilder.Entity<AIChatLog>().ToTable("AIChatLog");
        base.OnModelCreating(modelBuilder);
    }
}
```

**2.4 — Repositories in `Insidash.DAL/Repositories/`**

```csharp
// ITallySnapshotRepository.cs
public interface ITallySnapshotRepository
{
    TallySnapshot GetLatest(string companyId, string dataType);
    void UpsertSnapshot(TallySnapshot snapshot);
}

// TallySnapshotRepository.cs
public class TallySnapshotRepository : ITallySnapshotRepository
{
    public TallySnapshot GetLatest(string companyId, string dataType)
    {
        using (var ctx = new InsidashTallyContext())
        {
            return ctx.TallySnapshots
                .Where(s => s.CompanyID == companyId && s.DataType == dataType)
                .OrderByDescending(s => s.SyncedAt)
                .FirstOrDefault();
        }
    }

    public void UpsertSnapshot(TallySnapshot incoming)
    {
        using (var ctx = new InsidashTallyContext())
        {
            var existing = ctx.TallySnapshots
                .FirstOrDefault(s => s.CompanyID == incoming.CompanyID
                                  && s.DataType  == incoming.DataType);
            if (existing != null)
            {
                existing.JsonContent = incoming.JsonContent;
                existing.RawXml      = incoming.RawXml;
                existing.SyncedAt    = DateTime.Now;
                existing.RecordCount = incoming.RecordCount;
            }
            else
            {
                incoming.SnapshotID = Guid.NewGuid().ToString();
                incoming.SyncedAt   = DateTime.Now;
                ctx.TallySnapshots.Add(incoming);
            }
            ctx.SaveChanges();
        }
    }
}

// IAIChatLogRepository.cs + AIChatLogRepository.cs — same pattern, InsertLog method only
```

**2.5 — Connection string in `Web.config` / `App.config`**

```xml
<connectionStrings>
  <add name="InsidashTallyDb"
       connectionString="Server=<DB_LAPTOP_IP>,1433;Database=InsidashTallySandbox;
                         User Id=insidash_dev;Password=<your_password>;
                         MultipleActiveResultSets=True;"
       providerName="System.Data.SqlClient"/>
</connectionStrings>
```

---

### Phase 3 — BLL (Tally XML Parser + AI Service + Token Budget)

**3.1 — Tally XML Parser in `Insidash.BLL/Parsers/TallyXmlParser.cs`**

Tally Prime's XML export format for Ledgers looks like this:

```xml
<ENVELOPE>
  <BODY>
    <DATA>
      <TALLYMESSAGE>
        <LEDGER NAME="Cash" RESERVEDNAME="">
          <PARENT>Cash-in-hand</PARENT>
          <CLOSINGBALANCE>50000.00 Dr</CLOSINGBALANCE>
        </LEDGER>
      </TALLYMESSAGE>
    </DATA>
  </BODY>
</ENVELOPE>
```

Your parser needs to handle this:

```csharp
using System.Collections.Generic;
using System.Xml.Linq;
using Newtonsoft.Json;

public class TallyXmlParser
{
    public ParsedTallyData ParseLedgers(string rawXml)
    {
        var doc    = XDocument.Parse(rawXml);
        var ledgers = doc.Descendants("LEDGER")
            .Select(l => new {
                Name           = (string)l.Attribute("NAME"),
                Parent         = (string)l.Element("PARENT"),
                ClosingBalance = (string)l.Element("CLOSINGBALANCE")
            })
            .ToList();

        return new ParsedTallyData
        {
            DataType    = "Ledgers",
            RecordCount = ledgers.Count,
            JsonContent = JsonConvert.SerializeObject(ledgers)
        };
    }

    public ParsedTallyData ParseVouchers(string rawXml)
    {
        var doc = XDocument.Parse(rawXml);
        var vouchers = doc.Descendants("VOUCHER")
            .Select(v => new {
                Date        = (string)v.Element("DATE"),
                VchType     = (string)v.Element("VOUCHERTYPENAME"),
                PartyName   = (string)v.Element("PARTYNAME"),
                Amount      = (string)v.Element("AMOUNT"),
                Narration   = (string)v.Element("NARRATION")
            })
            .ToList();

        return new ParsedTallyData
        {
            DataType    = "Vouchers",
            RecordCount = vouchers.Count,
            JsonContent = JsonConvert.SerializeObject(vouchers)
        };
    }
}

public class ParsedTallyData
{
    public string DataType    { get; set; }
    public int    RecordCount { get; set; }
    public string JsonContent { get; set; }
}
```

**3.2 — Token Budget Service (critical, missing from original)**

Passing the full `JsonContent` of 500+ ledger entries to an LLM on every chat message is wasteful and will hit limits. Add this:

```csharp
public class TokenBudgetService
{
    // Rough estimate: 1 token ≈ 4 chars
    private const int MaxContextChars = 12000; // ~3000 tokens for context

    public string BuildContextJson(string fullJson, string userQuestion)
    {
        if (fullJson == null) return "{}";

        // If it fits, send as-is
        if (fullJson.Length <= MaxContextChars)
            return fullJson;

        // Otherwise: deserialize, filter by relevance to the question, re-serialize
        // For now, truncate and flag — replace with keyword filtering in Phase 5
        var truncated = fullJson.Substring(0, MaxContextChars);
        // Ensure valid JSON by cutting at last complete object
        int lastBrace = truncated.LastIndexOf("},");
        if (lastBrace > 0)
            truncated = truncated.Substring(0, lastBrace + 1) + "]";

        return truncated;
    }
}
```

**3.3 — AI Services in `Insidash.BLL/Services/`**

```csharp
// IAIService.cs
public interface IAIService
{
    Task<AIServiceResult> ChatAsync(string systemPrompt, string userMessage);
    string ProviderName { get; }
}

public class AIServiceResult
{
    public bool   Success      { get; set; }
    public string Content      { get; set; }
    public string ErrorMessage { get; set; }
    public string ProviderUsed { get; set; }
}

// GroqAIService.cs
public class GroqAIService : IAIService
{
    private readonly HttpClient _client;
    private readonly string _apiKey;
    public string ProviderName => "Groq";

    public GroqAIService(string apiKey)
    {
        _apiKey = apiKey;
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
    }

    public async Task<AIServiceResult> ChatAsync(string systemPrompt, string userMessage)
    {
        var body = new {
            model = "llama-3.3-70b-versatile",
            max_tokens = 1024,
            messages = new[] {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            }
        };

        try
        {
            var response = await _client.PostAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            );

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                return new AIServiceResult { Success = false, ErrorMessage = err, ProviderUsed = ProviderName };
            }

            string raw = await response.Content.ReadAsStringAsync();
            dynamic json = JsonConvert.DeserializeObject(raw);
            return new AIServiceResult {
                Success      = true,
                Content      = (string)json.choices[0].message.content,
                ProviderUsed = ProviderName
            };
        }
        catch (Exception ex)
        {
            return new AIServiceResult { Success = false, ErrorMessage = ex.Message, ProviderUsed = ProviderName };
        }
    }
}
```

Implement `ClaudeAIService` identically but pointing to `https://api.anthropic.com/v1/messages` with the Anthropic request body format (`model: claude-sonnet-4-20250514`, `anthropic-version: 2023-06-01` header).

**3.4 — AIManager with proper fallback**

```csharp
public class AIManager
{
    private readonly List<IAIService> _providers = new List<IAIService>();
    private readonly TokenBudgetService _tokenBudget = new TokenBudgetService();

    public AIManager()
    {
        string claudeKey = ConfigurationManager.AppSettings["CLAUDE_API_KEY"];
        string groqKey   = ConfigurationManager.AppSettings["GROQ_API_KEY"];

        if (!string.IsNullOrEmpty(claudeKey))
            _providers.Add(new ClaudeAIService(claudeKey));
        if (!string.IsNullOrEmpty(groqKey))
            _providers.Add(new GroqAIService(groqKey));

        if (_providers.Count == 0)
            throw new InvalidOperationException("No AI provider keys configured in Web.config.");
    }

    public async Task<AIServiceResult> ProcessChatAsync(
        string userMessage, string fullContextJson, string companyName)
    {
        string optimizedContext = _tokenBudget.BuildContextJson(fullContextJson, userMessage);
        string systemPrompt     = BuildSystemPrompt(companyName, optimizedContext);

        foreach (var provider in _providers)
        {
            var result = await provider.ChatAsync(systemPrompt, userMessage);
            if (result.Success) return result;
            // log failure, try next provider
        }

        return new AIServiceResult {
            Success = false,
            ErrorMessage = "All AI providers failed.",
            Content = "I'm unable to process your request right now. Please try again later."
        };
    }

    private string BuildSystemPrompt(string companyName, string contextJson)
    {
        return $@"You are TalkWithTally, an AI assistant for {companyName}.
You help users understand their Tally Prime accounting data.
Answer questions based strictly on the data below. 
If the answer is not in the data, say so — do not guess.
Be concise and use INR (₹) for currency values.

TALLY DATA:
{contextJson}";
    }
}
```

Add API keys to `Web.config` `<appSettings>`:
```xml
<add key="CLAUDE_API_KEY" value="sk-ant-..."/>
<add key="GROQ_API_KEY"   value="gsk_..."/>
```

---

### Phase 4 — Presentation Layer (Web API 2 Controllers)

**4.1 — Auth middleware for SyncToken validation**

Before the controllers, add a simple `DelegatingHandler` or `ActionFilter` that reads the `X-Sync-Token` header and validates it against `TallyCompanyConfig`:

```csharp
public class SyncTokenAuthFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(HttpActionContext ctx)
    {
        IEnumerable<string> headerVals;
        if (!ctx.Request.Headers.TryGetValues("X-Sync-Token", out headerVals))
        {
            ctx.Response = ctx.Request.CreateResponse(
                HttpStatusCode.Unauthorized, "Missing X-Sync-Token header");
            return;
        }

        string token = headerVals.First();
        using (var db = new InsidashTallyContext())
        {
            bool valid = db.TallyCompanyConfigs
                           .Any(c => c.SyncToken == token && c.IsActive);
            if (!valid)
                ctx.Response = ctx.Request.CreateResponse(
                    HttpStatusCode.Unauthorized, "Invalid sync token");
        }
        base.OnActionExecuting(ctx);
    }
}
```

**4.2 — Controller**

```csharp
[RoutePrefix("api/tally")]
public class TallyApiController : ApiController
{
    private readonly TallyXmlParser _parser = new TallyXmlParser();
    private readonly TallySnapshotRepository _snapshotRepo = new TallySnapshotRepository();
    private readonly AIChatLogRepository _chatLogRepo = new AIChatLogRepository();
    private readonly AIManager _aiManager = new AIManager();

    [HttpPost]
    [Route("sync")]
    [SyncTokenAuthFilter]
    public IHttpActionResult Sync([FromBody] SyncPayload payload)
    {
        if (payload == null || string.IsNullOrEmpty(payload.RawXml))
            return BadRequest("RawXml is required.");

        // Resolve companyID from token
        string companyId;
        string token = Request.Headers.GetValues("X-Sync-Token").First();
        using (var db = new InsidashTallyContext())
        {
            companyId = db.TallyCompanyConfigs
                          .Where(c => c.SyncToken == token)
                          .Select(c => c.CompanyID)
                          .First();
        }

        ParsedTallyData parsed;
        switch (payload.DataType?.ToLower())
        {
            case "vouchers": parsed = _parser.ParseVouchers(payload.RawXml); break;
            default:         parsed = _parser.ParseLedgers(payload.RawXml);  break;
        }

        _snapshotRepo.UpsertSnapshot(new TallySnapshot {
            CompanyID   = companyId,
            DataType    = parsed.DataType,
            JsonContent = parsed.JsonContent,
            RawXml      = payload.RawXml,
            RecordCount = parsed.RecordCount
        });

        return Ok(new { status = "synced", records = parsed.RecordCount });
    }

    [HttpPost]
    [Route("chat")]
    public async Task<IHttpActionResult> Chat([FromBody] ChatRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Message))
            return BadRequest("Message is required.");

        var start = DateTime.Now;

        // Fetch the latest snapshot for this company
        var snapshot = _snapshotRepo.GetLatest(request.CompanyId, "Ledgers");
        string context = snapshot?.JsonContent ?? "{}";

        // Get company name for the system prompt
        string companyName = "your company";
        // (optionally query Company table here)

        var result = await _aiManager.ProcessChatAsync(
            request.Message, context, companyName);

        // Log to AIChatLog
        _chatLogRepo.InsertLog(new AIChatLog {
            LogID        = Guid.NewGuid().ToString(),
            CompanyID    = request.CompanyId,
            UserQuestion = request.Message,
            AIResponse   = result.Content,
            ResponseType = "Tally",
            IsSuccess    = result.Success,
            ErrorMessage = result.ErrorMessage,
            ExecutionTime = ((int)(DateTime.Now - start).TotalMilliseconds).ToString(),
            CreatedDate  = DateTime.Now
        });

        return Ok(new { response = result.Content, provider = result.ProviderUsed });
    }

    [HttpGet]
    [Route("history/{companyId}")]
    public IHttpActionResult History(string companyId, int page = 1, int pageSize = 20)
    {
        var logs = _chatLogRepo.GetByCompany(companyId, page, pageSize);
        return Ok(logs);
    }

    [HttpGet]
    [Route("status/{companyId}")]
    public IHttpActionResult Status(string companyId)
    {
        var ledger   = _snapshotRepo.GetLatest(companyId, "Ledgers");
        var vouchers = _snapshotRepo.GetLatest(companyId, "Vouchers");
        return Ok(new {
            ledgersSyncedAt   = ledger?.SyncedAt,
            ledgerCount       = ledger?.RecordCount ?? 0,
            vouchersSyncedAt  = vouchers?.SyncedAt,
            voucherCount      = vouchers?.RecordCount ?? 0
        });
    }
}

public class SyncPayload { public string DataType { get; set; } public string RawXml { get; set; } }
public class ChatRequest { public string CompanyId { get; set; } public string Message { get; set; } }
```

**4.3 — WebApiConfig.cs (register routes)**

```csharp
public static class WebApiConfig
{
    public static void Register(HttpConfiguration config)
    {
        config.MapHttpAttributeRoutes();
        config.Formatters.JsonFormatter.SerializerSettings =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
    }
}
```

---

### Phase 5 — Tally Sync Agent (Console App)

**5.1 — App.config**

```xml
<appSettings>
  <add key="TallyBaseUrl"  value="http://localhost:9000"/>
  <add key="ApiBaseUrl"    value="http://<DEV_LAPTOP_IP>:<PORT>"/>
  <add key="SyncToken"     value="twt_dev_token_..."/>
  <add key="SyncIntervalMs" value="300000"/>
</appSettings>
```

**5.2 — Program.cs (async-safe, no `.Result` deadlocks)**

```csharp
using System;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Insidash.TallySyncAgent
{
    class Program
    {
        private static readonly HttpClient _tallyClient = new HttpClient();
        private static readonly HttpClient _apiClient   = new HttpClient();
        private static readonly string _tallyUrl  = ConfigurationManager.AppSettings["TallyBaseUrl"];
        private static readonly string _apiUrl    = ConfigurationManager.AppSettings["ApiBaseUrl"];
        private static readonly string _syncToken = ConfigurationManager.AppSettings["SyncToken"];
        private static readonly int _interval    = int.Parse(ConfigurationManager.AppSettings["SyncIntervalMs"]);

        static void Main(string[] args)
        {
            _apiClient.DefaultRequestHeaders.Add("X-Sync-Token", _syncToken);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TalkWithTally Sync Agent started.");
            RunAsync().GetAwaiter().GetResult();
        }

        private static async Task RunAsync()
        {
            while (true)
            {
                await SyncDataType("Ledgers",  BuildLedgerXml());
                await SyncDataType("Vouchers", BuildVoucherXml());
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cycle complete. Next sync in {_interval/60000} min.");
                await Task.Delay(_interval);
            }
        }

        private static async Task SyncDataType(string dataType, string xmlEnvelope)
        {
            try
            {
                // Pull from Tally
                var tallyResponse = await _tallyClient.PostAsync(
                    _tallyUrl,
                    new StringContent(xmlEnvelope, Encoding.UTF8, "text/xml"));

                if (!tallyResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tally error for {dataType}: {tallyResponse.StatusCode}");
                    return;
                }

                string rawXml = await tallyResponse.Content.ReadAsStringAsync();

                // Push to API
                var payload = new { DataType = dataType, RawXml = rawXml };
                var apiResponse = await _apiClient.PostAsync(
                    $"{_apiUrl}/api/tally/sync",
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

                string apiResult = await apiResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {dataType} sync: {apiResult}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error syncing {dataType}: {ex.Message}");
            }
        }

        private static string BuildLedgerXml() => @"
<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Data</TYPE>
    <ID>List of Ledger</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";

        private static string BuildVoucherXml() => @"
<ENVELOPE>
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
        <SVFROMDATE>$$SystemDate-30</SVFROMDATE>
        <SVTODATE>$$SystemDate</SVTODATE>
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";
    }
}
```

The key fix over the original: fully `async/await` all the way down via `RunAsync().GetAwaiter().GetResult()` at the top — no `.Result` blocking anywhere inside the loop.

---

### Phase 6 — Postman Testing Checklist

Test in this exact order:

1. `GET /api/tally/status/{companyId}` → should return zeros (no data yet)
2. Manual `POST /api/tally/sync` with header `X-Sync-Token: <your_token>`, body: `{"DataType":"Ledgers","RawXml":"<ENVELOPE>...</ENVELOPE>"}` using a sample ledger XML
3. `GET /api/tally/status/{companyId}` → should now show ledger count > 0
4. `POST /api/tally/chat` body: `{"CompanyId":"<id>","Message":"What are my top 5 ledgers by closing balance?"}` → verify AI response
5. `GET /api/tally/history/{companyId}` → verify the chat was logged
6. Start the Sync Agent console app and watch it auto-sync, then repeat step 3 to confirm it updated

---

### What to build next (Phase 7, after the above works)

Once the core pipeline is stable, the natural extension is relevance-based context filtering in `TokenBudgetService` — instead of a crude character truncation, deserialize the JSON, score each ledger/voucher entry against keywords in the user's question, and pass only the top-N relevant records to the AI. This is the biggest quality lever and directly builds on your context engineering work from the INSIDASH deck.