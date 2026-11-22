# Bins for Windows 11

**Native grouped taskbar icon launcher for Windows 11.**

Organize apps, Microsoft Store apps, folders, websites, Smart Live Folders, quick text snippets, and system utilities into crisp Fluent mosaic **Bins** pinned directly to your Windows 11 taskbar.

Built in pure **C#**, **WPF** (WinUI 3 Dark / Light / System Fluent styling), and **Win32 / DWM / Shell COM Interop** — lightweight, native, and with **zero third-party NuGet dependencies**.

[Website](https://app.piyushagade.xyz/bins/) · [Releases](https://github.com/piyushagade/Bins/releases) · [Report an Issue](https://github.com/piyushagade/Bins/issues) · [Privacy & Analytics](analytics.md)

---

## Download & Install

- **Installer (Recommended):** Download the latest `Bins-Setup-x64.exe` from [app.piyushagade.xyz/bins](https://app.piyushagade.xyz/bins/) or [GitHub Releases](https://github.com/piyushagade/Bins/releases).
- **Requirements:** Windows 11 (x64). Runs on the built-in **.NET Framework 4.8** — no extra runtime installation required.

---

## Features

### Mosaic Icons, Badges & Per-Bin Styling
- **5 Icon Styles:** **2×2 Grid Mosaic**, **3×3 Mini-Grid**, **Fanned Stack** (layered tilted cards), **Emoji / Monogram Badge** (tinted gradient squircle with custom emoji or 1–3 letter monogram), or **Custom Icon** (built-in Windows 11 icon gallery or any `.ico`, `.png`, `.jpg`, `.bmp`, `.exe`, `.dll`).
- **Color Tints & Backplates:** Optional frosted rounded squircle icon backplate and Windows 11 accent color tints (`Blue`, `Cyan`, `Emerald`, `Amber`, `Rose`, `Purple`, `Pink`) across taskbar icons, flyout titles, category pills, and running indicators.
- **Fluent Appearance:** Choose **Acrylic**, **Mica**, or **Solid** flyout backdrops, adjust **Flyout surface opacity** (`40%–100%`), scale **Tile icon size** (`24` / `32` / `40` / `48 px`), and follow **Dark / Light / System** themes.

### Apps, Live Folders, Snippets, Sub-Bins & System Utilities
- **Unified App Picker:** Add classic Start Menu apps and packaged **Microsoft Store / UWP apps** (`shell:AppsFolder`), `.exe` apps, `.lnk` / `.url` shortcuts, scripts, documents, or **Folders** with **Quick Folder Peek**.
- **Smart Live Folders & Recent Files:** Mirror any live directory (`Downloads`, `Desktop`, `Documents`, etc.) or **Windows Recent Items** with extension filtering (e.g., `*.pdf;*.docx`), sorting (`Newest`, `Name`, `Type`), and item limits.
- **Nested Sub-Bins:** Place any Bin inside another Bin as a tile and navigate inline with Back (`←` / `Backspace`) breadcrumbs.
- **Quick Text Snippets:** One-click tiles that copy frequently used commands, replies, or templates straight to the clipboard.
- **Built-in System & Media Tiles:** Live **Recycle Bin**, **Screen Snip**, **Task Manager**, **Windows Settings**, **Toggle Mute**, **Play / Pause**, **Next / Previous Track**, **Audio Output Switcher**, **Toggle Dark / Light Mode**, **Clear Clipboard**, **Lock / Sleep / Restart / Shut Down**, and custom **PowerShell** one-liners.

### Running-App Switcher, Unread Badges & Drag-and-Drop
- **Taskbar Running Indicators & Hover Peek:** Native click-through Windows 11 multi-dot / active-pill indicators under pinned taskbar Bins, compact Acrylic **"Open in Bin"** hover preview strip (`×` close + 1-click focus/minimize), multi-window count badges, and optional **Float open apps first** per Bin.
- **Live Notification / Unread Badges:** Automatically detects unread counts in running window titles (e.g., `(3) Slack`, `[12] Outlook`) and displays red notification badges on flyout tiles and taskbar indicators.
- **Keep Open Apps Inside Their Bin:** Pin any Bin on the Windows 11 taskbar (`System.AppUserModel.ID` + `ICustomDestinationList`) and optionally keep open binned apps grouped inside their Bin while preserving `Alt+Tab`, `Win+Tab`, and Snap Assist.
- **Drag-and-Drop Workflows:** Drag and drop files directly onto App, Folder, or Recycle Bin tiles, reorder tiles freely, or drag items between Bins.

### Omni-Search, Hotkeys, Profiles & Power Tools
- **Global Cross-Bin Omni-Search (`Ctrl+Alt+Space`):** Spotlight-style launcher that searches across all Bins and Smart Live Folders from anywhere.
- **Keyboard Navigation & Hover Mode:** Per-Bin global hotkeys, flyout keyboard shortcuts (`1`–`9`, `Tab`, arrow keys, inline filter), and optional **Hover-to-Open**.
- **Bin Profiles:** Save and switch taskbar Bin layouts (*Work*, *Dev*, *Gaming*, *All Bins*) in one click from Settings, the System Tray, or CLI (`Bins.exe --profile <name>`).
- **Categories & Per-Item Properties:** Organize items into named `Tabs` or `Headers` with custom launch arguments, working directory, window state, UAC elevation, and staggered **Open workspace** launch delays.
- **File Explorer "Add to Bin" Context Menu:** Optional per-user right-click menu (`HKCU`, no admin required) to send any file, app, or folder into a Bin.
- **Broken Link & Duplicate Checker:** Automatically flags missing targets and duplicates with 1-click **Relocate** and cleanup actions.
- **Rolling Auto-Backups & Cloud Sync:** Keeps the last 5 timestamped config snapshots in `%LOCALAPPDATA%\WinBins\backups` with 1-click restore, plus portable `.bins` archive export/import and OneDrive/Dropbox sync.

---

## Building from Source

### Prerequisites
- Windows 11 (x64)
- **MSBuild** (via Visual Studio 2022 or [Visual Studio Build Tools](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) with .NET desktop build tools)
- *(Optional)* **Inno Setup 6** (`winget install JRSoftware.InnoSetup`) to build the installer

### Build Commands

```powershell
# Build Release x64 executable (bin\Release\Bins.exe)
.\build.ps1

# Build and launch immediately
.\build.ps1 -Run

# Build Debug configuration
.\build.ps1 -Configuration Debug

# Build Release and package Inno Setup installer + SHA256SUMS.txt (dist\)
.\build.ps1 -Package
```

No NuGet restore step is required — [Bins.csproj](Bins.csproj) references only built-in .NET Framework 4.8 and Windows WPF/UIAutomation assemblies.

---

## Command-Line Flags

| Command | Description |
| --- | --- |
| `Bins.exe --profile <name>` | Switch active Bin profile |
| `Bins.exe --search` | Open Global Cross-Bin Omni-Search |
| `Bins.exe --bin <id>` | Open a specific Bin flyout |
| `Bins.exe --open-all <id>` | Launch all workspace items in a Bin |
| `Bins.exe --settings` | Open Bins Manager directly to Settings |
| `Bins.exe --background` | Start minimized in the system tray |
| `Bins.exe --quit` | Cleanly exit a running instance |

---

## Data Storage & Privacy

- **Local-First Storage:** All configuration, generated multi-resolution `.ico` files, and rolling auto-backups are stored locally in `%LOCALAPPDATA%\WinBins`.
- **Update Checks:** Bins checks GitHub Releases at most once every 24 hours to notify you when a newer version is available ([`Services/UpdateCheckService.cs`](Services/UpdateCheckService.cs)).
- **Privacy-First Analytics:** Bins includes an optional, zero-PII aggregate counter mechanism (bodyless `POST` requests that simply increment anonymous integer counters like daily active count and version adoption). It collects **no personal data, no device IDs, no file paths, no app names, and no third-party tracking SDKs**, and can be turned off in one click under **Settings → Privacy → Send diagnostics & usage**. See [analytics.md](analytics.md) for full details and every counter key.

---

## Contributing & Feedback

Bug reports, Windows 11 taskbar edge cases, feature ideas, and pull requests are welcome!
- Open an issue on [GitHub Issues](https://github.com/piyushagade/Bins/issues) with your Windows 11 build number (from `winver`) and reproduction steps.
- For code changes, keep dependencies zero (pure .NET Framework 4.8 / WPF / Win32) and verify `.\build.ps1` builds cleanly.

---

## License

Copyright © 2026 Piyush Agade.

Bins is licensed under the **[PolyForm Noncommercial License 1.0.0](LICENSE)** (`PolyForm-Noncommercial-1.0.0`). You are free to use, inspect, build, and modify Bins for noncommercial purposes; commercial use, resale, or redistribution for commercial purposes is not permitted without prior permission.
