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
        private readonly DispatcherTimer _orderPollTimer;
        private readonly DispatcherTimer _pinSyncDebounceTimer;
        private readonly DispatcherTimer _pinFollowUpTimer;
        private readonly DispatcherTimer _taskbandStampTimer;
        private FileSystemWatcher _pinnedWatcher;
        private FileSystemWatcher _implicitPinnedWatcher;
        private long _lastTaskbandStamp;
        private volatile bool _suppressWatcherEvents;
        private bool _classRegistered;
        private bool _capturingOrder;

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
        public event Action BinClosedFromTaskbar;
        public event Action TaskbarPinStateChanged;

        public TaskbarBinHostManager(ConfigService configService, IconService iconService, Dispatcher dispatcher)
        {
            _configService = configService;
            _iconService = iconService;
            _dispatcher = dispatcher;
            _wndProcDelegate = WndProc;

            _orderPollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _orderPollTimer.Tick += (_, __) => CaptureTaskbarOrderAsync();
            _orderPollTimer.Start();

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
                Interval = TimeSpan.FromMilliseconds(650)
            };
            _taskbandStampTimer.Tick += (_, __) =>
            {
                long currentStamp = ShortcutService.GetTaskbandPinStamp();
                if (currentStamp != _lastTaskbandStamp)
                {
                    _lastTaskbandStamp = currentStamp;
                    SchedulePinnedSync();
                }
            };
            _taskbandStampTimer.Start();

            InitializePinnedFolderWatcher();
        }

        private void SchedulePinnedSync()
        {
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
                }
            }

            if (configDirty)
            {
                _configService.Save(notify: false);
            }

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

        private void CaptureTaskbarOrderAsync()
        {
            if (_capturingOrder || _byBinId.Count < 2)
                return;

            var activeBinIds = _byBinId.Keys.ToList();
            _capturingOrder = true;

            Task.Run(() =>
            {
                try
                {
                    var orderedIds = ReadBinOrderFromTaskbarUiAutomation(activeBinIds);
                    if (orderedIds != null && orderedIds.Count > 1)
                    {
                        _dispatcher.BeginInvoke(new Action(() =>
                        {
                            ApplyCapturedOrder(orderedIds);
                        }));
                    }
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
            if (_byBinId.Count < 2)
                return;

            try
            {
                var orderedIds = ReadBinOrderFromTaskbarUiAutomation(_byBinId.Keys.ToList());
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

        private static List<string> ReadBinOrderFromTaskbarUiAutomation(List<string> activeBinIds)
        {
            if (activeBinIds == null || activeBinIds.Count < 2)
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

        public void Dispose()
        {
            _orderPollTimer.Stop();
            _pinSyncDebounceTimer.Stop();
            _pinFollowUpTimer.Stop();
            _taskbandStampTimer.Stop();

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
