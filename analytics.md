# Bins Analytics Counter URLs

All analytics use the lightweight counter API at `https://app.piyushagade.xyz/counter/bins/<key>`.
- **Read a count:** `GET https://app.piyushagade.xyz/counter/bins/<key>`
- **Increment a count (used by the app/site):** `POST https://app.piyushagade.xyz/counter/bins/<key>/up`

---

## Core Counters

| Metric | Counter Key | Read URL (`GET`) | Deduplication |
| --- | --- | --- | --- |
| Website Downloads | `downloads` | https://app.piyushagade.xyz/counter/bins/downloads | Once per browser session |
| App Launches (Bins Startups) | `app-launches` | https://app.piyushagade.xyz/counter/bins/app-launches | Once per app launch |
| App Installs (First Run) | `installs` | https://app.piyushagade.xyz/counter/bins/installs | Once ever per machine |
| Daily Active Users (DAU) | `dau-YYYY-MM-DD` (UTC) | https://app.piyushagade.xyz/counter/bins/dau-2026-11-22 | Once per UTC day per machine |
| Weekly Active Users (WAU) | `wau-YYYY-Www` (ISO-8601 UTC) | https://app.piyushagade.xyz/counter/bins/wau-2026-W47 | Once per ISO week per machine |
| Version Adoption | `ver-X-Y-Z` | https://app.piyushagade.xyz/counter/bins/ver-1-5-6 | Once per version per machine |
| Daily Error Count | `errors-YYYY-MM-DD` (UTC) | https://app.piyushagade.xyz/counter/bins/errors-2026-11-22 | Max once per UTC day per machine |

---

## Feature Usage Counters (Max 1 ping per UTC day per machine)

| Feature | Counter Key | Read URL (`GET`) |
| --- | --- | --- |
| Bin Flyout Opened | `feat-flyout` | https://app.piyushagade.xyz/counter/bins/feat-flyout |
| Omni-Search Opened | `feat-omnisearch` | https://app.piyushagade.xyz/counter/bins/feat-omnisearch |
| Launch All / Workspace | `feat-launch-all` | https://app.piyushagade.xyz/counter/bins/feat-launch-all |
| Profile Switched | `feat-profiles` | https://app.piyushagade.xyz/counter/bins/feat-profiles |
| Live Folder Configured | `feat-livefolder` | https://app.piyushagade.xyz/counter/bins/feat-livefolder |

---

## Quick Check (PowerShell)

```powershell
$today = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
$keys = @(
    "downloads",
    "app-launches",
    "installs",
    "ver-1-5-6",
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

## Notes
- **Privacy:** Zero PII is collected — no usernames, machine IDs, file paths, bin names, or app names.
- **Opt-out:** Controlled by **Settings → Privacy → Send diagnostics & usage** (enabled by default, stored in `config.json` as `SendDiagnosticsAndUsage`).
- **Local dedupe state:** Stored per machine in `%LocalAppData%\WinBins\analytics.json` (excluded from Cloud Sync and `.bins` backups).
