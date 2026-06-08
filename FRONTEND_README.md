# TalkWithTally — Frontend Implementation Plan

> **Purpose:** This document covers the complete frontend build of the TalkWithTally chatbot widget. You are building this from scratch as a standalone HTML/CSS/JS prototype — a blank page with only the chatbot widget visible in the bottom-right corner. Once working, the entire widget block (HTML + CSS + JS) is copied into the real Insidash dashboard with zero structural changes needed. Tech stack is strictly: HTML5, CSS3, Bootstrap 5, JavaScript, jQuery, AJAX. No React, no Vue, no bundler.

---

## Phase 0 — Environment Setup & Folder Structure

> **Start here.** Before writing a single line of widget code, get the project folder, dependencies, and dev server in place. This phase produces a blank browser page that you can confirm is working before any widget code is added.

### Step 0.1 — Prerequisites (install if not already present)

| Tool | Version | Purpose | Download |
|---|---|---|---|
| Node.js | 18+ LTS | Only needed to run `live-server` for local dev | nodejs.org |
| live-server | latest | Auto-refreshes browser on file save — no manual F5 | `npm install -g live-server` |
| VS Code | latest | Recommended editor | code.visualstudio.com |
| Git | any | Version control | git-scm.com |

> **Why live-server and not just opening the HTML file directly?** AJAX calls (`$.ajax`) made from `file://` URLs are blocked by the browser's CORS policy. You must serve the files over `http://localhost` even during local development. `live-server` does this with one command.

### Step 0.2 — Folder structure

Create this exact folder layout. Every file referenced in later phases maps to a path here.

```
TalkWithTally-Frontend/
│
├── index.html                  ← Standalone dev shell (blank page + widget)
│
├── css/
│   ├── talkwithtally.css       ← Widget styles (Phase 3)
│   └── vendor/
│       └── bootstrap.min.css   ← Bootstrap 5 (downloaded, not CDN — see Step 0.4)
│
├── js/
│   ├── talkwithtally.js        ← Widget logic (Phase 4)
│   └── vendor/
│       ├── jquery.min.js       ← jQuery 3.7
│       └── bootstrap.bundle.min.js  ← Bootstrap 5 JS + Popper bundled
│
├── assets/
│   └── icons/
│       └── tally-icon.svg      ← Optional: custom trigger button icon
│
└── README.md                   ← This file (copy here for the coding agent)
```

Create the folders now:

```bash
mkdir TalkWithTally-Frontend
cd TalkWithTally-Frontend
mkdir -p css/vendor js/vendor assets/icons
```

### Step 0.3 — Download vendor dependencies (no CDN)

You are downloading libraries locally so the widget works offline and in Insidash's production environment without relying on external CDNs.

**jQuery 3.7.1 (minified):**
```
Download from: https://code.jquery.com/jquery-3.7.1.min.js
Save to:       js/vendor/jquery.min.js
```

**Bootstrap 5.3.3 (CSS + JS bundle):**
```
Download from: https://getbootstrap.com/docs/5.3/getting-started/download/
→ Click "Download" → extract the zip
→ Copy: css/bootstrap.min.css      → your css/vendor/bootstrap.min.css
→ Copy: js/bootstrap.bundle.min.js → your js/vendor/bootstrap.bundle.min.js
```

Verify your vendor folder looks like this:
```
js/vendor/jquery.min.js               (~90KB)
js/vendor/bootstrap.bundle.min.js     (~80KB)
css/vendor/bootstrap.min.css          (~200KB)
```

### Step 0.4 — Create `index.html` — the dev shell

This is the standalone blank page used during development. It has no sidebar, no header, no Insidash UI — just a white page and the widget in the bottom-right corner. When you copy to production later, you take everything from `<!-- TWT WIDGET START -->` to `<!-- TWT WIDGET END -->` and paste it into the real layout.

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>TalkWithTally — Dev Preview</title>

    <!-- Bootstrap 5 -->
    <link rel="stylesheet" href="css/vendor/bootstrap.min.css" />

    <!-- TalkWithTally widget styles -->
    <!-- Created in Phase 3. File must exist before opening in browser. -->
    <link rel="stylesheet" href="css/talkwithtally.css" />

    <style>
        /*
         * DEV SHELL STYLES ONLY
         * These styles are NOT copied to production.
         * They exist purely to give the blank page a neutral background
         * so the widget renders against something visible during development.
         */
        * { box-sizing: border-box; margin: 0; padding: 0; }

        body {
            background: #f0f2f5;
            min-height: 100vh;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
        }

        .dev-notice {
            position: fixed;
            top: 16px;
            left: 50%;
            transform: translateX(-50%);
            background: #1a202c;
            color: #a0aec0;
            font-size: 12px;
            padding: 6px 16px;
            border-radius: 20px;
            white-space: nowrap;
            z-index: 100;
            letter-spacing: 0.3px;
        }

        .dev-notice strong { color: #68d391; }
    </style>
</head>
<body>

    <!-- DEV ONLY: remove this div when copying to production -->
    <div class="dev-notice">
        🛠 Dev Preview &nbsp;|&nbsp; <strong>TalkWithTally widget only</strong>
        &nbsp;|&nbsp; Click the teal button → bottom right
    </div>

    <!--
    ════════════════════════════════════════════════════════════
    TWT WIDGET START
    Copy everything between these comments into Insidash layout.
    ════════════════════════════════════════════════════════════
    -->

    <!--
        SERVER-SIDE INJECTION POINT
        In production (Insidash .cshtml layout), replace this script block with:
            <script>var TWT_COMPANY_ID = @(Session["CompanyID"] ?? "0");</script>
        For local dev, hardcode a test CompanyID that has data in your DB.
    -->
    <script>
        var TWT_COMPANY_ID = 10891;  // ← change to a CompanyID that has TallyLedger rows
    </script>

    <!-- Trigger button — place near existing "Ask AI" button in production -->
    <div id="twt-trigger-btn" title="TalkWithTally — Ask your Tally data">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
             stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 2L2 7l10 5 10-5-10-5z"/>
            <path d="M2 17l10 5 10-5"/>
            <path d="M2 12l10 5 10-5"/>
        </svg>
        <span>Tally AI</span>
    </div>

    <!-- Widget container — all screens live inside here -->
    <div id="twt-widget" class="twt-hidden">
        <!-- Screens injected in Phase 2 -->
    </div>

    <!--
    ════════════════════════════════════════════════════════════
    TWT WIDGET END
    ════════════════════════════════════════════════════════════
    -->

    <!-- Vendor JS — load order is critical: jQuery first, then Bootstrap -->
    <script src="js/vendor/jquery.min.js"></script>
    <script src="js/vendor/bootstrap.bundle.min.js"></script>

    <!-- TalkWithTally logic — always last -->
    <!-- Created in Phase 4. File must exist (even empty) before opening in browser. -->
    <script src="js/talkwithtally.js"></script>

</body>
</html>
```

### Step 0.5 — Create placeholder files so the browser doesn't 404

Before you open the page, create empty placeholder files for the CSS and JS that will be built in later phases. This prevents browser console errors from breaking other scripts:

```bash
# From inside TalkWithTally-Frontend/

# Empty CSS placeholder
echo "/* talkwithtally.css — built in Phase 3 */" > css/talkwithtally.css

# Empty JS placeholder
echo "/* talkwithtally.js — built in Phase 4 */" > js/talkwithtally.js
```

### Step 0.6 — Start the dev server and verify the blank page

```bash
# From inside TalkWithTally-Frontend/
live-server --port=5500 --open=index.html
```

Your browser opens to `http://localhost:5500`. You should see:
- ✅ A grey/light page with a small dark pill at the top: *"Dev Preview | TalkWithTally widget only"*
- ✅ No console errors (check DevTools → Console)
- ✅ No 404s in the Network tab
- ❌ No widget button yet — that comes in Phase 2

### Step 0.7 — Configure API base URL for local dev

The widget will make AJAX calls to your backend API. During local development your API runs at a different port than `live-server`. You have two options:

**Option A — API running locally (recommended for dev):**
Your `Insidash.TallyApi` project runs on e.g. `http://localhost:5000`. Open `js/talkwithtally.js` (after Phase 4) and set:
```javascript
const API_BASE = 'http://localhost:5000/api/tally';
```

You will also need to confirm CORS is enabled on `http://localhost:5500` in `WebApiConfig.cs`:
```csharp
var cors = new EnableCorsAttribute(
    origins: "http://localhost:5500",  // add dev origin
    headers: "*",
    methods: "GET,POST");
config.EnableCors(cors);
```

**Option B — API already deployed to cloud server:**
```javascript
const API_BASE = 'https://your-cloud-server.com/api/tally';
```

No CORS change needed if the cloud server already has `*` origin allowed.

### Step 0.8 — Production copy checklist (for when you integrate into Insidash)

When this widget is ready to move into the real Insidash dashboard, here is exactly what gets copied where:

| What | From dev file | To production |
|---|---|---|
| Widget HTML | Everything between `TWT WIDGET START` and `TWT WIDGET END` in `index.html` | Paste into Insidash `_Layout.cshtml` before `</body>` |
| Widget CSS | `css/talkwithtally.css` (entire file) | Copy to Insidash `Content/css/talkwithtally.css`, add `<link>` in layout `<head>` |
| Widget JS | `js/talkwithtally.js` (entire file) | Copy to Insidash `Scripts/talkwithtally.js`, add `<script>` in layout before `</body>` |
| jQuery | Already in Insidash | Skip — do not double-load |
| Bootstrap 5 | Already in Insidash | Skip — do not double-load |
| `TWT_COMPANY_ID` script block | Hardcoded `10891` in dev | Replace with `@(Session["CompanyID"] ?? "0")` server injection |
| Dev notice div | `<div class="dev-notice">` | Delete — do not copy to production |

> **Do not copy `js/vendor/` or `css/vendor/` to production.** Insidash already loads jQuery and Bootstrap. Loading them twice breaks Bootstrap's JS components.

---

## Architecture Snapshot — What Already Exists vs What We Build

| Layer | Status | Detail |
|---|---|---|
| `POST /api/tally/chat` | ✅ Built | NL2SQL pipeline, returns `{ response, serviceUsed, elapsedMs }` |
| `POST /api/tally/sync` | ✅ Built | Sync endpoint called by Windows agent |
| `GET /api/connector/version` | ✅ Built | Returns latest connector version |
| `GET /api/tally/sync-status` | 🔨 New (Phase 1) | Returns last sync time + record counts |
| `POST /api/tally/sync-now` | 🔨 New (Phase 1) | Triggers immediate sync cycle |
| `GET /api/tally/suggestions` | 🔨 New (Phase 1) | Returns suggestion chips for this company |
| Frontend widget HTML/CSS/JS | 🔨 New (Phase 2–6) | Everything in this document |

---

## Final UI Layout — Reference Before Starting

```
Bottom-right corner of Insidash dashboard:

                    ┌─────────────────────────────────────┐
                    │  🏦 TalkWithTally          [_] [X]  │  ← Header
                    │  ● Connected  🔄 Today 14:32 [Sync] │  ← Sync bar
                    ├─────────────────────────────────────┤
                    │                                     │
                    │   [AI message bubble]               │  ← Chat area
                    │               [User bubble]         │
                    │   [AI message bubble]               │
                    │   ┌───────────────────┐             │
                    │   │ Name  │ Balance   │             │  ← Table response
                    │   │ Cash  │ ₹50,000   │             │
                    │   └───────────────────┘             │
                    │                                     │
                    ├─────────────────────────────────────┤
                    │  [Ledger Balance] [Trial Balance]   │  ← Suggestion chips
                    │  [Top Debtors]   [Cash & Bank]      │
                    ├─────────────────────────────────────┤
                    │  Ask about your Tally data...  [➤]  │  ← Input bar
                    └─────────────────────────────────────┘

[Ask AI]  ← existing button, untouched
[Tally]   ← new button, sits above it
```

---

## Phase 1 — Backend: Two New API Endpoints

> Build these first. The frontend is useless without them. Both go in `Insidash.TallyApi`.

### Step 1.1 — `GET /api/tally/sync-status`

This endpoint is called by the frontend on every popup open to determine the connection state and populate the sync bar in the header.

Add to `TallyApiController.cs`:

```csharp
[HttpGet, Route("sync-status")]
public IHttpActionResult GetSyncStatus()
{
    // CompanyID comes from the authenticated session — never from request body
    int companyId = GetAuthenticatedCompanyId(); // your existing auth helper

    using (var db = new InsidashTallyContext())
    {
        // Check if this company has any synced Tally data at all
        var snapshots = db.Database.SqlQuery<SyncStatusDto>(@"
            SELECT
                s.DataType,
                s.RecordCount,
                s.LastSyncedAt,
                s.SyncStatus
            FROM TallySyncState s
            WHERE s.CompanyID = @p0
            ORDER BY s.LastSyncedAt DESC",
            companyId).ToList();

        // Also check TallyActivationKey to know if connector is installed
        bool isActivated = db.TallyActivationKeys
            .Any(k => k.CompanyID == companyId && k.IsActivated && k.IsActive);

        if (!isActivated || !snapshots.Any())
        {
            return Ok(new
            {
                status          = "not_connected",
                isActivated     = isActivated,
                lastSyncedAt    = (string)null,
                totalLedgers    = 0,
                totalVouchers   = 0,
                downloadUrl     = "https://your-server.com/downloads/InsidashTallyConnector_Setup.exe"
            });
        }

        var lastSync   = snapshots.OrderByDescending(s => s.LastSyncedAt).First();
        int ledgerCount  = db.Database.SqlQuery<int>(
            "SELECT COUNT(*) FROM TallyLedger WHERE CompanyID = @p0", companyId).First();
        int voucherCount = db.Database.SqlQuery<int>(
            "SELECT COUNT(*) FROM TallyVoucher WHERE CompanyID = @p0", companyId).First();

        return Ok(new
        {
            status        = "connected",
            isActivated   = true,
            lastSyncedAt  = lastSync.LastSyncedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            totalLedgers  = ledgerCount,
            totalVouchers = voucherCount,
            syncStatus    = lastSync.SyncStatus  // "Success" or "Failed"
        });
    }
}

public class SyncStatusDto
{
    public string   DataType     { get; set; }
    public int      RecordCount  { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public string   SyncStatus   { get; set; }
}
```

### Step 1.2 — `POST /api/tally/sync-now`

Called when the user clicks "Sync Now" in the chat header. This does NOT directly talk to Tally — the Windows agent owns the Tally connection. Instead, this endpoint sets a flag in the DB that the agent polls, so the agent triggers a sync ahead of its 5-minute schedule.

First, add a `SyncRequest` table in SSMS:

```sql
USE Popway_BillingERP;
GO

CREATE TABLE TallySyncRequest (
    RequestID   NVARCHAR(50) NOT NULL PRIMARY KEY DEFAULT NEWID(),
    CompanyID   INT          NOT NULL,
    RequestedAt DATETIME     NOT NULL DEFAULT GETDATE(),
    IsProcessed BIT          NOT NULL DEFAULT 0,
    ProcessedAt DATETIME     NULL
);
```

Then the endpoint:

```csharp
[HttpPost, Route("sync-now")]
public IHttpActionResult RequestSync()
{
    int companyId = GetAuthenticatedCompanyId();

    using (var db = new InsidashTallyContext())
    {
        // Only allow one pending request at a time
        bool alreadyPending = db.Database.SqlQuery<int>(@"
            SELECT COUNT(*) FROM TallySyncRequest
            WHERE CompanyID = @p0 AND IsProcessed = 0", companyId).First() > 0;

        if (alreadyPending)
            return Ok(new { queued = true, message = "Sync already in progress." });

        db.Database.ExecuteSqlCommand(@"
            INSERT INTO TallySyncRequest (RequestID, CompanyID, RequestedAt, IsProcessed)
            VALUES (NEWID(), @p0, GETDATE(), 0)", companyId);

        return Ok(new { queued = true, message = "Sync request sent to connector." });
    }
}
```

Update the Windows sync agent (`ConnectorService.cs`) to check this table at the start of each loop iteration:

```csharp
// Add inside RunLoop(), before Task.Delay
bool manualRequested = CheckForSyncRequest(companyId, conn);
if (manualRequested || timeForScheduledSync)
{
    await _engine.RunFullSyncAsync();
    MarkSyncRequestProcessed(companyId, conn);
}
```

### Step 1.3 — `GET /api/tally/suggestions`

Returns the Tally-specific suggestion chips. These are sourced from the existing `AISuggestion` table filtered to Tally domains. Based on live DB inspection, the Tally-relevant suggestions are DomainID 1 (Sales), 2 (Customer), 3 (Product) — but we need a dedicated Tally domain seeded.

First, seed a Tally-specific domain and suggestions in SSMS:

```sql
-- Add Tally domain (only if it doesn't exist)
INSERT INTO AIDomain (DomainID, Name, Keywords, IsActive, CompanyID, CreatedDate)
VALUES ('tally_001', 'Tally', 'tally,ledger,voucher,balance,trial,stock', '1', NULL, GETDATE());

-- Tally-specific suggestions
INSERT INTO AISuggestion (SuggestionID, DomainID, ActionID, Text, Keywords, IsActive, CompanyID, CreatedDate)
VALUES
('tally_s1', 'tally_001', NULL, 'Ledger Balance',    'ledger,balance,khaata',          '1', NULL, GETDATE()),
('tally_s2', 'tally_001', NULL, 'Trial Balance',      'trial balance,trial,pakka hisab', '1', NULL, GETDATE()),
('tally_s3', 'tally_001', NULL, 'Top Debtors',        'debtor,sundry,baki,outstanding',  '1', NULL, GETDATE()),
('tally_s4', 'tally_001', NULL, 'Cash & Bank',        'cash,bank,hand,naqdh',            '1', NULL, GETDATE()),
('tally_s5', 'tally_001', NULL, 'Profit & Loss',      'profit,loss,p&l,nafa,nuksan',     '1', NULL, GETDATE()),
('tally_s6', 'tally_001', NULL, 'Stock Summary',      'stock,inventory,maal,item',       '1', NULL, GETDATE()),
('tally_s7', 'tally_001', NULL, 'Pending Receivables','pending,receivable,lena,due',     '1', NULL, GETDATE()),
('tally_s8', 'tally_001', NULL, 'Sales Vouchers',     'sales,sale,vikri,invoice',        '1', NULL, GETDATE());
```

Then the endpoint:

```csharp
[HttpGet, Route("suggestions")]
public IHttpActionResult GetSuggestions()
{
    using (var db = new InsidashTallyContext())
    {
        var suggestions = db.Database.SqlQuery<SuggestionDto>(@"
            SELECT s.SuggestionID, s.Text
            FROM AISuggestion s
            INNER JOIN AIDomain d ON d.DomainID = s.DomainID
            WHERE d.Name = 'Tally' AND s.IsActive = '1'
            ORDER BY s.SuggestionID")
            .ToList();

        return Ok(suggestions);
    }
}

public class SuggestionDto
{
    public string SuggestionID { get; set; }
    public string Text         { get; set; }
}
```

### Step 1.4 — Verify all three endpoints with Postman

```
GET  /api/tally/sync-status  → { "status": "not_connected" } or { "status": "connected", "totalLedgers": 847 }
POST /api/tally/sync-now     → { "queued": true }
GET  /api/tally/suggestions  → [ { "text": "Ledger Balance" }, ... ] (8 items)
```

---

## Phase 2 — HTML Structure

> Create one new partial view or HTML include file in your Insidash project. The entire widget lives in a single self-contained block appended to the `<body>`.

### Step 2.1 — Floating trigger button

Add this to the bottom-right of your main layout file (e.g., `_Layout.cshtml` or `index.html`), placed just above the existing "Ask AI" button:

```html
<!-- TalkWithTally trigger button — sits above existing Ask AI button -->
<div id="twt-trigger-btn" title="TalkWithTally — Ask your Tally data">
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
         stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M12 2L2 7l10 5 10-5-10-5z"/>
        <path d="M2 17l10 5 10-5"/>
        <path d="M2 12l10 5 10-5"/>
    </svg>
    <span>Tally AI</span>
</div>
```

### Step 2.2 — Main widget container

Add this block immediately after the trigger button. It contains all three screens (splash, not-connected, and chat) as separate `div`s — JavaScript will show/hide them:

```html
<!-- ═══════════════════════════════════════════════════════════
     TalkWithTally Widget Container
     All screens live inside this single popup container
     ═══════════════════════════════════════════════════════════ -->
<div id="twt-widget" class="twt-hidden">

    <!-- ── SCREEN 1: SPLASH (first-ever open) ─────────────────── -->
    <div id="twt-screen-splash" class="twt-screen">
        <div class="twt-splash-body">
            <div class="twt-splash-icon">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none"
                     stroke="#0d7377" stroke-width="1.5">
                    <path d="M12 2L2 7l10 5 10-5-10-5z"/>
                    <path d="M2 17l10 5 10-5"/>
                    <path d="M2 12l10 5 10-5"/>
                </svg>
            </div>
            <h2 class="twt-splash-title">TalkWithTally</h2>
            <p class="twt-splash-sub">Your Tally accounting data,<br>answered in plain English.</p>
            <ul class="twt-splash-features">
                <li>📊 Ledger balances &amp; Trial Balance</li>
                <li>📈 Sales, payments &amp; vouchers</li>
                <li>👥 Debtor &amp; creditor summaries</li>
                <li>📦 Stock &amp; inventory queries</li>
            </ul>
            <button id="twt-splash-proceed" class="twt-btn-primary">
                Get Started &rarr;
            </button>
        </div>
    </div>

    <!-- ── SCREEN 2: NOT CONNECTED (agent not installed) ──────── -->
    <div id="twt-screen-notconnected" class="twt-screen twt-hidden">
        <div class="twt-notconnected-body">
            <div class="twt-nc-icon">⚠️</div>
            <h3>Tally Connector Not Found</h3>
            <p>
                To use TalkWithTally, you need to install the
                <strong>Insidash Tally Connector</strong> on the Windows
                machine where Tally Prime is running.
            </p>
            <p class="twt-nc-steps-title">3 simple steps:</p>
            <ol class="twt-nc-steps">
                <li>Download &amp; install the connector below</li>
                <li>Enter your <strong>Activation Key</strong> (get it from your Insidash admin)</li>
                <li>Keep Tally Prime open — data syncs automatically</li>
            </ol>
            <a id="twt-download-btn" href="#" target="_blank" class="twt-btn-primary">
                ⬇ Download Tally Connector
            </a>
            <button id="twt-nc-retry" class="twt-btn-secondary">
                I've installed it — Check Again
            </button>
        </div>
    </div>

    <!-- ── SCREEN 3: MAIN CHAT ────────────────────────────────── -->
    <div id="twt-screen-chat" class="twt-screen twt-hidden">

        <!-- Header -->
        <div class="twt-header">
            <div class="twt-header-left">
                <span class="twt-dot" id="twt-status-dot"></span>
                <span class="twt-header-title">TalkWithTally</span>
                <span class="twt-header-badge">BETA</span>
            </div>
            <div class="twt-header-right">
                <button id="twt-close-btn" class="twt-icon-btn" title="Close">✕</button>
            </div>
        </div>

        <!-- Sync bar (below header) -->
        <div class="twt-syncbar" id="twt-syncbar">
            <span id="twt-sync-label">
                <svg class="twt-sync-icon" width="13" height="13" viewBox="0 0 24 24"
                     fill="none" stroke="currentColor" stroke-width="2.5">
                    <polyline points="23 4 23 10 17 10"/>
                    <polyline points="1 20 1 14 7 14"/>
                    <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>
                </svg>
                <span id="twt-sync-text">Checking sync...</span>
            </span>
            <button id="twt-sync-now-btn" class="twt-syncnow-btn" title="Sync latest Tally data now">
                Sync Now
            </button>
        </div>

        <!-- Sync success toast (hidden by default, drops from syncbar) -->
        <div id="twt-sync-toast" class="twt-sync-toast twt-hidden">
            ✓ Tally data synced successfully
        </div>

        <!-- Chat messages area -->
        <div class="twt-chat-area" id="twt-chat-area">
            <!-- Messages injected here by JS -->
        </div>

        <!-- Suggestion chips -->
        <div class="twt-chips" id="twt-chips">
            <!-- Chips injected here by JS from API -->
        </div>

        <!-- Input bar -->
        <div class="twt-input-bar">
            <input
                type="text"
                id="twt-input"
                class="twt-input"
                placeholder="Ask about your Tally data..."
                autocomplete="off"
                maxlength="300"
            />
            <button id="twt-send-btn" class="twt-send-btn" title="Send">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M2 21l21-9L2 3v7l15 2-15 2z"/>
                </svg>
            </button>
        </div>

        <div class="twt-footer-label">
            Powered by Insidash AI &nbsp;|&nbsp; Tally Prime data
        </div>

    </div>
    <!-- end screen-chat -->

</div>
<!-- end twt-widget -->
```

---

## Phase 3 — CSS Styling

> Add this as a new file: `Content/css/talkwithtally.css`. Link it in your layout after Bootstrap 5.

```css
/* ═══════════════════════════════════════════════════════════════
   TalkWithTally Widget Styles
   Depends on Bootstrap 5 being loaded first.
   All selectors prefixed with .twt- to avoid collisions.
   ═══════════════════════════════════════════════════════════════ */

/* ── CSS Variables ───────────────────────────────────────────── */
:root {
    --twt-primary:        #0d7377;
    --twt-primary-dark:   #0a5c60;
    --twt-primary-light:  #e8f5f5;
    --twt-accent:         #14a085;
    --twt-white:          #ffffff;
    --twt-bg:             #f8fafb;
    --twt-border:         #e2e8ea;
    --twt-text:           #1a202c;
    --twt-text-muted:     #718096;
    --twt-user-bubble:    #0d7377;
    --twt-ai-bubble:      #ffffff;
    --twt-shadow:         0 8px 32px rgba(0, 0, 0, 0.14);
    --twt-radius:         16px;
    --twt-radius-sm:      10px;
    --twt-width:          380px;
    --twt-height:         560px;
    --twt-bottom:         90px;   /* sits above the existing Ask AI button */
    --twt-right:          24px;
}

/* ── Utility ─────────────────────────────────────────────────── */
.twt-hidden { display: none !important; }

/* ── Trigger Button ──────────────────────────────────────────── */
#twt-trigger-btn {
    position: fixed;
    bottom: var(--twt-bottom);
    right: var(--twt-right);
    z-index: 9998;
    display: flex;
    align-items: center;
    gap: 7px;
    background: var(--twt-primary);
    color: var(--twt-white);
    border: none;
    border-radius: 50px;
    padding: 10px 18px 10px 14px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    box-shadow: 0 4px 16px rgba(13, 115, 119, 0.35);
    transition: background 0.2s, transform 0.15s, box-shadow 0.2s;
    user-select: none;
}

#twt-trigger-btn:hover {
    background: var(--twt-primary-dark);
    transform: translateY(-2px);
    box-shadow: 0 6px 20px rgba(13, 115, 119, 0.45);
}

/* ── Widget Container ────────────────────────────────────────── */
#twt-widget {
    position: fixed;
    bottom: calc(var(--twt-bottom) + 54px);
    right: var(--twt-right);
    z-index: 9999;
    width: var(--twt-width);
    height: var(--twt-height);
    background: var(--twt-white);
    border-radius: var(--twt-radius);
    box-shadow: var(--twt-shadow);
    border: 1px solid var(--twt-border);
    display: flex;
    flex-direction: column;
    overflow: hidden;
    animation: twt-slide-up 0.25s ease;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

@keyframes twt-slide-up {
    from { opacity: 0; transform: translateY(18px); }
    to   { opacity: 1; transform: translateY(0); }
}

/* ── Screens ─────────────────────────────────────────────────── */
.twt-screen {
    display: flex;
    flex-direction: column;
    flex: 1;
    overflow: hidden;
}

/* ── Splash Screen ───────────────────────────────────────────── */
.twt-splash-body {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 32px 28px;
    text-align: center;
    background: linear-gradient(160deg, #f0fafa 0%, #ffffff 100%);
}

.twt-splash-icon {
    width: 80px;
    height: 80px;
    background: var(--twt-primary-light);
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    margin-bottom: 16px;
}

.twt-splash-title {
    font-size: 22px;
    font-weight: 700;
    color: var(--twt-primary);
    margin-bottom: 6px;
}

.twt-splash-sub {
    color: var(--twt-text-muted);
    font-size: 14px;
    margin-bottom: 20px;
    line-height: 1.5;
}

.twt-splash-features {
    list-style: none;
    padding: 0;
    margin: 0 0 24px;
    text-align: left;
    width: 100%;
}

.twt-splash-features li {
    font-size: 13.5px;
    color: var(--twt-text);
    padding: 5px 0;
    border-bottom: 1px solid #f0f0f0;
}

.twt-splash-features li:last-child { border-bottom: none; }

/* ── Not Connected Screen ────────────────────────────────────── */
.twt-notconnected-body {
    flex: 1;
    padding: 28px 24px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    align-items: center;
    text-align: center;
}

.twt-nc-icon {
    font-size: 40px;
    margin-bottom: 12px;
}

.twt-notconnected-body h3 {
    font-size: 16px;
    font-weight: 700;
    color: var(--twt-text);
    margin-bottom: 10px;
}

.twt-notconnected-body p {
    font-size: 13.5px;
    color: var(--twt-text-muted);
    line-height: 1.55;
    margin-bottom: 12px;
}

.twt-nc-steps-title {
    font-weight: 600;
    color: var(--twt-text);
    margin-bottom: 6px !important;
}

.twt-nc-steps {
    text-align: left;
    font-size: 13px;
    color: var(--twt-text-muted);
    padding-left: 20px;
    margin-bottom: 20px;
    line-height: 1.8;
}

/* ── Buttons (shared) ────────────────────────────────────────── */
.twt-btn-primary {
    display: block;
    width: 100%;
    padding: 11px 20px;
    background: var(--twt-primary);
    color: var(--twt-white);
    border: none;
    border-radius: var(--twt-radius-sm);
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    text-align: center;
    text-decoration: none;
    transition: background 0.2s;
    margin-bottom: 10px;
}

.twt-btn-primary:hover { background: var(--twt-primary-dark); color: #fff; }

.twt-btn-secondary {
    display: block;
    width: 100%;
    padding: 9px 20px;
    background: transparent;
    color: var(--twt-primary);
    border: 1.5px solid var(--twt-primary);
    border-radius: var(--twt-radius-sm);
    font-size: 13.5px;
    font-weight: 500;
    cursor: pointer;
    transition: background 0.15s;
}

.twt-btn-secondary:hover { background: var(--twt-primary-light); }

/* ── Chat Header ─────────────────────────────────────────────── */
.twt-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 13px 16px;
    background: var(--twt-primary);
    color: var(--twt-white);
    flex-shrink: 0;
}

.twt-header-left {
    display: flex;
    align-items: center;
    gap: 8px;
}

.twt-header-title {
    font-size: 15px;
    font-weight: 700;
    letter-spacing: 0.2px;
}

.twt-header-badge {
    background: rgba(255,255,255,0.2);
    color: #fff;
    font-size: 10px;
    font-weight: 700;
    padding: 2px 6px;
    border-radius: 4px;
    letter-spacing: 0.5px;
}

/* Status dot — pulses green when connected */
.twt-dot {
    width: 9px;
    height: 9px;
    border-radius: 50%;
    background: #94a3b8;  /* grey = unknown */
    flex-shrink: 0;
}

.twt-dot.connected    { background: #4ade80; animation: twt-pulse 2s infinite; }
.twt-dot.not-synced   { background: #f59e0b; }
.twt-dot.error        { background: #f87171; }

@keyframes twt-pulse {
    0%, 100% { box-shadow: 0 0 0 0 rgba(74, 222, 128, 0.5); }
    50%       { box-shadow: 0 0 0 5px rgba(74, 222, 128, 0); }
}

.twt-icon-btn {
    background: none;
    border: none;
    color: rgba(255,255,255,0.8);
    font-size: 16px;
    cursor: pointer;
    padding: 2px 6px;
    border-radius: 4px;
    transition: background 0.15s, color 0.15s;
}
.twt-icon-btn:hover { background: rgba(255,255,255,0.15); color: #fff; }

/* ── Sync Bar ────────────────────────────────────────────────── */
.twt-syncbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 6px 14px;
    background: #f0fafa;
    border-bottom: 1px solid var(--twt-border);
    flex-shrink: 0;
}

#twt-sync-label {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 11.5px;
    color: var(--twt-text-muted);
}

.twt-sync-icon { flex-shrink: 0; }
.twt-sync-icon.spinning { animation: twt-spin 1s linear infinite; }

@keyframes twt-spin { to { transform: rotate(360deg); } }

.twt-syncnow-btn {
    background: none;
    border: 1px solid var(--twt-primary);
    color: var(--twt-primary);
    font-size: 11px;
    font-weight: 600;
    padding: 3px 10px;
    border-radius: 20px;
    cursor: pointer;
    transition: background 0.15s, color 0.15s;
    white-space: nowrap;
}

.twt-syncnow-btn:hover  { background: var(--twt-primary); color: #fff; }
.twt-syncnow-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
    pointer-events: none;
}

/* ── Sync Toast ──────────────────────────────────────────────── */
.twt-sync-toast {
    background: #dcfce7;
    color: #166534;
    font-size: 12px;
    font-weight: 500;
    padding: 6px 14px;
    border-bottom: 1px solid #bbf7d0;
    text-align: center;
    flex-shrink: 0;
    animation: twt-fade-in 0.2s ease;
}

@keyframes twt-fade-in {
    from { opacity: 0; transform: translateY(-4px); }
    to   { opacity: 1; transform: translateY(0); }
}

/* ── Chat Area ───────────────────────────────────────────────── */
.twt-chat-area {
    flex: 1;
    overflow-y: auto;
    padding: 14px 14px 8px;
    background: var(--twt-bg);
    display: flex;
    flex-direction: column;
    gap: 10px;
    scroll-behavior: smooth;
}

/* Scrollbar styling */
.twt-chat-area::-webkit-scrollbar { width: 4px; }
.twt-chat-area::-webkit-scrollbar-track { background: transparent; }
.twt-chat-area::-webkit-scrollbar-thumb { background: #d1d5db; border-radius: 4px; }

/* ── Message Bubbles ─────────────────────────────────────────── */
.twt-msg-row {
    display: flex;
    gap: 8px;
    max-width: 100%;
}

.twt-msg-row.user {
    flex-direction: row-reverse;
}

.twt-msg-avatar {
    width: 28px;
    height: 28px;
    border-radius: 50%;
    background: var(--twt-primary-light);
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 13px;
    flex-shrink: 0;
    align-self: flex-end;
}

.twt-msg-row.user .twt-msg-avatar {
    background: var(--twt-user-bubble);
    color: #fff;
}

.twt-bubble {
    max-width: 80%;
    padding: 10px 13px;
    border-radius: 14px;
    font-size: 13.5px;
    line-height: 1.55;
    color: var(--twt-text);
    background: var(--twt-ai-bubble);
    box-shadow: 0 1px 4px rgba(0,0,0,0.07);
    border-bottom-left-radius: 4px;
    word-break: break-word;
}

.twt-msg-row.user .twt-bubble {
    background: var(--twt-user-bubble);
    color: #fff;
    border-bottom-right-radius: 4px;
    border-bottom-left-radius: 14px;
}

/* ── Table responses inside AI bubbles ───────────────────────── */
.twt-bubble .twt-response-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 12px;
    margin-top: 8px;
    border-radius: 6px;
    overflow: hidden;
}

.twt-bubble .twt-response-table th {
    background: var(--twt-primary);
    color: #fff;
    padding: 6px 10px;
    text-align: left;
    font-weight: 600;
    font-size: 11px;
    text-transform: uppercase;
    letter-spacing: 0.3px;
}

.twt-bubble .twt-response-table td {
    padding: 6px 10px;
    border-bottom: 1px solid #f0f4f4;
    color: var(--twt-text);
}

.twt-bubble .twt-response-table tr:last-child td { border-bottom: none; }
.twt-bubble .twt-response-table tr:nth-child(even) td { background: #f8fafa; }

/* ── Typing Indicator ────────────────────────────────────────── */
.twt-typing {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 10px 13px;
    background: var(--twt-ai-bubble);
    border-radius: 14px;
    border-bottom-left-radius: 4px;
    box-shadow: 0 1px 4px rgba(0,0,0,0.07);
    width: fit-content;
}

.twt-typing span {
    width: 7px;
    height: 7px;
    background: var(--twt-primary);
    border-radius: 50%;
    opacity: 0.4;
    animation: twt-bounce 1.2s ease-in-out infinite;
}

.twt-typing span:nth-child(2) { animation-delay: 0.2s; }
.twt-typing span:nth-child(3) { animation-delay: 0.4s; }

@keyframes twt-bounce {
    0%, 80%, 100% { transform: translateY(0); opacity: 0.4; }
    40%            { transform: translateY(-6px); opacity: 1; }
}

/* ── Timestamp under bubbles ─────────────────────────────────── */
.twt-msg-time {
    font-size: 10.5px;
    color: #a0aec0;
    margin-top: 2px;
    padding: 0 4px;
    text-align: right;
}

.twt-msg-row.user .twt-msg-time { text-align: right; }
.twt-msg-row:not(.user) .twt-msg-time { text-align: left; }

/* ── Suggestion Chips ────────────────────────────────────────── */
.twt-chips {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    padding: 8px 12px;
    background: var(--twt-white);
    border-top: 1px solid var(--twt-border);
    flex-shrink: 0;
}

.twt-chip {
    background: var(--twt-primary-light);
    color: var(--twt-primary);
    border: 1px solid rgba(13, 115, 119, 0.2);
    border-radius: 20px;
    padding: 5px 12px;
    font-size: 12px;
    font-weight: 500;
    cursor: pointer;
    transition: background 0.15s, color 0.15s, border-color 0.15s;
    white-space: nowrap;
}

.twt-chip:hover {
    background: var(--twt-primary);
    color: #fff;
    border-color: var(--twt-primary);
}

/* ── Input Bar ───────────────────────────────────────────────── */
.twt-input-bar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 10px 12px;
    background: var(--twt-white);
    border-top: 1px solid var(--twt-border);
    flex-shrink: 0;
}

.twt-input {
    flex: 1;
    border: 1.5px solid var(--twt-border);
    border-radius: 22px;
    padding: 9px 16px;
    font-size: 13.5px;
    color: var(--twt-text);
    background: var(--twt-bg);
    outline: none;
    transition: border-color 0.15s;
}

.twt-input:focus { border-color: var(--twt-primary); background: #fff; }
.twt-input::placeholder { color: #a0aec0; }

.twt-send-btn {
    width: 38px;
    height: 38px;
    border-radius: 50%;
    background: var(--twt-primary);
    color: #fff;
    border: none;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    flex-shrink: 0;
    transition: background 0.15s, transform 0.1s;
}

.twt-send-btn:hover   { background: var(--twt-primary-dark); }
.twt-send-btn:active  { transform: scale(0.93); }
.twt-send-btn:disabled { opacity: 0.5; cursor: not-allowed; }

/* ── Footer label ────────────────────────────────────────────── */
.twt-footer-label {
    text-align: center;
    font-size: 10px;
    color: #c0ccd4;
    padding: 4px 0 6px;
    background: var(--twt-white);
    flex-shrink: 0;
}
```

---

## Phase 4 — JavaScript: State Machine & API Calls

> Create `Scripts/talkwithtally.js`. This is the core logic file. Load it at the bottom of your layout, after jQuery and Bootstrap 5.

### Step 4.1 — State management and config

```javascript
// ═══════════════════════════════════════════════════════════════
// TalkWithTally — Frontend Logic
// Depends on: jQuery, Bootstrap 5
// ═══════════════════════════════════════════════════════════════

const TWT = (function ($) {
    'use strict';

    // ── Config ─────────────────────────────────────────────────
    const API_BASE        = '/api/tally';         // adjust to match your routing
    const STORAGE_KEY     = 'twt_setup_done';     // localStorage key
    const SYNC_POLL_MS    = 8000;                 // poll sync status every 8s after sync-now
    const MAX_POLL_TRIES  = 10;                   // give up polling after ~80s

    // ── Internal state ─────────────────────────────────────────
    let _isOpen         = false;
    let _setupDone      = false;
    let _syncPollTimer  = null;
    let _pollCount      = 0;
    let _isSending      = false;
    let _downloadUrl    = '#';

    // ── DOM refs (populated in init) ───────────────────────────
    let $widget, $trigger, $chatArea, $input, $chips,
        $syncText, $syncIcon, $syncBtn, $syncToast,
        $statusDot, $screenSplash, $screenNotConnected, $screenChat;
```

### Step 4.2 — Initialization

```javascript
    // ── Init ───────────────────────────────────────────────────
    function init() {
        // Cache DOM refs
        $widget             = $('#twt-widget');
        $trigger            = $('#twt-trigger-btn');
        $chatArea           = $('#twt-chat-area');
        $input              = $('#twt-input');
        $chips              = $('#twt-chips');
        $syncText           = $('#twt-sync-text');
        $syncIcon           = $('.twt-sync-icon');
        $syncBtn            = $('#twt-sync-now-btn');
        $syncToast          = $('#twt-sync-toast');
        $statusDot          = $('#twt-status-dot');
        $screenSplash       = $('#twt-screen-splash');
        $screenNotConnected = $('#twt-screen-notconnected');
        $screenChat         = $('#twt-screen-chat');

        // Check if first-time setup was already done for this browser session
        _setupDone = localStorage.getItem(STORAGE_KEY) === '1';

        // Bind events
        $trigger.on('click', toggleWidget);
        $('#twt-close-btn').on('click', closeWidget);
        $('#twt-splash-proceed').on('click', onSplashProceed);
        $('#twt-nc-retry').on('click', checkConnectionAndProceed);
        $syncBtn.on('click', onSyncNow);
        $('#twt-send-btn').on('click', onSend);

        $input.on('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                onSend();
            }
        });

        // Download button gets URL from sync-status response
        $('#twt-download-btn').on('click', function () {
            window.open(_downloadUrl, '_blank');
        });
    }
```

### Step 4.3 — Widget open/close and screen routing

```javascript
    // ── Widget toggle ──────────────────────────────────────────
    function toggleWidget() {
        if (_isOpen) { closeWidget(); } else { openWidget(); }
    }

    function openWidget() {
        _isOpen = true;
        $widget.removeClass('twt-hidden');

        if (!_setupDone) {
            // First ever open — show splash
            showScreen('splash');
        } else {
            // Already set up — go straight to connection check
            showScreen('chat');
            checkConnectionAndProceed();
        }
    }

    function closeWidget() {
        _isOpen = false;
        $widget.addClass('twt-hidden');
        clearInterval(_syncPollTimer);
    }

    function showScreen(name) {
        $screenSplash.addClass('twt-hidden');
        $screenNotConnected.addClass('twt-hidden');
        $screenChat.addClass('twt-hidden');

        if (name === 'splash')        $screenSplash.removeClass('twt-hidden');
        if (name === 'not-connected') $screenNotConnected.removeClass('twt-hidden');
        if (name === 'chat')          $screenChat.removeClass('twt-hidden');
    }

    // Splash "Get Started" clicked
    function onSplashProceed() {
        showScreen('not-connected');  // immediately show loading state...
        checkConnectionAndProceed();  // ...then check if connector is installed
    }
```

### Step 4.4 — Connection check (the critical routing logic)

```javascript
    // ── Connection check ───────────────────────────────────────
    // Called: after splash, on retry, on every widget open after setup
    function checkConnectionAndProceed() {
        setSyncText('Checking Tally connection...');
        setStatusDot('');  // neutral

        $.ajax({
            url:      API_BASE + '/sync-status',
            method:   'GET',
            success: function (res) {
                if (res.status === 'connected') {
                    // Connector installed + data exists → go to chat
                    _setupDone = true;
                    localStorage.setItem(STORAGE_KEY, '1');
                    showScreen('chat');
                    updateSyncBar(res);
                    loadSuggestions();
                    showWelcomeMessage(res);
                } else {
                    // Not connected or no data yet
                    _downloadUrl = res.downloadUrl || '#';
                    showScreen('not-connected');
                }
            },
            error: function () {
                setSyncText('Connection error');
                setStatusDot('error');
            }
        });
    }
```

### Step 4.5 — Sync bar update and Sync Now

```javascript
    // ── Sync bar ───────────────────────────────────────────────
    function updateSyncBar(res) {
        if (!res.lastSyncedAt) {
            setSyncText('Never synced');
            setStatusDot('not-synced');
            return;
        }

        let syncDate = new Date(res.lastSyncedAt);
        let now      = new Date();
        let diffMin  = Math.floor((now - syncDate) / 60000);
        let label    = '';

        if (diffMin < 1)       label = 'Just now';
        else if (diffMin < 60) label = diffMin + ' min ago';
        else {
            // e.g. "Today, 14:32" or "Jun 3, 14:32"
            let isToday = syncDate.toDateString() === now.toDateString();
            let timeStr = syncDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
            label = isToday
                ? 'Today, ' + timeStr
                : syncDate.toLocaleDateString([], { month: 'short', day: 'numeric' }) + ', ' + timeStr;
        }

        setSyncText(label);
        setStatusDot(res.syncStatus === 'Success' ? 'connected' : 'error');
    }

    function setSyncText(text) { $syncText.text(text); }

    function setStatusDot(state) {
        $statusDot.removeClass('connected not-synced error');
        if (state) $statusDot.addClass(state);
    }

    // ── Sync Now button ────────────────────────────────────────
    function onSyncNow() {
        $syncBtn.prop('disabled', true);
        $syncIcon.addClass('spinning');
        setSyncText('Syncing...');
        $syncToast.addClass('twt-hidden');

        $.ajax({
            url:    API_BASE + '/sync-now',
            method: 'POST',
            success: function () {
                // Poll sync-status until LastSyncedAt updates
                _pollCount = 0;
                _syncPollTimer = setInterval(pollForSyncComplete, SYNC_POLL_MS);
            },
            error: function () {
                $syncIcon.removeClass('spinning');
                $syncBtn.prop('disabled', false);
                setSyncText('Sync request failed');
            }
        });
    }

    function pollForSyncComplete() {
        _pollCount++;
        if (_pollCount > MAX_POLL_TRIES) {
            clearInterval(_syncPollTimer);
            $syncIcon.removeClass('spinning');
            $syncBtn.prop('disabled', false);
            setSyncText('Sync timed out — try again');
            return;
        }

        $.ajax({
            url: API_BASE + '/sync-status',
            method: 'GET',
            success: function (res) {
                if (res.status === 'connected' && res.syncStatus === 'Success') {
                    let syncDate = new Date(res.lastSyncedAt);
                    let now      = new Date();
                    // Consider "fresh" if synced within last 30 seconds
                    if ((now - syncDate) / 1000 < 30) {
                        clearInterval(_syncPollTimer);
                        $syncIcon.removeClass('spinning');
                        $syncBtn.prop('disabled', false);
                        updateSyncBar(res);
                        showSyncToast();
                    }
                }
            }
        });
    }

    function showSyncToast() {
        $syncToast.removeClass('twt-hidden');
        setTimeout(function () {
            $syncToast.addClass('twt-hidden');
        }, 3500);  // auto-hide after 3.5s
    }
```

### Step 4.6 — Suggestion chips loader

```javascript
    // ── Suggestions ────────────────────────────────────────────
    function loadSuggestions() {
        $.ajax({
            url:     API_BASE + '/suggestions',
            method:  'GET',
            success: function (suggestions) {
                $chips.empty();
                $.each(suggestions, function (_, s) {
                    let $chip = $('<button class="twt-chip">')
                        .text(s.text)
                        .on('click', function () {
                            $input.val(s.text);
                            onSend();
                            // Hide chips after first use
                            $chips.addClass('twt-hidden');
                        });
                    $chips.append($chip);
                });
            }
        });
    }
```

### Step 4.7 — Chat: send message and render response

```javascript
    // ── Chat ───────────────────────────────────────────────────
    function onSend() {
        let message = $input.val().trim();
        if (!message || _isSending) return;

        _isSending = true;
        $input.val('');
        $('#twt-send-btn').prop('disabled', true);
        $chips.addClass('twt-hidden');  // hide chips after first real message

        // Render user message
        appendMessage('user', message);

        // Show typing indicator
        let $typing = appendTypingIndicator();

        // Call chat API
        $.ajax({
            url:         API_BASE + '/chat',
            method:      'POST',
            contentType: 'application/json',
            data:        JSON.stringify({
                companyId: TWT_COMPANY_ID,  // injected from server-side (see Phase 5)
                message:   message
            }),
            success: function (res) {
                $typing.remove();
                renderAIResponse(res.response);
            },
            error: function (xhr) {
                $typing.remove();
                let errMsg = '🤖 Something went wrong. Please try again.';
                if (xhr.status === 429) errMsg = '⏳ Too many requests. Please wait a moment.';
                appendMessage('ai', errMsg);
            },
            complete: function () {
                _isSending = false;
                $('#twt-send-btn').prop('disabled', false);
                $input.focus();
                scrollToBottom();
            }
        });
    }

    // Renders AI response — detects if it contains tabular data
    function renderAIResponse(text) {
        // Check if the response looks like it contains table-friendly data
        // (i.e., multiple "Name: Value" lines or pipe-separated content)
        if (looksLikeTable(text)) {
            let $bubble = buildTableBubble(text);
            appendRawBubble($bubble);
        } else {
            // Plain text — render with newline support
            appendMessage('ai', text);
        }
        scrollToBottom();
    }

    // Heuristic: if 3+ lines contain a ":" separator, it's probably tabular
    function looksLikeTable(text) {
        let lines = text.split('\n').filter(l => l.trim().length > 0);
        let keyValueLines = lines.filter(l => l.includes(':') && l.split(':').length === 2);
        return keyValueLines.length >= 3;
    }

    // Builds a Bootstrap table from "Key: Value\nKey: Value" format
    function buildTableBubble(text) {
        let lines = text.split('\n').filter(l => l.trim() && l.includes(':'));
        let $table = $('<table class="twt-response-table">');
        let $tbody = $('<tbody>');

        $.each(lines, function (_, line) {
            let parts = line.split(':');
            let key   = parts[0].trim();
            let val   = parts.slice(1).join(':').trim();  // re-join in case value has ":"
            $tbody.append(
                $('<tr>').append(
                    $('<td>').text(key),
                    $('<td>').text(val)
                )
            );
        });

        $table.append($tbody);
        let $bubble = $('<div class="twt-bubble">').append($table);
        return $bubble;
    }
```

### Step 4.8 — DOM helpers

```javascript
    // ── DOM helpers ────────────────────────────────────────────
    function appendMessage(role, text) {
        let isUser   = role === 'user';
        let avatar   = isUser ? '👤' : '🏦';
        let timeStr  = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

        // Convert newlines to <br> for AI responses
        let htmlText = isUser
            ? $('<span>').text(text).html()  // escape user input
            : text.replace(/\n/g, '<br>');   // allow newlines in AI output

        let $row = $('<div class="twt-msg-row ' + (isUser ? 'user' : '') + '">');
        let $avatar = $('<div class="twt-msg-avatar">').text(avatar);
        let $col    = $('<div>').css({ display: 'flex', flexDirection: 'column', maxWidth: '80%' });
        let $bubble = $('<div class="twt-bubble">').html(htmlText);
        let $time   = $('<div class="twt-msg-time">').text(timeStr);

        $col.append($bubble, $time);
        $row.append($avatar, $col);
        $chatArea.append($row);
        scrollToBottom();
        return $row;
    }

    function appendRawBubble($bubble) {
        let timeStr = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        let $row    = $('<div class="twt-msg-row">');
        let $avatar = $('<div class="twt-msg-avatar">').text('🏦');
        let $col    = $('<div>').css({ display: 'flex', flexDirection: 'column', maxWidth: '88%' });
        let $time   = $('<div class="twt-msg-time">').text(timeStr);
        $col.append($bubble, $time);
        $row.append($avatar, $col);
        $chatArea.append($row);
        scrollToBottom();
    }

    function appendTypingIndicator() {
        let $row = $('<div class="twt-msg-row">');
        let $avatar  = $('<div class="twt-msg-avatar">').text('🏦');
        let $typing  = $('<div class="twt-typing">')
            .append('<span></span><span></span><span></span>');
        $row.append($avatar, $typing);
        $chatArea.append($row);
        scrollToBottom();
        return $row;
    }

    function showWelcomeMessage(res) {
        let ledgers  = res.totalLedgers  ? res.totalLedgers.toLocaleString()  : '—';
        let vouchers = res.totalVouchers ? res.totalVouchers.toLocaleString() : '—';
        let msg = `👋 Welcome to TalkWithTally!\n\n`
                + `📊 ${ledgers} ledgers and ${vouchers} vouchers are synced from your Tally data.\n\n`
                + `Ask me anything — ledger balances, sales summaries, debtor lists, or try a suggestion below.`;
        appendMessage('ai', msg);
    }

    function scrollToBottom() {
        let el = $chatArea[0];
        el.scrollTop = el.scrollHeight;
    }

    // ── Public API ─────────────────────────────────────────────
    return { init: init };

})(jQuery);

// Boot on DOM ready
$(document).ready(function () { TWT.init(); });
```

---

## Phase 5 — Server-Side: CompanyID Injection

> The JavaScript needs to know the current company's ID to pass to `/api/tally/chat`. Never expose this from localStorage or a hidden input — inject it from the server-side session.

### Step 5.1 — Inject into layout as a JS variable

In your main layout file (`.cshtml` or equivalent), add this inside `<head>` or just before the closing `</body>`, after your auth session is established:

```html
<!-- Injected server-side — never hardcode or expose raw session data -->
<script>
    // CompanyID from authenticated server session
    // This variable is consumed by talkwithtally.js
    var TWT_COMPANY_ID = @(Session["CompanyID"] ?? "0");
</script>
```

If you use a different session mechanism (JWT claims, cookie, etc.), adjust accordingly. The key rule: `TWT_COMPANY_ID` must come from the server, not from JavaScript or localStorage.

### Step 5.2 — Link CSS and JS in your layout

```html
<!-- In <head> — after Bootstrap 5 CSS -->
<link rel="stylesheet" href="~/Content/css/talkwithtally.css" />

<!-- Before closing </body> — after jQuery and Bootstrap 5 JS -->
<script src="~/Scripts/talkwithtally.js"></script>
```

---

## Phase 6 — Security & Edge Cases

### Step 6.1 — Escape all user input before rendering

In `appendMessage()`, user text is passed through `$('<span>').text(text).html()` which jQuery escapes automatically. Never use `.html(userText)` directly — this is an XSS vector.

AI responses use `.html()` only for the newline-to-`<br>` replacement on already-server-returned text. This is acceptable since you control the server output, but ensure your AI response formatting never reflects user input back unescaped.

### Step 6.2 — Rate-limit the Send button

The `_isSending` flag in the state machine already prevents double-sends. Additionally, disable the button immediately on click and only re-enable it in the `complete` callback (which runs whether the AJAX succeeds or fails).

### Step 6.3 — Handle Tally going offline mid-session

If a user's Tally is closed while the chat is open, the next sync-status check will show a stale `LastSyncedAt` (more than 30 minutes old). Handle this visually:

```javascript
// Add inside updateSyncBar(), after computing diffMin:
if (diffMin > 30) {
    setStatusDot('not-synced');  // amber dot
    setSyncText('⚠ ' + label + ' — Tally may be offline');
} else {
    setStatusDot('connected');
    setSyncText(label);
}
```

### Step 6.4 — Prevent widget from opening over mobile breakpoints

Even though mobile responsiveness is out of scope for this phase, at minimum prevent the widget from opening on very narrow screens to avoid a broken layout:

```javascript
function openWidget() {
    if (window.innerWidth < 480) {
        alert('TalkWithTally is optimised for desktop. Please use a wider screen.');
        return;
    }
    // ... rest of openWidget
}
```

---

## Verification Checklist

### Phase 1 — API endpoints

```
GET  /api/tally/sync-status  (no agent installed) → { status: "not_connected", downloadUrl: "..." }
GET  /api/tally/sync-status  (agent installed + synced) → { status: "connected", totalLedgers: N }
POST /api/tally/sync-now     → { queued: true }
GET  /api/tally/suggestions  → array of 8 Tally-specific suggestions
```

### Phase 2–4 — Widget behavior

| Scenario | Expected behavior |
|---|---|
| First click on "Tally AI" button | Splash screen shown |
| Click "Get Started" | Checks sync status → routes to correct screen |
| Connector not installed | Not-connected screen with download button |
| Connector installed + synced | Chat screen with welcome message |
| Second click on "Tally AI" button | Goes straight to chat, skips splash |
| Ask "Ledger Balance" | Typing indicator → AI response → timestamp shown |
| Response has 3+ Key: Value lines | Renders as Bootstrap table inside bubble |
| Click "Sync Now" | Button disables, icon spins, polls every 8s |
| Sync completes | Green toast appears for 3.5s, timestamp updates |
| Tally offline > 30 min | Amber dot, warning text in sync bar |
| Press Enter in input | Same as clicking Send |
| Send while waiting for response | Ignored (`_isSending` guard) |

---

## Implementation Order Summary

```
Phase 1 → Backend: sync-status + sync-now + suggestions endpoints + TallySyncRequest table
Phase 2 → HTML: trigger button + widget container with all 3 screens
Phase 3 → CSS: talkwithtally.css (all styles, variables, animations)
Phase 4 → JS: talkwithtally.js (state machine, API calls, DOM rendering)
Phase 5 → Layout: TWT_COMPANY_ID server injection + CSS/JS file links
Phase 6 → Security: XSS guard verify + rate-limit check + offline indicator
```

> **Note to coding agent:** Do not start Phase 2 HTML until Phase 1's three endpoints are verified in Postman returning correct responses. The JavaScript state machine branches entirely on the `sync-status` response — if that endpoint returns wrong data, every screen routing decision will be wrong and will be very hard to debug from the frontend alone.
