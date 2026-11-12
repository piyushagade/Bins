using System;
using System.Collections.Generic;
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
using Microsoft.Win32;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;

namespace WinBins.Views
{
    public class BinPopupWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly RunningAppsTaskbarManager _runningAppsManager;
        private readonly Action<BinGroup> _onCustomizeRequested;
        private readonly Action<BinGroup> _onBinModified;

        private readonly Border _outerBorder;
        private readonly Grid _animRoot;
        private readonly TranslateTransform _slideTransform;
        private readonly DockPanel _headerDock;
        private readonly StackPanel _titleGroup;
        private readonly Image _flyoutHeaderIcon;
        private readonly TextBlock _headerTitle;
        private readonly Button _addBtn;
        private readonly Button _editBtn;
        private readonly ScrollViewer _gridScroll;
        private readonly UniformGrid _itemsGrid;
        private readonly Border _emptyStatePanel;
        private readonly Border _dropOverlay;

        private BinGroup _currentBin;
        private IntPtr _priorForegroundHwnd;
        private bool _suppressDeactivateClose;

        // Toggle-guard state so clicking the same taskbar icon while open closes cleanly instead of reopening
        private string _lastDeactivatedBinId;
        private long _lastDeactivatedTick;
        private System.Drawing.Point _lastDeactivatedCursor;

        public string CurrentBinId => _currentBin?.Id;

        public BinPopupWindow(
            ConfigService configService,
            IconService iconService,
            RunningAppsTaskbarManager runningAppsManager,
            Action<BinGroup> onCustomizeRequested,
            Action<BinGroup> onBinModified)
        {
            _configService = configService;
            _iconService = iconService;
            _runningAppsManager = runningAppsManager;
            _onCustomizeRequested = onCustomizeRequested;
            _onBinModified = onBinModified;

            Title = "Bin";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = FluentTheme.PopupBg;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            AllowDrop = true;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            });

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Interop.ApplyWindows11WindowStyling(hwnd);
                int one = 1;
                Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_DISALLOW_PEEK, ref one, sizeof(int));
                Win32Interop.DwmSetWindowAttribute(hwnd, Win32Interop.DWMWA_EXCLUDED_FROM_PEEK, ref one, sizeof(int));
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
                Padding = new Thickness(10, 10, 10, 10),
                MinWidth = 100
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

            _addBtn = FluentTheme.CreateIconButton("\uE710", "Add item", size: 28, iconSize: 12);
            _addBtn.Click += AddButton_Click;

            _editBtn = FluentTheme.CreateIconButton("\uE713", "Customize Bin", size: 28, iconSize: 12.5);
            _editBtn.Margin = new Thickness(2, 0, 0, 0);
            _editBtn.Click += (_, __) =>
            {
                BinGroup target = _currentBin;
                HidePopup();
                _onCustomizeRequested?.Invoke(target);
            };

            actionButtons.Children.Add(_addBtn);
            actionButtons.Children.Add(_editBtn);
            DockPanel.SetDock(actionButtons, Dock.Right);
            _headerDock.Children.Add(actionButtons);

            _titleGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent
            };

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

            // Empty state panel
            _emptyStatePanel = new Border
            {
                Padding = new Thickness(22, 24, 22, 24),
                Visibility = Visibility.Collapsed
            };
            var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            emptyStack.Children.Add(new TextBlock
            {
                Text = "Empty Bin",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            emptyStack.Children.Add(new TextBlock
            {
                Text = "Drop items here or right-click to add",
                FontSize = 12,
                Foreground = FluentTheme.TextMuted,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            _emptyStatePanel.Child = emptyStack;
            mainStack.Children.Add(_emptyStatePanel);

            // Items grid inside a max-height scroll viewer
            _itemsGrid = new UniformGrid
            {
                Columns = 4,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            _gridScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420,
                Content = _itemsGrid
            };
            mainStack.Children.Add(_gridScroll);

            _animRoot.Children.Add(mainStack);

            // Drag & drop highlight overlay
            _dropOverlay = new Border
            {
                Background = FluentTheme.BrushFromHex("#DC202020"),
                BorderBrush = FluentTheme.AccentBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "Drop to add",
                    FontFamily = FluentTheme.DisplayFont,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FluentTheme.TextPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            _animRoot.Children.Add(_dropOverlay);

            _outerBorder.Child = _animRoot;
            Content = _outerBorder;

            Deactivated += BinPopupWindow_Deactivated;
            PreviewKeyDown += BinPopupWindow_PreviewKeyDown;
            DragEnter += BinPopupWindow_DragEnter;
            DragLeave += (_, __) => _dropOverlay.Visibility = Visibility.Collapsed;
            Drop += BinPopupWindow_Drop;
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

            ShowForBin(bin, anchorScreenPx);
        }

        public void ShowForBin(BinGroup bin, System.Drawing.Point anchorScreenPx)
        {
            if (bin == null)
                return;

            IntPtr currentFg = Win32Interop.GetForegroundWindow();
            IntPtr ownHwnd = new WindowInteropHelper(this).Handle;
            if (currentFg != IntPtr.Zero && currentFg != ownHwnd)
            {
                _priorForegroundHwnd = currentFg;
            }

            _currentBin = bin;
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
                Win32Interop.ApplyWindows11WindowStyling(hwnd);
                Win32Interop.SetForegroundWindow(hwnd);
            }
            Activate();
            Focus();

            PlayEntranceAnimation();
        }

        public void RefreshIfShowing(BinGroup bin)
        {
            if (IsVisible && bin != null && string.Equals(_currentBin?.Id, bin.Id, StringComparison.OrdinalIgnoreCase))
            {
                _currentBin = bin;
                RebuildContent();
                UpdateLayout();
            }
        }

        public void HidePopup()
        {
            _dropOverlay.Visibility = Visibility.Collapsed;
            if (IsVisible)
            {
                Hide();
            }
        }

        private void GetDensityMetrics(out double iconSize, out double baseTileW, out double baseTileH, out double fontSize, out double labelMaxH)
        {
            BinFlyoutDensity density = _currentBin?.Density ?? BinFlyoutDensity.Normal;
            switch (density)
            {
                case BinFlyoutDensity.Compact:
                    iconSize = 28;
                    baseTileW = 62;
                    baseTileH = 64;
                    fontSize = 10.5;
                    labelMaxH = 28;
                    break;

                case BinFlyoutDensity.Spacious:
                    iconSize = 44;
                    baseTileW = 94;
                    baseTileH = 98;
                    fontSize = 12.0;
                    labelMaxH = 34;
                    break;

                default:
                    iconSize = 36;
                    baseTileW = 78;
                    baseTileH = 82;
                    fontSize = 11.5;
                    labelMaxH = 32;
                    break;
            }
        }

        private void RebuildContent()
        {
            if (_currentBin == null)
                return;

            bool showTitle = _currentBin.ShowFlyoutTitle;
            bool showAdd = _currentBin.ShowFlyoutAddButton;
            bool showEdit = _currentBin.ShowFlyoutEditButton;
            bool anyHeader = showTitle || showAdd || showEdit;

            _headerDock.Visibility = anyHeader ? Visibility.Visible : Visibility.Collapsed;
            _titleGroup.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
            _addBtn.Visibility = showAdd ? Visibility.Visible : Visibility.Collapsed;
            _editBtn.Visibility = showEdit ? Visibility.Visible : Visibility.Collapsed;

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

            int pad = Math.Max(0, Math.Min(20, _currentBin.ItemPadding));
            double outerSide = Math.Max(6, 6 + pad * 0.6);
            double outerTop = anyHeader ? 9 : outerSide;
            _outerBorder.Padding = new Thickness(outerSide, outerTop, outerSide, outerSide);

            int count = _currentBin.Items?.Count ?? 0;
            _itemsGrid.Children.Clear();

            if (count == 0)
            {
                _emptyStatePanel.Visibility = Visibility.Visible;
                _gridScroll.Visibility = Visibility.Collapsed;
                return;
            }

            _emptyStatePanel.Visibility = Visibility.Collapsed;
            _gridScroll.Visibility = Visibility.Visible;

            int cols = Math.Max(1, Math.Min(10, _currentBin.Columns));
            _itemsGrid.Columns = Math.Min(cols, Math.Max(1, count));

            GetDensityMetrics(out double iconSize, out double baseTileW, out double baseTileH, out double fontSize, out double labelMaxH);
            double tileW = baseTileW + pad * 1.2;
            double tileH = baseTileH + pad * 1.2;
            double marginHalf = Math.Round(pad / 2.0, 1);
            double fullRowHeight = tileH + marginHalf * 2.0;

            if (_currentBin.MaxRows > 0)
            {
                _gridScroll.MaxHeight = _currentBin.MaxRows * fullRowHeight + 2;
            }
            else
            {
                _gridScroll.MaxHeight = 440;
            }

            for (int i = 0; i < _currentBin.Items.Count; i++)
            {
                BinItem item = _currentBin.Items[i];
                int index = i;
                _itemsGrid.Children.Add(CreateItemTile(item, index, iconSize, tileW, tileH, marginHalf, pad, fontSize, labelMaxH));
            }
        }

        private UIElement CreateItemTile(
            BinItem item,
            int index,
            double iconSize,
            double tileW,
            double tileH,
            double marginHalf,
            int pad,
            double fontSize,
            double labelMaxH)
        {
            List<RunningWindowEntry> runningWindows = _runningAppsManager?.GetRunningWindowsForItem(item) ?? new List<RunningWindowEntry>();
            bool isRunning = runningWindows.Count > 0;
            bool wasActiveApp = isRunning && runningWindows.Any(w => w.Hwnd == _priorForegroundHwnd && !w.IsMinimized);

            double innerVPad = Math.Max(3, 4 + pad * 0.35);
            string tooltipText = isRunning
                ? (runningWindows.Count == 1
                    ? $"{item.Name} (Running)"
                    : $"{item.Name} ({runningWindows.Count} windows open)")
                : item.Name;

            var tile = new Border
            {
                Width = tileW,
                Height = tileH,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(0),
                Margin = new Thickness(marginHalf),
                Cursor = Cursors.Hand,
                ToolTip = tooltipText
            };

            var tileOverlayGrid = new Grid();

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, innerVPad, 4, Math.Max(4, innerVPad))
            };

            var iconImg = new Image
            {
                Width = iconSize,
                Height = iconSize,
                Source = _iconService.GetItemImageSource(item),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, Math.Max(3, 4 + pad * 0.2))
            };
            RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.HighQuality);
            stack.Children.Add(iconImg);

            var label = new TextBlock
            {
                Text = item.Name,
                Width = Math.Max(44, tileW - 10),
                FontSize = fontSize,
                Foreground = FluentTheme.TextPrimary,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = labelMaxH
            };
            stack.Children.Add(label);
            tileOverlayGrid.Children.Add(stack);

            if (isRunning)
            {
                // Subtle Windows 11 taskbar running pill indicator anchored at the bottom edge
                var runningPill = new Border
                {
                    Width = wasActiveApp ? 16 : 8,
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = wasActiveApp ? FluentTheme.AccentBrush : FluentTheme.TextMuted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 2),
                    IsHitTestVisible = false
                };
                tileOverlayGrid.Children.Add(runningPill);
            }

            tile.Child = tileOverlayGrid;

            tile.MouseEnter += (_, __) => tile.Background = FluentTheme.SubtleHoverBg;
            tile.MouseLeave += (_, __) => tile.Background = Brushes.Transparent;

            tile.MouseLeftButtonUp += (s, e) =>
            {
                bool ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                bool keepOpen = shiftHeld || !(_configService.Current?.ClosePopupOnLaunch ?? true);

                List<RunningWindowEntry> liveWindows = !ctrlHeld
                    ? (_runningAppsManager?.GetRunningWindowsForItem(item) ?? new List<RunningWindowEntry>())
                    : null;

                if (!keepOpen)
                {
                    HidePopup();
                }

                if (liveWindows != null && liveWindows.Count > 0)
                {
                    // Activate existing running window (cycling to the next window if multiple are open and first was active)
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

                e.Handled = true;
            };

            tile.MouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    if (_configService.Current?.ClosePopupOnLaunch ?? true)
                    {
                        HidePopup();
                    }
                    LauncherService.LaunchItem(item);
                    e.Handled = true;
                }
            };

            tile.MouseRightButtonUp += (s, e) =>
            {
                OpenTileContextMenu(tile, item, index);
                e.Handled = true;
            };

            return tile;
        }

        private void OuterBorder_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentBin == null)
                return;

            OpenHeaderContextMenu(_outerBorder);
            e.Handled = true;
        }

        private void OpenHeaderContextMenu(FrameworkElement anchor)
        {
            if (_currentBin == null)
                return;

            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.MousePoint;

            menu.Items.Add(FluentTheme.CreateMenuItem("Add Installed App...", AddFromStartMenu, iconGlyph: "\uE737"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Add App or Shortcut...", AddFilesViaDialog, iconGlyph: "\uE7C3"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Add Folder...", AddFolderViaDialog, iconGlyph: "\uE8B7"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Add Website / URL...", AddUrlViaDialog, iconGlyph: "\uE774"));
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

            _suppressDeactivateClose = true;
            menu.Closed += (_, __) =>
            {
                _suppressDeactivateClose = false;
                if (IsVisible)
                    Activate();
            };
            menu.IsOpen = true;
        }

        private void OpenTileContextMenu(FrameworkElement target, BinItem item, int index)
        {
            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;

            bool isUrl = item.Kind == BinItemKind.WebUrl || BinItem.IsWebUrl(item.TargetPath);
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
                }
                else
                {
                    menu.Items.Add(FluentTheme.CreateMenuItem("Open", () =>
                    {
                        HidePopup();
                        LauncherService.LaunchItem(item);
                    }, iconGlyph: "\uE8A7"));
                }

                if (item.Kind != BinItemKind.Folder)
                {
                    menu.Items.Add(FluentTheme.CreateMenuItem("Run as administrator", () =>
                    {
                        HidePopup();
                        LauncherService.LaunchItem(item, runAsAdmin: true);
                    }, iconGlyph: "\uE7EF"));
                }

                menu.Items.Add(FluentTheme.CreateMenuItem("Open file location", () =>
                {
                    HidePopup();
                    LauncherService.OpenItemLocation(item);
                }, iconGlyph: "\uE8B7"));
            }

            menu.Items.Add(FluentTheme.CreateMenuSeparator());

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
            if (!string.IsNullOrWhiteSpace(defaultName) &&
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

            _suppressDeactivateClose = true;
            menu.Closed += (_, __) =>
            {
                _suppressDeactivateClose = false;
                if (IsVisible)
                    Activate();
            };
            menu.IsOpen = true;
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

            menu.Items.Add(FluentTheme.CreateMenuItem("Installed App...", AddFromStartMenu, iconGlyph: "\uE737"));
            menu.Items.Add(FluentTheme.CreateMenuItem("App or Shortcut...", AddFilesViaDialog, iconGlyph: "\uE7C3"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Folder...", AddFolderViaDialog, iconGlyph: "\uE8B7"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Website / URL...", AddUrlViaDialog, iconGlyph: "\uE774"));

            _suppressDeactivateClose = true;
            menu.Closed += (_, __) =>
            {
                _suppressDeactivateClose = false;
                if (IsVisible)
                    Activate();
            };
            menu.IsOpen = true;
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
                        var item = BinItem.FromPath(app.ShortcutPath);
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

            int popupW = Math.Max(100, (int)Math.Ceiling(ActualWidth * scale));
            int popupH = Math.Max(80, (int)Math.Ceiling(ActualHeight * scale));

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
                    Win32Interop.SWP_NOSIZE | Win32Interop.SWP_SHOWWINDOW);
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
            if (_suppressDeactivateClose)
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
            if (e.Key == Key.Escape)
            {
                HidePopup();
                e.Handled = true;
            }
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

        private void BinPopupWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || TryExtractDroppedWebUrl(e.Data, out _))
            {
                e.Effects = DragDropEffects.Copy;
                _dropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void BinPopupWindow_Drop(object sender, DragEventArgs e)
        {
            _dropOverlay.Visibility = Visibility.Collapsed;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
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
