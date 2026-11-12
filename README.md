# Bins for Windows 11

A native Windows 11 grouped taskbar icon launcher (**v1.2.1**), built with C#, WPF (WinUI 3 Dark Fluent styling), and Win32 Shell / DWM COM Interop.

## Features

1. **Grid Mosaic Icon or Custom Icon**:
   - **Grid Mosaic View**: Dynamically renders up to 4 sub-icons onto a transparent `128×128` ARGB canvas (`Win32Interop.CreateGroupMosaicIconHandle`) with lossless 32-bit alpha:
     - **2 icons**: `62×62` each at `(0, 0)` and `(66, 66)`
     - **3 icons**: `58×58` each at `(0, 0)`, `(70, 0)`, and `(35, 70)` (`12px` separation)
     - **4+ icons (`2×2` grid)**: `58×58` each at `(0, 0)`, `(70, 0)`, `(0, 70)`, and `(70, 70)` (`12px` padding)
   - **Custom Icon**: Choose from a built-in gallery of Windows 11 icons (`imageres.dll` / `shell32.dll`) or browse any `.ico`, `.png`, `.jpg`, `.bmp`, `.exe`, or `.dll`.

2. **Apps, Folders, Shortcuts & Websites**:
   - Add installed apps from a searchable **Start Menu Apps** picker
   - Add `.exe` applications, `.lnk` / `.url` shortcuts, scripts, or documents
   - Add **Folders** (opens directly in File Explorer)
   - Add **Websites / URLs** (opens in your default browser and automatically fetches high-DPI site favicons)
   - **Per-Item Customization**: Rename items in Bins or set custom icons per item without modifying the underlying shortcut or file on disk
   - **Drag & Drop** apps, folders, shortcuts, or URLs onto either the **Bins Manager** or the **Bin Flyout**

3. **Windows 11 Taskbar Integration & Smart Window Switching**:
   - **Taskbar Pinning**: Pin any Bin directly on the Windows 11 Taskbar (`System.AppUserModel.ID` + `ImplicitAppShortcuts` / `Taskband` detection) to lock its position across restarts and suppress taskbar hover thumbnail previews
   - **Hide Duplicate Taskbar Icons**: Optional per-Bin (**Flyout options → Hide open app icons**) and global (**Settings → Hide duplicate taskbar icons**) toggles hide standalone taskbar buttons for open binned apps (`ITaskbarList`) while turning the Bin flyout into a smart window switcher (`Ctrl+Click` or middle-click opens a new window)
   - **Floating Flyout Options Sidebar**: Customize Bin icon mode, header icon, tile size (`Compact` / `Normal` / `Spacious`), columns, max rows, padding, and flyout header buttons
   - **System Tray & Startup**: Runs silently in the System Tray with an optional **Launch at Windows startup** (`--background`) toggle

## Build & Run

```powershell
# Build Release x64 executable (bin\Release\Bins.exe)
.\build.ps1

# Build and launch immediately
.\build.ps1 -Run
```

Configuration and generated multi-resolution `.ico` files are stored in `%LOCALAPPDATA%\WinBins`.
