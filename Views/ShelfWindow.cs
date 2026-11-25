using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;

namespace WinBins.Views
{
    /// <summary>
    /// Cursor-triggered "Shelf" launcher menu supporting both cascading vertical List mode and compact Grid mode.
    /// </summary>
    public class ShelfWindow : Window
    {
        private sealed class ShelfRowEntry
        {
            public Border RowBorder;
            public string Label;
            public BitmapSource Icon;
            public BinItem Item;
            public BinGroup SubmenuBin;
            public string SubmenuFolderPath;
            public Action OnActivate;
            public bool HasSubmenu => SubmenuBin != null || !string.IsNullOrEmpty(SubmenuFolderPath);
        }

        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly Action _onOpenSettings;
        private readonly ShelfWindow _parentShelf;
        private readonly List<ShelfRowEntry> _rows = new List<ShelfRowEntry>();
        private readonly Stack<BinGroup> _gridNavStack = new Stack<BinGroup>();
        private readonly DispatcherTimer _hoverSubmenuTimer;

        private Border _outerBorder;
        private DockPanel _rootDock;
        private Border _bottomBarBorder;
        private StackPanel _contentPanel;
        private ScrollViewer _scrollViewer;
        private ShelfWindow _activeChild;
        private ShelfRowEntry _hoveredSubmenuRow;
        private int _selectedIndex = -1;
        private bool _isClosingCascade;

        public bool IsSubmenu => _parentShelf != null;

        public ShelfWindow(
            ConfigService configService,
            IconService iconService,
            Action onOpenSettings = null,
            ShelfWindow parentShelf = null)
        {
            _configService = configService;
            _iconService = iconService;
            _onOpenSettings = onOpenSettings;
            _parentShelf = parentShelf;

            Title = "Shelf";
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ShowInTaskbar = false;
            Topmost = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            FluentTheme.ApplyGlobalResources(Resources);

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0)
            });

            _hoverSubmenuTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(140)
            };
            _hoverSubmenuTimer.Tick += (_, __) =>
            {
                _hoverSubmenuTimer.Stop();
                if (_hoveredSubmenuRow != null && _hoveredSubmenuRow.HasSubmenu && !_isClosingCascade)
                {
                    OpenSubmenuForRow(_hoveredSubmenuRow);
                }
            };

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                ApplyDwmBackdrop(hwnd);
            };

            Deactivated += OnShelfDeactivated;
            PreviewKeyDown += OnShelfPreviewKeyDown;
            Closed += (_, __) =>
            {
                _hoverSubmenuTimer.Stop();
                CloseChildSubmenu();
            };

            BuildShell();
        }

        private void BuildShell()
        {
            _outerBorder = new Border
            {
                Background = FluentTheme.PopupBg,
                BorderBrush = FluentTheme.SubtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                MinWidth = 240,
                MaxWidth = 420
            };

            _rootDock = new DockPanel();

            _bottomBarBorder = new Border
            {
                BorderBrush = FluentTheme.DividerBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(4, 3, 4, 4),
                Visibility = Visibility.Collapsed
            };
            DockPanel.SetDock(_bottomBarBorder, Dock.Bottom);
            _rootDock.Children.Add(_bottomBarBorder);

            _contentPanel = new StackPanel
            {
                Margin = new Thickness(4, 4, 4, 4)
            };
            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 520,
                Content = _contentPanel
            };

            _rootDock.Children.Add(_scrollViewer);
            _outerBorder.Child = _rootDock;
            Content = _outerBorder;
        }

        private void ApplyDwmBackdrop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                var config = _configService?.Current;
                BinBackdropStyle backdrop = config != null ? config.FlyoutBackdrop : BinBackdropStyle.Acrylic;
                int opacity = config != null ? config.FlyoutOpacityPercent : 82;
                Win32Interop.ApplyFlyoutBackdropAndOpacity(
                    hwnd,
                    (int)backdrop,
                    opacity,
                    FluentTheme.IsLightThemeActive);
            }
            catch
            {
            }
        }

        public void ShowAtCursor(int physicalX, int physicalY)
        {
            _gridNavStack.Clear();
            RebuildRootContent();
            ShowPositionedAtPhysicalPoint(physicalX, physicalY);
        }

        private void ShowAsSubmenuForBin(BinGroup bin, double anchorLeftDip, double anchorRightDip, double anchorTopDip)
        {
            RebuildBinListContent(bin);
            ShowPositionedNextToRow(anchorLeftDip, anchorRightDip, anchorTopDip);
        }

        private void ShowAsSubmenuForFolder(string folderPath, double anchorLeftDip, double anchorRightDip, double anchorTopDip)
        {
            RebuildFolderListContent(folderPath);
            ShowPositionedNextToRow(anchorLeftDip, anchorRightDip, anchorTopDip);
        }

        private void ShowPositionedAtPhysicalPoint(int physicalX, int physicalY)
        {
            UpdateLayout();
            Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

            PositionAtPhysicalPoint(physicalX, physicalY, GetDpiScaleForPoint(IntPtr.Zero));

            Win32Interop.AllowSetForegroundWindow(-1);
            Show();

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            UpdateLayout();
            PositionAtPhysicalPoint(physicalX, physicalY, GetDpiScaleForPoint(hwnd));
            _outerBorder?.InvalidateVisual();

            Win32Interop.SetForegroundWindow(hwnd);
            Activate();
            Focus();
        }

        private void PositionAtPhysicalPoint(int physicalX, int physicalY, double dpiScale)
        {
            if (dpiScale <= 0) dpiScale = 1.0;

            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(physicalX, physicalY));
            var workArea = screen.WorkingArea;
            double cursorX = physicalX / dpiScale;
            double cursorY = physicalY / dpiScale;

            double winW = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
            double winH = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
            if (winW <= 0) winW = 244;
            if (winH <= 0) winH = 200;

            double workLeft = workArea.Left / dpiScale;
            double workTop = workArea.Top / dpiScale;
            double workRight = workArea.Right / dpiScale;
            double workBottom = workArea.Bottom / dpiScale;

            double left = cursorX + 4;
            if (left + winW > workRight - 6)
                left = cursorX - winW - 4;
            if (left < workLeft + 6)
                left = workLeft + 6;

            double top;
            if (cursorY >= workBottom)
            {
                top = workBottom - winH - 8;
            }
            else
            {
                top = cursorY + 4;
                if (top + winH > workBottom - 6)
                    top = cursorY - winH - 4;
            }

            if (top < workTop + 6)
                top = workTop + 6;

            Left = left;
            Top = top;
        }

        public bool ContainsPhysicalPoint(int physicalX, int physicalY)
        {
            ShelfWindow current = this;
            while (current != null)
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(current).Handle;
                    if (hwnd != IntPtr.Zero && Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT rect))
                    {
                        if (physicalX >= rect.Left && physicalX <= rect.Right &&
                            physicalY >= rect.Top && physicalY <= rect.Bottom)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                }
                current = current._activeChild;
            }
            return false;
        }

        private void ShowPositionedNextToRow(double anchorLeftDip, double anchorRightDip, double anchorTopDip)
        {
            UpdateLayout();
            Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

            PositionNextToRow(anchorLeftDip, anchorRightDip, anchorTopDip, GetDpiScaleForPoint(IntPtr.Zero));

            Show();

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            UpdateLayout();
            PositionNextToRow(anchorLeftDip, anchorRightDip, anchorTopDip, GetDpiScaleForPoint(hwnd));
            _outerBorder?.InvalidateVisual();
            Activate();
        }

        private void PositionNextToRow(double anchorLeftDip, double anchorRightDip, double anchorTopDip, double dpiScale)
        {
            if (dpiScale <= 0) dpiScale = 1.0;

            double winW = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
            double winH = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
            if (winW <= 0) winW = 230;
            if (winH <= 0) winH = 180;

            var workArea = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)(anchorRightDip * dpiScale), (int)(anchorTopDip * dpiScale))).WorkingArea;

            double left = anchorRightDip + 2;
            double top = anchorTopDip - 4;

            double workLeft = workArea.Left / dpiScale;
            double workTop = workArea.Top / dpiScale;
            double workRight = workArea.Right / dpiScale;
            double workBottom = workArea.Bottom / dpiScale;

            if (left + winW > workRight - 6)
                left = anchorLeftDip - winW - 2;
            if (left < workLeft + 6)
                left = workLeft + 6;

            if (top + winH > workBottom - 6)
                top = workBottom - winH - 6;
            if (top < workTop + 6)
                top = workTop + 6;

            Left = left;
            Top = top;
        }

        private double GetDpiScaleForPoint(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    uint dpi = Win32Interop.GetDpiForWindow(hwnd);
                    if (dpi >= 96)
                    {
                        return dpi / 96.0;
                    }
                }

                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }
            catch
            {
            }

            return VisualTreeHelper.GetDpi(this).DpiScaleX;
        }

        private List<BinGroup> GetShelfBins()
        {
            var bins = _configService?.Current?.Bins;
            if (bins == null)
                return new List<BinGroup>();

            return bins
                .Where(b => b != null && b.IncludeInShelf)
                .OrderBy(b => b.TaskbarOrder)
                .ToList();
        }

        private void RebuildRootContent()
        {
            _rows.Clear();
            _contentPanel.Children.Clear();
            _selectedIndex = -1;

            var shelfBins = GetShelfBins();
            if (shelfBins.Count == 0)
            {
                BuildEmptyState();
                return;
            }

            _outerBorder.MinWidth = 244;
            _outerBorder.MaxWidth = 340;
            BuildListRootContent(shelfBins);
        }

        private void BuildEmptyState()
        {
            _bottomBarBorder.Visibility = Visibility.Collapsed;
            var stack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };

            stack.Children.Add(new TextBlock
            {
                Text = "Your Shelf is empty",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                Margin = new Thickness(0, 0, 0, 4)
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Enable \"Add to Shelf\" on any Bin in Settings, or add all your Bins now.",
                FontSize = 11.5,
                Foreground = FluentTheme.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 240,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var addAllBtn = FluentTheme.CreateButton("Add all Bins", isPrimary: true);
            addAllBtn.Click += (_, __) =>
            {
                if (_configService?.Current?.Bins != null)
                {
                    foreach (var bin in _configService.Current.Bins)
                    {
                        if (bin != null)
                            bin.IncludeInShelf = true;
                    }
                    _configService.Save();
                    RebuildRootContent();
                }
            };
            addAllBtn.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(addAllBtn);

            var customizeBtn = FluentTheme.CreateButton("Settings", isPrimary: false);
            customizeBtn.Click += (_, __) =>
            {
                CloseEntireCascade();
                _onOpenSettings?.Invoke();
            };
            btnRow.Children.Add(customizeBtn);

            stack.Children.Add(btnRow);
            _contentPanel.Children.Add(stack);
        }

        private void BuildListRootContent(List<BinGroup> shelfBins)
        {
            var rootBins = shelfBins.Where(b =>
                b.ShelfPlacement == ShelfBinPlacement.TopLevel ||
                (b.ShelfPlacement == ShelfBinPlacement.Submenu && b.ShelfShowItemsAtRoot)).ToList();
            var bottomBarBins = shelfBins.Where(b => b.ShelfPlacement == ShelfBinPlacement.BottomBar).ToList();
            var submenuBins = shelfBins.Where(b =>
                b.ShelfPlacement == ShelfBinPlacement.Submenu && !b.ShelfShowItemsAtRoot).ToList();

            bool addedTopItems = false;
            foreach (var bin in rootBins)
            {
                var items = ResolveBinItems(bin);
                foreach (var item in items)
                {
                    AddItemRow(item);
                    addedTopItems = true;
                }
            }

            if (addedTopItems && submenuBins.Count > 0)
            {
                AddDivider();
            }

            foreach (var bin in submenuBins)
            {
                AddBinSubmenuRow(bin);
            }

            bool hasListContent = addedTopItems || submenuBins.Count > 0;
            _scrollViewer.Visibility = hasListContent ? Visibility.Visible : Visibility.Collapsed;

            if (bottomBarBins.Count > 0)
            {
                _bottomBarBorder.BorderThickness = hasListContent ? new Thickness(0, 1, 0, 0) : new Thickness(0);
                BuildBottomBinsBar(bottomBarBins);
            }
            else
            {
                _bottomBarBorder.Child = null;
                _bottomBarBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildBottomBinsBar(List<BinGroup> bottomBarBins)
        {
            var cfg = _configService?.Current;
            int cols = cfg?.ShelfBottomColumns ?? 4;
            int maxRows = cfg?.ShelfBottomMaxRows ?? 2;
            int iconSize = cfg?.ShelfBottomIconSize ?? 20;
            BinFlyoutDensity density = cfg?.ShelfBottomDensity ?? BinFlyoutDensity.Normal;
            bool showLabels = cfg?.ShelfBottomShowLabels ?? true;

            if (cols < 2) cols = 2;
            if (maxRows < 1) maxRows = 1;
            if (iconSize < 16) iconSize = 16;

            double padH = density == BinFlyoutDensity.Compact ? 3.0 : density == BinFlyoutDensity.Spacious ? 8.0 : 5.0;
            double padV = density == BinFlyoutDensity.Compact ? 4.0 : density == BinFlyoutDensity.Spacious ? 9.0 : 6.0;
            double tileWidth = showLabels
                ? Math.Max(56.0, iconSize + 36.0 + (padH * 2.0))
                : Math.Max(36.0, iconSize + 14.0 + (padH * 2.0));
            double tileSlotWidth = tileWidth + 2.0; // +2 for Margin(1,0,1,0)
            double rowHeight = iconSize + (padV * 2.0) + (showLabels ? 22.0 : 4.0);
            double fixedBarWidth = cols * tileSlotWidth;
            bool hasListContent = _scrollViewer.Visibility == Visibility.Visible;

            var allBottomItems = new List<BinItem>();
            foreach (var bin in bottomBarBins)
            {
                var items = ResolveBinItems(bin);
                if (items != null && items.Count > 0)
                {
                    allBottomItems.AddRange(items);
                }
            }

            if (allBottomItems.Count == 0)
            {
                _bottomBarBorder.Child = null;
                _bottomBarBorder.Visibility = Visibility.Collapsed;
                return;
            }

            int pageSize = cols * maxRows;
            int pageCount = (int)Math.Ceiling(allBottomItems.Count / (double)pageSize);
            bool hasPagination = pageCount > 1;
            double dotsWidth = hasPagination ? 18.0 : 0.0;

            _outerBorder.MinWidth = hasListContent ? 244 : Math.Max(140, fixedBarWidth + dotsWidth + 8);
            _outerBorder.MaxWidth = Math.Max(340, fixedBarWidth + dotsWidth + 16);

            var containerGrid = new Grid
            {
                MinWidth = fixedBarWidth + dotsWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            containerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (hasPagination)
            {
                containerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            int visibleRows = Math.Min(maxRows, (int)Math.Ceiling(allBottomItems.Count / (double)cols));
            var barGrid = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = cols,
                Rows = visibleRows,
                Height = visibleRows * rowHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(barGrid, 0);
            containerGrid.Children.Add(barGrid);

            var dots = new List<System.Windows.Shapes.Ellipse>();
            int currentPage = 0;

            Action<int> renderPage = null;
            renderPage = pageIndex =>
            {
                currentPage = Math.Max(0, Math.Min(pageCount - 1, pageIndex));
                barGrid.Children.Clear();

                int start = currentPage * pageSize;
                int end = Math.Min(allBottomItems.Count, start + pageSize);
                for (int i = start; i < end; i++)
                {
                    var captured = allBottomItems[i];
                    string label = string.IsNullOrWhiteSpace(captured.Name) ? captured.TargetPath : captured.Name;
                    BitmapSource icon = _iconService?.GetItemImageSource(captured);
                    barGrid.Children.Add(CreateBottomBinItemTile(
                        label,
                        icon,
                        tileWidth,
                        iconSize,
                        padH,
                        padV,
                        showLabels,
                        () => LaunchAndMaybeClose(captured)));
                }

                for (int d = 0; d < dots.Count; d++)
                {
                    dots[d].Fill = d == currentPage ? FluentTheme.TextPrimary : FluentTheme.SubtleBorder;
                    dots[d].Opacity = d == currentPage ? 0.95 : 0.65;
                }
            };

            if (hasPagination)
            {
                var dotsPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                };

                for (int p = 0; p < pageCount; p++)
                {
                    int targetPage = p;
                    var dot = new System.Windows.Shapes.Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Margin = new Thickness(2, 3, 2, 3),
                        Cursor = Cursors.Hand,
                        ToolTip = $"Page {targetPage + 1} of {pageCount}"
                    };
                    dot.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        renderPage(targetPage);
                    };
                    dots.Add(dot);
                    dotsPanel.Children.Add(dot);
                }

                Grid.SetColumn(dotsPanel, 1);
                containerGrid.Children.Add(dotsPanel);

                containerGrid.PreviewMouseWheel += (_, e) =>
                {
                    if (e.Delta < 0 && currentPage < pageCount - 1)
                    {
                        renderPage(currentPage + 1);
                    }
                    else if (e.Delta > 0 && currentPage > 0)
                    {
                        renderPage(currentPage - 1);
                    }
                    e.Handled = true;
                };
            }

            renderPage(0);

            _bottomBarBorder.MouseEnter += (_, __) =>
            {
                _hoverSubmenuTimer.Stop();
                _hoveredSubmenuRow = null;
                CloseChildSubmenu();
                SetSelectedIndex(-1);
            };

            _bottomBarBorder.Child = containerGrid;
            _bottomBarBorder.Visibility = Visibility.Visible;
        }

        private Border CreateBottomBinItemTile(
            string label,
            BitmapSource icon,
            double tileWidth,
            int iconSize,
            double padH,
            double padV,
            bool showLabels,
            Action onClick)
        {
            var tile = new Border
            {
                MinWidth = tileWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Transparent,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(padH, padV, padH, padV),
                Margin = new Thickness(1, 0, 1, 0),
                Cursor = Cursors.Hand,
                ToolTip = label
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (icon != null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = icon,
                    Width = iconSize,
                    Height = iconSize,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, showLabels ? 4 : 0)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                stack.Children.Add(img);
            }

            if (showLabels)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = label ?? string.Empty,
                    FontSize = 10.5,
                    Foreground = FluentTheme.TextPrimary,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = Math.Max(36.0, tileWidth - (padH * 2.0))
                });
            }

            tile.Child = stack;
            tile.MouseEnter += (_, __) =>
            {
                _hoverSubmenuTimer.Stop();
                _hoveredSubmenuRow = null;
                CloseChildSubmenu();
                SetSelectedIndex(-1);
                tile.Background = FluentTheme.SubtleHoverBg;
            };
            tile.MouseLeave += (_, __) => tile.Background = System.Windows.Media.Brushes.Transparent;
            tile.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick?.Invoke();
            };

            return tile;
        }

        private void RebuildBinListContent(BinGroup bin)
        {
            _rows.Clear();
            _contentPanel.Children.Clear();
            _selectedIndex = -1;
            _outerBorder.MinWidth = 220;
            _outerBorder.MaxWidth = 340;

            if (bin == null)
                return;

            var items = ResolveBinItems(bin);
            if (items.Count == 0)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "(Empty Bin)",
                    FontSize = 12,
                    Foreground = FluentTheme.TextMuted,
                    Margin = new Thickness(12, 8, 12, 8)
                });
                return;
            }

            foreach (var item in items)
            {
                AddItemRow(item);
            }
        }

        private void RebuildFolderListContent(string folderPath)
        {
            _rows.Clear();
            _contentPanel.Children.Clear();
            _selectedIndex = -1;
            _outerBorder.MinWidth = 220;
            _outerBorder.MaxWidth = 340;

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "(Folder not found)",
                    FontSize = 12,
                    Foreground = FluentTheme.TextMuted,
                    Margin = new Thickness(12, 8, 12, 8)
                });
                return;
            }

            var entries = new List<BinItem>();
            try
            {
                foreach (var dir in Directory.GetDirectories(folderPath).OrderBy(Path.GetFileName).Take(20))
                {
                    entries.Add(new BinItem
                    {
                        Name = Path.GetFileName(dir),
                        TargetPath = dir,
                        Kind = BinItemKind.Folder
                    });
                }

                foreach (var file in Directory.GetFiles(folderPath).OrderBy(Path.GetFileName).Take(25))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(name))
                        name = Path.GetFileName(file);

                    entries.Add(new BinItem
                    {
                        Name = name,
                        TargetPath = file,
                        Kind = BinItemKind.FileOrUrl
                    });
                }
            }
            catch
            {
            }

            string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = folderPath;

            AddActionRow("\uE8B7", $"Open {folderName}", () =>
            {
                LaunchAndMaybeClose(new BinItem
                {
                    Name = folderName,
                    TargetPath = folderPath,
                    Kind = BinItemKind.Folder
                });
            });

            if (entries.Count == 0)
            {
                AddDivider();
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "(Empty folder)",
                    FontSize = 12,
                    Foreground = FluentTheme.TextMuted,
                    Margin = new Thickness(12, 8, 12, 8)
                });
                return;
            }

            AddDivider();
            foreach (var item in entries)
            {
                AddItemRow(item);
            }
        }

        private List<BinItem> ResolveBinItems(BinGroup bin)
        {
            if (bin == null)
                return new List<BinItem>();

            if (!string.IsNullOrWhiteSpace(bin.LiveFolderPath) || bin.LiveFolderRecentMode)
            {
                return LauncherService.GetLiveFolderItems(bin) ?? new List<BinItem>();
            }

            return bin.Items ?? new List<BinItem>();
        }

        private void AddItemRow(BinItem item)
        {
            if (item == null)
                return;

            BinGroup subBin = null;
            string subFolder = null;

            if (item.Kind == BinItemKind.SubBin)
            {
                string subBinId = BinItem.GetSubBinId(item.TargetPath);
                subBin = _configService?.Current?.Bins?.FirstOrDefault(b => string.Equals(b.Id, subBinId, StringComparison.OrdinalIgnoreCase));
            }
            else if (item.Kind == BinItemKind.Folder && !string.IsNullOrWhiteSpace(item.TargetPath) && Directory.Exists(item.TargetPath))
            {
                subFolder = item.TargetPath;
            }

            BitmapSource icon = _iconService?.GetItemImageSource(item);
            var entry = new ShelfRowEntry
            {
                Label = string.IsNullOrWhiteSpace(item.Name) ? item.TargetPath : item.Name,
                Icon = icon,
                Item = item,
                SubmenuBin = subBin,
                SubmenuFolderPath = subFolder
            };

            entry.OnActivate = () =>
            {
                if (!string.IsNullOrEmpty(entry.SubmenuFolderPath))
                {
                    LaunchAndMaybeClose(item);
                    return;
                }

                if (entry.HasSubmenu)
                {
                    OpenSubmenuForRow(entry);
                    return;
                }

                LaunchAndMaybeClose(item);
            };

            AddMenuRow(entry);
        }

        private void AddBinSubmenuRow(BinGroup bin)
        {
            if (bin == null)
                return;

            BitmapSource icon = GetBinIconSource(bin);
            var entry = new ShelfRowEntry
            {
                Label = bin.Name,
                Icon = icon,
                SubmenuBin = bin
            };
            entry.OnActivate = () => OpenSubmenuForRow(entry);

            AddMenuRow(entry);
        }

        private void AddActionRow(string glyph, string label, Action onClick, bool isDangerTint = false)
        {
            var row = CreateRowContainer();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconText = new TextBlock
            {
                Text = glyph,
                FontFamily = FluentTheme.IconFont,
                FontSize = 13,
                Foreground = isDangerTint ? FluentTheme.DangerBrush : FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(iconText, 0);
            grid.Children.Add(iconText);

            var titleText = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(titleText, 1);
            grid.Children.Add(titleText);

            row.Child = grid;

            var entry = new ShelfRowEntry
            {
                RowBorder = row,
                Label = label,
                OnActivate = onClick
            };
            WireRowInteractions(entry);
            _rows.Add(entry);
            _contentPanel.Children.Add(row);
        }

        private void AddMenuRow(ShelfRowEntry entry)
        {
            var row = CreateRowContainer();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (entry.Icon != null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = entry.Icon,
                    Width = 18,
                    Height = 18,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                Grid.SetColumn(img, 0);
                grid.Children.Add(img);
            }

            var label = new TextBlock
            {
                Text = entry.Label ?? string.Empty,
                FontSize = 13,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            if (entry.HasSubmenu)
            {
                var chevron = new TextBlock
                {
                    Text = "\uE76C",
                    FontFamily = FluentTheme.IconFont,
                    FontSize = 10,
                    Foreground = FluentTheme.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 2, 0)
                };
                Grid.SetColumn(chevron, 2);
                grid.Children.Add(chevron);
            }

            row.Child = grid;
            entry.RowBorder = row;
            WireRowInteractions(entry);
            _rows.Add(entry);
            _contentPanel.Children.Add(row);
        }

        private Border CreateRowContainer()
        {
            return new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand
            };
        }

        private void WireRowInteractions(ShelfRowEntry entry)
        {
            int idx = _rows.Count;
            entry.RowBorder.MouseEnter += (_, __) =>
            {
                SetSelectedIndex(idx);
                _hoverSubmenuTimer.Stop();

                if (entry.HasSubmenu)
                {
                    _hoveredSubmenuRow = entry;
                    _hoverSubmenuTimer.Start();
                }
                else
                {
                    _hoveredSubmenuRow = null;
                    CloseChildSubmenu();
                }
            };

            entry.RowBorder.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                entry.OnActivate?.Invoke();
            };
        }

        private void AddDivider()
        {
            _contentPanel.Children.Add(new Border
            {
                Height = 1,
                Background = FluentTheme.DividerBrush,
                Margin = new Thickness(0, 4, 0, 4)
            });
        }

        private void BuildGridRootContent(List<BinGroup> shelfBins)
        {
            _contentPanel.Children.Clear();
            _rows.Clear();
            _selectedIndex = -1;

            if (_gridNavStack.Count > 0)
            {
                BuildGridBinDetail(_gridNavStack.Peek());
                return;
            }

            // Header
            var header = new DockPanel { Margin = new Thickness(6, 4, 6, 8) };
            var closeBtn = FluentTheme.CreateIconButton("\uE711", "Close Shelf", isDanger: true);
            closeBtn.Click += (_, __) => CloseEntireCascade();
            DockPanel.SetDock(closeBtn, Dock.Right);
            header.Children.Add(closeBtn);

            var settingsBtn = FluentTheme.CreateIconButton("\uE713", "Customize Shelf");
            settingsBtn.Click += (_, __) =>
            {
                CloseEntireCascade();
                _onOpenSettings?.Invoke();
            };
            settingsBtn.Margin = new Thickness(0, 0, 4, 0);
            DockPanel.SetDock(settingsBtn, Dock.Right);
            header.Children.Add(settingsBtn);

            header.Children.Add(new TextBlock
            {
                Text = "Shelf",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            _contentPanel.Children.Add(header);

            var wrap = new WrapPanel { MaxWidth = 380 };

            var rootBins = shelfBins.Where(b => b.ShelfShowItemsAtRoot).ToList();
            foreach (var bin in rootBins)
            {
                foreach (var item in ResolveBinItems(bin))
                {
                    wrap.Children.Add(CreateGridTile(
                        string.IsNullOrWhiteSpace(item.Name) ? item.TargetPath : item.Name,
                        _iconService?.GetItemImageSource(item),
                        () =>
                        {
                            if (item.Kind == BinItemKind.SubBin)
                            {
                                string subId = BinItem.GetSubBinId(item.TargetPath);
                                var subBin = _configService?.Current?.Bins?.FirstOrDefault(b => string.Equals(b.Id, subId, StringComparison.OrdinalIgnoreCase));
                                if (subBin != null)
                                {
                                    _gridNavStack.Push(subBin);
                                    BuildGridRootContent(shelfBins);
                                    return;
                                }
                            }
                            LaunchAndMaybeClose(item);
                        }));
                }
            }

            foreach (var bin in shelfBins.Where(b => !b.ShelfShowItemsAtRoot))
            {
                var targetBin = bin;
                wrap.Children.Add(CreateGridTile(
                    targetBin.Name,
                    GetBinIconSource(targetBin),
                    () =>
                    {
                        _gridNavStack.Push(targetBin);
                        BuildGridRootContent(shelfBins);
                    }));
            }

            _contentPanel.Children.Add(wrap);
        }

        private void BuildGridBinDetail(BinGroup bin)
        {
            var header = new DockPanel { Margin = new Thickness(4, 4, 4, 8) };
            var backBtn = FluentTheme.CreateIconButton("\uE72B", "Back");
            backBtn.Click += (_, __) =>
            {
                if (_gridNavStack.Count > 0)
                    _gridNavStack.Pop();
                BuildGridRootContent(GetShelfBins());
            };
            backBtn.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(backBtn, Dock.Left);
            header.Children.Add(backBtn);

            var closeBtn = FluentTheme.CreateIconButton("\uE711", "Close Shelf", isDanger: true);
            closeBtn.Click += (_, __) => CloseEntireCascade();
            DockPanel.SetDock(closeBtn, Dock.Right);
            header.Children.Add(closeBtn);

            header.Children.Add(new TextBlock
            {
                Text = bin.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            _contentPanel.Children.Add(header);

            var wrap = new WrapPanel { MaxWidth = 380 };
            foreach (var item in ResolveBinItems(bin))
            {
                var captured = item;
                wrap.Children.Add(CreateGridTile(
                    string.IsNullOrWhiteSpace(captured.Name) ? captured.TargetPath : captured.Name,
                    _iconService?.GetItemImageSource(captured),
                    () =>
                    {
                        if (captured.Kind == BinItemKind.SubBin)
                        {
                            string subId = BinItem.GetSubBinId(captured.TargetPath);
                            var subBin = _configService?.Current?.Bins?.FirstOrDefault(b => string.Equals(b.Id, subId, StringComparison.OrdinalIgnoreCase));
                            if (subBin != null)
                            {
                                _gridNavStack.Push(subBin);
                                BuildGridRootContent(GetShelfBins());
                                return;
                            }
                        }
                        LaunchAndMaybeClose(captured);
                    }));
            }

            _contentPanel.Children.Add(wrap);
        }

        private Border CreateGridTile(string label, BitmapSource icon, Action onClick)
        {
            var tile = new Border
            {
                Width = 84,
                Padding = new Thickness(6, 8, 6, 8),
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            if (icon != null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = icon,
                    Width = 32,
                    Height = 32,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                stack.Children.Add(img);
            }

            stack.Children.Add(new TextBlock
            {
                Text = label ?? string.Empty,
                FontSize = 11,
                Foreground = FluentTheme.TextPrimary,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 72
            });

            tile.Child = stack;

            tile.MouseEnter += (_, __) => tile.Background = FluentTheme.SubtleHoverBg;
            tile.MouseLeave += (_, __) => tile.Background = System.Windows.Media.Brushes.Transparent;
            tile.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick?.Invoke();
            };

            return tile;
        }

        private BitmapSource GetBinIconSource(BinGroup bin)
        {
            if (bin == null || _iconService == null)
                return null;

            try
            {
                using (Bitmap bmp = _iconService.CreateBinBitmap(bin))
                {
                    if (bmp == null)
                        return null;
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        var src = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        return src;
                    }
                    finally
                    {
                        Win32Interop.DeleteObject(hBitmap);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private void OpenSubmenuForRow(ShelfRowEntry entry)
        {
            if (entry == null || !entry.HasSubmenu || entry.RowBorder == null)
                return;

            CloseChildSubmenu();

            System.Windows.Point topLeft = entry.RowBorder.PointToScreen(new System.Windows.Point(0, 0));
            double dpiScale = GetDpiScaleForPoint(new WindowInteropHelper(this).Handle);
            double leftDip = topLeft.X / dpiScale;
            double topDip = topLeft.Y / dpiScale;
            double rightDip = leftDip + entry.RowBorder.ActualWidth;

            var child = new ShelfWindow(_configService, _iconService, _onOpenSettings, parentShelf: this);
            _activeChild = child;

            if (entry.SubmenuBin != null)
            {
                child.ShowAsSubmenuForBin(entry.SubmenuBin, leftDip, rightDip, topDip);
            }
            else if (!string.IsNullOrEmpty(entry.SubmenuFolderPath))
            {
                child.ShowAsSubmenuForFolder(entry.SubmenuFolderPath, leftDip, rightDip, topDip);
            }
        }

        private void CloseChildSubmenu()
        {
            if (_activeChild != null)
            {
                var child = _activeChild;
                _activeChild = null;
                try
                {
                    child._isClosingCascade = true;
                    child.Close();
                }
                catch
                {
                }
            }
        }

        public void CloseEntireCascade()
        {
            ShelfWindow root = this;
            while (root._parentShelf != null)
            {
                root = root._parentShelf;
            }

            root._isClosingCascade = true;
            root.CloseChildSubmenu();
            try
            {
                root.Close();
            }
            catch
            {
            }
        }

        private void LaunchAndMaybeClose(BinItem item)
        {
            if (item == null)
                return;

            bool closeOnLaunch = _configService?.Current?.ShelfCloseOnLaunch ?? true;
            if (closeOnLaunch)
            {
                CloseEntireCascade();
            }

            LauncherService.LaunchItem(item);
        }

        private void SetSelectedIndex(int idx)
        {
            if (_selectedIndex >= 0 && _selectedIndex < _rows.Count)
            {
                _rows[_selectedIndex].RowBorder.Background = System.Windows.Media.Brushes.Transparent;
            }

            _selectedIndex = idx;

            if (_selectedIndex >= 0 && _selectedIndex < _rows.Count)
            {
                _rows[_selectedIndex].RowBorder.Background = FluentTheme.SubtleHoverBg;
            }
        }

        private void OnShelfPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_parentShelf != null)
                {
                    _parentShelf.CloseChildSubmenu();
                    _parentShelf.Activate();
                }
                else if (_gridNavStack.Count > 0)
                {
                    _gridNavStack.Pop();
                    BuildGridRootContent(GetShelfBins());
                }
                else
                {
                    CloseEntireCascade();
                }
                return;
            }

            if (_rows.Count == 0)
                return;

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                int next = (_selectedIndex + 1) % _rows.Count;
                SetSelectedIndex(next);
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                int prev = _selectedIndex <= 0 ? _rows.Count - 1 : _selectedIndex - 1;
                SetSelectedIndex(prev);
            }
            else if (e.Key == Key.Right && _selectedIndex >= 0 && _selectedIndex < _rows.Count)
            {
                var row = _rows[_selectedIndex];
                if (row.HasSubmenu)
                {
                    e.Handled = true;
                    OpenSubmenuForRow(row);
                }
            }
            else if (e.Key == Key.Left && _parentShelf != null)
            {
                e.Handled = true;
                _parentShelf.CloseChildSubmenu();
                _parentShelf.Activate();
            }
            else if (e.Key == Key.Enter && _selectedIndex >= 0 && _selectedIndex < _rows.Count)
            {
                e.Handled = true;
                _rows[_selectedIndex].OnActivate?.Invoke();
            }
        }

        private void OnShelfDeactivated(object sender, EventArgs e)
        {
            if (_isClosingCascade || !(_configService?.Current?.ShelfCloseOnClickOutside ?? true))
                return;

            // Defer check so focus transfer to a child/parent ShelfWindow completes first
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_isClosingCascade)
                    return;

                if (!IsAnyWindowInCascadeActive())
                {
                    CloseEntireCascade();
                }
            }));
        }

        private bool IsAnyWindowInCascadeActive()
        {
            ShelfWindow root = this;
            while (root._parentShelf != null)
            {
                root = root._parentShelf;
            }

            var current = root;
            while (current != null)
            {
                if (current.IsActive)
                    return true;
                current = current._activeChild;
            }

            return false;
        }
    }
}
