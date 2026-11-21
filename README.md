# Bins for Windows 11

A native Windows 11 grouped taskbar icon launcher (**v1.4.1**), built with C#, WPF (WinUI 3 Dark / Light / System Fluent styling), and Win32 Shell / DWM COM Interop.

## Features

1. **Grid Mosaic Icon, Frosted & Colored Backplates, or Custom Icon**:
   - **Grid Mosaic View**: Dynamically renders up to 4 sub-icons onto a `128×128` ARGB canvas (`Win32Interop.CreateGroupMosaicIconHandle`) with lossless 32-bit alpha, with an optional frosted rounded squircle **Icon backplate** per Bin.
   - **Per-Bin Color Tints**: Tint any Bin's taskbar squircle backplate, flyout title, active category pills, running-app indicators, and search badges with preset Windows 11 accent colors (`Blue`, `Cyan`, `Emerald`, `Amber`, `Rose`, `Purple`, `Pink`).
   - **Custom Icon**: Choose from a built-in gallery of Windows 11 icons (`imageres.dll` / `shell32.dll`) or browse any `.ico`, `.png`, `.jpg`, `.bmp`, `.exe`, or `.dll`.

2. **Apps, Microsoft Store / UWP Apps, Folders, Websites, System Tiles & Smart Live Folders**:
   - Add classic Start Menu apps **and** packaged **Microsoft Store / UWP apps** (`shell:AppsFolder`) from a unified searchable picker
   - Add `.exe` applications, `.lnk` / `.url` shortcuts, scripts, or documents
   - Add **Folders** with **Quick Folder Peek** (hover chevron inside the flyout or right-click **Peek folder** to browse nested subfolders and files directly from the menu)
   - Add **Smart Live Folders**: Connect any Bin to a live directory (e.g., `Downloads`, `Desktop`, `Documents`, or custom folder) with configurable sorting (`Newest`, `Name`, `Type`) and item cap, drag files **out** of the flyout or **into** a Smart Bin, or pin live items permanently
   - Add **Websites / URLs** (opens in your default browser and automatically fetches high-DPI site favicons)
   - Add **Built-in System & Power Tiles**: Live **Recycle Bin** (dynamically switches empty/full icon, shows item count & byte size, accepts dropped files to recycle, and right-click **Empty Recycle Bin**), **Screen Snip**, **Task Manager**, **Windows Settings**, **Lock PC**, **Sleep**, **Restart**, **Shut Down**, and custom **PowerShell** one-liners

3. **Named Categories / Pill Tabs & Per-Item Launch Properties**:
   - **Categories (`Tabs` or `Headers`)**: Organize items inside a Bin into named categories displayed either as horizontal WinUI 3 pill tabs (`Tab` / `Shift+Tab` to cycle) or stacked section headers
   - **Per-Item Properties Dialog**: Configure custom command-line arguments, working directory, window state (`Normal` / `Maximized` / `Minimized`), **Always run as administrator** (with UAC shield badge), and **Smart Workspace ("Open all")** inclusion & staggered launch delay

4. **Running-App Window Switcher & Drag-and-Drop Power Workflows**:
   - **Running-App Indicators & Switcher**: Windows 11 pill indicators under running app tiles; click to toggle minimize/restore or cycle open windows, `Shift+Click` / `Middle-Click` for a new instance, or right-click to switch/close individual windows
   - **Drop Files Onto App, Folder, or Recycle Bin Tiles**: Drag a file over a Bin's taskbar icon to pop open its flyout automatically, then drop onto an **App** tile to open with that app, a **Folder** tile to move/copy (`SHFileOperationW`), or the **Recycle Bin** tile to delete with Undo support
   - **Drag-and-Drop Reordering & Cross-Bin Move**: Reorder tiles directly inside the flyout, reorder items and Bins in Bins Manager, or drag items onto another Bin in the sidebar to move them

5. **Global Cross-Bin Omni-Search, Hotkeys & Hover-to-Open**:
   - **Global Cross-Bin Search (`Ctrl+Alt+Space`)**: Spotlight-style WinUI 3 launcher searches across every Bin and Smart Live Folder from anywhere (with customizable global shortcut, `Ctrl+K` inside Bins Manager, `--search` Jump List task, and system tray item)
   - **Per-Bin Global Hotkeys**: Assign custom system-wide shortcuts (e.g. `Alt+1`, `Ctrl+Shift+C`) in **Flyout options** to toggle any Bin instantly
   - **Flyout Keyboard Navigation**: Press `1`–`9` to launch/switch to tiles immediately, `Tab` to switch category tabs, arrow keys + `Enter` to navigate, or start typing to filter items inline
   - **Hover-to-Open Mode**: Optional classic Bins behavior in **Settings** pops open a Bin's flyout when hovering over its taskbar icon and auto-dismisses when leaving

6. **Windows 11 Taskbar Integration, Smart Workspaces, Themes, Backup & Cloud Sync**:
   - **Taskbar Pinning & Native Jump Lists**: Pin any Bin on the Windows 11 Taskbar (`System.AppUserModel.ID` + `ICustomDestinationList`) for right-click Jump List access to items, staggered **Open workspace**, `Edit`, and `Bins settings`
   - **Hide Duplicate Taskbar Icons**: Optional per-Bin and global toggles hide standalone taskbar buttons for open binned apps (`ITaskbarList`)
   - **Acrylic Blur & Light / Dark / System Theme**: Native Windows 11 DWM Acrylic backdrop blur (`DWMSBT_TRANSIENTWINDOW`) and `Dark` / `Light` / `System` color themes
   - **Portable `.bins` Backup, Cloud Sync & Update Checker**: One-click `.bins` archive export/import, custom OneDrive / Dropbox sync folder support, and GitHub release update checking

## Build & Run

```powershell
# Build Release x64 executable (bin\Release\Bins.exe)
.\build.ps1

# Build and launch immediately
.\build.ps1 -Run
```

Configuration and generated multi-resolution `.ico` files are stored in `%LOCALAPPDATA%\WinBins`.
