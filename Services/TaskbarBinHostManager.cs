using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Services
{
    public class TaskbarBinHostManager : IDisposable
    {
        private const string WindowClassName = "WinBins_TaskbarHost_WndClass";

        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly Dispatcher _dispatcher;

        // Keep WndProc delegate rooted for process lifetime so GC never collects native callback
        private readonly Win32Interop.WndProcDelegate _wndProcDelegate;
        private readonly DispatcherTimer _pinSyncDebounceTimer;
        private readonly DispatcherTimer _pinFollowUpTimer;
        private readonly DispatcherTimer _taskbandStampTimer;
        private readonly DispatcherTimer _hoverMonitorTimer;
        private readonly EventHandler _displaySettingsChangedHandler;
        private FileSystemWatcher _pinnedWatcher;
        private FileSystemWatcher _implicitPinnedWatcher;
        private long _lastTaskbandStamp;
        private volatile bool _suppressWatcherEvents;
        private bool _classRegistered;
        private bool _capturingOrder;

        private readonly object _buttonRectsLock = new object();
        private Dictionary<string, System.Windows.Rect> _cachedButtonRects = new Dictionary<string, System.Windows.Rect>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, AutomationElement> _cachedButtonElements = new Dictionary<string, AutomationElement>(StringComparer.OrdinalIgnoreCase);
        private volatile bool _fastRefreshingRects;
        private long _lastButtonRectScanTick;
        private string _hoveredBinId;
        private int _hoverEnterTick;
        private bool _hoverFiredForCurrentStay;
        private bool _wasLeftButtonDownOutsideTray;
        private System.Drawing.Point? _activeHoveredBinAnchor;
        private readonly Win32Interop.WinEventDelegate _tooltipWinEventDelegate;
        private IntPtr _tooltipShowHook = IntPtr.Zero;
        private IntPtr _tooltipUncloakHook = IntPtr.Zero;
        private IntPtr _hookedTrayHwnd = IntPtr.Zero;
        private uint _hookedTrayPid;

        private class HostedBinWindow
        {
            public string BinId;
            public IntPtr Hwnd;
            public IntPtr CurrentBigIconHandle;
            public IntPtr CurrentSmallIconHandle;
            public string AppliedIconSignature;
            public string AppliedAumid;
            public long LastTriggerTick;
        }

        private readonly Dictionary<string, HostedBinWindow> _byBinId = new Dictionary<string, HostedBinWindow>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntPtr, HostedBinWindow> _byHwnd = new Dictionary<IntPtr, HostedBinWindow>();
        private readonly HashSet<string> _knownPinnedBinIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event Action<BinGroup, System.Drawing.Point> BinTaskbarIconClicked;
        public event Action<BinGroup, System.Drawing.Point, bool> BinTaskbarHoverTriggered;
        public event Action<BinGroup, System.Drawing.Point> BinTaskbarHoverSwitchTriggered;
        public event Action<BinGroup, System.Drawing.Point> BinTaskbarPeekTriggered;
        public Func<bool> IsPopupCurrentlyOpen { get; set; }
        public event Action TaskbarButtonRectsUpdated;
        public event Action BinClosedFromTaskbar;
        public event Action TaskbarPinStateChanged;

        public TaskbarBinHostManager(ConfigService configService, IconService iconService, Dispatcher dispatcher)
        {
            _configService = configService;
            _iconService = iconService;
            _dispatcher = dispatcher;
            _wndProcDelegate = WndProc;
            _tooltipWinEventDelegate = OnExplorerTooltipWindowShown;
            EnsureTooltipWinEventHooks();

            _pinSyncDebounceTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(260)
            };
            _pinSyncDebounceTimer.Tick += PinSyncDebounceTimer_Tick;

            _pinFollowUpTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(950)
            };
            _pinFollowUpTimer.Tick += (_, __) =>
            {
                _pinFollowUpTimer.Stop();
                ReconcilePinnedBins();
            };

            _lastTaskbandStamp = ShortcutService.GetTaskbandPinStamp();
            _taskbandStampTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _taskbandStampTimer.Tick += (_, __) =>
            {
                long currentStamp = ShortcutService.GetTaskbandPinStamp();
                if (currentStamp != _lastTaskbandStamp)
                {
                    _lastTaskbandStamp = currentStamp;
                    ShortcutService.InvalidatePinnedCache();
                    SchedulePinnedSync();
                }
            };
            _taskbandStampTimer.Start();

            _hoverMonitorTimer = new DispatcherTimer(DispatcherPriority.Input, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(110)
            };
            _hoverMonitorTimer.Tick += HoverMonitorTimer_Tick;
            _hoverMonitorTimer.Start();

            _displaySettingsChangedHandler = (_, __) =>
            {
                _dispatcher.BeginInvoke(new Action(() => CaptureTaskbarOrderAsync(forceRectScan: true)));
            };
            try
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += _displaySettingsChangedHandler;
            }
            catch
            {
            }

            InitializePinnedFolderWatcher();
        }

        private void SchedulePinnedSync()
        {
            ShortcutService.InvalidatePinnedCache();
            _pinSyncDebounceTimer.Stop();
            _pinSyncDebounceTimer.Start();
            _pinFollowUpTimer.Stop();
            _pinFollowUpTimer.Start();
        }

        private void InitializePinnedFolderWatcher()
        {
            FileSystemEventHandler scheduleSync = (_, __) =>
            {
                if (_suppressWatcherEvents)
                    return;

                _dispatcher.BeginInvoke(new Action(SchedulePinnedSync));
            };
            RenamedEventHandler scheduleRenameSync = (_, __) =>
            {
                if (_suppressWatcherEvents)
                    return;

                _dispatcher.BeginInvoke(new Action(SchedulePinnedSync));
            };

            try
            {
                string pinnedDir = ShortcutService.GetUserPinnedTaskbarFolder();
                if (Directory.Exists(pinnedDir))
                {
                    _pinnedWatcher = new FileSystemWatcher(pinnedDir, "*.lnk")
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    _pinnedWatcher.Created += scheduleSync;
                    _pinnedWatcher.Changed += scheduleSync;
                    _pinnedWatcher.Deleted += scheduleSync;
                    _pinnedWatcher.Renamed += scheduleRenameSync;
                }
            }
            catch
            {
            }

            try
            {
                string implicitDir = ShortcutService.GetImplicitAppShortcutsFolder();
                if (Directory.Exists(implicitDir))
                {
                    _implicitPinnedWatcher = new FileSystemWatcher(implicitDir, "*.lnk")
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    _implicitPinnedWatcher.Created += scheduleSync;
                    _implicitPinnedWatcher.Changed += scheduleSync;
                    _implicitPinnedWatcher.Deleted += scheduleSync;
                    _implicitPinnedWatcher.Renamed += scheduleRenameSync;
                }
            }
            catch
            {
            }
        }

        private void PinSyncDebounceTimer_Tick(object sender, EventArgs e)
        {
            _pinSyncDebounceTimer.Stop();
            ReconcilePinnedBins();
        }

        private void ReconcilePinnedBins()
        {
            var bins = _configService.Current?.Bins;
            if (bins == null)
                return;

            bool configDirty = false;
            foreach (BinGroup bin in bins)
            {
                bool currentlyPinned = ShortcutService.IsBinPinnedOnTaskbar(bin);
                if (currentlyPinned)
                {
                    _knownPinnedBinIds.Add(bin.Id);
                    if (!bin.IsEnabledOnTaskbar)
                    {
                        bin.IsEnabledOnTaskbar = true;
                        configDirty = true;
                    }

                    // Once pinned on the Windows 11 Taskbar, destroy the temporary host HWND so the pinned launcher
                    // shortcut stands on its own (zero window thumbnail preview on hover & zero running indicator bar).
                    if (_byBinId.ContainsKey(bin.Id))
                    {
                        DestroyHost(bin.Id);
                        SyncShortcutsGuarded(bin);
                    }
                }
                else if (_knownPinnedBinIds.Remove(bin.Id))
                {
                    // User explicitly unpinned this Bin from the taskbar
                    if (bin.IsEnabledOnTaskbar)
                    {
                        bin.IsEnabledOnTaskbar = false;
                        configDirty = true;
                    }
                    DestroyHost(bin.Id);
                    ShortcutService.RemoveStartMenuShortcut(bin.Id);
                    JumpListService.DeleteBinJumpList(ShortcutService.ResolveActiveAppUserModelId(bin));
                }
            }

            if (configDirty)
            {
                _configService.Save(notify: false);
            }

            JumpListService.SyncManagerJumpList(_configService, _iconService);
            CaptureTaskbarOrderAsync(forceRectScan: true);
            TaskbarPinStateChanged?.Invoke();
        }

        private void SyncShortcutsGuarded(BinGroup bin)
        {
            _suppressWatcherEvents = true;
            try
            {
                string icoPath = _iconService.EnsureBinIcoFile(bin);
                string aumid = ShortcutService.ResolveActiveAppUserModelId(bin);
                ShortcutService.SyncStartMenuAndPinnedShortcuts(bin, icoPath, aumid);
                JumpListService.SyncBinJumpList(_configService, _iconService, bin, aumid);
            }
            finally
            {
                _suppressWatcherEvents = false;
            }
        }

        private void EnsureWindowClassRegistered()
        {
            if (_classRegistered)
                return;

            var wc = new Win32Interop.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf(typeof(Win32Interop.WNDCLASSEXW)),
                style = 0,
                lpfnWndProc = _wndProcDelegate,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = Win32Interop.GetModuleHandleW(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = WindowClassName,
                hIconSm = IntPtr.Zero
            };

            Win32Interop.RegisterClassExW(ref wc);
            _classRegistered = true;
        }

        public void SyncAllBins()
        {
            EnsureWindowClassRegistered();

            var bins = _configService.Current?.Bins ?? new List<BinGroup>();
            bool configChanged = false;

            foreach (BinGroup bin in bins)
            {
                if (ShortcutService.IsBinPinnedOnTaskbar(bin))
                {
                    _knownPinnedBinIds.Add(bin.Id);
                    if (!bin.IsEnabledOnTaskbar)
                    {
                        bin.IsEnabledOnTaskbar = true;
                        configChanged = true;
                    }
                }
            }

            if (configChanged)
            {
                _configService.Save(notify: false);
            }

            var activeIds = new HashSet<string>(
                bins.Where(b => b.IsEnabledOnTaskbar).Select(b => b.Id),
                StringComparer.OrdinalIgnoreCase);

            var toRemove = _byBinId.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (string id in toRemove)
            {
                DestroyHost(id);
                ShortcutService.RemoveStartMenuShortcut(id);
            }

            // Spawn unpinned taskbar hosts in their saved left-to-right taskbar order
            int fallbackIdx = 0;
            var orderedActiveBins = bins
                .Select((b, idx) => new { Bin = b, OriginalIdx = idx })
                .Where(x => x.Bin.IsEnabledOnTaskbar)
                .OrderBy(x => x.Bin.TaskbarOrder)
                .ThenBy(x => x.OriginalIdx)
                .Select(x => x.Bin)
                .ToList();

            foreach (BinGroup bin in orderedActiveBins)
            {
                if (bin.TaskbarOrder == int.MaxValue)
                {
                    bin.TaskbarOrder = fallbackIdx;
                }
                fallbackIdx++;
                RefreshBin(bin);
            }

            JumpListService.SyncManagerJumpList(_configService, _iconService);
            CaptureTaskbarOrderAsync(forceRectScan: true);
        }

        public void RefreshBin(BinGroup bin)
        {
            if (bin == null)
                return;

            EnsureWindowClassRegistered();

            bool isPinned = ShortcutService.IsBinPinnedOnTaskbar(bin);
            if (isPinned)
            {
                _knownPinnedBinIds.Add(bin.Id);

                // Pinned Bins operate as pure taskbar launcher shortcuts via IPC — no live HWND needed!
                // This eliminates all window hover thumbnail previews and running-window indicator bars on Windows 11.
                DestroyHost(bin.Id);
                SyncShortcutsGuarded(bin);
                return;
            }

            if (!bin.IsEnabledOnTaskbar)
            {
                DestroyHost(bin.Id);
                ShortcutService.RemoveStartMenuShortcut(bin.Id);
                JumpListService.DeleteBinJumpList(ShortcutService.ResolveActiveAppUserModelId(bin));
                return;
            }

            string currentSig = IconService.ComputeIconSignature(bin);
            if (_byBinId.TryGetValue(bin.Id, out HostedBinWindow existing))
            {
                if (!string.Equals(existing.AppliedIconSignature, currentSig, StringComparison.Ordinal))
                {
                    DestroyHost(bin.Id);
                    CreateHost(bin, currentSig);
                }
                else
                {
                    Win32Interop.SetWindowTextW(existing.Hwnd, bin.Name ?? "Bin");
                    Win32Interop.DwmInvalidateIconicBitmaps(existing.Hwnd);
                    JumpListService.SyncBinJumpList(_configService, _iconService, bin, existing.AppliedAumid);
                }
            }
            else
            {
                CreateHost(bin, currentSig);
            }
        }

        private void CreateHost(BinGroup bin, string iconSignature)
        {
            uint style = Win32Interop.WS_OVERLAPPED | Win32Interop.WS_CAPTION | Win32Interop.WS_SYSMENU | Win32Interop.WS_MINIMIZEBOX;
            uint exStyle = Win32Interop.WS_EX_APPWINDOW;

            IntPtr hwnd = Win32Interop.CreateWindowExW(
                exStyle,
                WindowClassName,
                bin.Name ?? "Bin",
                style,
                -32000,
                -32000,
                1,
                1,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32Interop.GetModuleHandleW(null),
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
                return;

            string icoPath = _iconService.EnsureBinIcoFile(bin);
            string exePath = Assembly.GetExecutingAssembly().Location;
            string relaunchCmd = $"\"{exePath}\" --bin \"{bin.Id}\"";
            string stableAumid = ShortcutService.ResolveActiveAppUserModelId(bin);

            _suppressWatcherEvents = true;
            try
            {
                ShortcutService.SyncStartMenuAndPinnedShortcuts(bin, icoPath, stableAumid);
                JumpListService.SyncBinJumpList(_configService, _iconService, bin, stableAumid);
            }
            finally
            {
                _suppressWatcherEvents = false;
            }

            var host = new HostedBinWindow
            {
                BinId = bin.Id,
                Hwnd = hwnd,
                AppliedIconSignature = iconSignature,
                AppliedAumid = stableAumid
            };

            _byBinId[bin.Id] = host;
            _byHwnd[hwnd] = host;

            // Configure DWM attributes and icons BEFORE showing on the Windows 11 Taskbar
            int one = 1;
            Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_TRANSITIONS_FORCEDISABLED, ref one, sizeof(int));
            Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_FORCE_ICONIC_REPRESENTATION, ref one, sizeof(int));
            Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_HAS_ICONIC_BITMAP, ref one, sizeof(int));
            Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_DISALLOW_PEEK, ref one, sizeof(int));
            Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_EXCLUDED_FROM_PEEK, ref one, sizeof(int));

            Win32Interop.SetWindowAppUserModelProperties(
                hwnd,
                stableAumid,
                relaunchCmd,
                bin.Name ?? "Bin",
                icoPath);

            IntPtr bigIcon = _iconService.CreateBinIconHandle(bin);
            IntPtr smallIcon = _iconService.CreateBinSmallIconHandle(bin, 32);

            if (bigIcon != IntPtr.Zero)
            {
                Win32Interop.SendMessageW(hwnd, Win32Interop.WM_SETICON, (IntPtr)Win32Interop.ICON_BIG, bigIcon);
                host.CurrentBigIconHandle = bigIcon;
            }
            if (smallIcon != IntPtr.Zero)
            {
                Win32Interop.SendMessageW(hwnd, Win32Interop.WM_SETICON, (IntPtr)Win32Interop.ICON_SMALL, smallIcon);
                host.CurrentSmallIconHandle = smallIcon;
            }

            // Place minimized on Windows 11 Taskbar without stealing focus
            Win32Interop.ShowWindow(hwnd, Win32Interop.SW_SHOWMINNOACTIVE);
            Win32Interop.DwmInvalidateIconicBitmaps(hwnd);
        }

        public bool TryGetBinTaskbarRect(string binId, out System.Windows.Rect screenRect)
        {
            screenRect = System.Windows.Rect.Empty;
            if (string.IsNullOrWhiteSpace(binId))
                return false;

            lock (_buttonRectsLock)
            {
                if (_cachedButtonRects.TryGetValue(binId, out System.Windows.Rect r) && !r.IsEmpty)
                {
                    screenRect = r;
                    return true;
                }
            }
            return false;
        }

        public bool TryGetBinTaskbarAnchorPoint(string binId, out System.Drawing.Point anchorScreenPx)
        {
            anchorScreenPx = System.Drawing.Point.Empty;
            if (TryGetBinTaskbarRect(binId, out System.Windows.Rect r))
            {
                anchorScreenPx = new System.Drawing.Point(
                    (int)Math.Round(r.Left + r.Width / 2.0),
                    (int)Math.Round(r.Top + r.Height / 2.0));
                return true;
            }
            return false;
        }

        private void HoverMonitorTimer_Tick(object sender, EventArgs e)
        {
            bool hoverEnabled = _configService.Current?.HoverToOpenFlyout == true;
            bool leftDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LBUTTON) & 0x8000) != 0;

            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT pt))
                return;

            IntPtr trayHwnd = Win32Interop.FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            bool insideTray = false;
            if (trayHwnd != IntPtr.Zero && Win32Interop.GetWindowRect(trayHwnd, out Win32Interop.RECT trayRect))
            {
                insideTray = pt.X >= trayRect.Left && pt.X <= trayRect.Right &&
                             pt.Y >= trayRect.Top && pt.Y <= trayRect.Bottom;
            }

            if (!insideTray)
            {
                _wasLeftButtonDownOutsideTray = leftDown;
                _hoveredBinId = null;
                _hoverFiredForCurrentStay = false;
                _activeHoveredBinAnchor = null;
                return;
            }

            // If user released mouse button while inside tray, clear drag-origin state
            if (!leftDown)
            {
                _wasLeftButtonDownOutsideTray = false;
            }

            bool isDragHoverCandidate = leftDown && _wasLeftButtonDownOutsideTray;

            // Refresh cached button rects in background if empty or older than 30 seconds
            long nowTick = Environment.TickCount;
            bool needRefresh;
            string matchedBinId = null;
            System.Windows.Rect matchedRect = System.Windows.Rect.Empty;

            lock (_buttonRectsLock)
            {
                needRefresh = _cachedButtonRects.Count == 0 || Math.Abs(nowTick - _lastButtonRectScanTick) > 30000;
                foreach (var kv in _cachedButtonRects)
                {
                    System.Windows.Rect r = kv.Value;
                    if (pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom)
                    {
                        matchedBinId = kv.Key;
                        matchedRect = r;
                        break;
                    }
                }
            }

            if (needRefresh)
            {
                CaptureTaskbarOrderAsync(forceRectScan: true);
            }

            if (string.IsNullOrEmpty(matchedBinId))
            {
                _hoveredBinId = null;
                _hoverFiredForCurrentStay = false;
                _activeHoveredBinAnchor = null;
                return;
            }

            var currentBtnAnchor = new System.Drawing.Point(
                (int)Math.Round(matchedRect.Left + matchedRect.Width / 2.0),
                (int)Math.Round(matchedRect.Top + matchedRect.Height / 2.0));

            _activeHoveredBinAnchor = currentBtnAnchor;
            EnsureTooltipWinEventHooks(trayHwnd);

            // Always suppress the native Windows 11 taskbar button title tooltip while hovering any Bin icon
            Win32Interop.DismissTaskbarThumbnailWindows(currentBtnAnchor, cancelTrayHoverTooltip: true);

            if (!string.Equals(_hoveredBinId, matchedBinId, StringComparison.OrdinalIgnoreCase))
            {
                _hoveredBinId = matchedBinId;
                _hoverEnterTick = Environment.TickCount;
                _hoverFiredForCurrentStay = false;
                return;
            }

            bool popupOpen = IsPopupCurrentlyOpen != null && IsPopupCurrentlyOpen();

            if (!_hoverFiredForCurrentStay)
            {
                int thresholdMs = isDragHoverCandidate
                    ? 340
                    : (popupOpen
                        ? 150
                        : (hoverEnabled ? Math.Max(120, _configService.Current?.HoverOpenDelayMs ?? 280) : 260));

                if (Math.Abs(Environment.TickCount - _hoverEnterTick) >= thresholdMs)
                {
                    _hoverFiredForCurrentStay = true;
                    BinGroup bin = FindBin(matchedBinId);
                    if (bin != null)
                    {
                        var anchorPt = new System.Drawing.Point(
                            (int)Math.Round(matchedRect.Left + matchedRect.Width / 2.0),
                            (int)Math.Round(matchedRect.Top + matchedRect.Height / 2.0));

                        if (popupOpen && !leftDown)
                        {
                            Win32Interop.DismissTaskbarThumbnailWindows(anchorPt, cancelTrayHoverTooltip: true);
                            BinTaskbarHoverSwitchTriggered?.Invoke(bin, anchorPt);
                        }
                        else if (hoverEnabled || isDragHoverCandidate)
                        {
                            Win32Interop.DismissTaskbarThumbnailWindows(anchorPt, cancelTrayHoverTooltip: true);
                            BinTaskbarHoverTriggered?.Invoke(bin, anchorPt, isDragHoverCandidate);
                        }
                        else if (!leftDown)
                        {
                            BinTaskbarPeekTriggered?.Invoke(bin, anchorPt);
                        }
                    }
                }
            }
        }

        public void RequestButtonRectRefresh()
        {
            CaptureTaskbarOrderAsync(forceRectScan: true);
        }

        /// <summary>
        /// Ultra-fast (~0.1ms) background query of cached AutomationElement.BoundingRectangle for active Bins
        /// so bottom indicators stay locked onto centered Windows 11 taskbar buttons as apps open/close/animate.
        /// Falls back to a full UIA button scan only if an element is missing or invalidated.
        /// </summary>
        public void RefreshCachedButtonRectsFastAsync(IList<string> targetBinIds)
        {
            if (targetBinIds == null || targetBinIds.Count == 0 || _fastRefreshingRects || _capturingOrder)
                return;

            _fastRefreshingRects = true;
            Task.Run(() =>
            {
                try
                {
                    var pairs = new List<Tuple<string, AutomationElement>>(targetBinIds.Count);
                    lock (_buttonRectsLock)
                    {
                        foreach (string id in targetBinIds)
                        {
                            if (!_cachedButtonElements.TryGetValue(id, out AutomationElement el) || el == null)
                            {
                                CaptureTaskbarOrderAsync(forceRectScan: true);
                                return;
                            }
                            pairs.Add(Tuple.Create(id, el));
                        }
                    }

                    var updated = new Dictionary<string, System.Windows.Rect>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in pairs)
                    {
                        try
                        {
                            System.Windows.Rect r = pair.Item2.Current.BoundingRectangle;
                            if (r.IsEmpty || double.IsInfinity(r.Left) || double.IsNaN(r.Left) || r.Width < 12 || r.Height < 12)
                            {
                                lock (_buttonRectsLock)
                                {
                                    _cachedButtonElements.Remove(pair.Item1);
                                }
                                CaptureTaskbarOrderAsync(forceRectScan: true);
                                return;
                            }
                            updated[pair.Item1] = r;
                        }
                        catch
                        {
                            lock (_buttonRectsLock)
                            {
                                _cachedButtonElements.Remove(pair.Item1);
                            }
                            CaptureTaskbarOrderAsync(forceRectScan: true);
                            return;
                        }
                    }

                    bool anyMoved = false;
                    lock (_buttonRectsLock)
                    {
                        foreach (var kv in updated)
                        {
                            if (!_cachedButtonRects.TryGetValue(kv.Key, out System.Windows.Rect prev) ||
                                Math.Abs(prev.Left - kv.Value.Left) >= 0.5 ||
                                Math.Abs(prev.Top - kv.Value.Top) >= 0.5 ||
                                Math.Abs(prev.Width - kv.Value.Width) >= 0.5 ||
                                Math.Abs(prev.Height - kv.Value.Height) >= 0.5)
                            {
                                _cachedButtonRects[kv.Key] = kv.Value;
                                anyMoved = true;
                            }
                        }
                    }

                    if (anyMoved)
                    {
                        _dispatcher.BeginInvoke(new Action(() => TaskbarButtonRectsUpdated?.Invoke()));
                    }
                }
                catch
                {
                }
                finally
                {
                    _fastRefreshingRects = false;
                }
            });
        }

        private List<string> GetAllTaskbarActiveBinIds()
        {
            var bins = _configService.Current?.Bins;
            if (bins == null)
                return new List<string>();

            return bins
                .Where(b => b != null && (b.IsEnabledOnTaskbar || _knownPinnedBinIds.Contains(b.Id)))
                .Select(b => b.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void CaptureTaskbarOrderAsync(bool forceRectScan = false)
        {
            if (_capturingOrder)
                return;

            var activeBinIds = GetAllTaskbarActiveBinIds();
            if (activeBinIds.Count == 0)
                return;

            if (!forceRectScan && activeBinIds.Count < 2)
            {
                lock (_buttonRectsLock)
                {
                    if (_cachedButtonRects.Count > 0 && Math.Abs(Environment.TickCount - _lastButtonRectScanTick) < 3500)
                        return;
                }
            }

            _capturingOrder = true;

            Task.Run(() =>
            {
                try
                {
                    var orderedIds = ReadBinOrderAndRectsFromTaskbarUiAutomation(
                        activeBinIds,
                        out Dictionary<string, System.Windows.Rect> rects,
                        out Dictionary<string, AutomationElement> elements);
                    if (rects != null)
                    {
                        lock (_buttonRectsLock)
                        {
                            _cachedButtonRects = rects;
                            _cachedButtonElements = elements ?? new Dictionary<string, AutomationElement>(StringComparer.OrdinalIgnoreCase);
                            _lastButtonRectScanTick = Environment.TickCount;
                        }
                    }

                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (orderedIds != null && orderedIds.Count > 1)
                        {
                            ApplyCapturedOrder(orderedIds);
                        }
                        TaskbarButtonRectsUpdated?.Invoke();
                    }));
                }
                catch
                {
                }
                finally
                {
                    _capturingOrder = false;
                }
            });
        }

        public void CaptureTaskbarOrderSync()
        {
            var activeBinIds = GetAllTaskbarActiveBinIds();
            if (activeBinIds.Count == 0)
                return;

            try
            {
                var orderedIds = ReadBinOrderAndRectsFromTaskbarUiAutomation(
                    activeBinIds,
                    out Dictionary<string, System.Windows.Rect> rects,
                    out Dictionary<string, AutomationElement> elements);
                if (rects != null)
                {
                    lock (_buttonRectsLock)
                    {
                        _cachedButtonRects = rects;
                        _cachedButtonElements = elements ?? new Dictionary<string, AutomationElement>(StringComparer.OrdinalIgnoreCase);
                        _lastButtonRectScanTick = Environment.TickCount;
                    }
                }

                if (orderedIds != null && orderedIds.Count > 1)
                {
                    ApplyCapturedOrder(orderedIds);
                }
            }
            catch
            {
            }
        }

        private void ApplyCapturedOrder(List<string> orderedBinIds)
        {
            var bins = _configService.Current?.Bins;
            if (bins == null)
                return;

            bool changed = false;
            for (int i = 0; i < orderedBinIds.Count; i++)
            {
                BinGroup bin = FindBin(orderedBinIds[i]);
                if (bin != null && bin.TaskbarOrder != i)
                {
                    bin.TaskbarOrder = i;
                    changed = true;
                }
            }

            if (changed)
            {
                _configService.Save(notify: false);
            }
        }

        private static List<string> ReadBinOrderAndRectsFromTaskbarUiAutomation(
            List<string> activeBinIds,
            out Dictionary<string, System.Windows.Rect> foundRects,
            out Dictionary<string, AutomationElement> foundElements)
        {
            foundRects = new Dictionary<string, System.Windows.Rect>(StringComparer.OrdinalIgnoreCase);
            foundElements = new Dictionary<string, AutomationElement>(StringComparer.OrdinalIgnoreCase);
            if (activeBinIds == null || activeBinIds.Count == 0)
                return null;

            IntPtr trayHwnd = Win32Interop.FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (trayHwnd == IntPtr.Zero)
                return null;

            AutomationElement trayEl = AutomationElement.FromHandle(trayHwnd);
            if (trayEl == null)
                return null;

            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            AutomationElementCollection buttons = trayEl.FindAll(TreeScope.Descendants, condition);
            if (buttons == null || buttons.Count == 0)
                return null;

            var foundPositions = new List<Tuple<string, double>>();
            var matchedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < buttons.Count; i++)
            {
                try
                {
                    AutomationElement btn = buttons[i];
                    string autoId = btn.Current.AutomationId ?? string.Empty;
                    if (string.IsNullOrEmpty(autoId))
                        continue;

                    foreach (string binId in activeBinIds)
                    {
                        if (!matchedSet.Contains(binId) &&
                            autoId.IndexOf(binId, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            System.Windows.Rect rect = btn.Current.BoundingRectangle;
                            if (!rect.IsEmpty && !double.IsInfinity(rect.Left) && !double.IsNaN(rect.Left))
                            {
                                matchedSet.Add(binId);
                                foundRects[binId] = rect;
                                foundElements[binId] = btn;
                                foundPositions.Add(Tuple.Create(binId, rect.Left));
                            }
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            if (foundPositions.Count < 2)
                return null;

            return foundPositions
                .OrderBy(t => t.Item2)
                .Select(t => t.Item1)
                .ToList();
        }

        private void DestroyHost(string binId)
        {
            if (!_byBinId.TryGetValue(binId, out HostedBinWindow host))
                return;

            _byBinId.Remove(binId);
            if (host.Hwnd != IntPtr.Zero)
            {
                _byHwnd.Remove(host.Hwnd);
                Win32Interop.DestroyWindow(host.Hwnd);
            }

            if (host.CurrentBigIconHandle != IntPtr.Zero)
            {
                Win32Interop.DestroyIcon(host.CurrentBigIconHandle);
                host.CurrentBigIconHandle = IntPtr.Zero;
            }

            if (host.CurrentSmallIconHandle != IntPtr.Zero)
            {
                Win32Interop.DestroyIcon(host.CurrentSmallIconHandle);
                host.CurrentSmallIconHandle = IntPtr.Zero;
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (_byHwnd.TryGetValue(hWnd, out HostedBinWindow host))
            {
                switch (msg)
                {
                    case Win32Interop.WM_QUERYOPEN:
                        NotifyBinClicked(host);
                        return IntPtr.Zero;

                    case Win32Interop.WM_SYSCOMMAND:
                    {
                        int cmd = (int)(wParam.ToInt64() & 0xFFF0);
                        if (cmd == Win32Interop.SC_RESTORE || cmd == Win32Interop.SC_MAXIMIZE)
                        {
                            NotifyBinClicked(host);
                            return IntPtr.Zero;
                        }
                        break;
                    }

                    case Win32Interop.WM_ACTIVATE:
                    {
                        int activeState = (int)(wParam.ToInt64() & 0xFFFF);
                        if (activeState == Win32Interop.WA_ACTIVE || activeState == Win32Interop.WA_CLICKACTIVE)
                        {
                            Win32Interop.ShowWindow(hWnd, Win32Interop.SW_SHOWMINNOACTIVE);
                            NotifyBinClicked(host);
                            return IntPtr.Zero;
                        }
                        break;
                    }

                    case Win32Interop.WM_DWMSENDICONICTHUMBNAIL:
                    {
                        int maxW = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        int maxH = (short)(lParam.ToInt64() & 0xFFFF);
                        SendDwmThumbnail(host, maxW, maxH);
                        return IntPtr.Zero;
                    }

                    case Win32Interop.WM_DWMSENDICONICLIVEPREVIEWBITMAP:
                    {
                        SendDwmLivePreview(host);
                        return IntPtr.Zero;
                    }

                    case Win32Interop.WM_CLOSE:
                    {
                        BinGroup bin = FindBin(host.BinId);
                        if (bin != null && ShortcutService.IsBinPinnedOnTaskbar(bin))
                        {
                            _knownPinnedBinIds.Add(bin.Id);
                            if (!bin.IsEnabledOnTaskbar)
                            {
                                bin.IsEnabledOnTaskbar = true;
                                _configService.Save(notify: false);
                            }
                            DestroyHost(host.BinId);
                            SyncShortcutsGuarded(bin);
                            _dispatcher.BeginInvoke(new Action(() => TaskbarPinStateChanged?.Invoke()));
                            return IntPtr.Zero;
                        }

                        if (bin != null)
                        {
                            bin.IsEnabledOnTaskbar = false;
                            _configService.Save(notify: false);
                        }
                        DestroyHost(host.BinId);
                        ShortcutService.RemoveStartMenuShortcut(host.BinId);
                        _dispatcher.BeginInvoke(new Action(() => BinClosedFromTaskbar?.Invoke()));
                        return IntPtr.Zero;
                    }
                }
            }

            return Win32Interop.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private void SendDwmThumbnail(HostedBinWindow host, int maxW, int maxH)
        {
            BinGroup bin = FindBin(host.BinId);
            if (bin == null)
                return;

            using (Bitmap card = _iconService.CreateDwmPreviewCardBitmap(bin, maxW, maxH))
            {
                IntPtr hbmp = Win32Interop.CreateDwmArgbHBitmap(card);
                if (hbmp != IntPtr.Zero)
                {
                    try
                    {
                        Win32Interop.DwmSetIconicThumbnail(host.Hwnd, hbmp, 0);
                    }
                    finally
                    {
                        Win32Interop.DeleteObject(hbmp);
                    }
                }
            }
        }

        private void SendDwmLivePreview(HostedBinWindow host)
        {
            BinGroup bin = FindBin(host.BinId);
            if (bin == null)
                return;

            using (Bitmap card = _iconService.CreateDwmPreviewCardBitmap(bin, 240, 140))
            {
                IntPtr hbmp = Win32Interop.CreateDwmArgbHBitmap(card);
                if (hbmp != IntPtr.Zero)
                {
                    try
                    {
                        Win32Interop.DwmSetIconicLivePreviewBitmap(host.Hwnd, hbmp, IntPtr.Zero, 0);
                    }
                    finally
                    {
                        Win32Interop.DeleteObject(hbmp);
                    }
                }
            }
        }

        private void NotifyBinClicked(HostedBinWindow host)
        {
            long now = Environment.TickCount;
            if (Math.Abs(now - host.LastTriggerTick) < 160)
                return;

            host.LastTriggerTick = now;

            Win32Interop.GetCursorPos(out Win32Interop.POINT pt);
            var screenPt = new System.Drawing.Point(pt.X, pt.Y);

            _dispatcher.BeginInvoke(new Action(() =>
            {
                BinGroup bin = FindBin(host.BinId);
                if (bin != null)
                {
                    BinTaskbarIconClicked?.Invoke(bin, screenPt);
                }
            }));
        }

        private BinGroup FindBin(string id)
        {
            return _configService.Current?.Bins?.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureTooltipWinEventHooks(IntPtr knownTrayHwnd = default)
        {
            IntPtr trayHwnd = knownTrayHwnd != IntPtr.Zero
                ? knownTrayHwnd
                : Win32Interop.FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (trayHwnd == IntPtr.Zero)
                return;

            Win32Interop.GetWindowThreadProcessId(trayHwnd, out uint trayPid);
            if (trayPid == 0)
                return;

            if (_tooltipShowHook != IntPtr.Zero && _hookedTrayPid == trayPid && _hookedTrayHwnd == trayHwnd)
                return;

            UnhookTooltipWinEventHooks();
            _hookedTrayHwnd = trayHwnd;
            _hookedTrayPid = trayPid;

            uint flags = Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS;
            _tooltipShowHook = Win32Interop.SetWinEventHook(
                Win32Interop.EVENT_OBJECT_SHOW,
                Win32Interop.EVENT_OBJECT_SHOW,
                IntPtr.Zero,
                _tooltipWinEventDelegate,
                trayPid,
                0,
                flags);

            _tooltipUncloakHook = Win32Interop.SetWinEventHook(
                Win32Interop.EVENT_OBJECT_UNCLOAKED,
                Win32Interop.EVENT_OBJECT_UNCLOAKED,
                IntPtr.Zero,
                _tooltipWinEventDelegate,
                trayPid,
                0,
                flags);
        }

        private void UnhookTooltipWinEventHooks()
        {
            if (_tooltipShowHook != IntPtr.Zero)
            {
                try { Win32Interop.UnhookWinEvent(_tooltipShowHook); } catch { }
                _tooltipShowHook = IntPtr.Zero;
            }
            if (_tooltipUncloakHook != IntPtr.Zero)
            {
                try { Win32Interop.UnhookWinEvent(_tooltipUncloakHook); } catch { }
                _tooltipUncloakHook = IntPtr.Zero;
            }
            _hookedTrayHwnd = IntPtr.Zero;
            _hookedTrayPid = 0;
        }

        private void OnExplorerTooltipWindowShown(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (idObject != Win32Interop.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
                return;

            System.Drawing.Point? anchor = _activeHoveredBinAnchor;
            if (!anchor.HasValue || _hookedTrayPid == 0)
                return;

            Win32Interop.GetWindowRect(_hookedTrayHwnd, out Win32Interop.RECT trayRect);
            Win32Interop.TryDismissIfTaskbarTooltipWindow(hwnd, _hookedTrayHwnd, _hookedTrayPid, trayRect, anchor);
        }

        public void Dispose()
        {
            _pinSyncDebounceTimer.Stop();
            _pinFollowUpTimer.Stop();
            _taskbandStampTimer.Stop();
            _hoverMonitorTimer.Stop();
            _activeHoveredBinAnchor = null;
            UnhookTooltipWinEventHooks();

            if (_displaySettingsChangedHandler != null)
            {
                try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= _displaySettingsChangedHandler; } catch { }
            }

            CaptureTaskbarOrderSync();

            if (_pinnedWatcher != null)
            {
                _pinnedWatcher.EnableRaisingEvents = false;
                _pinnedWatcher.Dispose();
                _pinnedWatcher = null;
            }

            if (_implicitPinnedWatcher != null)
            {
                _implicitPinnedWatcher.EnableRaisingEvents = false;
                _implicitPinnedWatcher.Dispose();
                _implicitPinnedWatcher = null;
            }

            foreach (string id in _byBinId.Keys.ToList())
            {
                DestroyHost(id);
            }
        }
    }
}
