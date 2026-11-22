# Privacy & Analytics in Bins

Bins is designed to be **local-first and privacy-respecting**. Its built-in analytics are intentionally minimal, completely anonymous, and easy to audit or disable.

> **TL;DR:** Bins does **not** use Google Analytics, Sentry, PostHog, Mixpanel, or any third-party telemetry SDK. Every analytics event is a **bodyless HTTP `POST` (`Content-Length: 0`)** that increments a plain integer counter by `+1`. Zero personal data, zero device identifiers, and zero user content are ever collected or transmitted.

---

## What Is *Never* Collected

Bins **never** reads, stores, or transmits:
- Usernames, email addresses, account info, or Windows profile names
- Machine IDs, hardware hashes, MAC addresses, or persistent tracking tokens
- Bin names, app names, shortcuts, file paths, URLs, snippets, or folder contents
- Window titles, unread badge counts, or running application lists
- Crash dumps, stack traces, exception messages, or system specifications
- Any JSON payload or request body — every request has `Content-Length: 0`

---

## How It Works (and Why It's Innocuous)

1. **Simple `+1` Counters Only:** The app sends a `POST` request to `https://app.piyushagade.xyz/counter/bins/<key>/up` with an empty body. The server only sees that a named counter (for example, `dau-2026-11-22` or `ver-1-5-8`) went from `N` to `N + 1`.
2. **Local Deduplication:** To avoid double-counting without assigning you a user or device ID, Bins tracks which counters it has already incremented in a tiny local file at `%LOCALAPPDATA%\WinBins\analytics.json`. This file never leaves your machine and is excluded from Cloud Sync and `.bins` backups.
3. **Strict Rate Limits:** Each counter is capped locally — once ever for installs, once per version, once per UTC day for daily active usage / features / errors, and once per ISO week for weekly active usage.
4. **Silent & Non-Blocking:** Requests run asynchronously in the background with a 3.5-second timeout. If you are offline or the endpoint is unreachable, all errors are silently ignored and a circuit breaker backs off exponentially (`15m → 30m → 60m`) so it never wastes CPU, battery, or bandwidth.
5. **Fully Auditable:** The entire implementation lives in one self-contained file: [`Services/AnalyticsService.cs`](Services/AnalyticsService.cs).

---

## How to Opt Out

You can disable analytics completely at any time:
- **In the app:** Open **Settings → Privacy** and turn off **Send diagnostics & usage**.
- **In config:** Set `"SendDiagnosticsAndUsage": false` in `%LOCALAPPDATA%\WinBins\config.json`.

When disabled, Bins makes **zero** analytics network requests.

---

## Complete List of Counter Keys

Every single counter key the app or website can ever increment is listed below.

### Core Counters

| Metric | Counter Key | Read URL (`GET`) | Frequency Cap |
| --- | --- | --- | --- |
| Website Downloads | `downloads` | `https://app.piyushagade.xyz/counter/bins/downloads` | Once per browser session |
| App Launches | `app-launches` | `https://app.piyushagade.xyz/counter/bins/app-launches` | Once per app launch |
| App Installs (First Run) | `installs` | `https://app.piyushagade.xyz/counter/bins/installs` | Once ever per machine |
| Daily Active Users (DAU) | `dau-YYYY-MM-DD` (UTC) | `https://app.piyushagade.xyz/counter/bins/dau-2026-11-22` | Max once per UTC day |
| Weekly Active Users (WAU) | `wau-YYYY-Www` (ISO-8601 UTC) | `https://app.piyushagade.xyz/counter/bins/wau-2026-W47` | Max once per ISO week |
| Version Adoption | `ver-X-Y-Z` | `https://app.piyushagade.xyz/counter/bins/ver-1-5-8` | Once per version |
| Daily Error Count | `errors-YYYY-MM-DD` (UTC) | `https://app.piyushagade.xyz/counter/bins/errors-2026-11-22` | Max once per UTC day |

### Feature Usage Counters (Max 1 ping per UTC day per machine)

| Feature | Counter Key | Read URL (`GET`) |
| --- | --- | --- |
| Bin Flyout Opened | `feat-flyout` | `https://app.piyushagade.xyz/counter/bins/feat-flyout` |
| Omni-Search Opened | `feat-omnisearch` | `https://app.piyushagade.xyz/counter/bins/feat-omnisearch` |
| Launch All / Workspace | `feat-launch-all` | `https://app.piyushagade.xyz/counter/bins/feat-launch-all` |
| Profile Switched | `feat-profiles` | `https://app.piyushagade.xyz/counter/bins/feat-profiles` |
| Live Folder Configured | `feat-livefolder` | `https://app.piyushagade.xyz/counter/bins/feat-livefolder` |

---

## Inspecting Public Totals (PowerShell)

All aggregate counts are readable via `GET https://app.piyushagade.xyz/counter/bins/<key>`:

```powershell
$today = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
$keys = @(
    "downloads",
    "app-launches",
    "installs",
    "ver-1-5-8",
    "dau-$today",
    "errors-$today",
    "feat-flyout",
    "feat-omnisearch",
    "feat-launch-all",
    "feat-profiles",
    "feat-livefolder"
)

foreach ($k in $keys) {
    try {
        $res = Invoke-RestMethod "https://app.piyushagade.xyz/counter/bins/$k" -TimeoutSec 4
        "{0,-22} : {1}" -f $k, $res.count
    } catch {
        "{0,-22} : (no data / unavailable)" -f $k
    }
}
```
