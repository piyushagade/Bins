using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Views;

namespace WinBins.Services
{
    /// <summary>
    /// Renders native Windows 11 style click-through multi-dot / active-pill indicators along the bottom edge
    /// of taskbar Bin buttons so users can see at a glance how many apps in a Bin are open and whether one is focused.
    /// Uses pure Win32 UpdateLayeredWindow (WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE) in physical screen pixels.
    /// </summary>
    public sealed class TaskbarIndicatorOverlayManager : IDisposable
    {
        private const string OverlayClassName = "WinBins_TaskbarIndicator_WndClass";

        private sealed class IndicatorSlotWindow
        {
            public string BinId;
            public IntPtr Hwnd;
            public string LastSignature;
            public bool IsShown;
        }

        private readonly ConfigService _configService;
        private readonly TaskbarBinHostManager _taskbarManager;
        private readonly RunningAppsTaskbarManager _runningAppsManager;
        private readonly Dispatcher _dispatcher;
        private readonly Win32Interop.WndProcDelegate _wndProcDelegate;
        private readonly Dictionary<string, IndicatorSlotWindow> _overlays =
            new Dictionary<string, IndicatorSlotWindow>(StringComparer.OrdinalIgnoreCase);

        private readonly DispatcherTimer _fastWatchTimer;
        private readonly DispatcherTimer _settleBurstTimer;
        private int _settleBurstStepsRemaining;
        private bool _classRegistered;
        private int _lastRectRequestTick;
        private bool _disposed;

        public TaskbarIndicatorOverlayManager(
            ConfigService configService,
            TaskbarBinHostManager taskbarManager,
            RunningAppsTaskbarManager runningAppsManager,
            Dispatcher dispatcher)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _taskbarManager = taskbarManager ?? throw new ArgumentNullException(nameof(taskbarManager));
            _runningAppsManager = runningAppsManager ?? throw new ArgumentNullException(nameof(runningAppsManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _wndProcDelegate = OverlayWndProc;

            _runningAppsManager.RunningStateChanged += OnStateChanged;
            _taskbarManager.TaskbarButtonRectsUpdated += SyncIndicators;
            _taskbarManager.TaskbarPinStateChanged += OnStateChanged;
            FluentTheme.ThemeChanged += SyncIndicators;

            // Fast post-animation settle burst timer (~130ms intervals across ~520ms) so centered taskbar slide animations
            // snap indicators right under their Bins as soon as icons settle.
            _settleBurstTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(130)
            };
            _settleBurstTimer.Tick += (_, __) =>
            {
                if (_disposed || _settleBurstStepsRemaining <= 0 || _overlays.Count == 0)
                {
                    _settleBurstStepsRemaining = 0;
                    _settleBurstTimer.Stop();
                    return;
                }

                _settleBurstStepsRemaining--;
                _taskbarManager.RefreshCachedButtonRectsFastAsync(_overlays.Keys.ToList());
                if (_settleBurstStepsRemaining <= 0)
                {
                    _settleBurstTimer.Stop();
                }
            };

            // Continuous lightweight watch when indicators are visible (~550ms cached-element BoundingRectangle check)
            _fastWatchTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(550)
            };
            _fastWatchTimer.Tick += (_, __) =>
            {
                if (_overlays.Count > 0)
                {
                    _taskbarManager.RefreshCachedButtonRectsFastAsync(_overlays.Keys.ToList());
                }
                SyncIndicators();
            };
            _fastWatchTimer.Start();
        }

        public void NotifyTaskbarLayoutShifted()
        {
            if (_disposed)
                return;

            _settleBurstStepsRemaining = 4;
            if (!_settleBurstTimer.IsEnabled)
            {
                _settleBurstTimer.Start();
            }
        }

        private void OnStateChanged()
        {
            if (_disposed)
                return;

            SyncIndicators();

            if (_overlays.Count > 0)
            {
                NotifyTaskbarLayoutShifted();
            }
            else
            {
                int now = Environment.TickCount;
                if (Math.Abs(now - _lastRectRequestTick) > 2000)
                {
                    _lastRectRequestTick = now;
                    _taskbarManager.RequestButtonRectRefresh();
                }
            }
        }

        private void EnsureWindowClass()
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
                lpszClassName = OverlayClassName,
                hIconSm = IntPtr.Zero
            };

            Win32Interop.RegisterClassExW(ref wc);
            _classRegistered = true;
        }

        private static IntPtr OverlayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return Win32Interop.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        public void SyncIndicators()
        {
            if (_disposed)
                return;

            try
            {
                IntPtr trayHwnd = Win32Interop.FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
                if (trayHwnd == IntPtr.Zero || Win32Interop.IsTaskbarHiddenOrCoveredByFullscreen(trayHwnd))
                {
                    HideAllOverlays();
                    return;
                }

                var bins = _configService.Current?.Bins;
                if (bins == null || bins.Count == 0)
                {
                    DestroyAllOverlays();
                    return;
                }

                var eligibleBins = new List<BinGroup>();
                foreach (BinGroup bin in bins)
                {
                    if (bin == null || string.IsNullOrWhiteSpace(bin.Id))
                        continue;

                    bool isPinned = ShortcutService.IsBinPinnedOnTaskbar(bin);
                    bool onTaskbar = bin.IsEnabledOnTaskbar || isPinned;
                    if (!onTaskbar)
                        continue;

                    // Primarily needed when pinned (no HWND pill) or when Keep open apps inside Bin is enabled
                    if (!isPinned && !bin.HideRunningAppsOnTaskbar)
                        continue;

                    eligibleBins.Add(bin);
                }

                if (eligibleBins.Count == 0)
                {
                    DestroyAllOverlays();
                    return;
                }

                var runningByBin = _runningAppsManager.GetRunningAppsForBins(eligibleBins);
                var activeIdsWithOpenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool needRectRefresh = false;

                foreach (BinGroup bin in eligibleBins)
                {
                    if (!runningByBin.TryGetValue(bin.Id, out List<BinRunningAppSummary> running) || running.Count == 0)
                        continue;

                    if (!_taskbarManager.TryGetBinTaskbarRect(bin.Id, out System.Windows.Rect btnRect) ||
                        btnRect.Width < 16 || btnRect.Height < 16)
                    {
                        needRectRefresh = true;
                        continue;
                    }

                    activeIdsWithOpenApps.Add(bin.Id);
                    UpdateBinOverlay(bin, running, btnRect);
                }

                if (needRectRefresh)
                {
                    int now = Environment.TickCount;
                    if (Math.Abs(now - _lastRectRequestTick) > 10000)
                    {
                        _lastRectRequestTick = now;
                        _taskbarManager.RequestButtonRectRefresh();
                    }
                }

                // Hide or clean up overlays for Bins that no longer have open apps
                foreach (string id in _overlays.Keys.ToList())
                {
                    if (!activeIdsWithOpenApps.Contains(id))
                    {
                        DestroyOverlay(id);
                    }
                }
            }
            catch
            {
            }
        }

        private void UpdateBinOverlay(BinGroup bin, List<BinRunningAppSummary> runningApps, System.Windows.Rect btnRect)
        {
            EnsureWindowClass();

            int count = Math.Min(4, runningApps.Count);
            var fgFlags = new bool[count];
            for (int i = 0; i < count; i++)
            {
                fgFlags[i] = runningApps[i].IsForeground;
            }
            // If a 5th+ app in the Bin is currently focused, reflect focus on the last visible indicator slot
            if (runningApps.Count > 4 && !fgFlags.Any(f => f))
            {
                for (int i = 4; i < runningApps.Count; i++)
                {
                    if (runningApps[i].IsForeground)
                    {
                        fgFlags[count - 1] = true;
                        break;
                    }
                }
            }

            bool hasUnread = bin.ShowNotificationBadges &&
                             runningApps.Any(r => RunningAppsTaskbarManager.GetUnreadCountForWindows(r.Windows) > 0);
            Color accentColor = hasUnread
                ? Color.FromArgb(255, 255, 92, 108)
                : ResolveAccentDrawingColor(bin.AccentColorHex);
            bool lightTheme = FluentTheme.IsLightThemeActive;
            double scale = Math.Max(1.0, btnRect.Height / 48.0);
            int maxStripWidth = Math.Max(20, (int)Math.Floor(btnRect.Width - 4 * scale));

            var sigSb = new StringBuilder(64);
            sigSb.Append((int)Math.Round(btnRect.Left)).Append(',')
                 .Append((int)Math.Round(btnRect.Top)).Append(',')
                 .Append((int)Math.Round(btnRect.Width)).Append(',')
                 .Append((int)Math.Round(btnRect.Height)).Append('|')
                 .Append(accentColor.ToArgb()).Append('|')
                 .Append(lightTheme ? 'L' : 'D').Append('|');
            for (int i = 0; i < count; i++)
            {
                sigSb.Append(fgFlags[i] ? 'F' : 'O');
            }
            string signature = sigSb.ToString();

            if (!_overlays.TryGetValue(bin.Id, out IndicatorSlotWindow slot) || slot.Hwnd == IntPtr.Zero || !Win32Interop.IsWindow(slot.Hwnd))
            {
                uint exStyle = Win32Interop.WS_EX_LAYERED |
                               Win32Interop.WS_EX_TRANSPARENT |
                               Win32Interop.WS_EX_TOOLWINDOW |
                               Win32Interop.WS_EX_NOACTIVATE |
                               Win32Interop.WS_EX_TOPMOST;

                IntPtr hwnd = Win32Interop.CreateWindowExW(
                    exStyle,
                    OverlayClassName,
                    string.Empty,
                    Win32Interop.WS_POPUP,
                    0,
                    0,
                    1,
                    1,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    Win32Interop.GetModuleHandleW(null),
                    IntPtr.Zero);

                if (hwnd == IntPtr.Zero)
                    return;

                slot = new IndicatorSlotWindow
                {
                    BinId = bin.Id,
                    Hwnd = hwnd
                };
                _overlays[bin.Id] = slot;
            }

            if (!string.Equals(slot.LastSignature, signature, StringComparison.Ordinal))
            {
                using (Bitmap stripBmp = RenderIndicatorStripBitmap(fgFlags, accentColor, lightTheme, scale, maxStripWidth))
                {
                    int dstX = (int)Math.Round(btnRect.Left + (btnRect.Width - stripBmp.Width) / 2.0);
                    int dstY = (int)Math.Round(btnRect.Bottom - stripBmp.Height - Math.Max(2.0, 2.2 * scale));
                    ApplyLayeredBitmap(slot.Hwnd, stripBmp, dstX, dstY);
                }
                slot.LastSignature = signature;
            }

            if (!slot.IsShown)
            {
                Win32Interop.ShowWindow(slot.Hwnd, Win32Interop.SW_SHOWNOACTIVATE);
                slot.IsShown = true;
            }

            // Keep indicator above taskbar without activating
            Win32Interop.SetWindowPos(
                slot.Hwnd,
                Win32Interop.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                Win32Interop.SWP_NOSIZE | 0x0002 /* SWP_NOMOVE */ | Win32Interop.SWP_NOACTIVATE);
        }

        public static Bitmap RenderIndicatorStripBitmap(
            IList<bool> foregroundFlags,
            Color accentColor,
            bool isLightTheme,
            double scale = 1.0,
            int maxWidthPx = 44)
        {
            int count = foregroundFlags == null ? 0 : Math.Min(4, foregroundFlags.Count);
            if (count <= 0)
            {
                return new Bitmap(4, 4, PixelFormat.Format32bppArgb);
            }

            float pillH = Math.Max(3.0f, (float)(3.4 * scale));
            float focusedW = (float)((count >= 4 ? 11.0 : (count == 3 ? 12.5 : 15.0)) * scale);
            float openW = (float)((count >= 4 ? 4.4 : 5.2) * scale);
            float gap = (float)((count >= 4 ? 2.6 : 3.2) * scale);
            float padX = (float)Math.Ceiling(2.0 * scale);
            float padY = (float)Math.Ceiling(1.6 * scale);

            float rawTotalW = padX * 2 + (count - 1) * gap;
            for (int i = 0; i < count; i++)
            {
                rawTotalW += foregroundFlags[i] ? focusedW : openW;
            }

            if (rawTotalW > maxWidthPx && rawTotalW > 1)
            {
                float shrink = maxWidthPx / rawTotalW;
                focusedW *= shrink;
                openW *= shrink;
                gap *= shrink;
                rawTotalW = maxWidthPx;
            }

            int bmpW = Math.Max(8, (int)Math.Ceiling(rawTotalW));
            int bmpH = Math.Max(6, (int)Math.Ceiling(pillH + padY * 2));

            Color inactiveFill = isLightTheme
                ? Color.FromArgb(220, 75, 85, 99)
                : Color.FromArgb(225, 226, 232, 240);
            Color activeFill = Color.FromArgb(255, accentColor.R, accentColor.G, accentColor.B);
            Color haloColor = isLightTheme
                ? Color.FromArgb(55, 255, 255, 255)
                : Color.FromArgb(115, 0, 0, 0);

            var bmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float contentW = (count - 1) * gap;
                for (int i = 0; i < count; i++)
                {
                    contentW += foregroundFlags[i] ? focusedW : openW;
                }

                float curX = (bmpW - contentW) / 2.0f;
                float topY = (bmpH - pillH) / 2.0f;

                using (var haloPen = new Pen(haloColor, Math.Max(1.0f, (float)(0.9 * scale))))
                using (var activeBrush = new SolidBrush(activeFill))
                using (var inactiveBrush = new SolidBrush(inactiveFill))
                {
                    for (int i = 0; i < count; i++)
                    {
                        bool isFg = foregroundFlags[i];
                        float w = isFg ? focusedW : openW;
                        using (GraphicsPath pillPath = CreateFloatCapsulePath(curX, topY, w, pillH))
                        {
                            g.DrawPath(haloPen, pillPath);
                            g.FillPath(isFg ? activeBrush : inactiveBrush, pillPath);
                        }
                        curX += w + gap;
                    }
                }
            }

            return bmp;
        }

        private static GraphicsPath CreateFloatCapsulePath(float x, float y, float w, float h)
        {
            var path = new GraphicsPath();
            float d = Math.Min(w, h);
            if (w <= h + 0.2f)
            {
                path.AddEllipse(x, y, w, h);
                return path;
            }

            path.AddArc(x, y, d, h, 90, 180);
            path.AddArc(x + w - d, y, d, h, 270, 180);
            path.CloseFigure();
            return path;
        }

        private static Color ResolveAccentDrawingColor(string accentHex)
        {
            Color? custom = IconService.TryParseDrawingColor(accentHex);
            if (custom.HasValue)
                return custom.Value;

            System.Windows.Media.Color wpfAccent = FluentTheme.AccentBrush.Color;
            return Color.FromArgb(255, wpfAccent.R, wpfAccent.G, wpfAccent.B);
        }

        private static void ApplyLayeredBitmap(IntPtr hwnd, Bitmap bitmap, int screenX, int screenY)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = Win32Interop.CreateCompatibleDC(IntPtr.Zero);
            IntPtr hBitmap = Win32Interop.CreateDwmArgbHBitmap(bitmap);
            IntPtr oldBmp = IntPtr.Zero;

            try
            {
                if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero)
                    return;

                oldBmp = Win32Interop.SelectObject(memDc, hBitmap);
                var dstPt = new Win32Interop.POINT { X = screenX, Y = screenY };
                var size = new Win32Interop.SIZE(bitmap.Width, bitmap.Height);
                var srcPt = new Win32Interop.POINT { X = 0, Y = 0 };
                var blend = new Win32Interop.BLENDFUNCTION
                {
                    BlendOp = Win32Interop.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Win32Interop.AC_SRC_ALPHA
                };

                Win32Interop.UpdateLayeredWindow(
                    hwnd,
                    screenDc,
                    ref dstPt,
                    ref size,
                    memDc,
                    ref srcPt,
                    0,
                    ref blend,
                    Win32Interop.ULW_ALPHA);
            }
            finally
            {
                if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero)
                    Win32Interop.SelectObject(memDc, oldBmp);
                if (hBitmap != IntPtr.Zero)
                    Win32Interop.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero)
                    Win32Interop.DeleteDC(memDc);
            }
        }

        private void HideAllOverlays()
        {
            foreach (IndicatorSlotWindow slot in _overlays.Values)
            {
                if (slot.IsShown && slot.Hwnd != IntPtr.Zero && Win32Interop.IsWindow(slot.Hwnd))
                {
                    Win32Interop.ShowWindow(slot.Hwnd, Win32Interop.SW_HIDE);
                    slot.IsShown = false;
                }
            }
        }

        private void DestroyOverlay(string binId)
        {
            if (!_overlays.TryGetValue(binId, out IndicatorSlotWindow slot))
                return;

            _overlays.Remove(binId);
            if (slot.Hwnd != IntPtr.Zero && Win32Interop.IsWindow(slot.Hwnd))
            {
                Win32Interop.DestroyWindow(slot.Hwnd);
            }
        }

        private void DestroyAllOverlays()
        {
            foreach (string id in _overlays.Keys.ToList())
            {
                DestroyOverlay(id);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try { _fastWatchTimer?.Stop(); } catch { }
            try { _settleBurstTimer?.Stop(); } catch { }
            _runningAppsManager.RunningStateChanged -= OnStateChanged;
            _taskbarManager.TaskbarButtonRectsUpdated -= SyncIndicators;
            _taskbarManager.TaskbarPinStateChanged -= OnStateChanged;
            FluentTheme.ThemeChanged -= SyncIndicators;
            DestroyAllOverlays();
        }
    }
}
