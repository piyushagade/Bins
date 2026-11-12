# Bins for Windows 11

A native Windows 11 grouped taskbar icon launcher (**v1.3.0**), built with C#, WPF (WinUI 3 Dark / Light / System Fluent styling), and Win32 Shell / DWM COM Interop.

## Features

1. **Grid Mosaic Icon, Frosted Backplate, or Custom Icon**:
   - **Grid Mosaic View**: Dynamically renders up to 4 sub-icons onto a `128×128` ARGB canvas (`Win32Interop.CreateGroupMosaicIconHandle`) with lossless 32-bit alpha, with an optional frosted rounded squircle **Icon backplate** per Bin.
   - **Custom Icon**: Choose from a built-in gallery of Windows 11 icons (`imageres.dll` / `shell32.dll`) or browse any `.ico`, `.png`, `.jpg`, `.bmp`, `.exe`, or `.dll`.

2. **Apps, Microsoft Store / UWP Apps, Folders, Shortcuts & Websites**:
   - Add classic Start Menu apps **and** packaged **Microsoft Store / UWP apps** (`shell:AppsFolder`) from a unified searchable picker
   - Add `.exe` applications, `.lnk` / `.url` shortcuts, scripts, or documents
   - Add **Folders** with **Quick Folder Peek** (hover chevron inside the flyout or right-click **Peek folder** to browse nested subfolders and files directly from the menu)
   - Add **Websites / URLs** (opens in your default browser and automatically fetches high-DPI site favicons)
   - **Per-Item Customization**: Rename items in Bins or set custom icons per item without modifying the underlying shortcut or file on disk

3. **Drag-and-Drop Power Workflows**:
   - **Drop Files Onto App or Folder Tiles**: Drag a file over a Bin's taskbar icon to pop open its flyout automatically, then drop onto an **App** tile to open the file in that app or onto a **Folder** tile to move (`Ctrl` = copy) the file into that folder (`SHFileOperationW` with Undo support)
   - **Drag-and-Drop Reordering & Cross-Bin Move**: Reorder tiles directly inside the flyout, reorder items and Bins in Bins Manager, or drag items onto another Bin in the sidebar to move them

4. **Keyboard-First Workflow & Hover-to-Open**:
   - **Per-Bin Global Hotkeys**: Assign custom system-wide shortcuts (e.g. `Alt+1`, `Ctrl+Shift+C`) in **Flyout options** to toggle any Bin instantly
   - **Flyout Keyboard Navigation**: Press `1`–`9` to launch/switch to tiles immediately, arrow keys + `Enter` to navigate, or start typing to filter items inline
   - **Hover-to-Open Mode**: Optional classic Bins behavior in **Settings** pops open a Bin's flyout when hovering over its taskbar icon and auto-dismisses when leaving

5. **Windows 11 Taskbar Integration, Themes, Backup & Cloud Sync**:
   - **Taskbar Pinning & Native Jump Lists**: Pin any Bin on the Windows 11 Taskbar (`System.AppUserModel.ID` + `ICustomDestinationList`) for right-click Jump List access to items, `Open all`, `Edit`, and `Bins settings`
   - **Hide Duplicate Taskbar Icons**: Optional per-Bin and global toggles hide standalone taskbar buttons for open binned apps (`ITaskbarList`) while turning the Bin flyout into a smart window switcher
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
