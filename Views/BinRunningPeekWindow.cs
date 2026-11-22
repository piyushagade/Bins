using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;

namespace WinBins.Views
{
    /// <summary>
    /// Compact Windows 11 Acrylic hover peek strip above a taskbar Bin showing only its currently open windows,
    /// with 1-click focus/minimize toggle and per-window graceful close (×) without stealing active focus on hover.
    /// </summary>
    public sealed class BinRunningPeekWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly RunningAppsTaskbarManager _runningAppsManager;
        private readonly TaskbarBinHostManager _taskbarManager;

        private readonly Border _outerBorder;
        private readonly Grid _animRoot;
        private readonly TranslateTransform _slideTransform;
        private readonly Border _accentDot;
        private readonly TextBlock _headerTitle;
        private readonly Border _countBadge;
        private readonly TextBlock _countBadgeText;
        private readonly StackPanel _rowsPanel;
        private readonly ScrollViewer _rowsScroll;

        private readonly DispatcherTimer _dismissTimer;
        private BinGroup _currentBin;
        private System.Drawing.Point _lastAnchorPx;
        private IntPtr _priorForegroundHwnd;
        private int _outsideStartTick;

        public BinRunningPeekWindow(
            ConfigService configService,
            IconService iconService,
            RunningAppsTaskbarManager runningAppsManager,
            TaskbarBinHostManager taskbarManager)
        {
            _configService = configService;
            _iconService = iconService;
            _runningAppsManager = runningAppsManager;
            _taskbarManager = taskbarManager;

            Title = "Bin Open Windows";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0)
            });

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    long exStyle = Win32Interop.GetWindowLongPtrW(hwnd, Win32Interop.GWL_EXSTYLE).ToInt64();
                    exStyle |= Win32Interop.WS_EX_TOOLWINDOW | Win32Interop.WS_EX_NOACTIVATE;
                    Win32Interop.SetWindowLongPtrW(hwnd, Win32Interop.GWL_EXSTYLE, new IntPtr(exStyle));

                    ApplyDwmStyling(hwnd);
                    int one = 1;
                    Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_DISALLOW_PEEK, ref one, sizeof(int));
                    Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_EXCLUDED_FROM_PEEK, ref one, sizeof(int));
                }
            };

            FluentTheme.ThemeChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ApplyDwmStyling(hwnd);
                    }
                    if (IsVisible && _currentBin != null)
                    {
                        RebuildRows();
                    }
                }));
            };

            _dismissTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(55)
            };
            _dismissTimer.Tick += DismissTimer_Tick;

            _slideTransform = new TranslateTransform(0, 0);
            _animRoot = new Grid { RenderTransform = _slideTransform };

            _outerBorder = new Border
            {
                Background = FluentTheme.PopupBg,
                BorderBrush = FluentTheme.SubtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 7, 8, 7),
                MinWidth = 216,
                MaxWidth = 310
            };

            var rootStack = new StackPanel();

            // Header bar: [Accent Dot] Open in <Bin>        [N]
            var headerDock = new DockPanel
            {
                Margin = new Thickness(4, 0, 4, 5),
                LastChildFill = true
            };

            _countBadgeText = new TextBlock
            {
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _countBadge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = _countBadgeText
            };
            DockPanel.SetDock(_countBadge, Dock.Right);
            headerDock.Children.Add(_countBadge);

            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _accentDot = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            titleRow.Children.Add(_accentDot);

            _headerTitle = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 215,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titleRow.Children.Add(_headerTitle);
            headerDock.Children.Add(titleRow);

            rootStack.Children.Add(headerDock);

            _rowsPanel = new StackPanel();
            _rowsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 260,
                Content = _rowsPanel
            };
            rootStack.Children.Add(_rowsScroll);

            _animRoot.Children.Add(rootStack);
            _outerBorder.Child = _animRoot;
            Content = _outerBorder;
        }

        private void ApplyDwmStyling(IntPtr hwnd)
        {
            bool useAcrylic = _configService.Current?.UseAcrylicFlyout ?? true;
            Win32Interop.ApplyWindows11WindowStyling(
                hwnd,
                useMica: false,
                useAcrylic: useAcrylic,
                forceDarkMode: !FluentTheme.IsLightThemeActive);
        }

        public bool TryShowForBin(BinGroup bin, System.Drawing.Point anchorScreenPx)
        {
            if (bin == null || _runningAppsManager == null)
                return false;

            List<BinRunningAppSummary> running = _runningAppsManager.GetRunningAppsForBin(bin);
            if (running.Count == 0)
            {
                if (IsVisible && string.Equals(_currentBin?.Id, bin.Id, StringComparison.OrdinalIgnoreCase))
                {
                    HidePeek();
                }
                return false;
            }

            if (IsVisible && string.Equals(_currentBin?.Id, bin.Id, StringComparison.OrdinalIgnoreCase))
            {
                _outsideStartTick = 0;
                return true;
            }

            IntPtr fg = Win32Interop.GetForegroundWindow();
            IntPtr ownHwnd = new WindowInteropHelper(this).Handle;
            if (fg != IntPtr.Zero && fg != ownHwnd)
            {
                _priorForegroundHwnd = fg;
            }

            _currentBin = bin;
            _lastAnchorPx = anchorScreenPx;
            _outsideStartTick = 0;

            Win32Interop.DismissTaskbarThumbnailWindows(anchorScreenPx, cancelTrayHoverTooltip: true);
            RebuildRows(running);

            if (!IsVisible)
            {
                Opacity = 0;
                Show();
            }

            UpdateLayout();
            PositionAboveAnchor(anchorScreenPx);
            Opacity = 1;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyDwmStyling(hwnd);
            }

            _dismissTimer.Start();
            PlayEntranceAnimation();
            return true;
        }

        public void HidePeek()
        {
            _dismissTimer.Stop();
            _outsideStartTick = 0;
            if (IsVisible)
            {
                Hide();
            }
        }

        private bool RebuildRows(List<BinRunningAppSummary> preloaded = null)
        {
            _rowsPanel.Children.Clear();
            if (_currentBin == null)
                return false;

            List<BinRunningAppSummary> running = preloaded ?? _runningAppsManager.GetRunningAppsForBin(_currentBin);
            int totalWindows = running.Sum(r => r.Windows?.Count ?? 0);
            if (totalWindows == 0)
                return false;

            Brush binAccent = FluentTheme.GetEffectiveAccentBrush(_currentBin.AccentColorHex);
            _accentDot.Background = binAccent;
            _headerTitle.Text = $"Open in {_currentBin.Name}";
            _countBadge.Background = FluentTheme.GetTintedPillBgBrush(_currentBin.AccentColorHex, 42);
            _countBadge.BorderBrush = FluentTheme.GetTintedPillBgBrush(_currentBin.AccentColorHex, 95);
            _countBadge.BorderThickness = new Thickness(1);
            _countBadgeText.Text = totalWindows.ToString();

            foreach (BinRunningAppSummary appGroup in running)
            {
                ImageSource iconSrc = _iconService.GetItemImageSource(appGroup.Item);
                foreach (RunningWindowEntry win in appGroup.Windows)
                {
                    _rowsPanel.Children.Add(CreateWindowRow(appGroup.Item, win, iconSrc, binAccent));
                }
            }

            _rowsPanel.InvalidateMeasure();
            _rowsScroll.InvalidateMeasure();
            return true;
        }

        private Border CreateWindowRow(BinItem item, RunningWindowEntry win, ImageSource iconSrc, Brush binAccent)
        {
            bool isFocused = (win.Hwnd == _priorForegroundHwnd || win.IsForeground) && !win.IsMinimized;

            Brush idleBg = isFocused
                ? FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, 28)
                : Brushes.Transparent;
            Brush idleBorder = isFocused
                ? FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, 85)
                : Brushes.Transparent;

            var row = new Border
            {
                Background = idleBg,
                BorderBrush = idleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 6, 6),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                ToolTip = isFocused ? $"{win.Title} (Click to minimize)" : win.Title
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left active bar + 20x20 app icon
            var leftWrap = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            };

            leftWrap.Children.Add(new Border
            {
                Width = 3,
                Height = isFocused ? 14 : 7,
                CornerRadius = new CornerRadius(1.5),
                Background = isFocused ? binAccent : FluentTheme.TextMuted,
                Opacity = isFocused ? 1.0 : 0.65,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var iconImg = new Image
            {
                Width = 20,
                Height = 20,
                Source = iconSrc,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.HighQuality);
            leftWrap.Children.Add(iconImg);
            Grid.SetColumn(leftWrap, 0);
            grid.Children.Add(leftWrap);

            // Center title + optional app name / minimized subtitle
            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            string primaryTitle = string.IsNullOrWhiteSpace(win.Title) ? item.Name : win.Title;
            textStack.Children.Add(new TextBlock
            {
                Text = primaryTitle,
                FontSize = 12,
                FontWeight = isFocused ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = FluentTheme.TextPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 215
            });

            bool showSubtitle = win.IsMinimized ||
                                (!string.IsNullOrWhiteSpace(item.Name) &&
                                 !string.Equals(primaryTitle, item.Name, StringComparison.OrdinalIgnoreCase));
            if (showSubtitle)
            {
                string sub = win.IsMinimized ? $"{item.Name} • Minimized" : item.Name;
                textStack.Children.Add(new TextBlock
                {
                    Text = sub,
                    FontSize = 10.5,
                    Foreground = FluentTheme.TextMuted,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 215,
                    Margin = new Thickness(0, 0.5, 0, 0)
                });
            }

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // Right graceful close (×) button
            var closeIcon = new TextBlock
            {
                Text = "\uE711",
                FontFamily = FluentTheme.IconFont,
                FontSize = 9.5,
                Foreground = FluentTheme.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var closeBtn = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Opacity = 0.65,
                ToolTip = $"Close {primaryTitle}",
                Child = closeIcon
            };

            closeBtn.MouseEnter += (_, __) =>
            {
                closeBtn.Opacity = 1.0;
                closeBtn.Background = FluentTheme.SubtleHoverBg;
                closeIcon.Foreground = FluentTheme.DangerBrush;
            };
            closeBtn.MouseLeave += (_, __) =>
            {
                closeBtn.Opacity = 0.65;
                closeBtn.Background = Brushes.Transparent;
                closeIcon.Foreground = FluentTheme.TextMuted;
            };
            closeBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            closeBtn.MouseLeftButtonUp += (s, e) =>
            {
                CloseWindowAndRefresh(win.Hwnd);
                e.Handled = true;
            };

            Grid.SetColumn(closeBtn, 2);
            grid.Children.Add(closeBtn);

            row.Child = grid;

            row.MouseEnter += (_, __) =>
            {
                row.Background = FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, 44);
                row.BorderBrush = FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, 105);
                closeBtn.Opacity = 1.0;
            };
            row.MouseLeave += (_, __) =>
            {
                row.Background = idleBg;
                row.BorderBrush = idleBorder;
                closeBtn.Opacity = 0.65;
            };

            row.MouseLeftButtonUp += (s, e) =>
            {
                HidePeek();
                if (isFocused && Win32Interop.IsWindow(win.Hwnd) && !Win32Interop.IsIconic(win.Hwnd))
                {
                    Win32Interop.MinimizeWindow(win.Hwnd);
                }
                else
                {
                    Win32Interop.ActivateAndRestoreWindow(win.Hwnd);
                }
                e.Handled = true;
            };

            row.MouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    CloseWindowAndRefresh(win.Hwnd);
                    e.Handled = true;
                }
            };

            return row;
        }

        private void CloseWindowAndRefresh(IntPtr targetHwnd)
        {
            Win32Interop.CloseWindowGracefully(targetHwnd);
            var refreshDelay = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            refreshDelay.Tick += (_, __) =>
            {
                refreshDelay.Stop();
                _runningAppsManager?.SyncRunningWindows();
                if (!IsVisible || _currentBin == null)
                    return;

                if (!RebuildRows())
                {
                    HidePeek();
                }
                else
                {
                    UpdateLayout();
                    PositionAboveAnchor(_lastAnchorPx);
                }
            };
            refreshDelay.Start();
        }

        private void DismissTimer_Tick(object sender, EventArgs e)
        {
            if (!IsVisible || _currentBin == null)
            {
                _dismissTimer.Stop();
                return;
            }

            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT pt))
                return;

            bool insideSafeZone = false;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT peekRect))
            {
                const int pad = 16;
                if (pt.X >= peekRect.Left - pad && pt.X <= peekRect.Right + pad &&
                    pt.Y >= peekRect.Top - pad && pt.Y <= peekRect.Bottom + 24)
                {
                    insideSafeZone = true;
                }
            }

            bool insideButton = false;
            if (_taskbarManager != null &&
                _taskbarManager.TryGetBinTaskbarRect(_currentBin.Id, out Rect btnRect))
            {
                const double btnPad = 14;
                if (pt.X >= btnRect.Left - btnPad && pt.X <= btnRect.Right + btnPad &&
                    pt.Y >= btnRect.Top - 24 && pt.Y <= btnRect.Bottom + btnPad)
                {
                    insideSafeZone = true;
                    insideButton = true;
                }
            }

            if (insideButton)
            {
                // Suppress any delayed Windows 11 taskbar button title tooltip while hovering the Bin button
                if (Win32Interop.DismissTaskbarThumbnailWindows(_lastAnchorPx, cancelTrayHoverTooltip: false))
                {
                    Win32Interop.DismissTaskbarThumbnailWindows(_lastAnchorPx, cancelTrayHoverTooltip: true);
                }
            }

            if (insideSafeZone)
            {
                _outsideStartTick = 0;
            }
            else
            {
                if (_outsideStartTick == 0)
                {
                    _outsideStartTick = Environment.TickCount;
                }
                else if (Math.Abs(Environment.TickCount - _outsideStartTick) >= 240)
                {
                    HidePeek();
                }
            }
        }

        private void PositionAboveAnchor(System.Drawing.Point anchorPx)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            uint dpi = hwnd != IntPtr.Zero ? Win32Interop.GetDpiForWindow(hwnd) : 96;
            if (dpi == 0) dpi = 96;
            double scale = dpi / 96.0;

            int popupW = Math.Max(160, (int)Math.Ceiling(ActualWidth * scale));
            int popupH = Math.Max(44, (int)Math.Ceiling(ActualHeight * scale));

            var screen = System.Windows.Forms.Screen.FromPoint(anchorPx);
            System.Drawing.Rectangle work = screen.WorkingArea;
            System.Drawing.Rectangle bounds = screen.Bounds;

            const int margin = 12;
            int targetX;
            int targetY;

            bool taskbarTop = work.Top > bounds.Top + 4;
            bool taskbarLeft = work.Left > bounds.Left + 4;
            bool taskbarRight = work.Right < bounds.Right - 4;

            if (taskbarTop)
            {
                targetX = anchorPx.X - popupW / 2;
                targetY = work.Top + margin;
            }
            else if (taskbarLeft)
            {
                targetX = work.Left + margin;
                targetY = anchorPx.Y - popupH / 2;
            }
            else if (taskbarRight)
            {
                targetX = work.Right - popupW - margin;
                targetY = anchorPx.Y - popupH / 2;
            }
            else
            {
                targetX = anchorPx.X - popupW / 2;
                targetY = work.Bottom - popupH - 10;
            }

            if (targetX < work.Left + margin)
                targetX = work.Left + margin;
            if (targetX + popupW > work.Right - margin)
                targetX = Math.Max(work.Left + margin, work.Right - popupW - margin);

            if (targetY < work.Top + margin)
                targetY = work.Top + margin;
            if (targetY + popupH > work.Bottom - 10)
                targetY = Math.Max(work.Top + margin, work.Bottom - popupH - 10);

            if (hwnd != IntPtr.Zero)
            {
                Win32Interop.SetWindowPos(
                    hwnd,
                    Win32Interop.HWND_TOPMOST,
                    targetX,
                    targetY,
                    0,
                    0,
                    Win32Interop.SWP_NOSIZE | Win32Interop.SWP_SHOWWINDOW | Win32Interop.SWP_NOACTIVATE);
            }
            else
            {
                Left = targetX / scale;
                Top = targetY / scale;
            }
        }

        private void PlayEntranceAnimation()
        {
            var slideAnim = new DoubleAnimation
            {
                From = 8,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(115),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _slideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);

            var fadeAnim = new DoubleAnimation
            {
                From = 0.25,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(95)
            };
            _animRoot.BeginAnimation(OpacityProperty, fadeAnim);
        }
    }
}
