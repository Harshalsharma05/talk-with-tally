# TalkWithTally — Production Connector Implementation Plan

> **Purpose:** This document details the complete conversion of the current developer-only `Insidash.TallySyncAgent` console app into a production-grade Windows Service with a system tray UI, activation key onboarding, configurable Tally host, and an auto-update mechanism. Follow phases in strict order. Each phase builds on the previous one.

---

## Architecture Overview — What We Are Building

```
USER'S MACHINE (Windows, on-premise)
┌────────────────────────────────────────────────────────────┐
│  InsidashTallyConnector.exe  (Windows Service + Tray App)  │
│                                                            │
│  ┌─────────────────┐   ┌──────────────────────────────┐   │
│  │  Service Host   │   │  System Tray Application     │   │
│  │  (ServiceBase) │   │  (WinForms NotifyIcon)       │   │
│  │                 │   │  • Last synced: 2 min ago    │   │
│  │  • Starts on    │   │  • Status: Connected ✓       │   │
│  │    Windows boot │   │  • Right-click menu          │   │
│  │  • Sync loop    │   │  • Force sync now            │   │
│  │  • Auto-update  │   │  • Open settings             │   │
│  └────────┬────────┘   └──────────────────────────────┘   │
│           │ localhost:9000 (XML HTTP)                      │
│           ▼                                                │
│  ┌─────────────────┐                                       │
│  │  Tally Prime    │  (must be running and open)          │
│  └─────────────────┘                                       │
└───────────────────┬────────────────────────────────────────┘
                    │ HTTPS to your cloud server
                    ▼
YOUR CLOUD (Insidash.TallyApi)
┌────────────────────────────────────────────────────────────┐
│  /api/connector/activate  → validates key, returns token   │
│  /api/connector/version   → returns latest agent version   │
│  /api/tally/sync          → existing sync endpoint         │
│  /api/tally/chat          → existing chat endpoint         │
└────────────────────────────────────────────────────────────┘
```

---

## Key Schema Discovery

Before the phases, note what was found in your **live Insidash database** that directly impacts implementation:

| Discovery | Table | Columns | Impact |
|---|---|---|---|
| Tally config already stored on Company | `Company` | `TallyPort`, `TallyHost`, `TallyUrl` | No new columns needed for Tally connection config — read from these |
| Patch/version tracking table exists | `PatchUpdate` | `PatchUpdateNo`, `PatchUpdateDate`, `IsActive` | Reuse this table for agent version management, don't create a new one |
| License activation system exists | `LicenseActivate` | `LicenseID`, `CompanyID`, `CreatedOn` | Activation key table must link to this existing licensing model |

---

## New Project Structure

Add one new project to your existing solution. Everything else (`Insidash.BLL`, `Insidash.DAL`, `Insidash.TallyApi`) stays unchanged.

```
TalkWithTally/
├── Insidash.TallyApi/           ← Unchanged (add 2 new endpoints)
├── Insidash.BLL/                ← Unchanged
├── Insidash.DAL/                ← Unchanged (add 1 new entity)
├── Insidash.TallySyncAgent/     ← RETIRED (logic moves to new project)
└── Insidash.TallyConnector/     ← NEW PROJECT (Windows Service + Tray)
    ├── ConnectorService.cs          ← ServiceBase (the Windows Service)
    ├── TrayApplication.cs           ← WinForms NotifyIcon (system tray)
    ├── ActivationWindow.cs          ← First-run registration form
    ├── SettingsWindow.cs            ← Tally host/port config form
    ├── SyncEngine.cs                ← Sync logic (ported from TallySyncAgent)
    ├── AutoUpdater.cs               ← Version check + self-update
    ├── LocalConfig.cs               ← Encrypted local config reader/writer
    ├── Program.cs                   ← Entry point (decides service vs tray)
    ├── ProjectInstaller.cs          ← InstallUtil.exe registration
    └── App.config                   ← Cloud API base URL only
```

---

## Phase 1 — Database Layer: Activation Key Table

> **Do this first.** The cloud API needs a place to store activation keys before you build anything else. Run all SQL in SSMS on the DB laptop.

### Step 1.1 — Create `TallyActivationKey` table

```sql
USE Popway_BillingERP;
GO

CREATE TABLE TallyActivationKey (
    KeyID           NVARCHAR(50)  NOT NULL PRIMARY KEY,  -- GUID
    CompanyID       INT           NOT NULL,
    ActivationKey   NVARCHAR(32)  NOT NULL,               -- 16-char alphanumeric, shown to user
    SyncToken       NVARCHAR(64)  NOT NULL,               -- secret token used by sync agent after activation
    IsActivated     BIT           NOT NULL DEFAULT 0,
    ActivatedAt     DATETIME      NULL,
    MachineID       NVARCHAR(255) NULL,                   -- hardware fingerprint of activated machine
    AgentVersion    NVARCHAR(20)  NULL,                   -- version that registered
    CreatedOn       DATETIME      NOT NULL DEFAULT GETDATE(),
    ExpiresAt       DATETIME      NULL,                   -- NULL = never expires
    IsActive        BIT           NOT NULL DEFAULT 1,
    CONSTRAINT UQ_TallyActivationKey_Key    UNIQUE (ActivationKey),
    CONSTRAINT UQ_TallyActivationKey_Token  UNIQUE (SyncToken),
    CONSTRAINT FK_TallyActivationKey_Company
        FOREIGN KEY (CompanyID) REFERENCES Company(CompanyID)
        ON DELETE CASCADE
);

CREATE INDEX IX_TallyActivationKey_CompanyID ON TallyActivationKey (CompanyID);
```

### Step 1.2 — Add agent version tracking to `PatchUpdate`

The `PatchUpdate` table already exists and tracks application patches. You will reuse it for the connector agent versioning. No new table needed. Just understand the columns:

```sql
-- View current patch records
SELECT PatchUpdateID, PatchUpdateNo, PatchUpdateDate, IsActive
FROM PatchUpdate
ORDER BY PatchUpdateDate DESC;

-- When you release a new agent version, insert a row:
-- PatchUpdateNo = '1.0.2'  (semantic version of the connector)
-- IsActive = '1'           (only one row should have IsActive = '1' at any time)
-- CompanyID = NULL or '0'  (this is global, not per-company)
```

### Step 1.3 — Add an admin stored procedure to generate activation keys

Run this in SSMS. You will call this from your Insidash admin panel whenever a new company is onboarded for TalkWithTally:

```sql
CREATE PROCEDURE sp_GenerateTallyActivationKey
    @CompanyID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @KeyID       NVARCHAR(50) = NEWID();
    DECLARE @ActKey      NVARCHAR(32);
    DECLARE @SyncToken   NVARCHAR(64);

    -- Generate readable 16-char activation key (uppercase alphanumeric)
    SET @ActKey = UPPER(LEFT(REPLACE(CAST(NEWID() AS NVARCHAR(40)), '-', ''), 16));

    -- Generate 64-char secret sync token (never shown to user)
    SET @SyncToken = LOWER(REPLACE(CAST(NEWID() AS NVARCHAR(40)), '-', '')
                    + REPLACE(CAST(NEWID() AS NVARCHAR(40)), '-', ''));

    INSERT INTO TallyActivationKey
        (KeyID, CompanyID, ActivationKey, SyncToken, IsActivated, CreatedOn, IsActive)
    VALUES
        (@KeyID, @CompanyID, @ActKey, @SyncToken, 0, GETDATE(), 1);

    -- Return the key to display to the admin
    SELECT @ActKey AS ActivationKey, @CompanyID AS CompanyID;
END
GO

-- Usage: EXEC sp_GenerateTallyActivationKey @CompanyID = 5
```

### Step 1.4 — Verify

```sql
EXEC sp_GenerateTallyActivationKey @CompanyID = 5;
-- Should return one row with a 16-char ActivationKey like: 'A3F7K9M2P4R8T1X6'

SELECT * FROM TallyActivationKey WHERE CompanyID = 5;
-- Should show IsActivated = 0, ActivatedAt = NULL
```

---

## Phase 2 — Cloud API Layer: New Connector Endpoints

> Add two new endpoints to `Insidash.TallyApi`. These are the only API changes needed. Existing sync and chat endpoints are untouched.

### Step 2.1 — Add EF6 entity for `TallyActivationKey`

In `Insidash.DAL/Entities/`, create `TallyActivationKey.cs`:

```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("TallyActivationKey")]
public class TallyActivationKey
{
    [Key]
    public string KeyID         { get; set; }
    public int    CompanyID     { get; set; }
    public string ActivationKey { get; set; }
    public string SyncToken     { get; set; }
    public bool   IsActivated   { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string MachineID     { get; set; }
    public string AgentVersion  { get; set; }
    public DateTime CreatedOn   { get; set; }
    public DateTime? ExpiresAt  { get; set; }
    public bool   IsActive      { get; set; }
}
```

Add to `InsidashTallyContext.cs`:

```csharp
public DbSet<TallyActivationKey> TallyActivationKeys { get; set; }
```

### Step 2.2 — Create `ConnectorApiController.cs`

In `Insidash.TallyApi/Controllers/`, create a new controller:

```csharp
using System;
using System.Linq;
using System.Web.Http;

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
}

// ─── Request / Response DTOs ─────────────────────────────────────────────────

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
```

### Step 2.3 — Verify both endpoints with Postman

```
POST https://your-server/api/connector/activate
Body: { "ActivationKey": "A3F7K9M2P4R8T1X6", "MachineID": "test-machine", "AgentVersion": "1.0.0" }
Expected 200: { "syncToken": "abc123...", "companyId": 5, "tallyHost": "localhost", "tallyPort": "9000" }

POST same key again:
Expected 401: (key already activated)

GET https://your-server/api/connector/version
Expected 200: { "version": "1.0.0", "downloadUrl": "..." }
```

---

## Phase 3 — New Project: `Insidash.TallyConnector`

> Create the new Windows Service project. This replaces `Insidash.TallySyncAgent` entirely.

### Step 3.1 — Create the project in Visual Studio / VS Code

In your solution, add a new **Windows Forms Application** project targeting **.NET Framework 4.8** named `Insidash.TallyConnector`.

A Windows Forms project is chosen (not Console or Class Library) because:
- It can host both a `ServiceBase` (when launched by Windows SCM)
- And a `WinForms NotifyIcon` (when launched interactively for the tray icon)
- The same executable handles both modes based on how it is started

Install NuGet packages for this project:

```
nuget install Newtonsoft.Json     -Version 13.0.3
nuget install System.Security.Cryptography (already in .NET 4.8 BCL, no install needed)
```

Add project references to `Insidash.BLL` and `Insidash.DAL` (same as TallySyncAgent had).

### Step 3.2 — `LocalConfig.cs` — Encrypted local configuration

This class reads and writes a small config file on the user's machine. It stores the sync token and Tally connection details after first activation. The file is encrypted with DPAPI (Windows Data Protection API — built into .NET 4.8, no extra packages).

```csharp
// LocalConfig.cs
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

public class ConnectorConfig
{
    public string SyncToken      { get; set; }
    public int    CompanyID      { get; set; }
    public string TallyHost      { get; set; } = "localhost";
    public string TallyPort      { get; set; } = "9000";
    public int    SyncIntervalMs { get; set; } = 300000;
}

public static class LocalConfig
{
    // Stored in: C:\ProgramData\InsidashTallyConnector\config.dat
    private static readonly string ConfigDir  =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "InsidashTallyConnector");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.dat");

    public static bool Exists() => File.Exists(ConfigPath);

    public static void Save(ConnectorConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        string json       = JsonConvert.SerializeObject(config);
        byte[] plainBytes = Encoding.UTF8.GetBytes(json);

        // DPAPI encrypts with the machine key — only readable on this machine
        byte[] encryptedBytes = ProtectedData.Protect(
            plainBytes, null, DataProtectionScope.LocalMachine);

        File.WriteAllBytes(ConfigPath, encryptedBytes);
    }

    public static ConnectorConfig Load()
    {
        if (!Exists())
            throw new FileNotFoundException("Connector not activated. Config file missing.");

        byte[] encryptedBytes = File.ReadAllBytes(ConfigPath);
        byte[] plainBytes     = ProtectedData.Unprotect(
            encryptedBytes, null, DataProtectionScope.LocalMachine);

        string json = Encoding.UTF8.GetString(plainBytes);
        return JsonConvert.DeserializeObject<ConnectorConfig>(json);
    }

    public static void Delete() => File.Delete(ConfigPath);
}
```

### Step 3.3 — `MachineID.cs` — Hardware fingerprint

This generates a unique, stable ID for the machine. Used during activation so one key can't be shared across multiple machines.

```csharp
// MachineID.cs
using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

public static class MachineIdentifier
{
    public static string Get()
    {
        try
        {
            // Combine CPU ID + Motherboard serial for a stable fingerprint
            string cpuId    = GetWmiValue("Win32_Processor", "ProcessorId");
            string boardId  = GetWmiValue("Win32_BaseBoard", "SerialNumber");
            string raw      = cpuId + boardId;

            // Hash it so we never store raw hardware IDs
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, 32);
            }
        }
        catch
        {
            // Fallback: use machine name hash
            return Environment.MachineName.GetHashCode().ToString("X8");
        }
    }

    private static string GetWmiValue(string className, string propertyName)
    {
        using (var searcher = new System.Management.ManagementObjectSearcher(
            $"SELECT {propertyName} FROM {className}"))
        {
            foreach (var obj in searcher.Get())
                return obj[propertyName]?.ToString()?.Trim() ?? "";
        }
        return "";
    }
}
```

Add `System.Management` reference: right-click project → Add Reference → Assemblies → `System.Management`.

---

## Phase 4 — Core Service Logic

### Step 4.1 — `SyncEngine.cs` — Port sync logic from `TallySyncAgent`

Move the existing sync logic from `TallySyncAgent/Program.cs` into a clean engine class. The engine is reusable by both the service and a manual "Sync Now" button in the tray.

```csharp
// SyncEngine.cs
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

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

        var dataTypes = new[] { "Ledger", "Voucher", "StockItem" };
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
                result.Errors.Add($"{dataType}: {ex.Message}");
                OnStatusChanged?.Invoke($"{dataType} sync failed");
            }
        }

        result.CompletedAt = DateTime.Now;
        OnStatusChanged?.Invoke($"Last synced: {DateTime.Now:HH:mm}");
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

        var payload = JsonConvert.SerializeObject(new { DataType = dataType, RawXml = rawXml });
        using (var req = new HttpRequestMessage(HttpMethod.Post, _apiBase + "/api/tally/sync"))
        {
            req.Headers.Add("X-Sync-Token", _config.SyncToken);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var apiResp = await _apiClient.SendAsync(req);
            apiResp.EnsureSuccessStatusCode();
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
```

### Step 4.2 — `AutoUpdater.cs` — Self-update mechanism

```csharp
// AutoUpdater.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class AutoUpdater
{
    private static readonly HttpClient _client = new HttpClient();
    private readonly string _versionEndpoint;
    private readonly string _currentVersion;

    public AutoUpdater(string versionEndpoint)
    {
        _versionEndpoint = versionEndpoint;
        // Read from AssemblyInfo.cs — set this in your project properties
        _currentVersion = Assembly.GetExecutingAssembly()
            .GetName().Version.ToString(3); // e.g. "1.0.0"
    }

    public async Task<bool> CheckAndUpdateAsync()
    {
        try
        {
            string json     = await _client.GetStringAsync(_versionEndpoint);
            dynamic info    = JsonConvert.DeserializeObject(json);
            string latest   = (string)info.version;
            string url      = (string)info.downloadUrl;

            if (string.IsNullOrEmpty(url) || latest == _currentVersion)
                return false; // already up to date

            if (!IsNewerVersion(latest, _currentVersion))
                return false;

            // Download new exe to a temp file
            byte[] newExe = await _client.GetByteArrayAsync(url);
            string tempPath = Path.Combine(Path.GetTempPath(), "InsidashConnector_update.exe");
            File.WriteAllBytes(tempPath, newExe);

            // Write a small batch script that:
            // 1. Waits for current process to exit
            // 2. Copies new exe over the current one
            // 3. Restarts the service
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            string batchPath  = Path.Combine(Path.GetTempPath(), "insidash_update.bat");
            File.WriteAllText(batchPath,
                $"@echo off\r\n" +
                $"timeout /t 3 /nobreak >nul\r\n" +
                $"copy /y \"{tempPath}\" \"{currentExe}\"\r\n" +
                $"sc stop InsidashTallyConnector\r\n" +
                $"timeout /t 2 /nobreak >nul\r\n" +
                $"sc start InsidashTallyConnector\r\n" +
                $"del \"{batchPath}\"\r\n");

            // Launch the batch as a hidden process and exit this one
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
            {
                WindowStyle    = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            return true; // update triggered, caller should exit the service
        }
        catch
        {
            return false; // update failed silently — carry on
        }
    }

    private bool IsNewerVersion(string latest, string current)
    {
        return Version.TryParse(latest, out var l)
            && Version.TryParse(current, out var c)
            && l > c;
    }
}
```

### Step 4.3 — `ConnectorService.cs` — The Windows Service

```csharp
// ConnectorService.cs
using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;

public class ConnectorService : ServiceBase
{
    public const string SERVICE_NAME = "InsidashTallyConnector";
    public const string DISPLAY_NAME = "Insidash Tally Connector";

    private CancellationTokenSource _cts;
    private Task                    _syncTask;
    private SyncEngine              _engine;
    private AutoUpdater             _updater;
    private ConnectorConfig         _config;
    private string                  _apiBase;

    public ConnectorService()
    {
        ServiceName = SERVICE_NAME;
        CanStop     = true;
        CanPauseAndContinue = false;
    }

    protected override void OnStart(string[] args)
    {
        _apiBase = ConfigurationManager.AppSettings["ApiBaseUrl"];
        _config  = LocalConfig.Load();  // throws if not activated — service won't start
        _updater = new AutoUpdater(_apiBase + "/api/connector/version");
        _engine  = new SyncEngine(_config, _apiBase);
        _cts     = new CancellationTokenSource();

        _syncTask = Task.Run(() => RunLoop(_cts.Token));
    }

    protected override void OnStop()
    {
        _cts?.Cancel();
        _syncTask?.Wait(TimeSpan.FromSeconds(10));
    }

    private async Task RunLoop(CancellationToken ct)
    {
        // Check for updates on startup
        bool updateStarted = await _updater.CheckAndUpdateAsync();
        if (updateStarted) return; // batch will restart us after update

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _engine.RunFullSyncAsync();
            }
            catch (Exception ex)
            {
                // Log to Windows Event Log
                EventLog.WriteEntry(SERVICE_NAME,
                    $"Sync error: {ex.Message}", System.Diagnostics.EventLogEntryType.Warning);
            }

            // Wait for next sync interval (default 5 minutes)
            try { await Task.Delay(_config.SyncIntervalMs, ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
```

---

## Phase 5 — System Tray Application

### Step 5.1 — `ActivationWindow.cs` — First-run form

This WinForms form appears when the agent has no saved config (first run after install). It has one text field for the activation key and a Connect button.

```csharp
// ActivationWindow.cs
using System;
using System.Net.Http;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

public class ActivationWindow : Form
{
    private TextBox    _keyInput;
    private Button     _connectBtn;
    private Label      _statusLabel;
    private static readonly HttpClient _client = new HttpClient();

    public ActivationWindow()
    {
        Text            = "Insidash Tally Connector — Activation";
        Size            = new System.Drawing.Size(420, 220);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;

        var label = new Label {
            Text = "Enter your Activation Key:", Location = new System.Drawing.Point(20, 30),
            AutoSize = true
        };

        _keyInput = new TextBox {
            Location = new System.Drawing.Point(20, 55), Width = 360,
            CharacterCasing = CharacterCasing.Upper, Font = new System.Drawing.Font("Consolas", 12)
        };

        _connectBtn = new Button {
            Text = "Activate", Location = new System.Drawing.Point(20, 95),
            Width = 360, Height = 35
        };
        _connectBtn.Click += OnActivateClick;

        _statusLabel = new Label {
            Location = new System.Drawing.Point(20, 140), Width = 360,
            AutoSize = false, ForeColor = System.Drawing.Color.Red
        };

        Controls.AddRange(new Control[] { label, _keyInput, _connectBtn, _statusLabel });
    }

    private async void OnActivateClick(object sender, EventArgs e)
    {
        string key = _keyInput.Text.Trim();
        if (key.Length != 16) {
            _statusLabel.Text = "Key must be exactly 16 characters.";
            return;
        }

        _connectBtn.Enabled = false;
        _statusLabel.Text   = "Activating...";
        _statusLabel.ForeColor = System.Drawing.Color.Gray;

        try
        {
            string apiBase = System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"];
            var body       = JsonConvert.SerializeObject(new {
                activationKey = key,
                machineID     = MachineIdentifier.Get(),
                agentVersion  = System.Reflection.Assembly.GetExecutingAssembly()
                                    .GetName().Version.ToString(3)
            });

            using (var req = new HttpRequestMessage(HttpMethod.Post, apiBase + "/api/connector/activate"))
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp    = await _client.SendAsync(req);

                if (!resp.IsSuccessStatusCode) {
                    _statusLabel.Text      = "Invalid or already-used key. Contact Insidash support.";
                    _statusLabel.ForeColor = System.Drawing.Color.Red;
                    _connectBtn.Enabled    = true;
                    return;
                }

                string json  = await resp.Content.ReadAsStringAsync();
                dynamic data = JsonConvert.DeserializeObject(json);

                LocalConfig.Save(new ConnectorConfig {
                    SyncToken      = (string)data.syncToken,
                    CompanyID      = (int)data.companyId,
                    TallyHost      = (string)data.tallyHost ?? "localhost",
                    TallyPort      = (string)data.tallyPort ?? "9000",
                    SyncIntervalMs = (int)(data.syncIntervalMs ?? 300000)
                });

                MessageBox.Show(
                    "✓ Activation successful! The Tally Connector is now active.\n\n" +
                    "Tally data will sync automatically in the background.",
                    "Insidash Connected", MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text      = $"Connection error: {ex.Message}";
            _statusLabel.ForeColor = System.Drawing.Color.Red;
            _connectBtn.Enabled    = true;
        }
    }
}
```

### Step 5.2 — `TrayApplication.cs` — System tray icon and context menu

```csharp
// TrayApplication.cs
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

public class TrayApplication : ApplicationContext
{
    private NotifyIcon   _trayIcon;
    private SyncEngine   _engine;
    private ConnectorConfig _config;
    private System.Windows.Forms.Timer _syncTimer;

    public TrayApplication()
    {
        // Check activation
        if (!LocalConfig.Exists())
        {
            var activation = new ActivationWindow();
            if (activation.ShowDialog() != DialogResult.OK)
            {
                Application.Exit();
                return;
            }
        }

        _config = LocalConfig.Load();
        _engine = new SyncEngine(_config, System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"]);
        _engine.OnStatusChanged += UpdateTooltip;

        // Build tray icon
        _trayIcon = new NotifyIcon
        {
            Icon    = SystemIcons.Application, // Replace with your own .ico file
            Visible = true,
            Text    = "Insidash Tally Connector"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Sync Now",        null, OnSyncNow);
        menu.Items.Add("Open Settings",   null, OnOpenSettings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Connector",  null, OnExit);
        _trayIcon.ContextMenuStrip = menu;

        // Start periodic sync timer
        _syncTimer = new System.Windows.Forms.Timer { Interval = _config.SyncIntervalMs };
        _syncTimer.Tick += async (s, e) => await _engine.RunFullSyncAsync();
        _syncTimer.Start();

        // Run first sync immediately
        Task.Run(async () => await _engine.RunFullSyncAsync());

        // Check for updates on startup
        Task.Run(async () =>
        {
            var updater = new AutoUpdater(
                System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"] + "/api/connector/version");
            await updater.CheckAndUpdateAsync();
        });
    }

    private void UpdateTooltip(string message)
    {
        // Must update UI on the UI thread
        if (_trayIcon != null)
            _trayIcon.Text = $"Insidash | {message}";
    }

    private async void OnSyncNow(object sender, EventArgs e)
    {
        _trayIcon.ShowBalloonTip(2000, "Insidash", "Syncing Tally data...", ToolTipIcon.Info);
        await _engine.RunFullSyncAsync();
        _trayIcon.ShowBalloonTip(2000, "Insidash", "Sync complete ✓", ToolTipIcon.Info);
    }

    private void OnOpenSettings(object sender, EventArgs e)
    {
        new SettingsWindow(_config).ShowDialog();
        // Reload config in case user changed Tally host/port
        _config = LocalConfig.Load();
        _engine = new SyncEngine(_config, System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"]);
        _engine.OnStatusChanged += UpdateTooltip;
    }

    private void OnExit(object sender, EventArgs e)
    {
        _syncTimer.Stop();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
```

### Step 5.3 — `SettingsWindow.cs` — Tally host/port config form

```csharp
// SettingsWindow.cs
using System;
using System.Windows.Forms;

public class SettingsWindow : Form
{
    private TextBox _hostInput, _portInput;
    private Button  _saveBtn;

    public SettingsWindow(ConnectorConfig config)
    {
        Text            = "Insidash Tally Connector — Settings";
        Size            = new System.Drawing.Size(380, 200);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;

        var hostLabel = new Label { Text = "Tally Host/IP:", Location = new System.Drawing.Point(20,30), AutoSize=true };
        _hostInput    = new TextBox { Text = config.TallyHost, Location = new System.Drawing.Point(130,27), Width=210 };

        var portLabel = new Label { Text = "Tally Port:", Location = new System.Drawing.Point(20,65), AutoSize=true };
        _portInput    = new TextBox { Text = config.TallyPort, Location = new System.Drawing.Point(130,62), Width=80 };

        var hint = new Label {
            Text = "Default: localhost / 9000\nChange only if Tally runs on a different machine.",
            Location = new System.Drawing.Point(20, 95), AutoSize = true,
            ForeColor = System.Drawing.Color.Gray
        };

        _saveBtn = new Button { Text = "Save", Location = new System.Drawing.Point(270,130), Width=70 };
        _saveBtn.Click += (s, e) =>
        {
            config.TallyHost = _hostInput.Text.Trim();
            config.TallyPort = _portInput.Text.Trim();
            LocalConfig.Save(config);
            MessageBox.Show("Settings saved. Next sync will use the new address.", "Saved",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        };

        Controls.AddRange(new Control[] { hostLabel, _hostInput, portLabel, _portInput, hint, _saveBtn });
    }
}
```

### Step 5.4 — `Program.cs` — Entry point (service vs tray mode)

This is the key: the same executable detects how it was launched and behaves accordingly.

```csharp
// Program.cs
using System;
using System.ServiceProcess;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // If launched by Windows Service Control Manager → run as service
        // If launched interactively (double-click, startup shortcut) → run as tray app
        if (!Environment.UserInteractive)
        {
            // Launched by SCM — run as Windows Service
            ServiceBase.Run(new ConnectorService());
        }
        else
        {
            // Launched interactively — run as system tray application
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplication());
        }
    }
}
```

---

## Phase 6 — Windows Service Registration

### Step 6.1 — `ProjectInstaller.cs` — Service installer class

```csharp
// ProjectInstaller.cs
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

[RunInstaller(true)]
public class ProjectInstaller : Installer
{
    public ProjectInstaller()
    {
        var processInstaller = new ServiceProcessInstaller
        {
            Account = ServiceAccount.LocalSystem  // runs as SYSTEM — can reach network
        };

        var serviceInstaller = new ServiceInstaller
        {
            ServiceName  = ConnectorService.SERVICE_NAME,
            DisplayName  = ConnectorService.DISPLAY_NAME,
            Description  = "Syncs Tally Prime accounting data to Insidash cloud for AI-powered queries.",
            StartType    = ServiceStartMode.Automatic  // starts on Windows boot
        };

        Installers.Add(processInstaller);
        Installers.Add(serviceInstaller);
    }
}
```

### Step 6.2 — `App.config` for the connector

```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <!-- Cloud API base URL — change to your actual server before packaging -->
    <add key="ApiBaseUrl" value="https://your-cloud-server.com" />
  </appSettings>
</configuration>
```

### Step 6.3 — Manual install/uninstall commands (for testing before Inno Setup)

```batch
REM Install the service (run as Administrator)
C:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe InsidashTallyConnector.exe

REM Start the service
sc start InsidashTallyConnector

REM Check status
sc query InsidashTallyConnector

REM Uninstall the service
sc stop InsidashTallyConnector
C:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe /u InsidashTallyConnector.exe
```

---

## Phase 7 — Installer Packaging with Inno Setup

### Step 7.1 — Install Inno Setup

Download from: `https://jrsoftware.org/isdl.php` — free, no registration. Install on your dev machine.

### Step 7.2 — Create the installer script

Create `InsidashTallyConnector.iss` in the project root:

```iss
[Setup]
AppName=Insidash Tally Connector
AppVersion=1.0.0
AppPublisher=Insidash
DefaultDirName={commonpf}\InsidashTallyConnector
DefaultGroupName=Insidash
OutputDir=.\installer_output
OutputBaseFilename=InsidashTallyConnector_Setup_v1.0.0
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
; Require Windows 8.1+ (Service Control Manager APIs)
MinVersion=6.3

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Main executable and config
Source: ".\bin\Release\InsidashTallyConnector.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\bin\Release\InsidashTallyConnector.exe.config"; DestDir: "{app}"; Flags: ignoreversion
; All DLL dependencies (Newtonsoft.Json etc.)
Source: ".\bin\Release\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\bin\Release\Insidash.BLL.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: ".\bin\Release\Insidash.DAL.dll";   DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Shortcut in Start Menu for tray mode
Name: "{group}\Insidash Tally Connector"; Filename: "{app}\InsidashTallyConnector.exe"
; Add to Windows startup so tray icon launches on login
Name: "{commonstartup}\Insidash Tally Connector"; Filename: "{app}\InsidashTallyConnector.exe"

[Run]
; Install and start the Windows Service automatically after install
Filename: "{dotnet4032}\InstallUtil.exe"; Parameters: """{app}\InsidashTallyConnector.exe"""; \
    StatusMsg: "Registering Windows Service..."; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "start InsidashTallyConnector"; \
    StatusMsg: "Starting connector..."; Flags: runhidden waituntilterminated

; Launch the tray app immediately so user can see activation window
Filename: "{app}\InsidashTallyConnector.exe"; \
    Description: "Launch Insidash Tally Connector"; Flags: postinstall nowait

[UninstallRun]
; Stop and uninstall service on uninstall
Filename: "sc.exe"; Parameters: "stop InsidashTallyConnector"; Flags: runhidden
Filename: "{dotnet4032}\InstallUtil.exe"; Parameters: "/u ""{app}\InsidashTallyConnector.exe"""; \
    Flags: runhidden waituntilterminated
```

### Step 7.3 — Build the installer

1. Open Inno Setup Compiler
2. File → Open → select `InsidashTallyConnector.iss`
3. Build → Compile (`Ctrl+F9`)
4. Output: `installer_output\InsidashTallyConnector_Setup_v1.0.0.exe`

This single `.exe` is what you send to clients.

### Step 7.4 — User experience from the client's perspective

```
1. Client receives: InsidashTallyConnector_Setup_v1.0.0.exe
2. Double-clicks, clicks "Next" twice, clicks "Install"
3. Setup registers the Windows Service and launches the tray app
4. Activation window appears: "Enter your Activation Key"
5. Client types the 16-char key from their Insidash account
6. Clicks "Activate" → connects to your cloud API → validated
7. "✓ Activation successful" message appears
8. Tray icon appears in system tray
9. First sync begins automatically
10. Every 5 minutes thereafter, Tally data syncs silently
```

---

## Phase 8 — Auto-Update Release Process

> Every time you push a bug fix or new sync logic, follow this process to push the update to all installed agents automatically.

### Step 8.1 — Build and version the new release

In `Insidash.TallyConnector` project properties → Assembly Information:
- Increment `Assembly Version`: e.g., `1.0.1`
- Build in Release mode
- Run Inno Setup to produce `InsidashTallyConnector_Setup_v1.0.1.exe`

Upload the new `.exe` to a stable URL on your server:
```
https://your-cloud-server.com/downloads/InsidashTallyConnector_1.0.1.exe
```

### Step 8.2 — Register the new version in `PatchUpdate` table

```sql
-- Mark previous version inactive
UPDATE PatchUpdate SET IsActive = '0' WHERE IsActive = '1';

-- Insert new version
INSERT INTO PatchUpdate (PatchUpdateID, PatchUpdateNo, PatchUpdateDate, IsActive, IsDelete)
VALUES (NEWID(), '1.0.1', GETDATE(), '1', '0');
```

### Step 8.3 — What happens automatically

On the next Windows Service restart (or within 5 minutes if using a periodic version check), every installed agent will:

1. Call `GET /api/connector/version` → receive `{ "version": "1.0.1", "downloadUrl": "..." }`
2. Compare against its own `Assembly Version`
3. If newer: download the new `.exe` to temp folder
4. Write the update batch script
5. Launch the batch, which waits 3 seconds, copies the file, restarts the service
6. Agent restarts with the new version — zero user interaction required

### Step 8.4 — Add periodic version check to the service loop

In `ConnectorService.cs`, add a daily update check alongside the sync loop:

```csharp
private async Task RunLoop(CancellationToken ct)
{
    // Update check on startup
    bool updated = await _updater.CheckAndUpdateAsync();
    if (updated) return;

    DateTime lastUpdateCheck = DateTime.Now;

    while (!ct.IsCancellationRequested)
    {
        await _engine.RunFullSyncAsync();

        // Check for updates once every 24 hours
        if ((DateTime.Now - lastUpdateCheck).TotalHours >= 24)
        {
            bool needsRestart = await _updater.CheckAndUpdateAsync();
            if (needsRestart) return;
            lastUpdateCheck = DateTime.Now;
        }

        try { await Task.Delay(_config.SyncIntervalMs, ct); }
        catch (TaskCanceledException) { break; }
    }
}
```

---

## Verification Checklist

### After Phase 1-2 (API layer)

```sql
-- Generate a test key
EXEC sp_GenerateTallyActivationKey @CompanyID = 5;
-- Note the returned ActivationKey

-- Postman: POST /api/connector/activate with that key
-- Expected: 200 with syncToken

-- Confirm activation recorded
SELECT IsActivated, ActivatedAt, MachineID FROM TallyActivationKey WHERE CompanyID = 5;
-- Expected: IsActivated = 1, ActivatedAt is populated

-- Postman: GET /api/connector/version
-- Expected: 200 with version string
```

### After Phase 5-6 (Service)

```batch
REM Build in Release mode first, then:
InstallUtil.exe InsidashTallyConnector.exe
sc start InsidashTallyConnector
sc query InsidashTallyConnector
REM Expected: STATE = 4 RUNNING

REM Check Windows Event Log for any errors
eventvwr.msc → Windows Logs → Application → filter by source "InsidashTallyConnector"
```

### After Phase 7 (Installer)

1. Run installer on a clean machine (not your dev machine — ideally a VM)
2. Confirm service is running: `sc query InsidashTallyConnector`
3. Confirm tray icon appears
4. Enter activation key → confirm connected
5. Wait 5 minutes → check `TallyLedger` table in SSMS for new rows with the company's data

### After Phase 8 (Auto-update)

1. Insert a new `PatchUpdate` row with version `1.0.1`
2. Wait for the daily check (or temporarily set check interval to 5 minutes for testing)
3. Watch `C:\Program Files\InsidashTallyConnector\InsidashTallyConnector.exe` file modification date change
4. Confirm service restarted: check Event Log for restart entry

---

## Implementation Order Summary

```
Phase 1 → DB: TallyActivationKey table + sp_GenerateTallyActivationKey
Phase 2 → API: ConnectorApiController (activate + version endpoints)
Phase 3 → New project: Insidash.TallyConnector — LocalConfig + MachineID
Phase 4 → Core logic: SyncEngine + AutoUpdater + ConnectorService
Phase 5 → Tray UI: ActivationWindow + TrayApplication + SettingsWindow + Program.cs
Phase 6 → Service registration: ProjectInstaller + App.config + manual sc commands
Phase 7 → Packaging: Inno Setup installer script → produces single .exe
Phase 8 → Update pipeline: PatchUpdate row → auto-update flow verified end-to-end
```

> **Note to coding agent:** Do not start Phase 5 (WinForms) until Phase 4's `SyncEngine` is tested independently by calling `RunFullSyncAsync()` from a plain console test and confirming data appears in `TallyLedger`. The tray UI is just a shell around the engine — if the engine is broken the UI won't reveal it clearly.
