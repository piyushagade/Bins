# Bins for Windows 11

A native Windows 11 grouped taskbar icon launcher (**v1.5.6**), built with C#, WPF (WinUI 3 Dark / Light / System Fluent styling), and Win32 Shell / DWM COM Interop.

## Features

1. **Multi-Style Mosaic Icons, Emoji/Monogram Badges, Frosted Backplates & Custom Icons**:
   - **Icon Styles**: Choose between **2×2 Grid Mosaic**, **3×3 Mini-Grid**, **Fanned Stack** (layered tilted cards), **Emoji / Monogram Badge** (tinted gradient squircle with custom emoji or 1–3 letter monogram), or **Custom Icon** (built-in Windows 11 gallery or any `.ico`, `.png`, `.jpg`, `.bmp`, `.exe`, `.dll`).
   - **Per-Bin Color Tints & Backplates**: Optional frosted rounded squircle **Icon backplate** and Windows 11 accent color tints (`Blue`, `Cyan`, `Emerald`, `Amber`, `Rose`, `Purple`, `Pink`) across taskbar icons, flyout titles, category pills, and running indicators.

2. **Apps, Store/UWP Apps, Folders, Websites, Utility Tiles, Snippets, Nested Sub-Bins & Smart Live Folders**:
   - Add classic Start Menu apps **and** packaged **Microsoft Store / UWP apps** (`shell:AppsFolder`) from a unified searchable picker
   - Add `.exe` applications, `.lnk` / `.url` shortcuts, scripts, documents, or **Folders** with **Quick Folder Peek**
   - Add **Smart Live Folders & Windows Recent Files**: Mirror any live directory (`Downloads`, `Desktop`, `Documents`, etc.) **or** your **Windows Recent Items**, with optional extension filtering (e.g., `*.pdf;*.docx`), configurable sorting (`Newest`, `Name`, `Type`), and item cap
   - Add **Nested Sub-Bins**: Place any Bin inside another Bin as a tile and navigate seamlessly inline with Back (`←` / `Backspace`) breadcrumbs
   - Add **Quick Text Snippet Tiles**: 1-click tiles that copy frequently used commands, replies, or templates straight to the clipboard
   - Add **Built-in System, Media & Utility Tiles**: Live **Recycle Bin**, **Screen Snip**, **Task Manager**, **Windows Settings**, **Toggle Mute**, **Play / Pause**, **Next / Previous Track**, **Audio Output Switcher**, **Toggle Windows Dark / Light Mode**, **Clear Clipboard**, **Lock / Sleep / Restart / Shut Down**, and custom **PowerShell** one-liners

3. **Profiles, Explorer Context Menu, Health Checker & Categories**:
   - **Bin Profiles**: Save and switch taskbar Bin layouts (*Work*, *Dev*, *Gaming*, *All Bins*) in 1 click from Settings, the System Tray menu, or CLI (`Bins.exe --profile <name>`)
   - **File Explorer "Add to Bin" Context Menu**: Optional per-user right-click menu in File Explorer (`HKCU`, no admin required) to send any file, app, or folder directly into any Bin
   - **Broken Link & Duplicate Checker**: Automatically flags missing targets (`Missing`) and duplicates (`Duplicate`) in Bins Manager with 1-click **Relocate** and cleanup actions
   - **Categories (`Tabs` or `Headers`) & Per-Item Properties**: Organize items into named category tabs/headers with custom arguments, working directory, window state, UAC elevation, and staggered **Open workspace** delays

4. **Running-App Window Switcher, Unread Notification Badges & Drag-and-Drop Power Workflows**:
   - **Live Notification / Unread Badges**: Automatically parses unread counts from running app titles (e.g., `(3) Slack`, `[12] Outlook`) and displays red notification badges on flyout tiles and taskbar indicator dots
   - **Running-App Taskbar Dots, Hover Peek & Switcher**: Native click-through Windows 11 multi-dot / active-pill indicators under taskbar Bins, compact Acrylic **"Open in Bin"** hover preview strip (`×` close + 1-click focus/minimize), accented running tile cards with multi-window count badges (`2`, `3`, ...), and optional **Float open apps first** per Bin
   - **Drop Files Onto App, Folder, or Recycle Bin Tiles** & full drag-and-drop reordering / cross-Bin move

5. **Global Cross-Bin Omni-Search, Hotkeys & Hover-to-Open**:
   - **Global Cross-Bin Search (`Ctrl+Alt+Space`)**: Spotlight-style WinUI 3 launcher searches across every Bin and Smart Live Folder from anywhere
   - **Per-Bin Global Hotkeys**, **Flyout Keyboard Navigation** (`1`–`9`, `Tab`, arrows, inline filter), and optional **Hover-to-Open Mode**

6. **Windows 11 Taskbar Integration, Flyout Appearance Controls, Auto-Backups & Packaging**:
   - **Flyout Appearance Controls**: Choose **Acrylic**, **Mica**, or **Solid** backdrop materials, fine-tune **Flyout surface opacity** (`40%–100%`), and scale **Tile icon size** (`24` / `32` / `40` / `48 px`) alongside `Dark` / `Light` / `System` themes
   - **Taskbar Pinning, Native Jump Lists & Keep Open Apps Inside Their Bin**: Pin any Bin on the Windows 11 Taskbar (`System.AppUserModel.ID` + `ICustomDestinationList`) and optionally keep open binned apps inside their Bin while preserving `Alt+Tab` / `Win+Tab` / Snap Assist
   - **Rolling Auto-Backups, Portable `.bins` Archive, Cloud Sync & Installer**: Automatically retains the last 5 timestamped config snapshots in `%LOCALAPPDATA%\WinBins\backups` with 1-click restore, plus `.bins` export/import, OneDrive/Dropbox sync, GitHub update checking, Inno Setup script (`installer/Bins.iss`), and WinGet manifest (`installer/winget/PiyushAgade.Bins.yaml`)

## Build & Run

```powershell
# Build Release x64 executable (bin\Release\Bins.exe)
.\build.ps1

# Build and launch immediately
.\build.ps1 -Run
```

Configuration and generated multi-resolution `.ico` files are stored in `%LOCALAPPDATA%\WinBins`.
