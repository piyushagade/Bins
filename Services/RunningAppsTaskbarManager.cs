using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Services
{
    public sealed class RunningWindowEntry
    {
        public IntPtr Hwnd { get; set; }
        public string Title { get; set; }
        public bool IsForeground { get; set; }
        public bool IsMinimized { get; set; }
    }

    public sealed class RunningAppsTaskbarManager : IDisposable
    {
        private sealed class ResolvedTargetExe
        {
            public string SourceKey;
            public string FullExePath;
            public string ExeFileName;
            public bool IsExplorerExe;
        }

        private static readonly HashSet<string> GenericHostExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bins.exe",
            "cmd.exe",
            "powershell.exe",
            "pwsh.exe",
            "conhost.exe",
            "wt.exe",
            "rundll32.exe",
            "regsvr32.exe",
            "wscript.exe",
            "cscript.exe",
            "mshta.exe",
            "msiexec.exe",
            "javaw.exe",
            "java.exe",
            "python.exe",
            "pythonw.exe",
            "node.exe",
            "applicationframehost.exe",
            "update.exe"
        };

        private static readonly HashSet<string> ExcludedWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "Progman",
            "WorkerW",
            "TaskListThumbnailWnd",
            "XamlExplorerHostIslandWindow",
            "Windows.UI.Core.CoreWindow"
        };

        private readonly ConfigService _configService;
        private readonly Dispatcher _dispatcher;
        private readonly uint _currentPid;

        private readonly Dictionary<string, ResolvedTargetExe> _resolvedItemCache =
            new Dictionary<string, ResolvedTargetExe>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<IntPtr> _hiddenHwnds = new HashSet<IntPtr>();
        private readonly Win32Interop.WinEventDelegate _winEventProc;

        private IntPtr _showHook = IntPtr.Zero;
        private IntPtr _foregroundHook = IntPtr.Zero;
        private IntPtr _minimizeEndHook = IntPtr.Zero;

        private Win32Interop.ITaskbarList _taskbarList;
        private IntPtr _lastShellTrayHwnd = IntPtr.Zero;

        private DispatcherTimer _debounceTimer;
        private DispatcherTimer _pollTimer;
        private bool _disposed;

        public RunningAppsTaskbarManager(ConfigService configService, Dispatcher dispatcher)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _currentPid = (uint)Process.GetCurrentProcess().Id;

            _winEventProc = OnWinEvent;

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(75)
            };
            _debounceTimer.Tick += (_, __) =>
            {
                _debounceTimer.Stop();
                SyncRunningWindows();
            };

            _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _pollTimer.Tick += (_, __) => SyncRunningWindows();
            _pollTimer.Start();

            InstallWinEventHooks();
        }

        private void InstallWinEventHooks()
        {
            const uint flags = Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS;
            try
            {
                _showHook = Win32Interop.SetWinEventHook(
                    Win32Interop.EVENT_OBJECT_SHOW,
                    Win32Interop.EVENT_OBJECT_SHOW,
                    IntPtr.Zero,
                    _winEventProc,
                    0,
                    0,
                    flags);

                _foregroundHook = Win32Interop.SetWinEventHook(
                    Win32Interop.EVENT_SYSTEM_FOREGROUND,
                    Win32Interop.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _winEventProc,
                    0,
                    0,
                    flags);

                _minimizeEndHook = Win32Interop.SetWinEventHook(
                    Win32Interop.EVENT_SYSTEM_MINIMIZEEND,
                    Win32Interop.EVENT_SYSTEM_MINIMIZEEND,
                    IntPtr.Zero,
                    _winEventProc,
                    0,
                    0,
                    flags);
            }
            catch
            {
            }
        }

        private void OnWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (_disposed || hwnd == IntPtr.Zero)
                return;

            if (eventType == Win32Interop.EVENT_OBJECT_SHOW && idObject != Win32Interop.OBJID_WINDOW)
                return;

            if (!_debounceTimer.IsEnabled)
            {
                _debounceTimer.Start();
            }
        }

        public void SyncRunningWindows(bool forceReassert = false)
        {
            if (_disposed)
                return;

            try
            {
                var activeBins = new List<BinGroup>();
                var allBins = _configService.Current?.Bins;
                if (allBins != null)
                {
                    foreach (BinGroup bin in allBins)
                    {
                        if (bin == null || !bin.HideRunningAppsOnTaskbar)
                            continue;

                        bool onTaskbar = bin.IsEnabledOnTaskbar || ShortcutService.IsBinPinnedOnTaskbar(bin);
                        if (onTaskbar && bin.Items != null && bin.Items.Count > 0)
                        {
                            activeBins.Add(bin);
                        }
                    }
                }

                var targetFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var targetFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool hideExplorerWindows = false;

                foreach (BinGroup bin in activeBins)
                {
                    foreach (BinItem item in bin.Items)
                    {
                        ResolvedTargetExe resolved = ResolveItemExecutableTarget(item);
                        if (resolved == null)
                            continue;

                        if (resolved.IsExplorerExe)
                        {
                            hideExplorerWindows = true;
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(resolved.FullExePath))
                        {
                            targetFullPaths.Add(resolved.FullExePath);
                        }
                        if (!string.IsNullOrWhiteSpace(resolved.ExeFileName) &&
                            !GenericHostExeNames.Contains(resolved.ExeFileName))
                        {
                            targetFileNames.Add(resolved.ExeFileName);
                        }
                    }
                }

                bool hasAnyActiveTargets = targetFullPaths.Count > 0 || targetFileNames.Count > 0 || hideExplorerWindows;
                if (!hasAnyActiveTargets && _hiddenHwnds.Count == 0)
                    return;

                if (!EnsureTaskbarListReady(out bool explorerRestarted))
                    return;

                if (explorerRestarted)
                {
                    _hiddenHwnds.Clear();
                }

                var pidExeCache = new Dictionary<uint, string>();

                // 1. Prune closed/hidden windows and restore any previously hidden windows that are no longer targeted
                if (_hiddenHwnds.Count > 0)
                {
                    var snapshot = _hiddenHwnds.ToList();
                    foreach (IntPtr hwnd in snapshot)
                    {
                        if (!Win32Interop.IsWindow(hwnd) || !Win32Interop.IsWindowVisible(hwnd))
                        {
                            _hiddenHwnds.Remove(hwnd);
                            continue;
                        }

                        bool stillTargeted = false;
                        if (hasAnyActiveTargets &&
                            IsTaskbarEligibleAppWindow(hwnd, _currentPid, out uint pid, out string className) &&
                            TryGetCachedProcessExe(pid, pidExeCache, out string exePath))
                        {
                            stillTargeted = DoesProcessMatchTargets(
                                exePath,
                                className,
                                targetFullPaths,
                                targetFileNames,
                                hideExplorerWindows);
                        }

                        if (!stillTargeted)
                        {
                            if (!Win32Interop.IsWindowCloaked(hwnd))
                            {
                                try { _taskbarList.AddTab(hwnd); } catch { }
                            }
                            _hiddenHwnds.Remove(hwnd);
                        }
                    }
                }

                if (!hasAnyActiveTargets)
                    return;

                // 2. Discover running top-level windows matching active Bins and hide duplicate taskbar buttons
                Win32Interop.EnumWindows((hwnd, lParam) =>
                {
                    if (!IsTaskbarEligibleAppWindow(hwnd, _currentPid, out uint pid, out string className))
                        return true;

                    if (!TryGetCachedProcessExe(pid, pidExeCache, out string exePath))
                        return true;

                    if (DoesProcessMatchTargets(exePath, className, targetFullPaths, targetFileNames, hideExplorerWindows))
                    {
                        if (forceReassert || !_hiddenHwnds.Contains(hwnd))
                        {
                            try
                            {
                                if (_taskbarList.DeleteTab(hwnd) == 0)
                                {
                                    _hiddenHwnds.Add(hwnd);
                                }
                            }
                            catch
                            {
                            }
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Returns all open, taskbar-eligible windows belonging to the given BinItem's executable.
        /// Works whether HideRunningAppsOnTaskbar is enabled or disabled.
        /// </summary>
        public List<RunningWindowEntry> GetRunningWindowsForItem(BinItem item)
        {
            var results = new List<RunningWindowEntry>();
            if (_disposed || item == null)
                return results;

            ResolvedTargetExe resolved = ResolveItemExecutableTarget(item);
            if (resolved == null)
                return results;

            var singlePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var singleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!resolved.IsExplorerExe)
            {
                if (!string.IsNullOrWhiteSpace(resolved.FullExePath))
                    singlePaths.Add(resolved.FullExePath);
                if (!string.IsNullOrWhiteSpace(resolved.ExeFileName) && !GenericHostExeNames.Contains(resolved.ExeFileName))
                    singleNames.Add(resolved.ExeFileName);
            }

            if (!resolved.IsExplorerExe && singlePaths.Count == 0 && singleNames.Count == 0)
                return results;

            IntPtr fgHwnd = Win32Interop.GetForegroundWindow();
            var pidExeCache = new Dictionary<uint, string>();

            try
            {
                Win32Interop.EnumWindows((hwnd, lParam) =>
                {
                    if (!IsTaskbarEligibleAppWindow(hwnd, _currentPid, out uint pid, out string className))
                        return true;

                    if (!TryGetCachedProcessExe(pid, pidExeCache, out string exePath))
                        return true;

                    if (DoesProcessMatchTargets(exePath, className, singlePaths, singleNames, resolved.IsExplorerExe))
                    {
                        string title = Win32Interop.GetWindowTextSafe(hwnd);
                        results.Add(new RunningWindowEntry
                        {
                            Hwnd = hwnd,
                            Title = string.IsNullOrWhiteSpace(title) ? item.Name : title,
                            IsForeground = hwnd == fgHwnd,
                            IsMinimized = Win32Interop.IsIconic(hwnd)
                        });
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }

            return results;
        }

        private ResolvedTargetExe ResolveItemExecutableTarget(BinItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TargetPath))
                return null;

            if (item.Kind == BinItemKind.Folder ||
                item.Kind == BinItemKind.WebUrl ||
                BinItem.IsWebUrl(item.TargetPath))
            {
                return null;
            }

            string rawPath = item.TargetPath.Trim();
            string cacheKey = $"{item.Kind}|{rawPath}";
            if (_resolvedItemCache.TryGetValue(cacheKey, out ResolvedTargetExe cached))
            {
                return cached;
            }

            string expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim('"'));
            string ext = Path.GetExtension(expanded)?.ToLowerInvariant() ?? string.Empty;

            ResolvedTargetExe result = null;

            if (ext == ".exe")
            {
                string exeName = Path.GetFileName(expanded);
                if (!string.IsNullOrWhiteSpace(exeName) &&
                    !string.Equals(exeName, "Bins.exe", StringComparison.OrdinalIgnoreCase))
                {
                    result = new ResolvedTargetExe
                    {
                        SourceKey = cacheKey,
                        FullExePath = SafeNormalizeFullPath(expanded),
                        ExeFileName = exeName,
                        IsExplorerExe = string.Equals(exeName, "explorer.exe", StringComparison.OrdinalIgnoreCase)
                    };
                }
            }
            else if (ext == ".lnk" && ShortcutService.TryResolveShortcutTarget(expanded, out string lnkTarget, out string lnkArgs))
            {
                string targetExt = Path.GetExtension(lnkTarget)?.ToLowerInvariant() ?? string.Empty;
                if (targetExt == ".exe")
                {
                    string exeName = Path.GetFileName(lnkTarget);

                    // Handle Squirrel Update.exe --processStart <AppName.exe> shortcuts (Discord, Slack, etc.)
                    if (string.Equals(exeName, "Update.exe", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(lnkArgs) &&
                        TryExtractProcessStartExeName(lnkArgs, out string squirrelExe))
                    {
                        result = new ResolvedTargetExe
                        {
                            SourceKey = cacheKey,
                            FullExePath = null,
                            ExeFileName = squirrelExe,
                            IsExplorerExe = false
                        };
                    }
                    else if (!string.IsNullOrWhiteSpace(exeName) &&
                             !string.Equals(exeName, "Bins.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        result = new ResolvedTargetExe
                        {
                            SourceKey = cacheKey,
                            FullExePath = SafeNormalizeFullPath(lnkTarget),
                            ExeFileName = exeName,
                            IsExplorerExe = string.Equals(exeName, "explorer.exe", StringComparison.OrdinalIgnoreCase)
                        };
                    }
                }
            }

            _resolvedItemCache[cacheKey] = result;
            return result;
        }

        private static bool TryExtractProcessStartExeName(string args, out string exeName)
        {
            exeName = null;
            int idx = args.IndexOf("--processStart", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            string rest = args.Substring(idx + "--processStart".Length).TrimStart();
            if (rest.StartsWith("--process-start-args", StringComparison.OrdinalIgnoreCase) || rest.Length == 0)
                return false;

            string token;
            if (rest[0] == '"')
            {
                int endQuote = rest.IndexOf('"', 1);
                token = endQuote > 1 ? rest.Substring(1, endQuote - 1) : rest.Trim('"');
            }
            else
            {
                int space = rest.IndexOf(' ');
                token = space > 0 ? rest.Substring(0, space) : rest;
            }

            token = Path.GetFileName(token.Trim());
            if (!string.IsNullOrWhiteSpace(token) && token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exeName = token;
                return true;
            }

            return false;
        }

        private static string SafeNormalizeFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static bool TryGetCachedProcessExe(uint pid, Dictionary<uint, string> cache, out string exePath)
        {
            if (cache.TryGetValue(pid, out exePath))
            {
                return !string.IsNullOrWhiteSpace(exePath);
            }

            Win32Interop.TryGetProcessExecutablePath(pid, out exePath);
            cache[pid] = exePath;
            return !string.IsNullOrWhiteSpace(exePath);
        }

        private static bool DoesProcessMatchTargets(
            string processExePath,
            string windowClassName,
            HashSet<string> targetFullPaths,
            HashSet<string> targetFileNames,
            bool matchExplorerCabinet)
        {
            if (string.IsNullOrWhiteSpace(processExePath))
                return false;

            string procFileName = Path.GetFileName(processExePath);
            if (string.IsNullOrWhiteSpace(procFileName))
                return false;

            if (string.Equals(procFileName, "explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                return matchExplorerCabinet &&
                       string.Equals(windowClassName, "CabinetWClass", StringComparison.OrdinalIgnoreCase);
            }

            if (targetFullPaths.Contains(processExePath))
                return true;

            if (targetFileNames.Contains(procFileName))
                return true;

            return false;
        }

        private static bool IsTaskbarEligibleAppWindow(IntPtr hwnd, uint currentPid, out uint pid, out string className)
        {
            pid = 0;
            className = null;

            if (hwnd == IntPtr.Zero || !Win32Interop.IsWindow(hwnd) || !Win32Interop.IsWindowVisible(hwnd))
                return false;

            if (Win32Interop.IsWindowCloaked(hwnd))
                return false;

            long exStyle = Win32Interop.GetWindowLongPtrW(hwnd, Win32Interop.GWL_EXSTYLE).ToInt64();
            bool isAppWindow = (exStyle & Win32Interop.WS_EX_APPWINDOW) != 0;
            bool isToolWindow = (exStyle & Win32Interop.WS_EX_TOOLWINDOW) != 0;

            if (isToolWindow && !isAppWindow)
                return false;

            IntPtr owner = Win32Interop.GetWindow(hwnd, Win32Interop.GW_OWNER);
            if (owner != IntPtr.Zero && !isAppWindow)
                return false;

            if (!isAppWindow && Win32Interop.GetWindowTextLengthW(hwnd) <= 0)
                return false;

            Win32Interop.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0 || pid == currentPid)
                return false;

            className = Win32Interop.GetWindowClassNameSafe(hwnd);
            if (!string.IsNullOrEmpty(className) && ExcludedWindowClasses.Contains(className))
                return false;

            return true;
        }

        private bool EnsureTaskbarListReady(out bool explorerRestarted)
        {
            explorerRestarted = false;
            IntPtr shellTray = Win32Interop.FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (shellTray == IntPtr.Zero)
                return false;

            if (_lastShellTrayHwnd != IntPtr.Zero && _lastShellTrayHwnd != shellTray)
            {
                explorerRestarted = true;
            }
            _lastShellTrayHwnd = shellTray;

            if (_taskbarList != null)
                return true;

            try
            {
                var candidate = Win32Interop.CreateTaskbarList();
                if (candidate.HrInit() == 0)
                {
                    _taskbarList = candidate;
                    return true;
                }
                Marshal.ReleaseComObject(candidate);
            }
            catch
            {
            }

            return false;
        }

        public void RestoreAllHiddenWindows()
        {
            if (_hiddenHwnds.Count == 0)
                return;

            if (_taskbarList != null || EnsureTaskbarListReady(out _))
            {
                foreach (IntPtr hwnd in _hiddenHwnds.ToList())
                {
                    if (Win32Interop.IsWindow(hwnd) &&
                        Win32Interop.IsWindowVisible(hwnd) &&
                        !Win32Interop.IsWindowCloaked(hwnd))
                    {
                        try { _taskbarList.AddTab(hwnd); } catch { }
                    }
                }
            }

            _hiddenHwnds.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try { _debounceTimer?.Stop(); } catch { }
            try { _pollTimer?.Stop(); } catch { }

            if (_showHook != IntPtr.Zero)
            {
                Win32Interop.UnhookWinEvent(_showHook);
                _showHook = IntPtr.Zero;
            }
            if (_foregroundHook != IntPtr.Zero)
            {
                Win32Interop.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            if (_minimizeEndHook != IntPtr.Zero)
            {
                Win32Interop.UnhookWinEvent(_minimizeEndHook);
                _minimizeEndHook = IntPtr.Zero;
            }

            RestoreAllHiddenWindows();

            if (_taskbarList != null)
            {
                try { Marshal.ReleaseComObject(_taskbarList); } catch { }
                _taskbarList = null;
            }
        }
    }
}
