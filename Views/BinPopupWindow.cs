using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;

namespace WinBins.Views
{
    public class BinPopupWindow : Window
    {
        public const string InternalTileDragFormat = "WinBins.PopupTileIndex";

        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly RunningAppsTaskbarManager _runningAppsManager;
        private readonly TaskbarBinHostManager _taskbarManager;
        private readonly Action<BinGroup> _onCustomizeRequested;
        private readonly Action<BinGroup> _onBinModified;

        private readonly Border _outerBorder;
        private readonly Grid _animRoot;
        private readonly TranslateTransform _slideTransform;
        private readonly DockPanel _headerDock;
        private readonly StackPanel _titleGroup;
        private readonly Image _flyoutHeaderIcon;
        private readonly TextBlock _headerTitle;
        private readonly Button _launchAllBtn;
        private readonly Button _addBtn;
        private readonly Button _editBtn;
        private readonly Button _closeBtn;

        private readonly Border _filterBar;
        private readonly TextBlock _filterTextBlock;
        private readonly ScrollViewer _sectionTabsScroll;
        private readonly StackPanel _sectionTabsPanel;
        private readonly ScrollViewer _gridScroll;
        private readonly StackPanel _contentHostStack;
        private readonly UniformGrid _itemsGrid;
        private readonly Border _emptyStatePanel;
        private readonly TextBlock _emptyTitleText;
        private readonly TextBlock _emptySubText;
        private readonly Border _dropHintBanner;
        private readonly TextBlock _dropHintText;

        private readonly DispatcherTimer _hoverDismissTimer;
        private bool _openedByHover;
        private int _hoverOutsideStartTick;

        private readonly DispatcherTimer _topmostReassertTimer;
        private int _topmostReassertTicksRemaining;
        private bool _isPinnedOpen;
        private bool _launchedWhileOpen;

        private readonly Stack<BinGroup> _subBinStack = new Stack<BinGroup>();
        private Button _backBtn;
        private System.Drawing.Point _lastAnchorScreenPx;

        private BinGroup _currentBin;
        private IntPtr _priorForegroundHwnd;
        private bool _suppressDeactivateClose;
        private int _activeContextMenuCount;

        private string _filterText = string.Empty;
        private string _activeSectionTab = null; // null = All
        private readonly List<string> _availableTabs = new List<string>();
        private int _selectedTileIndex = -1;
        private readonly Action _themeChangedHandler;
        private readonly List<TileEntry> _displayedTiles = new List<TileEntry>();

        // Toggle-guard state so clicking the same taskbar icon while open closes cleanly instead of reopening
        private string _lastDeactivatedBinId;
        private long _lastDeactivatedTick;
        private System.Drawing.Point _lastDeactivatedCursor;

        private class TileEntry
        {
            public BinItem Item;
            public int OriginalIndex;
            public Border TileBorder;
            public Brush IdleBackground;
            public Brush IdleBorderBrush;
        }

        public string CurrentBinId => _currentBin?.Id;

        public bool IsBusyForHoverSwitch =>
            _suppressDeactivateClose ||
            _activeContextMenuCount > 0 ||
            _isPinnedOpen ||
            !string.IsNullOrEmpty(_filterText);

        /// <summary>
        /// Smoothly switches an already-open flyout to another Bin when the user glides over another
        /// Bin's taskbar icon, preserving whether the flyout was click-opened or hover-opened.
        /// </summary>
        public bool SwitchToBinOnHover(BinGroup bin, System.Drawing.Point anchorScreenPx)
        {
            if (!IsVisible || bin == null || IsBusyForHoverSwitch)
                return false;

            if (string.Equals(_currentBin?.Id, bin.Id, StringComparison.OrdinalIgnoreCase))
                return false;

            ShowForBin(bin, anchorScreenPx, openedByHover: _openedByHover);
            return true;
        }

        private bool IsKeepOpenMode =>
            _isPinnedOpen || !(_configService.Current?.ClosePopupOnLaunch ?? true);

        private bool ShouldStayOpenOnDeactivate =>
            _isPinnedOpen || _launchedWhileOpen;

        public BinPopupWindow(
            ConfigService configService,
            IconService iconService,
            RunningAppsTaskbarManager runningAppsManager,
            TaskbarBinHostManager taskbarManager,
            Action<BinGroup> onCustomizeRequested,
            Action<BinGroup> onBinModified)
        {
            _configService = configService;
            _iconService = iconService;
            _runningAppsManager = runningAppsManager;
            _taskbarManager = taskbarManager;
            _onCustomizeRequested = onCustomizeRequested;
            _onBinModified = onBinModified;

            Title = "Bin";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            AllowDrop = true;

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
                ApplyPopupDwmStyling(hwnd);
                int one = 1;
                Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_DISALLOW_PEEK, ref one, sizeof(int));
                Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_EXCLUDED_FROM_PEEK, ref one, sizeof(int));
                HwndSource.FromHwnd(hwnd)?.AddHook(PopupWndProc);
            };

            _themeChangedHandler = () =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ApplyPopupDwmStyling(hwnd);
                    }
                    if (IsVisible && _currentBin != null)
                    {
                        RebuildContent();
                    }
                }));
            };
            FluentTheme.ThemeChanged += _themeChangedHandler;
            Closed += (_, __) => FluentTheme.ThemeChanged -= _themeChangedHandler;

            _hoverDismissTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _hoverDismissTimer.Tick += HoverDismissTimer_Tick;

            _topmostReassertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _topmostReassertTimer.Tick += (_, __) =>
            {
                if (!IsVisible || !ShouldStayOpenOnDeactivate)
                {
                    _topmostReassertTimer.Stop();
                    return;
                }

                ReassertTopmost();
                if (--_topmostReassertTicksRemaining <= 0)
                {
                    _topmostReassertTimer.Stop();
                }
            };

            _slideTransform = new TranslateTransform(0, 0);
            _animRoot = new Grid
            {
                RenderTransform = _slideTransform
            };

            _outerBorder = new Border
            {
                Background = FluentTheme.PopupBg,
                BorderBrush = FluentTheme.SubtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 10, 10, 10)
            };
            _outerBorder.MouseRightButtonUp += OuterBorder_MouseRightButtonUp;

            var mainStack = new StackPanel();

            // Header bar (visibility of title, add, edit, or entire header controlled per-Bin)
            _headerDock = new DockPanel
            {
                Margin = new Thickness(4, 0, 2, 6),
                LastChildFill = true,
                Background = Brushes.Transparent
            };

            var actionButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _launchAllBtn = FluentTheme.CreateIconButton("\uE768", "Launch all", size: 28, iconSize: 11.5);
            _launchAllBtn.Visibility = Visibility.Collapsed;
            _launchAllBtn.Click += (_, __) =>
            {
                BinGroup target = _currentBin;
                if (target == null)
                    return;

                bool keepOpen = IsKeepOpenMode;
                if (keepOpen)
                {
                    EnterKeepOpenAfterLaunch();
                }
                else
                {
                    HidePopup();
                }

                _ = LauncherService.LaunchBinWorkspaceAsync(target, _runningAppsManager);
                if (keepOpen)
                {
                    EnsureOnTopAfterLaunch();
                }
            };

            _addBtn = FluentTheme.CreateIconButton("\uE710", "Add item", size: 28, iconSize: 12);
            _addBtn.Margin = new Thickness(2, 0, 0, 0);
            _addBtn.Click += AddButton_Click;

            _editBtn = FluentTheme.CreateIconButton("\uE713", "Customize Bin", size: 28, iconSize: 12.5);
            _editBtn.Margin = new Thickness(2, 0, 0, 0);
            _editBtn.Click += (_, __) =>
            {
                BinGroup target = _currentBin;
                HidePopup();
                _onCustomizeRequested?.Invoke(target);
            };

            _closeBtn = FluentTheme.CreateIconButton("\uE711", "Close Bin (Esc)", size: 28, iconSize: 11);
            _closeBtn.Margin = new Thickness(2, 0, 0, 0);
            _closeBtn.Visibility = Visibility.Collapsed;
            _closeBtn.Click += (_, __) => HidePopup();

            actionButtons.Children.Add(_launchAllBtn);
            actionButtons.Children.Add(_addBtn);
            actionButtons.Children.Add(_editBtn);
            actionButtons.Children.Add(_closeBtn);
            DockPanel.SetDock(actionButtons, Dock.Right);
            _headerDock.Children.Add(actionButtons);

            _titleGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent
            };

            _backBtn = FluentTheme.CreateIconButton("\uE72B", "Back to parent Bin (Backspace)", size: 26, iconSize: 11.5);
            _backBtn.Margin = new Thickness(0, 0, 6, 0);
            _backBtn.Visibility = Visibility.Collapsed;
            _backBtn.Click += (_, __) => NavigateBackSubBin();
            _titleGroup.Children.Add(_backBtn);

            _flyoutHeaderIcon = new Image
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_flyoutHeaderIcon, BitmapScalingMode.HighQuality);
            _titleGroup.Children.Add(_flyoutHeaderIcon);

            _headerTitle = new TextBlock
            {
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 240,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _titleGroup.Children.Add(_headerTitle);

            _titleGroup.MouseRightButtonUp += (s, e) =>
            {
                OpenHeaderContextMenu(_titleGroup);
                e.Handled = true;
            };

            _headerDock.Children.Add(_titleGroup);
            mainStack.Children.Add(_headerDock);

            // Type-to-filter inline search pill (visible only when user types)
            _filterBar = new Border
            {
                Background = FluentTheme.InputBg,
                BorderBrush = FluentTheme.SubtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 6, 4),
                Margin = new Thickness(2, 0, 2, 6),
                Visibility = Visibility.Collapsed
            };
            var filterDock = new DockPanel { LastChildFill = true };
            var clearFilterBtn = FluentTheme.CreateIconButton("\uE711", "Clear filter (Esc)", size: 20, iconSize: 9.5);
            clearFilterBtn.Click += (_, __) =>
            {
                _filterText = string.Empty;
                _selectedTileIndex = -1;
                RebuildAndRealign();
            };
            DockPanel.SetDock(clearFilterBtn, Dock.Right);
            filterDock.Children.Add(clearFilterBtn);

            var searchIcon = new TextBlock
            {
                Text = "\uE721",
                FontFamily = FluentTheme.IconFont,
                FontSize = 11.5,
                Foreground = FluentTheme.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(searchIcon, Dock.Left);
            filterDock.Children.Add(searchIcon);

            _filterTextBlock = new TextBlock
            {
                FontSize = 12,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            filterDock.Children.Add(_filterTextBlock);
            _filterBar.Child = filterDock;
            mainStack.Children.Add(_filterBar);

            // Empty state panel
            _emptyStatePanel = new Border
            {
                Padding = new Thickness(22, 24, 22, 24),
                Visibility = Visibility.Collapsed
            };
            var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            _emptyTitleText = new TextBlock
            {
                Text = "Empty Bin",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _emptySubText = new TextBlock
            {
                Text = "Drop items here or right-click to add",
                FontSize = 12,
                Foreground = FluentTheme.TextMuted,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            emptyStack.Children.Add(_emptyTitleText);
            emptyStack.Children.Add(_emptySubText);
            _emptyStatePanel.Child = emptyStack;
            mainStack.Children.Add(_emptyStatePanel);

            // Items grid / stacked section host inside a max-height scroll viewer
            _itemsGrid = new UniformGrid
            {
                Columns = 4,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _contentHostStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _contentHostStack.Children.Add(_itemsGrid);

            _gridScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420,
                FocusVisualStyle = null,
                Content = _contentHostStack
            };
            mainStack.Children.Add(_gridScroll);

            // Named section pill tabs bar anchored at the bottom (closest to the taskbar Bin icon)
            _sectionTabsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            _sectionTabsScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                FocusVisualStyle = null,
                Margin = new Thickness(2, 8, 2, 0),
                Visibility = Visibility.Collapsed,
                Content = _sectionTabsPanel
            };
            mainStack.Children.Add(_sectionTabsScroll);

            // Non-blocking contextual drop hint banner (keeps all tiles visible so user can drop onto an App or Folder tile!)
            _dropHintBanner = new Border
            {
                Background = FluentTheme.CardSelectedBg,
                BorderBrush = FluentTheme.AccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(2, 6, 2, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _dropHintText = new TextBlock
            {
                Text = "Drop to add to Bin",
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = FluentTheme.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _dropHintBanner.Child = _dropHintText;
            mainStack.Children.Add(_dropHintBanner);

            _animRoot.Children.Add(mainStack);

            _outerBorder.Child = _animRoot;
            Content = _outerBorder;

            Deactivated += BinPopupWindow_Deactivated;
            PreviewKeyDown += BinPopupWindow_PreviewKeyDown;
            PreviewTextInput += BinPopupWindow_PreviewTextInput;
            DragOver += BinPopupWindow_DragOver;
            DragLeave += BinPopupWindow_DragLeave;
            Drop += BinPopupWindow_Drop;
        }

        private IntPtr PopupWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Interop.WM_GETMINMAXINFO && lParam != IntPtr.Zero)
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32Interop.MINMAXINFO>(lParam);
                mmi.ptMinTrackSize.X = 1;
                mmi.ptMinTrackSize.Y = 1;
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ApplyPopupDwmStyling(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;

            BinBackdropStyle backdrop = _configService.Current?.FlyoutBackdrop ?? BinBackdropStyle.Acrylic;
            int opacity = _configService.Current?.FlyoutOpacityPercent ?? 82;
            Win32Interop.ApplyFlyoutBackdropAndOpacity(
                hwnd,
                (int)backdrop,
                opacity,
                FluentTheme.IsLightThemeActive);
        }

        public void ToggleAtScreenPoint(BinGroup bin, System.Drawing.Point anchorScreenPx)
        {
            if (bin == null)
                return;

            if (IsVisible && string.Equals(_currentBin?.Id, bin.Id, StringComparison.OrdinalIgnoreCase))
            {
                HidePopup();
                return;
            }

            long now = Environment.TickCount;
            if (!IsVisible &&
                string.Equals(_lastDeactivatedBinId, bin.Id, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(now - _lastDeactivatedTick) < 750)
            {
                int dx = anchorScreenPx.X - _lastDeactivatedCursor.X;
                int dy = anchorScreenPx.Y - _lastDeactivatedCursor.Y;
                if ((dx * dx + dy * dy) <= (72 * 72))
                {
                    _lastDeactivatedBinId = null;
                    return;
                }
            }

            ShowForBin(bin, anchorScreenPx, openedByHover: false);
        }

        public void ShowForBin(BinGroup bin, System.Drawing.Point anchorScreenPx, bool openedByHover = false)
        {
            if (bin == null)
                return;

            // If already showing this exact Bin from hover, just update hover state without restarting entrance animation
            if (IsVisible && string.Equals(_currentBin?.Id, bin.Id, StringComparison.OrdinalIgnoreCase))
            {
                if (!openedByHover)
                {
                    _openedByHover = false;
                    _hoverDismissTimer.Stop();
                }
                return;
            }

            IntPtr currentFg = Win32Interop.GetForegroundWindow();
            IntPtr ownHwnd = new WindowInteropHelper(this).Handle;
            if (currentFg != IntPtr.Zero && currentFg != ownHwnd)
            {
                _priorForegroundHwnd = currentFg;
            }

            Win32Interop.DismissTaskbarThumbnailWindows(anchorScreenPx, cancelTrayHoverTooltip: true);

            if (_taskbarManager != null &&
                _taskbarManager.TryGetBinTaskbarAnchorPoint(bin.Id, out System.Drawing.Point taskbarCenter))
            {
                var anchorScreen = System.Windows.Forms.Screen.FromPoint(anchorScreenPx);
                var taskbarScreen = System.Windows.Forms.Screen.FromPoint(taskbarCenter);
                if (string.Equals(anchorScreen.DeviceName, taskbarScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    anchorScreenPx = taskbarCenter;
                }
            }

            _subBinStack.Clear();
            _lastAnchorScreenPx = anchorScreenPx;
            _currentBin = bin;
            _filterText = string.Empty;
            _activeSectionTab = null;
            _selectedTileIndex = -1;
            _openedByHover = openedByHover;
            _hoverOutsideStartTick = 0;
            _isPinnedOpen = false;
            _launchedWhileOpen = false;
            _topmostReassertTimer.Stop();

            RebuildContent();

            if (!IsVisible)
            {
                Opacity = 0;
                Show();
            }

            UpdateLayout();
            PositionAboveTaskbarAnchor(anchorScreenPx);
            Opacity = 1;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyPopupDwmStyling(hwnd);
                if (!openedByHover)
                {
                    Win32Interop.SetForegroundWindow(hwnd);
                }
            }

            if (openedByHover)
            {
                _hoverDismissTimer.Start();
            }
            else
            {
                _hoverDismissTimer.Stop();
                Activate();
                Focus();
            }

            PlayEntranceAnimation();
        }

        private void HoverDismissTimer_Tick(object sender, EventArgs e)
        {
            if (!IsVisible || !_openedByHover || _currentBin == null || ShouldStayOpenOnDeactivate)
            {
                _hoverDismissTimer.Stop();
                return;
            }

            // Never auto-dismiss while a context menu/dialog is open, user is dragging, or user started typing a filter
            bool leftDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LBUTTON) & 0x8000) != 0;
            if (_suppressDeactivateClose || _activeContextMenuCount > 0 || leftDown || !string.IsNullOrEmpty(_filterText))
            {
                _hoverOutsideStartTick = 0;
                return;
            }

            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT pt))
                return;

            bool insideSafeZone = false;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT popupRect))
            {
                const int pad = 16;
                if (pt.X >= popupRect.Left - pad && pt.X <= popupRect.Right + pad &&
                    pt.Y >= popupRect.Top - pad && pt.Y <= popupRect.Bottom + 24)
                {
                    insideSafeZone = true;
                }
            }

            bool insideButton = false;
            Rect btnRect = Rect.Empty;
            if (_taskbarManager != null &&
                _taskbarManager.TryGetBinTaskbarRect(_currentBin.Id, out btnRect))
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
                var btnAnchor = new System.Drawing.Point(
                    (int)Math.Round(btnRect.Left + btnRect.Width / 2.0),
                    (int)Math.Round(btnRect.Top + btnRect.Height / 2.0));
                if (Win32Interop.DismissTaskbarThumbnailWindows(btnAnchor, cancelTrayHoverTooltip: false))
                {
                    Win32Interop.DismissTaskbarThumbnailWindows(btnAnchor, cancelTrayHoverTooltip: true);
                }
            }

            if (insideSafeZone)
            {
                _hoverOutsideStartTick = 0;
            }
            else
            {
                if (_hoverOutsideStartTick == 0)
                {
                    _hoverOutsideStartTick = Environment.TickCount;
                }
                else if (Math.Abs(Environment.TickCount - _hoverOutsideStartTick) >= 340)
                {
                    HidePopup();
                }
            }
        }

        public void RefreshIfShowing(BinGroup bin)
        {
            if (IsVisible && bin != null && string.Equals(_currentBin?.Id, bin.Id, StringComparison.OrdinalIgnoreCase))
            {
                _currentBin = bin;
                RebuildAndRealign();
            }
        }

        private void RebuildAndRealign()
        {
            RebuildContent();
            UpdateLayout();
            if (IsVisible)
            {
                PositionAboveTaskbarAnchor(_lastAnchorScreenPx);
            }
        }

        public void HidePopup()
        {
            _hoverDismissTimer.Stop();
            _topmostReassertTimer.Stop();
            _openedByHover = false;
            _isPinnedOpen = false;
            _launchedWhileOpen = false;
            ShowDropHint(null, highlightOuterBorder: false);
            if (IsVisible)
            {
                Hide();
            }
            _itemsGrid.Children.Clear();
            _contentHostStack.Children.Clear();
            _displayedTiles.Clear();
            if (_flyoutHeaderIcon != null)
                _flyoutHeaderIcon.Source = null;
        }

        private void ShowDropHint(string message, bool highlightOuterBorder)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                _dropHintBanner.Visibility = Visibility.Collapsed;
                _outerBorder.BorderBrush = FluentTheme.SubtleBorder;
            }
            else
            {
                _dropHintText.Text = message;
                _dropHintBanner.Visibility = Visibility.Visible;
                _outerBorder.BorderBrush = highlightOuterBorder ? FluentTheme.AccentBrush : FluentTheme.SubtleBorder;
            }
        }

        private void GetDensityMetrics(
            out double iconSize,
            out double baseTileW,
            out double baseTileH,
            out double fontSize,
            out double lineHeight,
            out double labelMaxH,
            out double baseIconTopPad,
            out double baseIconLabelGap)
        {
            int prefIconSize = _configService.Current?.TileIconSize ?? 32;
            double scale = prefIconSize / 32.0;

            BinFlyoutDensity density = _currentBin?.Density ?? BinFlyoutDensity.Normal;
            switch (density)
            {
                case BinFlyoutDensity.Compact:
                    iconSize = Math.Round(28 * scale);
                    baseTileW = Math.Round(64 * Math.Max(0.9, scale));
                    baseTileH = Math.Round(74 + (iconSize - 28));
                    fontSize = 10.5;
                    lineHeight = 13.5;
                    labelMaxH = 27.0;
                    baseIconTopPad = 6.0;
                    baseIconLabelGap = 4.0;
                    break;

                case BinFlyoutDensity.Spacious:
                    iconSize = Math.Round(44 * scale);
                    baseTileW = Math.Round(94 * Math.Max(0.9, scale));
                    baseTileH = Math.Round(102 + (iconSize - 44));
                    fontSize = 12.0;
                    lineHeight = 16.0;
                    labelMaxH = 32.0;
                    baseIconTopPad = 9.0;
                    baseIconLabelGap = 6.0;
                    break;

                default:
                    iconSize = Math.Round(36 * scale);
                    baseTileW = Math.Round(78 * Math.Max(0.9, scale));
                    baseTileH = Math.Round(90 + (iconSize - 36));
                    fontSize = 11.5;
                    lineHeight = 15.5;
                    labelMaxH = 31.0;
                    baseIconTopPad = 8.0;
                    baseIconLabelGap = 5.0;
                    break;
            }
        }

        private bool AnyLabelWrapsToMultipleLines(
            IEnumerable<Tuple<BinItem, int>> items,
            double labelWidth,
            double fontSize)
        {
            if (items == null)
                return false;

            double pixelsPerDip = 1.0;
            try
            {
                pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch
            {
                pixelsPerDip = 1.0;
            }

            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            double maxSingleLineWidth = Math.Max(1.0, labelWidth - 0.5);

            foreach (var entry in items)
            {
                string text = entry?.Item1?.Name;
                if (string.IsNullOrEmpty(text))
                    continue;

                if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
                    return true;

                var ft = new FormattedText(
                    text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection,
                    typeface,
                    fontSize,
                    Brushes.White,
                    pixelsPerDip);

                if (ft.Width > maxSingleLineWidth)
                    return true;
            }

            return false;
        }

        private static int CountLaunchAllItems(BinGroup bin)
        {
            if (bin == null)
                return 0;

            int count = (bin.Items ?? new List<BinItem>())
                .Count(it => it != null && it.IncludeInOpenAll && !BinItem.IsSystemCommand(it.TargetPath));
            if (count == 0 && !string.IsNullOrWhiteSpace(bin.LiveFolderPath))
            {
                string folder = Environment.ExpandEnvironmentVariables(bin.LiveFolderPath.Trim());
                if (Directory.Exists(folder))
                    return 1;
            }
            return count;
        }

        private bool UpdateHeaderVisibility(bool repositionIfHeaderToggled = false)
        {
            if (_currentBin == null)
                return false;

            Visibility prevHeaderVis = _headerDock.Visibility;

            bool showTitle = _currentBin.ShowFlyoutTitle;
            int launchAllCount = CountLaunchAllItems(_currentBin);
            bool showLaunchAll = _currentBin.ShowFlyoutLaunchAllButton && launchAllCount > 0;
            bool showAdd = _currentBin.ShowFlyoutAddButton;
            bool showEdit = _currentBin.ShowFlyoutEditButton;
            bool showClose = ShouldStayOpenOnDeactivate;
            bool anyHeader = showTitle || showLaunchAll || showAdd || showEdit || showClose;

            _headerDock.Visibility = anyHeader ? Visibility.Visible : Visibility.Collapsed;
            _titleGroup.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
            _launchAllBtn.Visibility = showLaunchAll ? Visibility.Visible : Visibility.Collapsed;
            if (showLaunchAll)
            {
                _launchAllBtn.ToolTip = $"Launch all ({launchAllCount} {(launchAllCount == 1 ? "item" : "items")})";
            }
            _addBtn.Visibility = showAdd ? Visibility.Visible : Visibility.Collapsed;
            _editBtn.Visibility = showEdit ? Visibility.Visible : Visibility.Collapsed;
            _closeBtn.Visibility = showClose ? Visibility.Visible : Visibility.Collapsed;

            if (repositionIfHeaderToggled && prevHeaderVis != _headerDock.Visibility && IsVisible)
            {
                UpdateLayout();
                PositionAboveTaskbarAnchor(_lastAnchorScreenPx);
            }

            return anyHeader;
        }

        private void EnterKeepOpenAfterLaunch()
        {
            _launchedWhileOpen = true;
            _openedByHover = false;
            _hoverDismissTimer.Stop();
            UpdateHeaderVisibility(repositionIfHeaderToggled: true);
        }

        private void EnsureOnTopAfterLaunch()
        {
            if (!IsVisible)
                return;

            ReassertTopmost();
            _topmostReassertTicksRemaining = 6;
            _topmostReassertTimer.Stop();
            _topmostReassertTimer.Start();
        }

        private void ReassertTopmost()
        {
            if (!IsVisible)
                return;

            Topmost = true;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                Win32Interop.SetWindowPos(
                    hwnd,
                    Win32Interop.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE);
            }
        }

        private void RebuildContent()
        {
            if (_currentBin == null)
                return;

            Brush binAccent = FluentTheme.GetEffectiveAccentBrush(_currentBin.AccentColorHex);

            bool showTitle = _currentBin.ShowFlyoutTitle;
            bool anyHeader = UpdateHeaderVisibility(repositionIfHeaderToggled: false);

            if (showTitle && !string.IsNullOrWhiteSpace(_currentBin.FlyoutIconPath))
            {
                _flyoutHeaderIcon.Source = _iconService.GetPathImageSource(_currentBin.FlyoutIconPath, _currentBin.FlyoutIconIndex);
                _flyoutHeaderIcon.Visibility = Visibility.Visible;
            }
            else
            {
                _flyoutHeaderIcon.Source = null;
                _flyoutHeaderIcon.Visibility = Visibility.Collapsed;
            }

            _headerTitle.Text = _currentBin.Name;
            _headerTitle.Foreground = string.IsNullOrWhiteSpace(_currentBin.AccentColorHex)
                ? FluentTheme.TextSecondary
                : binAccent;

            bool hasFilter = !string.IsNullOrWhiteSpace(_filterText);
            _filterBar.Visibility = hasFilter ? Visibility.Visible : Visibility.Collapsed;
            if (hasFilter)
            {
                _filterTextBlock.Text = _filterText;
            }

            int pad = Math.Max(0, Math.Min(20, _currentBin.ItemPadding));
            double outerSide = Math.Max(4, 4 + pad * 0.6);

            _itemsGrid.Children.Clear();
            _contentHostStack.Children.Clear();
            _sectionTabsPanel.Children.Clear();
            _availableTabs.Clear();
            _displayedTiles.Clear();

            var combined = new List<Tuple<BinItem, int>>();
            var pinnedItems = _currentBin.Items ?? new List<BinItem>();
            for (int i = 0; i < pinnedItems.Count; i++)
            {
                if (pinnedItems[i] != null)
                {
                    combined.Add(Tuple.Create(pinnedItems[i], i));
                }
            }

            if (_backBtn != null)
            {
                _backBtn.Visibility = _subBinStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            bool hasLiveFolder = !string.IsNullOrWhiteSpace(_currentBin.LiveFolderPath) || _currentBin.LiveFolderRecentMode;
            if (hasLiveFolder)
            {
                List<BinItem> liveItems = LauncherService.GetLiveFolderItems(_currentBin);
                string liveSectionName = pinnedItems.Count > 0
                    ? (_currentBin.LiveFolderRecentMode ? "Recent Files" : "Live Folder")
                    : string.Empty;
                foreach (BinItem live in liveItems)
                {
                    live.Section = liveSectionName;
                    combined.Add(Tuple.Create(live, -1));
                }
            }

            if (combined.Count == 0)
            {
                _sectionTabsScroll.Visibility = Visibility.Collapsed;
                double emptyOuterTop = (anyHeader || hasFilter) ? 9 : outerSide;
                _outerBorder.Padding = new Thickness(outerSide, emptyOuterTop, outerSide, outerSide);
                _emptyTitleText.Text = hasLiveFolder ? "Folder is Empty" : "Empty Bin";
                _emptySubText.Text = hasLiveFolder
                    ? (_currentBin.LiveFolderRecentMode ? "Windows Recent Items" : _currentBin.LiveFolderPath)
                    : "Drop items here or right-click to add";
                _emptyStatePanel.Visibility = Visibility.Visible;
                _gridScroll.Visibility = Visibility.Collapsed;
                return;
            }

            // Discover named sections
            var sectionNames = combined
                .Select(t => (t.Item1.Section ?? string.Empty).Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool hasSections = sectionNames.Count > 0 && !hasFilter;
            bool useTabs = hasSections && _currentBin.SectionLayout == BinSectionLayout.Tabs;
            bool useStackedHeaders = hasSections && _currentBin.SectionLayout == BinSectionLayout.StackedHeaders;

            if (useTabs)
            {
                bool hasUngrouped = combined.Any(t => string.IsNullOrWhiteSpace(t.Item1.Section));
                if (hasUngrouped)
                {
                    _availableTabs.Add("__ungrouped__");
                }
                foreach (string sec in sectionNames)
                {
                    _availableTabs.Add(sec);
                }

                if (_availableTabs.Count > 0 &&
                    (_activeSectionTab == null ||
                     !_availableTabs.Any(t => string.Equals(t, _activeSectionTab, StringComparison.OrdinalIgnoreCase))))
                {
                    _activeSectionTab = _availableTabs[0];
                }

                if (_availableTabs.Count > 1)
                {
                    foreach (string tabKey in _availableTabs)
                    {
                        string capturedKey = tabKey;
                        string label = tabKey == "__ungrouped__" ? "General" : tabKey;
                        bool isActive = string.Equals(_activeSectionTab, capturedKey, StringComparison.OrdinalIgnoreCase);

                        var pill = new Border
                        {
                            CornerRadius = new CornerRadius(11),
                            Padding = new Thickness(10, 3, 10, 3),
                            Margin = new Thickness(0, 0, 5, 0),
                            Cursor = Cursors.Hand,
                            Background = isActive
                                ? FluentTheme.GetTintedPillBgBrush(_currentBin.AccentColorHex, 48)
                                : FluentTheme.CardBg,
                            BorderBrush = isActive ? binAccent : FluentTheme.CardBorder,
                            BorderThickness = new Thickness(1),
                            Child = new TextBlock
                            {
                                Text = label,
                                FontSize = 11.5,
                                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                                Foreground = isActive ? FluentTheme.TextPrimary : FluentTheme.TextSecondary
                            }
                        };
                        pill.MouseLeftButtonUp += (_, e) =>
                        {
                            _activeSectionTab = capturedKey;
                            _selectedTileIndex = -1;
                            RebuildAndRealign();
                            e.Handled = true;
                        };
                        _sectionTabsPanel.Children.Add(pill);
                    }
                    _sectionTabsScroll.Visibility = Visibility.Visible;
                }
                else
                {
                    _sectionTabsScroll.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                _sectionTabsScroll.Visibility = Visibility.Collapsed;
            }

            bool tabsVisible = _sectionTabsScroll.Visibility == Visibility.Visible;
            double outerTop = (anyHeader || hasFilter) ? 9 : outerSide;
            double outerBottom = tabsVisible ? 9 : outerSide;
            _outerBorder.Padding = new Thickness(outerSide, outerTop, outerSide, outerBottom);

            var matching = new List<Tuple<BinItem, int>>();
            foreach (var pair in combined)
            {
                BinItem it = pair.Item1;
                if (hasFilter)
                {
                    bool matchesText =
                        (!string.IsNullOrEmpty(it.Name) && it.Name.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(it.Section) && it.Section.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(it.TargetPath) && it.TargetPath.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!matchesText)
                        continue;
                }
                else if (useTabs && _activeSectionTab != null)
                {
                    string itemSec = (it.Section ?? string.Empty).Trim();
                    if (_activeSectionTab == "__ungrouped__")
                    {
                        if (!string.IsNullOrEmpty(itemSec))
                            continue;
                    }
                    else if (!string.Equals(itemSec, _activeSectionTab, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                matching.Add(pair);
            }

            if (matching.Count == 0)
            {
                _emptyTitleText.Text = "No matching items";
                _emptySubText.Text = hasFilter ? "Press Esc to clear filter" : "No items in this category";
                _emptyStatePanel.Visibility = Visibility.Visible;
                _gridScroll.Visibility = Visibility.Collapsed;
                return;
            }

            _emptyStatePanel.Visibility = Visibility.Collapsed;
            _gridScroll.Visibility = Visibility.Visible;

            // Single-pass lookup of open windows for all items in this Bin
            var runningByItem = new Dictionary<BinItem, List<RunningWindowEntry>>();
            if (_runningAppsManager != null)
            {
                foreach (BinRunningAppSummary summary in _runningAppsManager.GetRunningAppsForBin(_currentBin))
                {
                    if (summary?.Item != null && summary.Windows != null && summary.Windows.Count > 0)
                    {
                        runningByItem[summary.Item] = summary.Windows;
                    }
                }
            }

            if (_currentBin.FloatOpenAppsFirst && runningByItem.Count > 0)
            {
                matching = matching
                    .OrderByDescending(m => runningByItem.ContainsKey(m.Item1))
                    .ToList();
            }

            if (hasFilter && (_selectedTileIndex < 0 || _selectedTileIndex >= matching.Count))
            {
                _selectedTileIndex = 0;
            }
            else if (_selectedTileIndex >= matching.Count)
            {
                _selectedTileIndex = matching.Count - 1;
            }

            int cols = Math.Max(1, Math.Min(10, _currentBin.Columns));
            bool showLabels = _currentBin.ShowItemLabels;
            GetDensityMetrics(
                out double iconSize,
                out double baseTileW,
                out double baseTileH,
                out double fontSize,
                out double lineHeight,
                out double labelMaxH,
                out double baseIconTopPad,
                out double baseIconLabelGap);
            double tileW;
            double tileH;
            double iconTopPad;
            double iconLabelGap;
            if (showLabels)
            {
                tileW = baseTileW + pad * 1.2;
                tileH = baseTileH + pad * 1.2;
                double labelWidth = Math.Max(44, tileW - 10);
                if (!AnyLabelWrapsToMultipleLines(matching, labelWidth, fontSize))
                {
                    tileH -= lineHeight;
                    labelMaxH = lineHeight;
                }
                iconTopPad = Math.Round(baseIconTopPad + pad * 0.35, 1);
                iconLabelGap = Math.Round(baseIconLabelGap + pad * 0.15, 1);
            }
            else
            {
                double iconOnlyPad = Math.Round(baseIconTopPad + pad * 0.45, 1);
                double tileSide = Math.Round(iconSize + iconOnlyPad * 2 + 4);
                tileW = tileSide;
                tileH = tileSide;
                iconTopPad = 0;
                iconLabelGap = 0;
                labelMaxH = 0;
            }
            // Always preserve a crisp minimum visual gutter (5 px) between adjacent tiles even at 0 px padding
            double marginHalf = Math.Round(2.5 + pad / 2.0, 1);
            double fullRowHeight = tileH + marginHalf * 2.0;

            if (_currentBin.MaxRows > 0)
            {
                _gridScroll.MaxHeight = _currentBin.MaxRows * fullRowHeight + 2;
            }
            else
            {
                _gridScroll.MaxHeight = 440;
            }

            if (useStackedHeaders)
            {
                var groups = new List<Tuple<string, List<Tuple<BinItem, int>>>>();
                var ungrouped = matching.Where(m => string.IsNullOrWhiteSpace(m.Item1.Section)).ToList();
                if (ungrouped.Count > 0)
                {
                    groups.Add(Tuple.Create("General", ungrouped));
                }
                foreach (string sec in sectionNames)
                {
                    var secItems = matching
                        .Where(m => string.Equals((m.Item1.Section ?? string.Empty).Trim(), sec, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (secItems.Count > 0)
                    {
                        groups.Add(Tuple.Create(sec, secItems));
                    }
                }

                int runningDisplayIdx = 0;
                _itemsGrid.Columns = cols;
                for (int gIdx = 0; gIdx < groups.Count; gIdx++)
                {
                    var grp = groups[gIdx];
                    _contentHostStack.Children.Add(new TextBlock
                    {
                        Text = grp.Item1,
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FluentTheme.TextMuted,
                        Margin = new Thickness(6, gIdx == 0 ? 2 : 10, 4, 3)
                    });

                    var secGrid = new UniformGrid
                    {
                        Columns = Math.Min(cols, Math.Max(1, grp.Item2.Count)),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                    foreach (var entry in grp.Item2)
                    {
                        runningByItem.TryGetValue(entry.Item1, out List<RunningWindowEntry> preloadedWins);
                        Border tileBorder = CreateItemTile(
                            entry.Item1,
                            entry.Item2,
                            runningDisplayIdx,
                            iconSize,
                            tileW,
                            tileH,
                            marginHalf,
                            pad,
                            fontSize,
                            lineHeight,
                            labelMaxH,
                            iconTopPad,
                            iconLabelGap,
                            preloadedWins,
                            out Brush idleBg,
                            out Brush idleBorder);
                        _displayedTiles.Add(new TileEntry
                        {
                            Item = entry.Item1,
                            OriginalIndex = entry.Item2,
                            TileBorder = tileBorder,
                            IdleBackground = idleBg,
                            IdleBorderBrush = idleBorder
                        });
                        secGrid.Children.Add(tileBorder);
                        runningDisplayIdx++;
                    }
                    _contentHostStack.Children.Add(secGrid);
                }
            }
            else
            {
                _itemsGrid.Columns = Math.Min(cols, Math.Max(1, matching.Count));
                _contentHostStack.Children.Add(_itemsGrid);

                for (int displayIdx = 0; displayIdx < matching.Count; displayIdx++)
                {
                    BinItem item = matching[displayIdx].Item1;
                    int originalIndex = matching[displayIdx].Item2;
                    runningByItem.TryGetValue(item, out List<RunningWindowEntry> preloadedWins);
                    Border tileBorder = CreateItemTile(
                        item,
                        originalIndex,
                        displayIdx,
                        iconSize,
                        tileW,
                        tileH,
                        marginHalf,
                        pad,
                        fontSize,
                        lineHeight,
                        labelMaxH,
                        iconTopPad,
                        iconLabelGap,
                        preloadedWins,
                        out Brush idleBg,
                        out Brush idleBorder);

                    _displayedTiles.Add(new TileEntry
                    {
                        Item = item,
                        OriginalIndex = originalIndex,
                        TileBorder = tileBorder,
                        IdleBackground = idleBg,
                        IdleBorderBrush = idleBorder
                    });
                    _itemsGrid.Children.Add(tileBorder);
                }
            }

            ApplySelectionVisuals();
            _contentHostStack.InvalidateMeasure();
            _gridScroll.InvalidateMeasure();
        }

        private void ApplySelectionVisuals()
        {
            Brush binAccent = FluentTheme.GetEffectiveAccentBrush(_currentBin?.AccentColorHex);
            for (int i = 0; i < _displayedTiles.Count; i++)
            {
                TileEntry entry = _displayedTiles[i];
                Border b = entry.TileBorder;
                bool selected = (i == _selectedTileIndex);
                b.Background = selected ? FluentTheme.CardSelectedBg : (entry.IdleBackground ?? Brushes.Transparent);
                b.BorderBrush = selected ? binAccent : (entry.IdleBorderBrush ?? Brushes.Transparent);
            }
        }

        private Border CreateItemTile(
            BinItem item,
            int originalIndex,
            int displayIdx,
            double iconSize,
            double tileW,
            double tileH,
            double marginHalf,
            int pad,
            double fontSize,
            double lineHeight,
            double labelMaxH,
            double iconTopPad,
            double iconLabelGap,
            List<RunningWindowEntry> preloadedWindows,
            out Brush idleBgOut,
            out Brush idleBorderOut)
        {
            Brush binAccent = FluentTheme.GetEffectiveAccentBrush(_currentBin?.AccentColorHex);
            List<RunningWindowEntry> runningWindows = preloadedWindows ?? (_runningAppsManager?.GetRunningWindowsForItem(item) ?? new List<RunningWindowEntry>());
            bool isRunning = runningWindows.Count > 0;
            bool wasActiveApp = isRunning && runningWindows.Any(w => (w.Hwnd == _priorForegroundHwnd || w.IsForeground) && !w.IsMinimized);

            Brush idleBg = isRunning
                ? FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, wasActiveApp ? (byte)34 : (byte)20)
                : Brushes.Transparent;
            Brush idleBorder = isRunning
                ? FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, wasActiveApp ? (byte)108 : (byte)56)
                : Brushes.Transparent;
            idleBgOut = idleBg;
            idleBorderOut = idleBorder;

            string targetExpanded = Environment.ExpandEnvironmentVariables((item.TargetPath ?? string.Empty).Trim());
            bool isFolder = item.Kind == BinItemKind.Folder || (!string.IsNullOrEmpty(targetExpanded) && Directory.Exists(targetExpanded));
            bool isWebUrl = item.Kind == BinItemKind.WebUrl || BinItem.IsWebUrl(targetExpanded);
            bool isSystemCmd = item.Kind == BinItemKind.SystemCommand || BinItem.IsSystemCommand(targetExpanded);
            bool isRecycleBin = isSystemCmd && string.Equals(BinItem.GetSystemActionKey(targetExpanded), "recyclebin", StringComparison.OrdinalIgnoreCase);

            double bottomPad = Math.Round(7.0 + pad * 0.2, 1);
            string keyHint = displayIdx < 9 ? $" [{displayIdx + 1}]" : string.Empty;
            string tooltipText;
            if (isRecycleBin && Win32Interop.TryGetRecycleBinStatus(out long rbCount, out long rbBytes))
            {
                tooltipText = rbCount > 0
                    ? $"{item.Name}{keyHint} ({rbCount} items • {LauncherService.FormatByteSize(rbBytes)})"
                    : $"{item.Name}{keyHint} (Empty)";
            }
            else if (isRunning)
            {
                tooltipText = runningWindows.Count == 1
                    ? $"{item.Name}{keyHint} (Running)"
                    : $"{item.Name}{keyHint} ({runningWindows.Count} windows open)";
            }
            else
            {
                tooltipText = $"{item.Name}{keyHint}";
            }

            var tile = new Border
            {
                Width = tileW,
                Height = tileH,
                Background = idleBg,
                BorderBrush = idleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(0),
                Margin = new Thickness(marginHalf),
                Cursor = Cursors.Hand,
                AllowDrop = true,
                ToolTip = tooltipText
            };

            var tileOverlayGrid = new Grid();

            bool showLabels = _currentBin?.ShowItemLabels ?? true;

            // Fixed top-anchored 2-row layout when labels are shown so every icon sits on the same horizontal line;
            // centered single-cell layout when icon labels are hidden.
            var contentGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = showLabels
                    ? new Thickness(4, iconTopPad, 4, bottomPad)
                    : new Thickness(0, 0, 0, 2)
            };
            if (showLabels)
            {
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(iconSize + iconLabelGap, GridUnitType.Pixel) });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            var iconWrap = new Grid
            {
                Width = iconSize,
                Height = iconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = showLabels ? VerticalAlignment.Top : VerticalAlignment.Center
            };

            var iconImg = new Image
            {
                Width = iconSize,
                Height = iconSize,
                Source = _iconService.GetItemImageSource(item)
            };
            RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.HighQuality);
            iconWrap.Children.Add(iconImg);

            if (runningWindows.Count > 1)
            {
                iconWrap.Children.Add(new Border
                {
                    MinWidth = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(7.5),
                    Background = binAccent,
                    BorderBrush = FluentTheme.FlyoutBg,
                    BorderThickness = new Thickness(1.2),
                    Padding = new Thickness(3.5, 0, 3.5, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -3, -5, 0),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = runningWindows.Count > 9 ? "9+" : runningWindows.Count.ToString(),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = FluentTheme.AccentInkBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            int unreadCount = (_currentBin?.ShowNotificationBadges ?? true)
                ? RunningAppsTaskbarManager.GetUnreadCountForWindows(runningWindows)
                : 0;

            if (unreadCount > 0)
            {
                iconWrap.Children.Add(new Border
                {
                    MinWidth = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = FluentTheme.BrushFromHex("#E81123"),
                    BorderBrush = FluentTheme.FlyoutBg,
                    BorderThickness = new Thickness(1.2),
                    Padding = new Thickness(3.5, 0, 3.5, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(-5, -3, 0, 0),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = unreadCount > 99 ? "99+" : unreadCount.ToString(),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            if (item.Kind == BinItemKind.SubBin || BinItem.IsSubBin(item.TargetPath))
            {
                iconWrap.Children.Add(new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = FluentTheme.FlyoutBg,
                    BorderBrush = FluentTheme.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, -2, -2),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = "\uE76C",
                        FontFamily = FluentTheme.IconFont,
                        FontSize = 8,
                        Foreground = binAccent,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            if (item.RunAsAdmin)
            {
                iconWrap.Children.Add(new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = FluentTheme.FlyoutBg,
                    BorderBrush = FluentTheme.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, -2, -2),
                    Child = new TextBlock
                    {
                        Text = "\uEA18",
                        FontFamily = FluentTheme.IconFont,
                        FontSize = 8.5,
                        Foreground = binAccent,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            if (showLabels)
            {
                Grid.SetRow(iconWrap, 0);
            }
            contentGrid.Children.Add(iconWrap);

            if (showLabels)
            {
                var label = new TextBlock
                {
                    Text = item.Name,
                    Width = Math.Max(44, tileW - 10),
                    FontSize = fontSize,
                    LineHeight = lineHeight,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    Foreground = FluentTheme.TextPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = labelMaxH
                };
                Grid.SetRow(label, 1);
                contentGrid.Children.Add(label);
            }
            tileOverlayGrid.Children.Add(contentGrid);

            if (isRunning)
            {
                // Windows 11 taskbar running pill indicator anchored at the bottom edge
                var runningPill = new Border
                {
                    Width = wasActiveApp ? 18 : (runningWindows.Count > 1 ? 14 : 10),
                    Height = 3.4,
                    CornerRadius = new CornerRadius(1.7),
                    Background = binAccent,
                    Opacity = wasActiveApp ? 1.0 : 0.90,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 2),
                    IsHitTestVisible = false
                };
                tileOverlayGrid.Children.Add(runningPill);
            }

            // Quick Folder Peek hover chevron button on Folder tiles
            Border peekBtn = null;
            if (isFolder)
            {
                peekBtn = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(4),
                    Background = FluentTheme.CardBg,
                    BorderBrush = FluentTheme.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 3, 0),
                    Opacity = 0,
                    Cursor = Cursors.Hand,
                    ToolTip = $"Peek inside {item.Name}",
                    Child = new TextBlock
                    {
                        Text = "\uE76C",
                        FontFamily = FluentTheme.IconFont,
                        FontSize = 9.5,
                        Foreground = FluentTheme.TextSecondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                peekBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
                peekBtn.MouseLeftButtonUp += (s, e) =>
                {
                    OpenFolderPeekPopupMenu(peekBtn, targetExpanded);
                    e.Handled = true;
                };

                tileOverlayGrid.Children.Add(peekBtn);
            }

            tile.Child = tileOverlayGrid;

            Point? dragStartPoint = null;
            bool didDrag = false;

            tile.MouseEnter += (_, __) =>
            {
                if (_selectedTileIndex != displayIdx)
                {
                    tile.Background = isRunning
                        ? FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, 48)
                        : FluentTheme.SubtleHoverBg;
                    tile.BorderBrush = isRunning
                        ? FluentTheme.GetTintedPillBgBrush(_currentBin?.AccentColorHex, 118)
                        : Brushes.Transparent;
                }
                if (peekBtn != null)
                    peekBtn.Opacity = 1;
            };
            tile.MouseLeave += (_, __) =>
            {
                bool selected = (_selectedTileIndex == displayIdx);
                tile.Background = selected ? FluentTheme.CardSelectedBg : idleBg;
                tile.BorderBrush = selected ? binAccent : idleBorder;
                if (peekBtn != null)
                    peekBtn.Opacity = 0;
            };

            tile.PreviewMouseLeftButtonDown += (s, e) =>
            {
                // If user opened flyout via hover and clicks inside, promote to interactive focus
                if (_openedByHover)
                {
                    _openedByHover = false;
                    _hoverDismissTimer.Stop();
                    Activate();
                }
                dragStartPoint = e.GetPosition(this);
                didDrag = false;
            };

            tile.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed &&
                    dragStartPoint.HasValue &&
                    string.IsNullOrEmpty(_filterText))
                {
                    Point pos = e.GetPosition(this);
                    if (Math.Abs(pos.X - dragStartPoint.Value.X) > 7 ||
                        Math.Abs(pos.Y - dragStartPoint.Value.Y) > 7)
                    {
                        didDrag = true;
                        dragStartPoint = null;
                        _suppressDeactivateClose = true;
                        try
                        {
                            var data = new DataObject();
                            DragDropEffects effects = DragDropEffects.None;

                            if (!item.IsLiveFolderItem && originalIndex >= 0)
                            {
                                data.SetData(InternalTileDragFormat, originalIndex);
                                effects |= DragDropEffects.Move;
                            }

                            if (!isWebUrl && !isSystemCmd &&
                                !string.IsNullOrWhiteSpace(targetExpanded) &&
                                (File.Exists(targetExpanded) || Directory.Exists(targetExpanded)))
                            {
                                data.SetData(DataFormats.FileDrop, new[] { targetExpanded });
                                effects |= DragDropEffects.Copy | DragDropEffects.Move;
                            }

                            if (effects != DragDropEffects.None)
                            {
                                DragDrop.DoDragDrop(tile, data, effects);
                            }
                        }
                        finally
                        {
                            _suppressDeactivateClose = false;
                            ShowDropHint(null, highlightOuterBorder: false);
                            if (IsVisible)
                                Activate();
                        }
                    }
                }
            };

            tile.MouseLeftButtonUp += (s, e) =>
            {
                dragStartPoint = null;
                if (didDrag)
                {
                    didDrag = false;
                    e.Handled = true;
                    return;
                }

                ActivateOrLaunchTileItem(item);
                e.Handled = true;
            };

            tile.MouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    bool keepOpen = IsKeepOpenMode;
                    if (keepOpen)
                    {
                        EnterKeepOpenAfterLaunch();
                    }
                    else
                    {
                        HidePopup();
                    }
                    LauncherService.LaunchItem(item);
                    if (keepOpen)
                    {
                        EnsureOnTopAfterLaunch();
                    }
                    e.Handled = true;
                }
            };

            tile.MouseRightButtonUp += (s, e) =>
            {
                OpenTileContextMenu(tile, item, originalIndex, isFolder ? targetExpanded : null);
                e.Handled = true;
            };

            // Tile-level Drag & Drop:
            // 1) Internal tile reordering
            // 2) Dropping files onto a Folder tile moves/copies into that folder
            // 3) Dropping files onto an App/Shortcut tile opens those files with that app (unless Alt held or dropping .lnk shortcuts to add)
            tile.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(InternalTileDragFormat))
                {
                    int fromIdx = (int)e.Data.GetData(InternalTileDragFormat);
                    if (originalIndex >= 0 && fromIdx != originalIndex)
                    {
                        e.Effects = DragDropEffects.Move;
                        tile.BorderBrush = binAccent;
                        tile.Background = FluentTheme.CardSelectedBg;
                        ShowDropHint($"Move to position {originalIndex + 1}", highlightOuterBorder: false);
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                    e.Handled = true;
                    return;
                }

                bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                if (!altHeld && e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles &&
                    droppedFiles.Length > 0)
                {
                    if (isRecycleBin)
                    {
                        e.Effects = DragDropEffects.Move;
                        tile.BorderBrush = FluentTheme.DangerBrush;
                        tile.Background = FluentTheme.CardSelectedBg;
                        string noun = droppedFiles.Length == 1 ? Path.GetFileName(droppedFiles[0]) : $"{droppedFiles.Length} items";
                        ShowDropHint($"Move {noun} to Recycle Bin", highlightOuterBorder: false);
                        e.Handled = true;
                        return;
                    }

                    if (isFolder)
                    {
                        bool ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                        e.Effects = ctrlHeld ? DragDropEffects.Copy : DragDropEffects.Move;
                        tile.BorderBrush = binAccent;
                        tile.Background = FluentTheme.CardSelectedBg;
                        string verb = ctrlHeld ? "Copy" : "Move";
                        string noun = droppedFiles.Length == 1 ? Path.GetFileName(droppedFiles[0]) : $"{droppedFiles.Length} items";
                        ShowDropHint($"{verb} {noun} into \"{item.Name}\" (Ctrl = Copy, Alt = Add to Bin)", highlightOuterBorder: false);
                        e.Handled = true;
                        return;
                    }

                    // If dropping non-.lnk/.exe documents onto an App tile, open with that app!
                    bool allShortcutsOrExes = droppedFiles.All(f =>
                    {
                        string ext = Path.GetExtension(f)?.ToLowerInvariant();
                        return ext == ".lnk" || ext == ".exe" || ext == ".appref-ms";
                    });

                    if (!isWebUrl && !isSystemCmd && !allShortcutsOrExes)
                    {
                        e.Effects = DragDropEffects.Copy;
                        tile.BorderBrush = binAccent;
                        tile.Background = FluentTheme.CardSelectedBg;
                        string noun = droppedFiles.Length == 1 ? Path.GetFileName(droppedFiles[0]) : $"{droppedFiles.Length} files";
                        ShowDropHint($"Open {noun} with {item.Name} (Alt = Add to Bin)", highlightOuterBorder: false);
                        e.Handled = true;
                        return;
                    }
                }
            };

            tile.DragLeave += (s, e) =>
            {
                bool selected = (_selectedTileIndex == displayIdx);
                tile.BorderBrush = selected ? binAccent : idleBorder;
                tile.Background = selected ? FluentTheme.CardSelectedBg : idleBg;
            };

            tile.Drop += (s, e) =>
            {
                tile.BorderBrush = idleBorder;
                tile.Background = idleBg;
                ShowDropHint(null, highlightOuterBorder: false);

                if (e.Data.GetDataPresent(InternalTileDragFormat))
                {
                    int fromIdx = (int)e.Data.GetData(InternalTileDragFormat);
                    if (originalIndex >= 0 && fromIdx != originalIndex)
                    {
                        MoveItem(fromIdx, originalIndex);
                    }
                    e.Handled = true;
                    return;
                }

                bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                if (!altHeld && e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles &&
                    droppedFiles.Length > 0)
                {
                    if (isRecycleBin)
                    {
                        Win32Interop.ShellSendFilesToRecycleBin(droppedFiles);
                        if (_currentBin != null)
                        {
                            _currentBin.IconRevision++;
                            RebuildContent();
                            _onBinModified?.Invoke(_currentBin);
                        }
                        e.Handled = true;
                        return;
                    }

                    if (isFolder)
                    {
                        bool ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                        HidePopup();
                        Win32Interop.ShellCopyOrMoveFilesToFolder(droppedFiles, targetExpanded, copy: ctrlHeld);
                        e.Handled = true;
                        return;
                    }

                    bool allShortcutsOrExes = droppedFiles.All(f =>
                    {
                        string ext = Path.GetExtension(f)?.ToLowerInvariant();
                        return ext == ".lnk" || ext == ".exe" || ext == ".appref-ms";
                    });

                    if (!isWebUrl && !isSystemCmd && !allShortcutsOrExes)
                    {
                        bool keepOpen = IsKeepOpenMode;
                        if (keepOpen)
                        {
                            EnterKeepOpenAfterLaunch();
                        }
                        else
                        {
                            HidePopup();
                        }
                        LauncherService.LaunchItemWithFiles(item, droppedFiles);
                        if (keepOpen)
                        {
                            EnsureOnTopAfterLaunch();
                        }
                        e.Handled = true;
                        return;
                    }
                }
            };

            return tile;
        }

        private void ActivateOrLaunchTileItem(BinItem item)
        {
            if (item == null)
                return;

            bool ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool forceNewInstance = shiftHeld || ctrlHeld;
            bool keepOpen = IsKeepOpenMode;

            if (item.Kind == BinItemKind.SubBin || BinItem.IsSubBin(item.TargetPath))
            {
                string targetBinId = BinItem.GetSubBinId(item.TargetPath);
                BinGroup targetBin = _configService.Current?.Bins?.FirstOrDefault(b =>
                    string.Equals(b.Id, targetBinId, StringComparison.OrdinalIgnoreCase));
                if (targetBin != null && !string.Equals(targetBin.Id, _currentBin?.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _subBinStack.Push(_currentBin);
                    _currentBin = targetBin;
                    _filterText = string.Empty;
                    _activeSectionTab = null;
                    _selectedTileIndex = -1;
                    RebuildContent();
                    UpdateLayout();
                    PositionAboveTaskbarAnchor(_lastAnchorScreenPx);
                }
                return;
            }

            if (item.Kind == BinItemKind.Snippet || BinItem.IsSnippet(item.TargetPath))
            {
                if (LauncherService.LaunchItem(item))
                {
                    ShowDropHint($"Copied \"{item.Name}\" to clipboard", highlightOuterBorder: false);
                    var clearTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                    clearTimer.Tick += (_, __) =>
                    {
                        clearTimer.Stop();
                        ShowDropHint(null, highlightOuterBorder: false);
                        if (!keepOpen && IsVisible)
                        {
                            HidePopup();
                        }
                    };
                    clearTimer.Start();
                }
                return;
            }

            List<RunningWindowEntry> liveWindows = !forceNewInstance
                ? (_runningAppsManager?.GetRunningWindowsForItem(item) ?? new List<RunningWindowEntry>())
                : null;

            if (keepOpen)
            {
                EnterKeepOpenAfterLaunch();
            }
            else
            {
                HidePopup();
            }

            if (liveWindows != null && liveWindows.Count > 0)
            {
                if (liveWindows.Count == 1 && liveWindows[0].Hwnd == _priorForegroundHwnd && !liveWindows[0].IsMinimized)
                {
                    Win32Interop.MinimizeWindow(liveWindows[0].Hwnd);
                    _priorForegroundHwnd = IntPtr.Zero;
                    if (keepOpen)
                    {
                        EnsureOnTopAfterLaunch();
                    }
                    return;
                }

                RunningWindowEntry targetWin = liveWindows[0];
                if (liveWindows.Count > 1)
                {
                    int priorIdx = liveWindows.FindIndex(w => w.Hwnd == _priorForegroundHwnd && !w.IsMinimized);
                    if (priorIdx >= 0)
                    {
                        targetWin = liveWindows[(priorIdx + 1) % liveWindows.Count];
                    }
                }

                Win32Interop.ActivateAndRestoreWindow(targetWin.Hwnd);
                _priorForegroundHwnd = targetWin.Hwnd;
            }
            else
            {
                LauncherService.LaunchItem(item);
            }

            if (keepOpen)
            {
                EnsureOnTopAfterLaunch();
            }
        }

        private void NavigateBackSubBin()
        {
            if (_subBinStack.Count == 0)
                return;

            _currentBin = _subBinStack.Pop();
            _filterText = string.Empty;
            _activeSectionTab = null;
            _selectedTileIndex = -1;
            RebuildContent();
            UpdateLayout();
            PositionAboveTaskbarAnchor(_lastAnchorScreenPx);
        }

        private void OpenFolderPeekPopupMenu(FrameworkElement anchor, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Right;

            PopulateFolderPeekMenu(menu, folderPath, _iconService, () => HidePopup(), depth: 0);
            ShowManagedContextMenu(menu);
        }

        private void ShowManagedContextMenu(ContextMenu menu)
        {
            if (menu == null)
                return;

            bool leafItemClicked = false;
            bool wasEscape = false;

            menu.AddHandler(
                MenuItem.ClickEvent,
                new RoutedEventHandler((_, e) =>
                {
                    if (!(e.OriginalSource is MenuItem mi) || !mi.HasItems)
                    {
                        leafItemClicked = true;
                    }
                }),
                handledEventsToo: true);

            menu.PreviewKeyDown += (_, e) =>
            {
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.Escape)
                {
                    wasEscape = true;
                }
            };

            _activeContextMenuCount++;
            menu.Closed += (_, __) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _activeContextMenuCount = Math.Max(0, _activeContextMenuCount - 1);
                    if (_activeContextMenuCount > 0 || !IsVisible || _suppressDeactivateClose)
                        return;

                    if (leafItemClicked || wasEscape)
                    {
                        Activate();
                        return;
                    }

                    // Dismissed by clicking outside the menu — keep BinPopupWindow open only if click was inside the flyout
                    bool clickedInsidePopup = false;
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero &&
                        Win32Interop.GetCursorPos(out Win32Interop.POINT pt) &&
                        Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT rect))
                    {
                        clickedInsidePopup = pt.X >= rect.Left && pt.X <= rect.Right &&
                                             pt.Y >= rect.Top && pt.Y <= rect.Bottom;
                    }

                    if (clickedInsidePopup)
                    {
                        Activate();
                    }
                    else if (ShouldStayOpenOnDeactivate)
                    {
                        ReassertTopmost();
                    }
                    else
                    {
                        if (Win32Interop.GetCursorPos(out Win32Interop.POINT curPt))
                        {
                            _lastDeactivatedBinId = _currentBin?.Id;
                            _lastDeactivatedTick = Environment.TickCount;
                            _lastDeactivatedCursor = new System.Drawing.Point(curPt.X, curPt.Y);
                        }
                        HidePopup();
                    }
                }), DispatcherPriority.ContextIdle);
            };

            menu.IsOpen = true;
        }

        public static void PopulateFolderPeekMenu(
            ItemsControl parentMenu,
            string folderPath,
            IconService iconService,
            Action onBeforeLaunch,
            int depth = 0)
        {
            parentMenu.Items.Clear();

            string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = folderPath;

            parentMenu.Items.Add(FluentTheme.CreateMenuItem($"Open \"{folderName}\" in Explorer", () =>
            {
                onBeforeLaunch?.Invoke();
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{folderPath}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }, iconGlyph: "\uE8B7"));

            parentMenu.Items.Add(FluentTheme.CreateMenuSeparator());

            List<DirectoryInfo> subDirs = new List<DirectoryInfo>();
            List<FileInfo> files = new List<FileInfo>();

            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                subDirs = dirInfo.EnumerateDirectories()
                    .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(20)
                    .ToList();

                files = dirInfo.EnumerateFiles()
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(25)
                    .ToList();
            }
            catch
            {
            }

            if (subDirs.Count == 0 && files.Count == 0)
            {
                var emptyItem = FluentTheme.CreateMenuItem("(Empty folder)", null);
                emptyItem.IsEnabled = false;
                parentMenu.Items.Add(emptyItem);
                return;
            }

            foreach (DirectoryInfo subDir in subDirs)
            {
                string subPath = subDir.FullName;
                if (depth < 1)
                {
                    var subMenuItem = new MenuItem
                    {
                        Header = subDir.Name,
                        Style = FluentTheme.CreateWin11MenuItemStyle(),
                        Icon = CreateMenuImageIcon(iconService?.GetPathImageSource(subPath))
                    };
                    // Lazy placeholder so cascading submenu chevron appears immediately
                    subMenuItem.Items.Add(new MenuItem { Header = "Loading..." });
                    bool populated = false;
                    subMenuItem.SubmenuOpened += (_, __) =>
                    {
                        if (!populated)
                        {
                            populated = true;
                            PopulateFolderPeekMenu(subMenuItem, subPath, iconService, onBeforeLaunch, depth + 1);
                        }
                    };
                    parentMenu.Items.Add(subMenuItem);
                }
                else
                {
                    var dirItem = FluentTheme.CreateMenuItem(subDir.Name, () =>
                    {
                        onBeforeLaunch?.Invoke();
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"\"{subPath}\"",
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    });
                    dirItem.Icon = CreateMenuImageIcon(iconService?.GetPathImageSource(subPath));
                    parentMenu.Items.Add(dirItem);
                }
            }

            foreach (FileInfo file in files)
            {
                string filePath = file.FullName;
                string displayName = file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(file.Name)
                    : file.Name;

                var fileItem = FluentTheme.CreateMenuItem(displayName, () =>
                {
                    onBeforeLaunch?.Invoke();
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                });
                fileItem.Icon = CreateMenuImageIcon(iconService?.GetPathImageSource(filePath));
                parentMenu.Items.Add(fileItem);
            }
        }

        private static UIElement CreateMenuImageIcon(ImageSource source)
        {
            if (source == null)
                return null;

            var img = new Image
            {
                Width = 16,
                Height = 16,
                Source = source,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        private void OuterBorder_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentBin == null)
                return;

            OpenHeaderContextMenu(_outerBorder);
            e.Handled = true;
        }

        private MenuItem BuildSystemActionsSubmenu(Action<BinItem> onCreated)
        {
            var sysMenu = new MenuItem
            {
                Header = "System action",
                Style = FluentTheme.CreateWin11MenuItemStyle(),
                Icon = FluentTheme.CreateFluentIcon("\uE770", 13.5)
            };

            void AddSys(string label, string key, string glyph)
            {
                sysMenu.Items.Add(FluentTheme.CreateMenuItem(label, () =>
                {
                    onCreated?.Invoke(BinItem.FromSystemAction(key, label));
                }, iconGlyph: glyph));
            }

            AddSys("Recycle Bin (Live)", "recyclebin", "\uE74D");
            AddSys("Screen Snip", "snip", "\uE7A6");
            AddSys("Task Manager", "taskmgr", "\uE9D9");
            AddSys("Windows Settings", "settings", "\uE713");
            sysMenu.Items.Add(FluentTheme.CreateMenuSeparator());
            AddSys("Lock PC", "lock", "\uE72E");
            AddSys("Sleep", "sleep", "\uE708");
            AddSys("Restart PC", "restart", "\uE777");
            AddSys("Shut Down", "shutdown", "\uE7E8");
            sysMenu.Items.Add(FluentTheme.CreateMenuSeparator());
            sysMenu.Items.Add(FluentTheme.CreateMenuItem("PowerShell Command...", () =>
            {
                AddCustomPowerShellItem();
            }, iconGlyph: "\uE756"));

            return sysMenu;
        }

        private void AddCustomPowerShellItem()
        {
            if (_currentBin == null)
                return;

            _suppressDeactivateClose = true;
            try
            {
                BinItem draft = BinItem.FromSystemAction("powershell", "PowerShell Command", "");
                var existingSections = _currentBin.Items?.Select(i => i?.Section).Where(s => !string.IsNullOrWhiteSpace(s));
                var dlg = new ItemPropertiesDialogWindow(draft, existingSections) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _currentBin.Items.Add(draft);
                    _currentBin.IconRevision++;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }
            }
            finally
            {
                _suppressDeactivateClose = false;
                if (IsVisible) Activate();
            }
        }

        private void OpenHeaderContextMenu(FrameworkElement anchor)
        {
            if (_currentBin == null)
                return;

            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.MousePoint;

            int wsCount = CountLaunchAllItems(_currentBin);
            if (wsCount > 0)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem($"Launch all ({wsCount} {(wsCount == 1 ? "item" : "items")})", () =>
                {
                    BinGroup target = _currentBin;
                    bool keepOpen = IsKeepOpenMode;
                    if (keepOpen)
                    {
                        EnterKeepOpenAfterLaunch();
                    }
                    else
                    {
                        HidePopup();
                    }
                    _ = LauncherService.LaunchBinWorkspaceAsync(target, _runningAppsManager);
                    if (keepOpen)
                    {
                        EnsureOnTopAfterLaunch();
                    }
                }, iconGlyph: "\uE768"));
                menu.Items.Add(FluentTheme.CreateMenuSeparator());
            }

            menu.Items.Add(FluentTheme.CreateMenuItem(
                _isPinnedOpen ? "Unpin Bin" : "Pin Bin open on top",
                () =>
                {
                    _isPinnedOpen = !_isPinnedOpen;
                    if (_isPinnedOpen)
                    {
                        _openedByHover = false;
                        _hoverDismissTimer.Stop();
                        ReassertTopmost();
                    }
                    UpdateHeaderVisibility(repositionIfHeaderToggled: true);
                },
                iconGlyph: _isPinnedOpen ? "\uE77A" : "\uE718"));
            menu.Items.Add(FluentTheme.CreateMenuSeparator());

            menu.Items.Add(FluentTheme.CreateMenuItem("Add Installed / Store App...", AddFromStartMenu, iconGlyph: "\uE737"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Add App or Shortcut...", AddFilesViaDialog, iconGlyph: "\uE7C3"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Add Folder...", AddFolderViaDialog, iconGlyph: "\uE8B7"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Add Website / URL...", AddUrlViaDialog, iconGlyph: "\uE774"));
            menu.Items.Add(BuildSystemActionsSubmenu(sysItem =>
            {
                if (_currentBin != null && sysItem != null)
                {
                    _currentBin.Items.Add(sysItem);
                    _currentBin.IconRevision++;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }
            }));
            menu.Items.Add(FluentTheme.CreateMenuSeparator());

            menu.Items.Add(FluentTheme.CreateMenuItem("Set flyout icon...", () =>
            {
                _suppressDeactivateClose = true;
                try
                {
                    var picker = new IconPickerWindow(
                        _iconService,
                        _currentBin.FlyoutIconPath,
                        _currentBin.FlyoutIconIndex,
                        allowClear: !string.IsNullOrWhiteSpace(_currentBin.FlyoutIconPath),
                        dialogTitle: "Choose Bin Flyout Icon")
                    {
                        Owner = this
                    };
                    if (picker.ShowDialog() == true)
                    {
                        _currentBin.FlyoutIconPath = picker.SelectedIconPath;
                        _currentBin.FlyoutIconIndex = picker.SelectedIconIndex;
                        _configService.Save(notify: false);
                        RebuildContent();
                        _onBinModified?.Invoke(_currentBin);
                    }
                }
                finally
                {
                    _suppressDeactivateClose = false;
                    Activate();
                }
            }, iconGlyph: "\uEB9F"));

            if (!string.IsNullOrWhiteSpace(_currentBin.FlyoutIconPath))
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Remove flyout icon", () =>
                {
                    _currentBin.FlyoutIconPath = string.Empty;
                    _currentBin.FlyoutIconIndex = 0;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }, iconGlyph: "\uE711"));
            }

            menu.Items.Add(FluentTheme.CreateMenuSeparator());
            menu.Items.Add(FluentTheme.CreateMenuItem("Customize Bin...", () =>
            {
                BinGroup target = _currentBin;
                HidePopup();
                _onCustomizeRequested?.Invoke(target);
            }, iconGlyph: "\uE713"));

            ShowManagedContextMenu(menu);
        }

        private void OpenTileContextMenu(FrameworkElement target, BinItem item, int index, string folderPathForPeek)
        {
            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;

            bool isUrl = item.Kind == BinItemKind.WebUrl || BinItem.IsWebUrl(item.TargetPath);
            bool isPackaged = item.Kind == BinItemKind.PackagedApp || BinItem.IsPackagedApp(item.TargetPath);
            bool isSystemCmd = item.Kind == BinItemKind.SystemCommand || BinItem.IsSystemCommand(item.TargetPath);
            bool isRecycleBin = isSystemCmd && string.Equals(BinItem.GetSystemActionKey(item.TargetPath), "recyclebin", StringComparison.OrdinalIgnoreCase);

            if (item.IsLiveFolderItem)
            {
                if (!string.IsNullOrWhiteSpace(folderPathForPeek) && Directory.Exists(folderPathForPeek))
                {
                    var peekSubMenu = new MenuItem
                    {
                        Header = "Peek folder",
                        Style = FluentTheme.CreateWin11MenuItemStyle(),
                        Icon = FluentTheme.CreateFluentIcon("\uE8B7", 13.5, FluentTheme.TextSecondary)
                    };
                    peekSubMenu.Items.Add(new MenuItem { Header = "Loading..." });
                    bool populated = false;
                    peekSubMenu.SubmenuOpened += (_, __) =>
                    {
                        if (!populated)
                        {
                            populated = true;
                            PopulateFolderPeekMenu(peekSubMenu, folderPathForPeek, _iconService, () => HidePopup(), depth: 0);
                        }
                    };
                    menu.Items.Add(peekSubMenu);
                }

                menu.Items.Add(FluentTheme.CreateMenuItem("Open", () =>
                {
                    HidePopup();
                    LauncherService.LaunchItem(item);
                }, iconGlyph: "\uE8A7"));

                menu.Items.Add(FluentTheme.CreateMenuItem("Open file location", () =>
                {
                    HidePopup();
                    LauncherService.OpenItemLocation(item);
                }, iconGlyph: "\uE8B7"));

                menu.Items.Add(FluentTheme.CreateMenuSeparator());
                menu.Items.Add(FluentTheme.CreateMenuItem("Pin permanently to Bin", () =>
                {
                    if (_currentBin != null && !string.IsNullOrWhiteSpace(item.TargetPath))
                    {
                        _currentBin.Items.Add(BinItem.FromPath(item.TargetPath));
                        _currentBin.IconRevision++;
                        _configService.Save(notify: false);
                        RebuildContent();
                        _onBinModified?.Invoke(_currentBin);
                    }
                }, iconGlyph: "\uE718"));

                ShowManagedContextMenu(menu);
                return;
            }

            if (isUrl)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Open in browser", () =>
                {
                    HidePopup();
                    LauncherService.LaunchItem(item);
                }, iconGlyph: "\uE774"));

                menu.Items.Add(FluentTheme.CreateMenuItem("Edit URL...", () =>
                {
                    _suppressDeactivateClose = true;
                    try
                    {
                        string prevUrl = item.TargetPath;
                        var dlg = new AddUrlDialogWindow(
                            _iconService,
                            item.TargetPath,
                            item.Name,
                            item.CustomIconPath,
                            item.CustomIconIndex)
                        {
                            Owner = this
                        };
                        if (dlg.ShowDialog() == true && _currentBin != null)
                        {
                            bool urlChanged = !string.Equals(prevUrl, dlg.NormalizedUrl, StringComparison.OrdinalIgnoreCase);
                            item.TargetPath = dlg.NormalizedUrl;
                            item.Name = dlg.DisplayName;
                            item.Kind = BinItemKind.WebUrl;
                            item.CustomIconPath = dlg.CustomIconPath ?? string.Empty;
                            item.CustomIconIndex = dlg.CustomIconIndex;
                            _currentBin.IconRevision++;
                            _configService.Save(notify: false);
                            RebuildContent();
                            _onBinModified?.Invoke(_currentBin);

                            if (string.IsNullOrWhiteSpace(item.CustomIconPath) && urlChanged)
                            {
                                FetchPopupItemFavicon(_currentBin, item);
                            }
                        }
                    }
                    finally
                    {
                        _suppressDeactivateClose = false;
                        Activate();
                    }
                }, iconGlyph: "\uE71B"));

                menu.Items.Add(FluentTheme.CreateMenuItem("Copy URL", () =>
                {
                    try { Clipboard.SetText(item.TargetPath ?? string.Empty); } catch { }
                }, iconGlyph: "\uE8C8"));
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(folderPathForPeek) && Directory.Exists(folderPathForPeek))
                {
                    var peekSubMenu = new MenuItem
                    {
                        Header = "Peek folder",
                        Style = FluentTheme.CreateWin11MenuItemStyle(),
                        Icon = new TextBlock
                        {
                            Text = "\uE8B7",
                            FontFamily = FluentTheme.IconFont,
                            FontSize = 13.5,
                            Foreground = FluentTheme.TextSecondary,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    peekSubMenu.Items.Add(new MenuItem { Header = "Loading..." });
                    bool populated = false;
                    peekSubMenu.SubmenuOpened += (_, __) =>
                    {
                        if (!populated)
                        {
                            populated = true;
                            PopulateFolderPeekMenu(peekSubMenu, folderPathForPeek, _iconService, () => HidePopup(), depth: 0);
                        }
                    };
                    menu.Items.Add(peekSubMenu);
                }

                List<RunningWindowEntry> liveWindows = _runningAppsManager?.GetRunningWindowsForItem(item) ?? new List<RunningWindowEntry>();
                if (liveWindows.Count > 0)
                {
                    if (liveWindows.Count == 1)
                    {
                        IntPtr targetHwnd = liveWindows[0].Hwnd;
                        menu.Items.Add(FluentTheme.CreateMenuItem("Switch to window", () =>
                        {
                            HidePopup();
                            Win32Interop.ActivateAndRestoreWindow(targetHwnd);
                        }, iconGlyph: "\uE737"));
                    }
                    else
                    {
                        foreach (RunningWindowEntry win in liveWindows.Take(4))
                        {
                            IntPtr targetHwnd = win.Hwnd;
                            string rawTitle = string.IsNullOrWhiteSpace(win.Title) ? item.Name : win.Title.Trim();
                            string shortTitle = rawTitle.Length > 38 ? rawTitle.Substring(0, 35) + "..." : rawTitle;
                            menu.Items.Add(FluentTheme.CreateMenuItem($"Switch to: {shortTitle}", () =>
                            {
                                HidePopup();
                                Win32Interop.ActivateAndRestoreWindow(targetHwnd);
                            }, iconGlyph: "\uE737"));
                        }
                    }

                    menu.Items.Add(FluentTheme.CreateMenuItem("Open new window", () =>
                    {
                        HidePopup();
                        LauncherService.LaunchItem(item);
                    }, iconGlyph: "\uE8A7"));

                    string closeLabel = liveWindows.Count == 1 ? "Close window" : $"Close all {liveWindows.Count} windows";
                    menu.Items.Add(FluentTheme.CreateMenuItem(closeLabel, () =>
                    {
                        foreach (RunningWindowEntry win in liveWindows)
                        {
                            Win32Interop.CloseWindowGracefully(win.Hwnd);
                        }
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (IsVisible) RebuildContent();
                        }), DispatcherPriority.Background);
                    }, isDanger: true, iconGlyph: "\uE711"));
                }
                else
                {
                    menu.Items.Add(FluentTheme.CreateMenuItem("Open", () =>
                    {
                        HidePopup();
                        LauncherService.LaunchItem(item);
                    }, iconGlyph: "\uE8A7"));
                }

                if (isRecycleBin)
                {
                    Win32Interop.TryGetRecycleBinStatus(out long rbCount, out _);
                    var emptyRbItem = FluentTheme.CreateMenuItem("Empty Recycle Bin", () =>
                    {
                        _suppressDeactivateClose = true;
                        try
                        {
                            IntPtr hwnd = new WindowInteropHelper(this).Handle;
                            Win32Interop.EmptyRecycleBin(hwnd, noConfirm: false);
                            if (_currentBin != null)
                            {
                                _currentBin.IconRevision++;
                                RebuildContent();
                                _onBinModified?.Invoke(_currentBin);
                            }
                        }
                        finally
                        {
                            _suppressDeactivateClose = false;
                            if (IsVisible) Activate();
                        }
                    }, isDanger: rbCount > 0, iconGlyph: "\uE74D");
                    emptyRbItem.IsEnabled = rbCount > 0;
                    menu.Items.Add(emptyRbItem);
                }

                if (item.Kind != BinItemKind.Folder && !isPackaged && !isSystemCmd)
                {
                    menu.Items.Add(FluentTheme.CreateMenuItem("Run as administrator", () =>
                    {
                        HidePopup();
                        LauncherService.LaunchItem(item, runAsAdmin: true);
                    }, iconGlyph: "\uE7EF"));
                }

                if (!isPackaged && !isSystemCmd)
                {
                    menu.Items.Add(FluentTheme.CreateMenuItem("Open file location", () =>
                    {
                        HidePopup();
                        LauncherService.OpenItemLocation(item);
                    }, iconGlyph: "\uE8B7"));
                }
            }

            menu.Items.Add(FluentTheme.CreateMenuSeparator());

            menu.Items.Add(FluentTheme.CreateMenuItem("Properties...", () =>
            {
                _suppressDeactivateClose = true;
                try
                {
                    var existingSecs = _currentBin?.Items?.Select(i => i?.Section).Where(s => !string.IsNullOrWhiteSpace(s));
                    var dlg = new ItemPropertiesDialogWindow(item, existingSecs) { Owner = this };
                    if (dlg.ShowDialog() == true && _currentBin != null)
                    {
                        _currentBin.IconRevision++;
                        _configService.Save(notify: false);
                        RebuildContent();
                        _onBinModified?.Invoke(_currentBin);
                    }
                }
                finally
                {
                    _suppressDeactivateClose = false;
                    if (IsVisible) Activate();
                }
            }, iconGlyph: "\uE946"));

            // Category / Tab quick submenu
            var catMenu = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(item.Section) ? "Set category / tab" : $"Category: {item.Section}",
                Style = FluentTheme.CreateWin11MenuItemStyle(),
                Icon = FluentTheme.CreateFluentIcon("\uE8EC", 13.5)
            };
            var existingCategories = (_currentBin?.Items ?? new List<BinItem>())
                .Select(i => (i?.Section ?? string.Empty).Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string cat in existingCategories)
            {
                string capturedCat = cat;
                catMenu.Items.Add(FluentTheme.CreateMenuItem(capturedCat, () =>
                {
                    item.Section = capturedCat;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }, iconGlyph: string.Equals(item.Section, capturedCat, StringComparison.OrdinalIgnoreCase) ? "\uE73E" : null));
            }
            catMenu.Items.Add(FluentTheme.CreateMenuItem("New category / tab...", () =>
            {
                _suppressDeactivateClose = true;
                try
                {
                    var dlg = new RenameDialogWindow("Category / Tab", "Enter a category or tab name for this item:", item.Section)
                    {
                        Owner = this
                    };
                    if (dlg.ShowDialog() == true && _currentBin != null)
                    {
                        item.Section = dlg.ResultText;
                        _configService.Save(notify: false);
                        RebuildContent();
                        _onBinModified?.Invoke(_currentBin);
                    }
                }
                finally
                {
                    _suppressDeactivateClose = false;
                    if (IsVisible) Activate();
                }
            }, iconGlyph: "\uE710"));
            if (!string.IsNullOrWhiteSpace(item.Section))
            {
                catMenu.Items.Add(FluentTheme.CreateMenuItem("Clear category", () =>
                {
                    item.Section = string.Empty;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }, iconGlyph: "\uE711"));
            }
            menu.Items.Add(catMenu);

            menu.Items.Add(FluentTheme.CreateMenuItem("Rename...", () =>
            {
                _suppressDeactivateClose = true;
                try
                {
                    var dlg = new RenameDialogWindow(
                        "Rename Item",
                        "Display name in Bins (original file or shortcut is unchanged):",
                        item.Name)
                    {
                        Owner = this
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        item.Name = dlg.ResultText;
                        _configService.Save(notify: false);
                        RebuildContent();
                        _onBinModified?.Invoke(_currentBin);
                    }
                }
                finally
                {
                    _suppressDeactivateClose = false;
                    Activate();
                }
            }, iconGlyph: "\uE70F"));

            menu.Items.Add(FluentTheme.CreateMenuItem("Change icon...", () =>
            {
                _suppressDeactivateClose = true;
                try
                {
                    var picker = new IconPickerWindow(
                        _iconService,
                        item.CustomIconPath,
                        item.CustomIconIndex,
                        allowClear: !string.IsNullOrWhiteSpace(item.CustomIconPath),
                        dialogTitle: $"Choose Icon — {item.Name}",
                        clearButtonLabel: "Reset Default Icon",
                        clearButtonGlyph: "\uE777")
                    {
                        Owner = this
                    };
                    if (picker.ShowDialog() == true && _currentBin != null)
                    {
                        item.CustomIconPath = picker.SelectedIconPath ?? string.Empty;
                        item.CustomIconIndex = picker.SelectedIconIndex;
                        _currentBin.IconRevision++;
                        _configService.Save(notify: false);
                        RebuildContent();
                        _onBinModified?.Invoke(_currentBin);
                    }
                }
                finally
                {
                    _suppressDeactivateClose = false;
                    Activate();
                }
            }, iconGlyph: "\uEB9F"));

            if (!string.IsNullOrWhiteSpace(item.CustomIconPath))
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Reset default icon", () =>
                {
                    if (_currentBin != null)
                    {
                        item.CustomIconPath = string.Empty;
                        item.CustomIconIndex = 0;
                        _currentBin.IconRevision++;
                        _configService.Save(notify: false);
                        RebuildContent();
                        _onBinModified?.Invoke(_currentBin);
                    }
                }, iconGlyph: "\uE777"));
            }

            string defaultName = BinItem.DeriveDisplayName(item.TargetPath);
            if (!isPackaged && !string.IsNullOrWhiteSpace(defaultName) &&
                !string.Equals(item.Name, defaultName, StringComparison.Ordinal))
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Reset default name", () =>
                {
                    item.Name = defaultName;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }, iconGlyph: "\uE777"));
            }

            if (index > 0)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Move earlier", () => MoveItem(index, index - 1), iconGlyph: "\uE74A"));
            }
            if (_currentBin != null && index < _currentBin.Items.Count - 1)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Move later", () => MoveItem(index, index + 1), iconGlyph: "\uE74B"));
            }

            menu.Items.Add(FluentTheme.CreateMenuSeparator());

            menu.Items.Add(FluentTheme.CreateMenuItem("Remove", () =>
            {
                if (_currentBin != null)
                {
                    _currentBin.Items.Remove(item);
                    _currentBin.IconRevision++;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }
            }, isDanger: true, iconGlyph: "\uE74D"));

            ShowManagedContextMenu(menu);
        }

        private void MoveItem(int fromIdx, int toIdx)
        {
            if (_currentBin == null || fromIdx < 0 || toIdx < 0 ||
                fromIdx >= _currentBin.Items.Count || toIdx >= _currentBin.Items.Count)
                return;

            BinItem item = _currentBin.Items[fromIdx];
            _currentBin.Items.RemoveAt(fromIdx);
            _currentBin.Items.Insert(toIdx, item);
            _currentBin.IconRevision++;
            _configService.Save(notify: false);
            RebuildContent();
            _onBinModified?.Invoke(_currentBin);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement btn))
                return;

            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;

            menu.Items.Add(FluentTheme.CreateMenuItem("Installed / Store App...", AddFromStartMenu, iconGlyph: "\uE737"));
            menu.Items.Add(FluentTheme.CreateMenuItem("App or Shortcut...", AddFilesViaDialog, iconGlyph: "\uE7C3"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Folder...", AddFolderViaDialog, iconGlyph: "\uE8B7"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Website / URL...", AddUrlViaDialog, iconGlyph: "\uE774"));
            menu.Items.Add(FluentTheme.CreateMenuSeparator());
            menu.Items.Add(BuildSystemActionsSubmenu(sysItem =>
            {
                if (_currentBin != null && sysItem != null)
                {
                    _currentBin.Items.Add(sysItem);
                    _currentBin.IconRevision++;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }
            }));

            ShowManagedContextMenu(menu);
        }

        private void AddFromStartMenu()
        {
            if (_currentBin == null)
                return;

            _suppressDeactivateClose = true;
            try
            {
                var picker = new StartMenuPickerWindow(_iconService) { Owner = this };
                if (picker.ShowDialog() == true && picker.SelectedApps.Count > 0)
                {
                    foreach (var app in picker.SelectedApps)
                    {
                        BinItem item = (app.IsPackagedApp || BinItem.IsPackagedApp(app.ShortcutPath))
                            ? BinItem.FromPackagedApp(app.ShortcutPath, app.Name)
                            : BinItem.FromPath(app.ShortcutPath);
                        item.Name = app.Name;
                        _currentBin.Items.Add(item);
                    }
                    _currentBin.IconRevision++;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(_currentBin);
                }
            }
            finally
            {
                _suppressDeactivateClose = false;
                Activate();
            }
        }

        private void AddFilesViaDialog()
        {
            if (_currentBin == null)
                return;

            _suppressDeactivateClose = true;
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Add Application, Shortcut, or File to Bin",
                    Multiselect = true,
                    DereferenceLinks = false,
                    Filter = "Apps & Shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All Files (*.*)|*.*"
                };

                if (dlg.ShowDialog(this) == true && dlg.FileNames.Length > 0)
                {
                    AddPathsToCurrentBin(dlg.FileNames);
                }
            }
            finally
            {
                _suppressDeactivateClose = false;
                Activate();
            }
        }

        private void AddFolderViaDialog()
        {
            if (_currentBin == null)
                return;

            _suppressDeactivateClose = true;
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Choose a folder to add to this Bin";
                    dlg.ShowNewFolderButton = false;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    {
                        AddPathsToCurrentBin(new[] { dlg.SelectedPath });
                    }
                }
            }
            finally
            {
                _suppressDeactivateClose = false;
                Activate();
            }
        }

        private void AddUrlViaDialog()
        {
            if (_currentBin == null)
                return;

            _suppressDeactivateClose = true;
            try
            {
                var dlg = new AddUrlDialogWindow(_iconService) { Owner = this };
                if (dlg.ShowDialog() == true && _currentBin != null)
                {
                    BinGroup owningBin = _currentBin;
                    BinItem urlItem = BinItem.FromUrl(
                        dlg.NormalizedUrl,
                        dlg.DisplayName,
                        dlg.CustomIconPath,
                        dlg.CustomIconIndex);

                    owningBin.Items.Add(urlItem);
                    owningBin.IconRevision++;
                    _configService.Save(notify: false);
                    RebuildContent();
                    _onBinModified?.Invoke(owningBin);

                    if (string.IsNullOrWhiteSpace(urlItem.CustomIconPath))
                    {
                        FetchPopupItemFavicon(owningBin, urlItem);
                    }
                }
            }
            finally
            {
                _suppressDeactivateClose = false;
                Activate();
            }
        }

        private void FetchPopupItemFavicon(BinGroup owningBin, BinItem item)
        {
            if (owningBin == null || item == null)
                return;

            _iconService.TryFetchWebsiteFaviconAsync(item, savedPngPath =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(savedPngPath))
                    {
                        item.CustomIconPath = savedPngPath;
                        item.CustomIconIndex = 0;
                        owningBin.IconRevision++;
                        _configService.Save(notify: false);
                        if (IsVisible && string.Equals(_currentBin?.Id, owningBin.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            RebuildContent();
                            UpdateLayout();
                        }
                        _onBinModified?.Invoke(owningBin);
                    }
                }));
            });
        }

        private void AddPathsToCurrentBin(IEnumerable<string> paths)
        {
            if (_currentBin == null || paths == null)
                return;

            bool added = false;
            foreach (string p in paths)
            {
                if (File.Exists(p) || Directory.Exists(p))
                {
                    _currentBin.Items.Add(BinItem.FromPath(p));
                    added = true;
                }
            }

            if (added)
            {
                _currentBin.IconRevision++;
                _configService.Save(notify: false);
                RebuildContent();
                _onBinModified?.Invoke(_currentBin);
            }
        }

        private void PositionAboveTaskbarAnchor(System.Drawing.Point anchorPx)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            uint dpi = hwnd != IntPtr.Zero ? Win32Interop.GetDpiForWindow(hwnd) : 96;
            if (dpi == 0) dpi = 96;
            double scale = dpi / 96.0;

            int popupW = Math.Max(48, (int)Math.Ceiling(ActualWidth * scale));
            int popupH = Math.Max(48, (int)Math.Ceiling(ActualHeight * scale));

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

            uint swpFlags = Win32Interop.SWP_NOSIZE | Win32Interop.SWP_SHOWWINDOW;
            if (_openedByHover)
            {
                swpFlags |= Win32Interop.SWP_NOACTIVATE;
            }

            if (hwnd != IntPtr.Zero)
            {
                Win32Interop.SetWindowPos(
                    hwnd,
                    Win32Interop.HWND_TOPMOST,
                    targetX,
                    targetY,
                    0,
                    0,
                    swpFlags);
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
                From = 10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(130),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _slideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);

            var fadeAnim = new DoubleAnimation
            {
                From = 0.25,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(110)
            };
            _animRoot.BeginAnimation(OpacityProperty, fadeAnim);
        }

        private void BinPopupWindow_Deactivated(object sender, EventArgs e)
        {
            if (_suppressDeactivateClose || _activeContextMenuCount > 0 || ShouldStayOpenOnDeactivate)
                return;

            if (IsVisible)
            {
                Win32Interop.GetCursorPos(out Win32Interop.POINT pt);
                _lastDeactivatedBinId = _currentBin?.Id;
                _lastDeactivatedTick = Environment.TickCount;
                _lastDeactivatedCursor = new System.Drawing.Point(pt.X, pt.Y);
                HidePopup();
            }
        }

        private void BinPopupWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(_filterText))
                {
                    _filterText = string.Empty;
                    _selectedTileIndex = -1;
                    RebuildAndRealign();
                }
                else
                {
                    HidePopup();
                }
                e.Handled = true;
                return;
            }

            if (key == Key.Tab && _availableTabs.Count > 1 && string.IsNullOrEmpty(_filterText))
            {
                int curTabIdx = _availableTabs.FindIndex(t => string.Equals(t, _activeSectionTab, StringComparison.OrdinalIgnoreCase));
                if (curTabIdx < 0) curTabIdx = 0;
                bool reverse = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                int nextTabIdx = reverse
                    ? (curTabIdx - 1 + _availableTabs.Count) % _availableTabs.Count
                    : (curTabIdx + 1) % _availableTabs.Count;
                _activeSectionTab = _availableTabs[nextTabIdx];
                _selectedTileIndex = -1;
                RebuildAndRealign();
                e.Handled = true;
                return;
            }

            if (key == Key.Back)
            {
                if (!string.IsNullOrEmpty(_filterText))
                {
                    _filterText = _filterText.Substring(0, _filterText.Length - 1);
                    RebuildAndRealign();
                    e.Handled = true;
                    return;
                }
                if (_subBinStack.Count > 0)
                {
                    NavigateBackSubBin();
                    e.Handled = true;
                    return;
                }
            }

            // Quick launch 1..9 when filter is empty (or Alt+1..9 anytime)
            bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            bool noCtrlWin = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == ModifierKeys.None;
            if (noCtrlWin && (string.IsNullOrEmpty(_filterText) || altHeld))
            {
                int digit = -1;
                if (key >= Key.D1 && key <= Key.D9)
                    digit = (int)(key - Key.D1);
                else if (key >= Key.NumPad1 && key <= Key.NumPad9)
                    digit = (int)(key - Key.NumPad1);

                if (digit >= 0 && digit < _displayedTiles.Count)
                {
                    ActivateOrLaunchTileItem(_displayedTiles[digit].Item);
                    e.Handled = true;
                    return;
                }
            }

            // Arrow-key tile navigation + Enter to launch
            int count = _displayedTiles.Count;
            if (count > 0)
            {
                int cols = Math.Max(1, _itemsGrid.Columns);
                if (key == Key.Right)
                {
                    _selectedTileIndex = _selectedTileIndex < 0 ? 0 : (_selectedTileIndex + 1) % count;
                    ApplySelectionVisuals();
                    e.Handled = true;
                    return;
                }
                if (key == Key.Left)
                {
                    _selectedTileIndex = _selectedTileIndex <= 0 ? count - 1 : _selectedTileIndex - 1;
                    ApplySelectionVisuals();
                    e.Handled = true;
                    return;
                }
                if (key == Key.Down)
                {
                    if (_selectedTileIndex < 0)
                        _selectedTileIndex = 0;
                    else if (_selectedTileIndex + cols < count)
                        _selectedTileIndex += cols;
                    ApplySelectionVisuals();
                    e.Handled = true;
                    return;
                }
                if (key == Key.Up)
                {
                    if (_selectedTileIndex < 0)
                        _selectedTileIndex = 0;
                    else if (_selectedTileIndex - cols >= 0)
                        _selectedTileIndex -= cols;
                    ApplySelectionVisuals();
                    e.Handled = true;
                    return;
                }
                if (key == Key.Enter)
                {
                    int targetIdx = _selectedTileIndex >= 0 && _selectedTileIndex < count ? _selectedTileIndex : 0;
                    ActivateOrLaunchTileItem(_displayedTiles[targetIdx].Item);
                    e.Handled = true;
                    return;
                }
            }
        }

        private void BinPopupWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
                return;

            // Ignore Ctrl/Alt shortcuts
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != ModifierKeys.None)
                return;

            char ch = e.Text[0];
            if (char.IsControl(ch))
                return;

            // Single digit 1..9 when filter is empty is handled by PreviewKeyDown quick-launch
            if (string.IsNullOrEmpty(_filterText) && ch >= '1' && ch <= '9')
                return;

            _filterText += e.Text;
            RebuildAndRealign();
            e.Handled = true;
        }

        private static bool TryExtractDroppedWebUrl(IDataObject data, out string normalizedUrl)
        {
            normalizedUrl = null;
            if (data == null)
                return false;

            try
            {
                string raw = null;
                if (data.GetDataPresent(DataFormats.UnicodeText))
                    raw = data.GetData(DataFormats.UnicodeText) as string;
                else if (data.GetDataPresent(DataFormats.Text))
                    raw = data.GetData(DataFormats.Text) as string;

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    string firstLine = raw.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(firstLine) && BinItem.IsWebUrl(firstLine))
                    {
                        normalizedUrl = firstLine.Trim();
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void BinPopupWindow_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(InternalTileDragFormat))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop) || TryExtractDroppedWebUrl(e.Data, out _))
            {
                bool pureSmartFolder = _currentBin != null &&
                                       (_currentBin.Items == null || _currentBin.Items.Count == 0) &&
                                       !string.IsNullOrWhiteSpace(_currentBin.LiveFolderPath) &&
                                       (Keyboard.Modifiers & ModifierKeys.Alt) == 0 &&
                                       e.Data.GetDataPresent(DataFormats.FileDrop);
                if (pureSmartFolder)
                {
                    bool ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    e.Effects = ctrlHeld ? DragDropEffects.Copy : DragDropEffects.Move;
                    ShowDropHint($"{(ctrlHeld ? "Copy" : "Move")} into {_currentBin.Name} folder (Alt = Pin to Bin)", highlightOuterBorder: true);
                }
                else
                {
                    e.Effects = DragDropEffects.Copy;
                    ShowDropHint("Drop here to add to Bin", highlightOuterBorder: true);
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
                ShowDropHint(null, highlightOuterBorder: false);
            }
            e.Handled = true;
        }

        private void BinPopupWindow_DragLeave(object sender, DragEventArgs e)
        {
            ShowDropHint(null, highlightOuterBorder: false);
        }

        private void BinPopupWindow_Drop(object sender, DragEventArgs e)
        {
            ShowDropHint(null, highlightOuterBorder: false);

            if (e.Data.GetDataPresent(InternalTileDragFormat))
            {
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    bool pureSmartFolder = _currentBin != null &&
                                           (_currentBin.Items == null || _currentBin.Items.Count == 0) &&
                                           !string.IsNullOrWhiteSpace(_currentBin.LiveFolderPath) &&
                                           (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
                    if (pureSmartFolder)
                    {
                        string liveDir = Environment.ExpandEnvironmentVariables(_currentBin.LiveFolderPath.Trim());
                        if (Directory.Exists(liveDir))
                        {
                            bool ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                            Win32Interop.ShellCopyOrMoveFilesToFolder(files, liveDir, copy: ctrlHeld);
                            _currentBin.IconRevision++;
                            RebuildContent();
                            _onBinModified?.Invoke(_currentBin);
                            e.Handled = true;
                            return;
                        }
                    }

                    AddPathsToCurrentBin(files);
                }
            }
            else if (_currentBin != null && TryExtractDroppedWebUrl(e.Data, out string droppedUrl))
            {
                BinGroup owningBin = _currentBin;
                BinItem urlItem = BinItem.FromUrl(droppedUrl);
                owningBin.Items.Add(urlItem);
                owningBin.IconRevision++;
                _configService.Save(notify: false);
                RebuildContent();
                _onBinModified?.Invoke(owningBin);
                FetchPopupItemFavicon(owningBin, urlItem);
            }
            e.Handled = true;
        }
    }
}
