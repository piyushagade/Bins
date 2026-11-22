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

    public sealed class BinRunningAppSummary
    {
        public BinItem Item { get; set; }
        public List<RunningWindowEntry> Windows { get; set; }
        public bool IsForeground { get; set; }
    }

    public sealed class RunningAppsTaskbarManager : IDisposable
    {
        private sealed class ResolvedTargetExe
        {
            public string SourceKey;
            public string FullExePath;
            public string ExeFileName;
            public string PackagePrefix;
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
            "MultitaskingViewFrame",
            "MSTaskSwWClass",
            "Windows.UI.Core.CoreWindow"
        };

        private readonly ConfigService _configService;
        private readonly Dispatcher _dispatcher;
        private readonly uint _currentPid;

        private readonly Dictionary<string, ResolvedTargetExe> _resolvedItemCache =
            new Dictionary<string, ResolvedTargetExe>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<IntPtr> _hiddenHwnds = new HashSet<IntPtr>();
        private readonly Win32Interop.WinEventDelegate _winEventProc;
        private readonly Win32Interop.LowLevelKeyboardProc _keyboardProc;

        private IntPtr _showHook = IntPtr.Zero;
        private IntPtr _foregroundHook = IntPtr.Zero;
        private IntPtr _minimizeEndHook = IntPtr.Zero;
        private IntPtr _switchHook = IntPtr.Zero;
        private IntPtr _keyboardHook = IntPtr.Zero;

        private Win32Interop.ITaskbarList _taskbarList;
        private IntPtr _lastShellTrayHwnd = IntPtr.Zero;

        private DispatcherTimer _debounceTimer;
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _switcherEndTimer;
        private bool _switcherRestoreActive;
        private int _switcherLastActivityTick;
        private bool _disposed;

        public event Action RunningStateChanged;

        public RunningAppsTaskbarManager(ConfigService configService, Dispatcher dispatcher)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _currentPid = (uint)Process.GetCurrentProcess().Id;

            _winEventProc = OnWinEvent;
            _keyboardProc = OnLowLevelKeyboard;

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(75)
            };
            _debounceTimer.Tick += (_, __) =>
            {
                _debounceTimer.Stop();
                SyncRunningWindows();
            };

            _switcherEndTimer = new DispatcherTimer(DispatcherPriority.Input, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(95)
            };
            _switcherEndTimer.Tick += OnSwitcherEndTimerTick;

            _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _pollTimer.Tick += (_, __) => SyncRunningWindows();
            _pollTimer.Start();

            InstallWinEventHooks();
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SyncRunningWindows()));
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

                _switchHook = Win32Interop.SetWinEventHook(
                    Win32Interop.EVENT_SYSTEM_SWITCHSTART,
                    Win32Interop.EVENT_SYSTEM_SWITCHEND,
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

        private void EnsureKeyboardHookState(bool shouldBeInstalled)
        {
            if (_disposed)
                return;

            if (shouldBeInstalled)
            {
                if (_keyboardHook != IntPtr.Zero)
                    return;

                try
                {
                    IntPtr hMod = IntPtr.Zero;
                    try { hMod = Process.GetCurrentProcess().MainModule?.BaseAddress ?? IntPtr.Zero; } catch { }
                    _keyboardHook = Win32Interop.SetWindowsHookExW(Win32Interop.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
                    if (_keyboardHook == IntPtr.Zero && hMod != IntPtr.Zero)
                    {
                        _keyboardHook = Win32Interop.SetWindowsHookExW(Win32Interop.WH_KEYBOARD_LL, _keyboardProc, IntPtr.Zero, 0);
                    }
                }
                catch
                {
                }
            }
            else if (_keyboardHook != IntPtr.Zero)
            {
                try { Win32Interop.UnhookWindowsHookEx(_keyboardHook); } catch { }
                _keyboardHook = IntPtr.Zero;
            }
        }

        private static bool IsKeyDown(int vk)
        {
            return (Win32Interop.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private static bool IsAltOrWinKeyDown()
        {
            return IsKeyDown(Win32Interop.VK_MENU) ||
                   IsKeyDown(Win32Interop.VK_LMENU) ||
                   IsKeyDown(Win32Interop.VK_RMENU) ||
                   IsKeyDown(Win32Interop.VK_LWIN) ||
                   IsKeyDown(Win32Interop.VK_RWIN);
        }

        private static bool IsTaskSwitcherWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Win32Interop.IsWindow(hwnd) || !Win32Interop.IsWindowVisible(hwnd))
                return false;

            string cls = Win32Interop.GetWindowClassNameSafe(hwnd);
            if (string.IsNullOrEmpty(cls))
                return false;

            if (string.Equals(cls, "MultitaskingViewFrame", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "MSTaskSwWClass", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Windows 11 XAML Alt+Tab ("Task Switching"), Win+Tab ("Task View"), and Snap Assist use
            // XamlExplorerHostIslandWindow with a non-empty title, whereas tray overflow flyouts have an empty title.
            if (string.Equals(cls, "XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase) &&
                Win32Interop.GetWindowTextLengthW(hwnd) > 0)
            {
                return true;
            }

            return false;
        }

        private IntPtr OnLowLevelKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= Win32Interop.HC_ACTION && !_disposed && lParam != IntPtr.Zero)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    var kbd = Marshal.PtrToStructure<Win32Interop.KBDLLHOOKSTRUCT>(lParam);
                    uint vk = kbd.vkCode;

                    if (msg == Win32Interop.WM_KEYDOWN || msg == Win32Interop.WM_SYSKEYDOWN)
                    {
                        if (vk == Win32Interop.VK_TAB && _hiddenHwnds.Count > 0)
                        {
                            bool altDown = (kbd.flags & Win32Interop.LLKHF_ALTDOWN) != 0 ||
                                           IsKeyDown(Win32Interop.VK_MENU) ||
                                           IsKeyDown(Win32Interop.VK_LMENU) ||
                                           IsKeyDown(Win32Interop.VK_RMENU);
                            bool winDown = IsKeyDown(Win32Interop.VK_LWIN) || IsKeyDown(Win32Interop.VK_RWIN);

                            if (altDown || winDown)
                            {
                                // Synchronously re-register hidden binned windows before Windows processes Tab
                                // so XAML Alt+Tab / Win+Tab includes all open binned apps in proper MRU order.
                                RestoreHiddenWindowsForSwitcher();
                            }
                        }
                    }
                    else if (msg == Win32Interop.WM_KEYUP || msg == Win32Interop.WM_SYSKEYUP)
                    {
                        if (_switcherRestoreActive &&
                            (vk == Win32Interop.VK_MENU ||
                             vk == Win32Interop.VK_LMENU ||
                             vk == Win32Interop.VK_RMENU ||
                             vk == Win32Interop.VK_LWIN ||
                             vk == Win32Interop.VK_RWIN ||
                             vk == Win32Interop.VK_ESCAPE ||
                             vk == Win32Interop.VK_RETURN))
                        {
                            ScheduleSwitcherEndCheck();
                        }
                    }
                }
                catch
                {
                }
            }

            return Win32Interop.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void RestoreHiddenWindowsForSwitcher()
        {
            _switcherEndTimer.Stop();
            _switcherLastActivityTick = Environment.TickCount;

            if (_switcherRestoreActive || _hiddenHwnds.Count == 0)
                return;

            if (_taskbarList == null && !EnsureTaskbarListReady(out _))
                return;

            _switcherRestoreActive = true;

            IntPtr fgBefore = Win32Interop.GetForegroundWindow();

            // Walk top-to-bottom Win32 Z-order so we can call AddTab from bottom-to-top (oldest to newest),
            // preserving natural MRU ordering inside Windows 11's XAML Alt+Tab switcher.
            var orderedHidden = new List<IntPtr>(_hiddenHwnds.Count);
            IntPtr cur = Win32Interop.GetTopWindow(IntPtr.Zero);
            int guard = 0;
            while (cur != IntPtr.Zero && guard++ < 16384)
            {
                if (_hiddenHwnds.Contains(cur))
                {
                    orderedHidden.Add(cur);
                    if (orderedHidden.Count == _hiddenHwnds.Count)
                        break;
                }
                cur = Win32Interop.GetWindow(cur, Win32Interop.GW_HWNDNEXT);
            }

            if (orderedHidden.Count < _hiddenHwnds.Count)
            {
                foreach (IntPtr remaining in _hiddenHwnds)
                {
                    if (!orderedHidden.Contains(remaining))
                    {
                        orderedHidden.Add(remaining);
                    }
                }
            }

            for (int i = orderedHidden.Count - 1; i >= 0; i--)
            {
                IntPtr h = orderedHidden[i];
                if (Win32Interop.IsWindow(h) && Win32Interop.IsWindowVisible(h) && !Win32Interop.IsWindowCloaked(h))
                {
                    try { _taskbarList.AddTab(h); } catch { }
                }
            }

            // Re-assert the current foreground window as active tab #0 so the first Alt+Tab press highlights #1.
            if (fgBefore != IntPtr.Zero &&
                Win32Interop.IsWindow(fgBefore) &&
                !IsTaskSwitcherWindow(fgBefore))
            {
                try { _taskbarList.ActivateTab(fgBefore); } catch { }
            }

            // Start end-watcher so even gesture-triggered switchers automatically re-hide when dismissed.
            _switcherEndTimer.Start();
        }

        private void ScheduleSwitcherEndCheck()
        {
            _switcherEndTimer.Stop();
            _switcherEndTimer.Start();
        }

        private void OnSwitcherEndTimerTick(object sender, EventArgs e)
        {
            _switcherEndTimer.Stop();
            if (_disposed || !_switcherRestoreActive)
                return;

            IntPtr fg = Win32Interop.GetForegroundWindow();
            if (IsAltOrWinKeyDown() || IsTaskSwitcherWindow(fg))
            {
                _switcherEndTimer.Start();
                return;
            }

            _switcherRestoreActive = false;
            SyncRunningWindows(forceReassert: true);
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

            if (eventType == Win32Interop.EVENT_SYSTEM_SWITCHSTART)
            {
                if (_hiddenHwnds.Count > 0)
                {
                    RestoreHiddenWindowsForSwitcher();
                }
                return;
            }

            if (eventType == Win32Interop.EVENT_SYSTEM_SWITCHEND)
            {
                if (_switcherRestoreActive)
                {
                    ScheduleSwitcherEndCheck();
                }
                return;
            }

            if (eventType == Win32Interop.EVENT_OBJECT_SHOW && idObject != Win32Interop.OBJID_WINDOW)
                return;

            if (eventType == Win32Interop.EVENT_SYSTEM_FOREGROUND || eventType == Win32Interop.EVENT_OBJECT_SHOW)
            {
                if (_hiddenHwnds.Count > 0 && IsTaskSwitcherWindow(hwnd))
                {
                    RestoreHiddenWindowsForSwitcher();
                    return;
                }

                if (_switcherRestoreActive && eventType == Win32Interop.EVENT_SYSTEM_FOREGROUND)
                {
                    ScheduleSwitcherEndCheck();
                    return;
                }
            }

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
                var targetPackagePrefixes = new List<string>();
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
                        if (!string.IsNullOrWhiteSpace(resolved.PackagePrefix) &&
                            !targetPackagePrefixes.Contains(resolved.PackagePrefix, StringComparer.OrdinalIgnoreCase))
                        {
                            targetPackagePrefixes.Add(resolved.PackagePrefix);
                        }
                    }
                }

                bool hasAnyActiveTargets =
                    targetFullPaths.Count > 0 ||
                    targetFileNames.Count > 0 ||
                    targetPackagePrefixes.Count > 0 ||
                    hideExplorerWindows;
                EnsureKeyboardHookState(hasAnyActiveTargets);

                if (!hasAnyActiveTargets && _hiddenHwnds.Count == 0)
                {
                    RunningStateChanged?.Invoke();
                    return;
                }

                // While Alt+Tab / Win+Tab / Snap Assist is active, keep binned windows visible in the switcher.
                if (_switcherRestoreActive)
                {
                    if (!hasAnyActiveTargets)
                    {
                        _switcherRestoreActive = false;
                        _switcherEndTimer.Stop();
                    }
                    else if (IsAltOrWinKeyDown() ||
                             IsTaskSwitcherWindow(Win32Interop.GetForegroundWindow()) ||
                             Math.Abs(Environment.TickCount - _switcherLastActivityTick) < 260)
                    {
                        return;
                    }
                    else
                    {
                        _switcherRestoreActive = false;
                        forceReassert = true;
                    }
                }

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
                            TryGetWindowProcessExe(hwnd, pid, pidExeCache, out string exePath))
                        {
                            stillTargeted = DoesProcessMatchTargets(
                                exePath,
                                className,
                                targetFullPaths,
                                targetFileNames,
                                targetPackagePrefixes,
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
                {
                    RunningStateChanged?.Invoke();
                    return;
                }

                // 2. Discover running top-level windows matching active Bins and hide standalone taskbar buttons
                Win32Interop.EnumWindows((hwnd, lParam) =>
                {
                    if (!IsTaskbarEligibleAppWindow(hwnd, _currentPid, out uint pid, out string className))
                        return true;

                    if (!TryGetWindowProcessExe(hwnd, pid, pidExeCache, out string exePath))
                        return true;

                    if (DoesProcessMatchTargets(exePath, className, targetFullPaths, targetFileNames, targetPackagePrefixes, hideExplorerWindows))
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

                RunningStateChanged?.Invoke();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Single-pass lookup returning all currently running items and their open windows for a BinGroup.
        /// </summary>
        public List<BinRunningAppSummary> GetRunningAppsForBin(BinGroup bin)
        {
            var summaries = new List<BinRunningAppSummary>();
            if (_disposed || bin?.Items == null || bin.Items.Count == 0)
                return summaries;

            var itemCandidates = new List<Tuple<BinItem, HashSet<string>, HashSet<string>, List<string>, bool, List<RunningWindowEntry>>>();
            foreach (BinItem item in bin.Items)
            {
                if (item == null)
                    continue;

                ResolvedTargetExe resolved = ResolveItemExecutableTarget(item);
                if (resolved == null)
                    continue;

                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pkgPrefixes = new List<string>();

                if (!resolved.IsExplorerExe)
                {
                    if (!string.IsNullOrWhiteSpace(resolved.FullExePath))
                        paths.Add(resolved.FullExePath);
                    if (!string.IsNullOrWhiteSpace(resolved.ExeFileName) && !GenericHostExeNames.Contains(resolved.ExeFileName))
                        names.Add(resolved.ExeFileName);
                    if (!string.IsNullOrWhiteSpace(resolved.PackagePrefix))
                        pkgPrefixes.Add(resolved.PackagePrefix);
                }

                if (!resolved.IsExplorerExe && paths.Count == 0 && names.Count == 0 && pkgPrefixes.Count == 0)
                    continue;

                itemCandidates.Add(Tuple.Create(item, paths, names, pkgPrefixes, resolved.IsExplorerExe, new List<RunningWindowEntry>()));
            }

            if (itemCandidates.Count == 0)
                return summaries;

            IntPtr fgHwnd = Win32Interop.GetForegroundWindow();
            var pidExeCache = new Dictionary<uint, string>();

            try
            {
                Win32Interop.EnumWindows((hwnd, lParam) =>
                {
                    if (!IsTaskbarEligibleAppWindow(hwnd, _currentPid, out uint pid, out string className))
                        return true;

                    if (!TryGetWindowProcessExe(hwnd, pid, pidExeCache, out string exePath))
                        return true;

                    string title = null;
                    bool isMin = false;
                    bool computedMeta = false;

                    for (int i = 0; i < itemCandidates.Count; i++)
                    {
                        var c = itemCandidates[i];
                        if (DoesProcessMatchTargets(exePath, className, c.Item2, c.Item3, c.Item4, c.Item5))
                        {
                            if (!computedMeta)
                            {
                                title = Win32Interop.GetWindowTextSafe(hwnd);
                                isMin = Win32Interop.IsIconic(hwnd);
                                computedMeta = true;
                            }

                            c.Item6.Add(new RunningWindowEntry
                            {
                                Hwnd = hwnd,
                                Title = string.IsNullOrWhiteSpace(title) ? c.Item1.Name : title,
                                IsForeground = hwnd == fgHwnd,
                                IsMinimized = isMin
                            });
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }

            foreach (var c in itemCandidates)
            {
                if (c.Item6.Count > 0)
                {
                    summaries.Add(new BinRunningAppSummary
                    {
                        Item = c.Item1,
                        Windows = c.Item6,
                        IsForeground = c.Item6.Any(w => w.IsForeground && !w.IsMinimized)
                    });
                }
            }

            return summaries;
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
            var singlePackages = new List<string>();
            if (!resolved.IsExplorerExe)
            {
                if (!string.IsNullOrWhiteSpace(resolved.FullExePath))
                    singlePaths.Add(resolved.FullExePath);
                if (!string.IsNullOrWhiteSpace(resolved.ExeFileName) && !GenericHostExeNames.Contains(resolved.ExeFileName))
                    singleNames.Add(resolved.ExeFileName);
                if (!string.IsNullOrWhiteSpace(resolved.PackagePrefix))
                    singlePackages.Add(resolved.PackagePrefix);
            }

            if (!resolved.IsExplorerExe && singlePaths.Count == 0 && singleNames.Count == 0 && singlePackages.Count == 0)
                return results;

            IntPtr fgHwnd = Win32Interop.GetForegroundWindow();
            var pidExeCache = new Dictionary<uint, string>();

            try
            {
                Win32Interop.EnumWindows((hwnd, lParam) =>
                {
                    if (!IsTaskbarEligibleAppWindow(hwnd, _currentPid, out uint pid, out string className))
                        return true;

                    if (!TryGetWindowProcessExe(hwnd, pid, pidExeCache, out string exePath))
                        return true;

                    if (DoesProcessMatchTargets(exePath, className, singlePaths, singleNames, singlePackages, resolved.IsExplorerExe))
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

            // Handle packaged Store / MSIX / UWP apps: shell:AppsFolder\<PackageFamilyName>!<AppId>
            if (expanded.StartsWith(BinItem.ShellAppsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string aumid = expanded.Substring(BinItem.ShellAppsPrefix.Length).Trim();
                int bang = aumid.IndexOf('!');
                string family = bang > 0 ? aumid.Substring(0, bang) : aumid;
                int lastUnderscore = family.LastIndexOf('_');
                string pkgPrefix = lastUnderscore > 2 ? family.Substring(0, lastUnderscore + 1) : null;
                if (!string.IsNullOrWhiteSpace(pkgPrefix))
                {
                    var pkgResult = new ResolvedTargetExe
                    {
                        SourceKey = cacheKey,
                        PackagePrefix = pkgPrefix,
                        IsExplorerExe = false
                    };
                    _resolvedItemCache[cacheKey] = pkgResult;
                    return pkgResult;
                }
            }

            string ext = Path.GetExtension(expanded)?.ToLowerInvariant() ?? string.Empty;

            ResolvedTargetExe result = null;

            if (ext == ".exe")
            {
                string exeName = Path.GetFileName(expanded);
                if (string.Equals(exeName, "wt.exe", StringComparison.OrdinalIgnoreCase))
                {
                    result = new ResolvedTargetExe
                    {
                        SourceKey = cacheKey,
                        FullExePath = SafeNormalizeFullPath(expanded),
                        ExeFileName = "WindowsTerminal.exe",
                        PackagePrefix = "Microsoft.WindowsTerminal_",
                        IsExplorerExe = false
                    };
                }
                else if (!string.IsNullOrWhiteSpace(exeName) &&
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

        private static bool TryGetWindowProcessExe(IntPtr hwnd, uint pid, Dictionary<uint, string> cache, out string exePath)
        {
            if (!TryGetCachedProcessExe(pid, cache, out exePath))
                return false;

            // Unwrap UWP ApplicationFrameHost.exe windows to their hosted CoreWindow's real app PID
            if (string.Equals(Path.GetFileName(exePath), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr coreChild = Win32Interop.FindWindowExW(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                if (coreChild != IntPtr.Zero)
                {
                    Win32Interop.GetWindowThreadProcessId(coreChild, out uint uwpPid);
                    if (uwpPid != 0 && uwpPid != pid && TryGetCachedProcessExe(uwpPid, cache, out string uwpExe))
                    {
                        exePath = uwpExe;
                    }
                }
            }

            return !string.IsNullOrWhiteSpace(exePath);
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
            List<string> targetPackagePrefixes,
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

            if (targetFullPaths != null && targetFullPaths.Contains(processExePath))
                return true;

            if (targetFileNames != null && targetFileNames.Contains(procFileName))
                return true;

            if (targetPackagePrefixes != null && targetPackagePrefixes.Count > 0 &&
                processExePath.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < targetPackagePrefixes.Count; i++)
                {
                    if (processExePath.IndexOf(@"\" + targetPackagePrefixes[i], StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

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
            _switcherRestoreActive = false;
            try { _switcherEndTimer?.Stop(); } catch { }

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
            try { _switcherEndTimer?.Stop(); } catch { }

            if (_keyboardHook != IntPtr.Zero)
            {
                try { Win32Interop.UnhookWindowsHookEx(_keyboardHook); } catch { }
                _keyboardHook = IntPtr.Zero;
            }

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
            if (_switchHook != IntPtr.Zero)
            {
                Win32Interop.UnhookWinEvent(_switchHook);
                _switchHook = IntPtr.Zero;
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
