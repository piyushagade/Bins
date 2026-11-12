using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;

namespace WinBins.Views
{
    public class MainWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly TaskbarBinHostManager _taskbarManager;
        private readonly BinPopupWindow _popupWindow;
        private readonly RunningAppsTaskbarManager _runningAppsManager;

        private enum WorkspacePane
        {
            Bin,
            Donate,
            Settings,
            About
        }

        private BinGroup _selectedBin;
        private WorkspacePane _activePane = WorkspacePane.Bin;
        private bool _flyoutOptionsExpanded;
        private bool _updatingUi;
        private string _editingItemId;

        // Sidebar controls
        private StackPanel _binsListPanel;
        private Border _donateNavRow;
        private Border _donateNavPill;
        private Border _settingsNavRow;
        private Border _settingsNavPill;
        private Border _aboutNavRow;
        private Border _aboutNavPill;

        // Right sheet views
        private Grid _binWorkspaceView;
        private ScrollViewer _donateView;
        private ScrollViewer _settingsView;
        private ScrollViewer _aboutView;
        private Border _emptyBinsView;
        private Border _dropHighlightOverlay;

        // Bin Header & Toolbar controls
        private Image _headerBinIcon;
        private TextBox _binNameBox;
        private TextBlock _binMetaSubtitle;
        private Button _addToTaskbarBtn;
        private Button _pinGuideBtn;
        private TaskbarPinGuideDialog _activePinGuideDialog;
        private Border _gridSegmentBtn;
        private TextBlock _gridSegmentText;
        private Border _customSegmentBtn;
        private TextBlock _customSegmentText;
        private Button _changeCustomIconBtn;

        // Floating Flyout Options Right Sidebar controls
        private Grid _flyoutDrawerOverlay;
        private Border _flyoutOptionsCard;
        private TranslateTransform _flyoutDrawerTranslate;
        private TextBlock _flyoutDrawerSubtitle;
        private StackPanel _customIconRowWrapper;
        private Image _customIconPreviewImg;
        private TextBlock _columnsValueText;
        private TextBlock _rowsValueText;
        private Border _densityCompactBtn;
        private TextBlock _densityCompactText;
        private Border _densityNormalBtn;
        private TextBlock _densityNormalText;
        private Border _densitySpaciousBtn;
        private TextBlock _densitySpaciousText;
        private TextBlock _paddingValueText;
        private Image _flyoutIconPreviewImg;
        private TextBlock _flyoutIconPlaceholderGlyph;
        private TextBlock _flyoutIconBtnLabel;
        private Button _clearFlyoutIconBtn;
        private CheckBox _showTitleToggle;
        private CheckBox _showAddToggle;
        private CheckBox _showEditToggle;
        private CheckBox _hideRunningAppsBinToggle;

        // Items list
        private StackPanel _itemsListPanel;

        // Global Settings page controls
        private CheckBox _closeOnLaunchToggle;
        private CheckBox _hideRunningAppsGlobalToggle;
        private CheckBox _minimizeToTrayToggle;
        private CheckBox _startupToggle;
        private CheckBox _showPinGuideToggle;

        public bool AllowRealClose { get; set; }

        public MainWindow(
            ConfigService configService,
            IconService iconService,
            TaskbarBinHostManager taskbarManager,
            BinPopupWindow popupWindow,
            RunningAppsTaskbarManager runningAppsManager = null)
        {
            _configService = configService;
            _iconService = iconService;
            _taskbarManager = taskbarManager;
            _popupWindow = popupWindow;
            _runningAppsManager = runningAppsManager;

            Title = "Bins";
            using (var appBmp = Win32Interop.CreateGroupMosaicBitmap(null))
            {
                Icon = IconService.ConvertBitmapToBitmapSource(appBmp);
            }

            Width = 880;
            Height = 640;
            MinWidth = 720;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = FluentTheme.ShellBg;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            AllowDrop = true;

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                // Lock caption bar to #202020 (COLORREF 0x00202020) matching SidebarBg/ShellBg with no focus Mica/accent tint
                Win32Interop.ApplyWindows11WindowStyling(hwnd, useMica: false, captionColorRef: 0x00202020);
            };

            BuildLayout();

            _selectedBin = _configService.Current?.Bins?.FirstOrDefault();
            RefreshFullUi();

            DragEnter += MainWindow_DragEnter;
            DragLeave += (_, __) => _dropHighlightOverlay.Visibility = Visibility.Collapsed;
            Drop += MainWindow_Drop;
            Closing += MainWindow_Closing;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _flyoutOptionsExpanded && string.IsNullOrEmpty(_editingItemId))
            {
                SetFlyoutOptionsOpen(false);
                e.Handled = true;
            }
        }

        public void SelectAndFocusBin(BinGroup bin)
        {
            if (bin != null)
            {
                _selectedBin = bin;
                _activePane = WorkspacePane.Bin;
            }
            RefreshFullUi();

            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        public void RefreshFullUi()
        {
            _updatingUi = true;
            try
            {
                var bins = _configService.Current?.Bins ?? new List<BinGroup>();
                if (_selectedBin != null && !bins.Contains(_selectedBin))
                {
                    _selectedBin = bins.FirstOrDefault(b => string.Equals(b.Id, _selectedBin.Id, StringComparison.OrdinalIgnoreCase))
                                   ?? bins.FirstOrDefault();
                }
                else if (_selectedBin == null && bins.Count > 0)
                {
                    _selectedBin = bins[0];
                }

                RebuildSidebar();
                RebuildRightPane();

                if (_configService.Current != null)
                {
                    _closeOnLaunchToggle.IsChecked = _configService.Current.ClosePopupOnLaunch;
                    if (_hideRunningAppsGlobalToggle != null)
                    {
                        _hideRunningAppsGlobalToggle.IsChecked = _configService.Current.HideRunningAppsOnTaskbar;
                    }
                    _minimizeToTrayToggle.IsChecked = _configService.Current.MinimizeToTrayOnClose;
                    _startupToggle.IsChecked = _configService.Current.LaunchAtStartup;
                    if (_showPinGuideToggle != null)
                    {
                        _showPinGuideToggle.IsChecked = !_configService.Current.HidePinToTaskbarPrompt;
                    }
                }
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void BuildLayout()
        {
            var rootGrid = new Grid { Background = FluentTheme.ShellBg };
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ================= LEFT NAVIGATION PANE =================
            var navGrid = new Grid { Margin = new Thickness(10, 12, 8, 10) };
            navGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            navGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            navGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var navHeader = new DockPanel { Margin = new Thickness(10, 2, 2, 8) };
            var newBinIconBtn = FluentTheme.CreateButton("New", isSubtle: true, padding: new Thickness(8, 4, 9, 4), iconGlyph: "\uE710");
            newBinIconBtn.ToolTip = "Create a new Bin";
            newBinIconBtn.Click += CreateNewBin_Click;
            DockPanel.SetDock(newBinIconBtn, Dock.Right);
            navHeader.Children.Add(newBinIconBtn);

            navHeader.Children.Add(new TextBlock
            {
                Text = "Bins",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetRow(navHeader, 0);
            navGrid.Children.Add(navHeader);

            var binsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _binsListPanel = new StackPanel();
            binsScroll.Content = _binsListPanel;
            Grid.SetRow(binsScroll, 1);
            navGrid.Children.Add(binsScroll);

            var navFooter = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            navFooter.Children.Add(new Border
            {
                Height = 1,
                Background = FluentTheme.DividerBrush,
                Margin = new Thickness(8, 0, 8, 6)
            });

            _donateNavRow = CreateFooterNavRow(
                "\uEB51",
                FluentTheme.DangerBrush,
                "Donate",
                WorkspacePane.Donate,
                out _donateNavPill);
            navFooter.Children.Add(_donateNavRow);

            _settingsNavRow = CreateFooterNavRow(
                "\uE713",
                FluentTheme.TextSecondary,
                "Settings",
                WorkspacePane.Settings,
                out _settingsNavPill);
            navFooter.Children.Add(_settingsNavRow);

            _aboutNavRow = CreateFooterNavRow(
                "\uE946",
                FluentTheme.TextSecondary,
                "About Bins",
                WorkspacePane.About,
                out _aboutNavPill);
            navFooter.Children.Add(_aboutNavRow);

            Grid.SetRow(navFooter, 2);
            navGrid.Children.Add(navFooter);

            Grid.SetColumn(navGrid, 0);
            rootGrid.Children.Add(navGrid);

            // ================= RIGHT WINUI 3 CONTENT SHEET =================
            var contentSheet = new Border
            {
                Background = FluentTheme.LayerBg,
                BorderBrush = FluentTheme.DividerBrush,
                BorderThickness = new Thickness(1, 1, 0, 0),
                CornerRadius = new CornerRadius(8, 0, 0, 0),
                Margin = new Thickness(0, 6, 0, 0)
            };

            var sheetHost = new Grid();

            _emptyBinsView = BuildEmptyBinsView();
            sheetHost.Children.Add(_emptyBinsView);

            _binWorkspaceView = BuildBinWorkspaceView();
            sheetHost.Children.Add(_binWorkspaceView);

            _flyoutDrawerOverlay = BuildFlyoutOptionsDrawerOverlay();
            sheetHost.Children.Add(_flyoutDrawerOverlay);

            _donateView = BuildDonateView();
            sheetHost.Children.Add(_donateView);

            _settingsView = BuildSettingsView();
            sheetHost.Children.Add(_settingsView);

            _aboutView = BuildAboutView();
            sheetHost.Children.Add(_aboutView);

            _dropHighlightOverlay = new Border
            {
                Background = FluentTheme.BrushFromHex("#D8202020"),
                BorderBrush = FluentTheme.AccentBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(18),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            var dropStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dropStack.Children.Add(FluentTheme.CreateFluentIcon("\uE710", 24, FluentTheme.AccentBrush));
            dropStack.Children.Add(new TextBlock
            {
                Text = "Drop to add to Bin",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                Margin = new Thickness(0, 8, 0, 0)
            });
            _dropHighlightOverlay.Child = dropStack;
            sheetHost.Children.Add(_dropHighlightOverlay);

            contentSheet.Child = sheetHost;
            Grid.SetColumn(contentSheet, 1);
            rootGrid.Children.Add(contentSheet);

            Content = rootGrid;
        }

        private Border BuildEmptyBinsView()
        {
            var container = new Border { Visibility = Visibility.Collapsed };
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "No Bins yet",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Create a Bin to group apps, folders, and shortcuts on your taskbar.",
                FontSize = 13,
                Foreground = FluentTheme.TextMuted,
                Margin = new Thickness(0, 6, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var btn = FluentTheme.CreateButton("Create Bin", isPrimary: true, iconGlyph: "\uE710");
            btn.HorizontalAlignment = HorizontalAlignment.Center;
            btn.Click += CreateNewBin_Click;
            stack.Children.Add(btn);

            container.Child = stack;
            return container;
        }

        private Grid BuildBinWorkspaceView()
        {
            var grid = new Grid { Margin = new Thickness(28, 24, 28, 20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Minimal Toolbar
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Scrollable Options + Items List

            // --- ROW 0: Minimal Page Header ---
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconTile = new Border
            {
                Width = 52,
                Height = 52,
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.SurfaceStroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 14, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Taskbar icon preview — Click to test flyout"
            };
            _headerBinIcon = new Image { Width = 38, Height = 38 };
            RenderOptions.SetBitmapScalingMode(_headerBinIcon, BitmapScalingMode.HighQuality);
            iconTile.Child = _headerBinIcon;
            iconTile.MouseEnter += (_, __) => iconTile.Background = FluentTheme.CardHoverBg;
            iconTile.MouseLeave += (_, __) => iconTile.Background = FluentTheme.CardBg;
            iconTile.MouseLeftButtonUp += (_, __) => OpenPopupPreview();
            Grid.SetColumn(iconTile, 0);
            headerGrid.Children.Add(iconTile);

            var titleStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 16, 0)
            };
            _binNameBox = FluentTheme.CreateInlineTitleTextBox("");
            _binNameBox.MaxWidth = 340;
            _binNameBox.HorizontalAlignment = HorizontalAlignment.Left;
            _binNameBox.TextChanged += BinNameBox_TextChanged;
            _binNameBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Escape)
                {
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };
            titleStack.Children.Add(_binNameBox);

            _binMetaSubtitle = new TextBlock
            {
                FontSize = 12,
                Foreground = FluentTheme.TextMuted,
                Margin = new Thickness(7, 1, 0, 0)
            };
            titleStack.Children.Add(_binMetaSubtitle);
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _addToTaskbarBtn = FluentTheme.CreateButton(
                "Add to Taskbar",
                isPrimary: true,
                padding: new Thickness(13, 7, 14, 7),
                iconGlyph: "\uE718");
            _addToTaskbarBtn.Margin = new Thickness(0, 0, 8, 0);
            _addToTaskbarBtn.ToolTip = "Place this Bin on your Windows 11 Taskbar";
            _addToTaskbarBtn.Click += AddToTaskbar_Click;
            actionRow.Children.Add(_addToTaskbarBtn);

            _pinGuideBtn = FluentTheme.CreateButton(
                "How to pin",
                padding: new Thickness(11, 7, 12, 7),
                iconGlyph: "\uE718");
            _pinGuideBtn.Margin = new Thickness(0, 0, 8, 0);
            _pinGuideBtn.ToolTip = "How to lock this Bin's taskbar position and hide its hover preview";
            _pinGuideBtn.Click += (_, __) => ShowTaskbarPinGuide(_selectedBin, forceShow: true);
            actionRow.Children.Add(_pinGuideBtn);

            var previewBtn = FluentTheme.CreateButton("Preview", padding: new Thickness(12, 7, 12, 7));
            previewBtn.ToolTip = "Open this Bin's flyout popup";
            previewBtn.Click += (_, __) => OpenPopupPreview();
            actionRow.Children.Add(previewBtn);

            var moreBtn = FluentTheme.CreateIconButton("\uE712", "More options", size: 32, iconSize: 14);
            moreBtn.Margin = new Thickness(4, 0, 0, 0);
            moreBtn.Click += BinMoreMenu_Click;
            actionRow.Children.Add(moreBtn);

            Grid.SetColumn(actionRow, 2);
            headerGrid.Children.Add(actionRow);

            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            // --- ROW 1: Minimal Inline Toolbar ---
            var toolbarDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            var rightControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // "Flyout options" button floating right next to "+ Add" with matching 32px height
            var flyoutExpanderContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            flyoutExpanderContent.Children.Add(new TextBlock
            {
                Text = "\uE713",
                FontFamily = FluentTheme.IconFont,
                FontSize = 12.5,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            });
            flyoutExpanderContent.Children.Add(new TextBlock
            {
                Text = "Flyout options",
                FontSize = 12.5,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });

            var flyoutExpanderBtn = FluentTheme.CreateButton("", padding: new Thickness(12, 0, 13, 0));
            flyoutExpanderBtn.Height = 32;
            flyoutExpanderBtn.Content = flyoutExpanderContent;
            flyoutExpanderBtn.ToolTip = "Open floating sidebar to customize taskbar icon, flyout layout, and header controls";
            flyoutExpanderBtn.Click += (_, __) => SetFlyoutOptionsOpen(!_flyoutOptionsExpanded);
            rightControls.Children.Add(flyoutExpanderBtn);

            var addBtn = FluentTheme.CreateButton("Add", padding: new Thickness(12, 0, 13, 0), iconGlyph: "\uE710");
            addBtn.Height = 32;
            addBtn.Margin = new Thickness(8, 0, 0, 0);
            addBtn.Click += AddMenuButton_Click;
            rightControls.Children.Add(addBtn);

            DockPanel.SetDock(rightControls, Dock.Right);
            toolbarDock.Children.Add(rightControls);

            var itemsSectionTitle = new TextBlock
            {
                Text = "Items",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };
            toolbarDock.Children.Add(itemsSectionTitle);

            Grid.SetRow(toolbarDock, 1);
            grid.Children.Add(toolbarDock);

            // --- ROW 2: Clean WinUI 3 Item List ---
            var itemsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 4, 0)
            };

            _itemsListPanel = new StackPanel();
            itemsScroll.Content = _itemsListPanel;
            Grid.SetRow(itemsScroll, 2);
            grid.Children.Add(itemsScroll);

            return grid;
        }

        private Grid BuildFlyoutOptionsDrawerOverlay()
        {
            var overlay = new Grid
            {
                Visibility = Visibility.Collapsed
            };

            // Light-dismiss backdrop scrim
            var scrim = new Border
            {
                Background = FluentTheme.BrushFromHex("#30000000"),
                CornerRadius = new CornerRadius(8, 0, 0, 0)
            };
            scrim.MouseLeftButtonDown += (s, e) =>
            {
                SetFlyoutOptionsOpen(false);
                e.Handled = true;
            };
            overlay.Children.Add(scrim);

            _flyoutDrawerTranslate = new TranslateTransform(0, 0);

            _flyoutOptionsCard = new Border
            {
                Width = 356,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 12, 12, 12),
                Background = FluentTheme.PopupBg,
                BorderBrush = FluentTheme.SubtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                RenderTransform = _flyoutDrawerTranslate,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 26,
                    ShadowDepth = 6,
                    Direction = 180,
                    Opacity = 0.48
                }
            };
            // Swallow mouse clicks inside the floating sidebar so they never bubble to scrim
            _flyoutOptionsCard.MouseLeftButtonDown += (s, e) => e.Handled = true;

            var drawerRoot = new Grid();
            drawerRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            drawerRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Divider
            drawerRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scrollable settings

            // --- Floating Sidebar Header ---
            var headerDock = new DockPanel
            {
                Margin = new Thickness(18, 12, 12, 10),
                LastChildFill = true
            };

            var closeBtn = FluentTheme.CreateIconButton("\uE711", "Close Flyout options", size: 28, iconSize: 11);
            closeBtn.VerticalAlignment = VerticalAlignment.Center;
            closeBtn.Click += (_, __) => SetFlyoutOptionsOpen(false);
            DockPanel.SetDock(closeBtn, Dock.Right);
            headerDock.Children.Add(closeBtn);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Flyout options",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary
            });
            _flyoutDrawerSubtitle = new TextBlock
            {
                FontSize = 11.5,
                Foreground = FluentTheme.TextMuted,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titleStack.Children.Add(_flyoutDrawerSubtitle);
            headerDock.Children.Add(titleStack);

            Grid.SetRow(headerDock, 0);
            drawerRoot.Children.Add(headerDock);

            var topDivider = CreateSettingsRowDivider();
            Grid.SetRow(topDivider, 1);
            drawerRoot.Children.Add(topDivider);

            // --- Scrollable Grouped WinUI 3 Settings Sections ---
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(14, 10, 14, 12)
            };

            var sectionsStack = new StackPanel();

            // ==================== SECTION 1: APPEARANCE ====================
            sectionsStack.Children.Add(CreateDrawerSectionLabel("Appearance", isFirst: true));

            var appearanceCard = CreateDrawerGroupCard();
            var appearanceRows = new StackPanel();

            // Taskbar icon mode selector ([ Grid | Custom ]) moved into Flyout Options
            var segmentedTrack = new Border
            {
                Background = FluentTheme.SegmentedTrackBg,
                BorderBrush = FluentTheme.SurfaceStroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                ToolTip = "Taskbar button icon style"
            };
            var segRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _gridSegmentBtn = CreateSegmentOption("\uF0E2", "Grid", out _gridSegmentText, () => SetIconMode(BinIconMode.GridMosaic));
            _customSegmentBtn = CreateSegmentOption("\uEB9F", "Custom", out _customSegmentText, () => SetIconMode(BinIconMode.CustomIcon));
            segRow.Children.Add(_gridSegmentBtn);
            segRow.Children.Add(_customSegmentBtn);
            segmentedTrack.Child = segRow;

            appearanceRows.Children.Add(CreateFlyoutSettingRow("Bin icon", segmentedTrack));

            // Conditional Custom Icon picker sub-row (visible when Custom is active)
            _customIconRowWrapper = new StackPanel { Visibility = Visibility.Collapsed };
            _customIconRowWrapper.Children.Add(CreateSettingsRowDivider());

            var customBtnInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _customIconPreviewImg = new Image
            {
                Width = 15,
                Height = 15,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_customIconPreviewImg, BitmapScalingMode.HighQuality);
            customBtnInner.Children.Add(_customIconPreviewImg);
            customBtnInner.Children.Add(new TextBlock
            {
                Text = "Choose...",
                FontSize = 12,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            });

            _changeCustomIconBtn = FluentTheme.CreateButton("", padding: new Thickness(10, 4, 10, 4));
            _changeCustomIconBtn.Content = customBtnInner;
            _changeCustomIconBtn.ToolTip = "Pick a custom taskbar icon for this Bin";
            _changeCustomIconBtn.Click += ChooseCustomIcon_Click;
            _customIconRowWrapper.Children.Add(CreateFlyoutSettingRow("Custom icon", _changeCustomIconBtn));
            appearanceRows.Children.Add(_customIconRowWrapper);

            appearanceRows.Children.Add(CreateSettingsRowDivider());

            // Flyout Header icon row
            var iconRightGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var flyoutBtnInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _flyoutIconPreviewImg = new Image
            {
                Width = 15,
                Height = 15,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_flyoutIconPreviewImg, BitmapScalingMode.HighQuality);
            flyoutBtnInner.Children.Add(_flyoutIconPreviewImg);

            _flyoutIconPlaceholderGlyph = new TextBlock
            {
                Text = "\uEB9F",
                FontFamily = FluentTheme.IconFont,
                FontSize = 12,
                Foreground = FluentTheme.TextMuted,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            flyoutBtnInner.Children.Add(_flyoutIconPlaceholderGlyph);

            _flyoutIconBtnLabel = new TextBlock
            {
                Text = "Set icon...",
                FontSize = 12,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            flyoutBtnInner.Children.Add(_flyoutIconBtnLabel);

            var flyoutIconBtn = FluentTheme.CreateButton("", padding: new Thickness(10, 4, 10, 4));
            flyoutIconBtn.Content = flyoutBtnInner;
            flyoutIconBtn.ToolTip = "Choose an optional icon shown before the Bin name in the flyout";
            flyoutIconBtn.Click += ChooseFlyoutIcon_Click;
            iconRightGroup.Children.Add(flyoutIconBtn);

            _clearFlyoutIconBtn = FluentTheme.CreateIconButton("\uE711", "Remove header icon", size: 26, iconSize: 10);
            _clearFlyoutIconBtn.Margin = new Thickness(6, 0, 0, 0);
            _clearFlyoutIconBtn.Click += (_, __) =>
            {
                if (_selectedBin != null)
                {
                    _selectedBin.FlyoutIconPath = string.Empty;
                    _selectedBin.FlyoutIconIndex = 0;
                    SaveAndSyncSelectedBin(bumpIcon: false);
                }
            };
            iconRightGroup.Children.Add(_clearFlyoutIconBtn);

            appearanceRows.Children.Add(CreateFlyoutSettingRow("Header icon", iconRightGroup));
            appearanceRows.Children.Add(CreateSettingsRowDivider());

            _hideRunningAppsBinToggle = FluentTheme.CreateToggleCheckBox(null, false);
            _hideRunningAppsBinToggle.ToolTip = "Hide duplicate taskbar icons when apps in this Bin are open";
            _hideRunningAppsBinToggle.Checked += HideRunningAppsBin_Changed;
            _hideRunningAppsBinToggle.Unchecked += HideRunningAppsBin_Changed;
            var hideOpenRow = CreateFlyoutSettingRow("Hide open app icons", _hideRunningAppsBinToggle);
            hideOpenRow.ToolTip = "Prevent running apps in this Bin from showing a second standalone icon on the taskbar";
            appearanceRows.Children.Add(hideOpenRow);

            appearanceCard.Child = appearanceRows;
            sectionsStack.Children.Add(appearanceCard);

            // ==================== SECTION 2: LAYOUT ====================
            sectionsStack.Children.Add(CreateDrawerSectionLabel("Layout"));

            var layoutCard = CreateDrawerGroupCard();
            var layoutRows = new StackPanel();

            var densityTrack = new Border
            {
                Background = FluentTheme.SegmentedTrackBg,
                BorderBrush = FluentTheme.SurfaceStroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2)
            };
            var densityRow = new StackPanel { Orientation = Orientation.Horizontal };
            _densityCompactBtn = CreateSegmentOption(null, "Compact", out _densityCompactText, () => SetDensity(BinFlyoutDensity.Compact));
            _densityNormalBtn = CreateSegmentOption(null, "Normal", out _densityNormalText, () => SetDensity(BinFlyoutDensity.Normal));
            _densitySpaciousBtn = CreateSegmentOption(null, "Spacious", out _densitySpaciousText, () => SetDensity(BinFlyoutDensity.Spacious));
            densityRow.Children.Add(_densityCompactBtn);
            densityRow.Children.Add(_densityNormalBtn);
            densityRow.Children.Add(_densitySpaciousBtn);
            densityTrack.Child = densityRow;

            layoutRows.Children.Add(CreateFlyoutSettingRow("Size", densityTrack));
            layoutRows.Children.Add(CreateSettingsRowDivider());

            layoutRows.Children.Add(CreateFlyoutSettingRow(
                "Columns",
                CreateBareStepper("columns", out _columnsValueText, () => AdjustColumns(-1), () => AdjustColumns(+1), valueWidth: 32)));
            layoutRows.Children.Add(CreateSettingsRowDivider());

            layoutRows.Children.Add(CreateFlyoutSettingRow(
                "Rows",
                CreateBareStepper("rows", out _rowsValueText, () => AdjustMaxRows(-1), () => AdjustMaxRows(+1), valueWidth: 40)));
            layoutRows.Children.Add(CreateSettingsRowDivider());

            layoutRows.Children.Add(CreateFlyoutSettingRow(
                "Padding",
                CreateBareStepper("padding", out _paddingValueText, () => AdjustItemPadding(-2), () => AdjustItemPadding(+2), valueWidth: 42)));

            layoutCard.Child = layoutRows;
            sectionsStack.Children.Add(layoutCard);

            // ==================== SECTION 3: HEADER ELEMENTS ====================
            sectionsStack.Children.Add(CreateDrawerSectionLabel("Flyout header"));

            var controlsCard = CreateDrawerGroupCard(isLast: true);
            var controlsRows = new StackPanel();

            _showTitleToggle = FluentTheme.CreateToggleCheckBox(null, true);
            _showTitleToggle.Checked += FlyoutHeaderVisibility_Changed;
            _showTitleToggle.Unchecked += FlyoutHeaderVisibility_Changed;
            controlsRows.Children.Add(CreateFlyoutSettingRow("Title", _showTitleToggle));
            controlsRows.Children.Add(CreateSettingsRowDivider());

            _showAddToggle = FluentTheme.CreateToggleCheckBox(null, true);
            _showAddToggle.Checked += FlyoutHeaderVisibility_Changed;
            _showAddToggle.Unchecked += FlyoutHeaderVisibility_Changed;
            controlsRows.Children.Add(CreateFlyoutSettingRow("Add button", _showAddToggle));
            controlsRows.Children.Add(CreateSettingsRowDivider());

            _showEditToggle = FluentTheme.CreateToggleCheckBox(null, true);
            _showEditToggle.Checked += FlyoutHeaderVisibility_Changed;
            _showEditToggle.Unchecked += FlyoutHeaderVisibility_Changed;
            controlsRows.Children.Add(CreateFlyoutSettingRow("Edit button", _showEditToggle));

            controlsCard.Child = controlsRows;
            sectionsStack.Children.Add(controlsCard);

            scroll.Content = sectionsStack;
            Grid.SetRow(scroll, 2);
            drawerRoot.Children.Add(scroll);

            _flyoutOptionsCard.Child = drawerRoot;
            overlay.Children.Add(_flyoutOptionsCard);

            return overlay;
        }

        private static TextBlock CreateDrawerSectionLabel(string text, bool isFirst = false)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextMuted,
                Margin = new Thickness(4, isFirst ? 0 : 10, 0, 5)
            };
        }

        private static Border CreateDrawerGroupCard(bool isLast = false)
        {
            return new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, isLast ? 2 : 0)
            };
        }

        private static StackPanel CreateBareStepper(
            string tooltipNoun,
            out TextBlock valueTextOut,
            Action onMinus,
            Action onPlus,
            double valueWidth = 34)
        {
            var group = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var minusBtn = FluentTheme.CreateIconButton("\uE738", $"Decrease {tooltipNoun}", size: 26, iconSize: 10);
            minusBtn.Click += (_, __) => onMinus?.Invoke();
            group.Children.Add(minusBtn);

            var valTb = new TextBlock
            {
                Text = "4",
                FontSize = 12.5,
                Foreground = FluentTheme.TextPrimary,
                Width = valueWidth,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            valueTextOut = valTb;
            group.Children.Add(valTb);

            var plusBtn = FluentTheme.CreateIconButton("\uE710", $"Increase {tooltipNoun}", size: 26, iconSize: 10);
            plusBtn.Click += (_, __) => onPlus?.Invoke();
            group.Children.Add(plusBtn);

            return group;
        }

        private static Border CreateSettingsRowDivider()
        {
            return new Border
            {
                Height = 1,
                Background = FluentTheme.DividerBrush
            };
        }

        private static Grid CreateFlyoutSettingRow(string labelText, UIElement rightControl)
        {
            var row = new Grid
            {
                Margin = new Thickness(15, 7, 15, 7),
                MinHeight = 28
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = labelText,
                FontSize = 13,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            rightControl.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            Grid.SetColumn(rightControl, 1);
            row.Children.Add(rightControl);

            return row;
        }

        private Border CreateSegmentOption(string glyph, string text, out TextBlock labelOut, Action onClick)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 4, 10, 4),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock icon = null;
            if (!string.IsNullOrEmpty(glyph))
            {
                icon = new TextBlock
                {
                    Text = glyph,
                    FontFamily = FluentTheme.IconFont,
                    FontSize = 12,
                    Foreground = FluentTheme.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                sp.Children.Add(icon);
            }

            var lbl = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            labelOut = lbl;
            sp.Children.Add(lbl);

            pill.Child = sp;
            pill.Tag = icon;
            pill.MouseLeftButtonUp += (_, __) => onClick?.Invoke();
            return pill;
        }

        private void SetFlyoutOptionsOpen(bool open)
        {
            bool wasOpen = _flyoutOptionsExpanded;
            _flyoutOptionsExpanded = open;
            UpdateFlyoutExpanderVisual(animateIn: open && !wasOpen);
        }

        private void UpdateFlyoutExpanderVisual(bool animateIn = false)
        {
            if (_flyoutDrawerOverlay == null)
                return;

            bool shouldShow = _flyoutOptionsExpanded && _activePane == WorkspacePane.Bin && _selectedBin != null;
            _flyoutDrawerOverlay.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;

            if (shouldShow && animateIn && _flyoutDrawerTranslate != null)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var slideAnim = new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
                var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
                _flyoutDrawerTranslate.BeginAnimation(TranslateTransform.XProperty, slideAnim);
                _flyoutOptionsCard.BeginAnimation(OpacityProperty, fadeAnim);
            }
            else if (shouldShow && _flyoutDrawerTranslate != null)
            {
                _flyoutDrawerTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                _flyoutDrawerTranslate.X = 0;
                _flyoutOptionsCard.BeginAnimation(OpacityProperty, null);
                _flyoutOptionsCard.Opacity = 1.0;
            }
        }

        private ScrollViewer BuildSettingsView()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Visibility = Visibility.Collapsed
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(32, 26, 32, 24)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Settings",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 18)
            });

            _closeOnLaunchToggle = FluentTheme.CreateToggleCheckBox(null, true);
            _closeOnLaunchToggle.Checked += GlobalSetting_Changed;
            _closeOnLaunchToggle.Unchecked += GlobalSetting_Changed;
            panel.Children.Add(CreateSettingsCard(
                "\uE8A7",
                "Close popup after launching",
                "Automatically dismiss the Bin flyout when you open an item",
                _closeOnLaunchToggle));

            _hideRunningAppsGlobalToggle = FluentTheme.CreateToggleCheckBox(null, false);
            _hideRunningAppsGlobalToggle.Checked += GlobalSetting_Changed;
            _hideRunningAppsGlobalToggle.Unchecked += GlobalSetting_Changed;
            panel.Children.Add(CreateSettingsCard(
                "\uECAA",
                "Hide duplicate taskbar icons",
                "Hide standalone taskbar icons when apps inside a taskbar Bin are open",
                _hideRunningAppsGlobalToggle));

            _minimizeToTrayToggle = FluentTheme.CreateToggleCheckBox(null, true);
            _minimizeToTrayToggle.Checked += GlobalSetting_Changed;
            _minimizeToTrayToggle.Unchecked += GlobalSetting_Changed;
            panel.Children.Add(CreateSettingsCard(
                "\uE75B",
                "Keep running in system tray",
                "Keep taskbar Bins active in the background when closing this window",
                _minimizeToTrayToggle));

            _startupToggle = FluentTheme.CreateToggleCheckBox(null, false);
            _startupToggle.Checked += GlobalSetting_Changed;
            _startupToggle.Unchecked += GlobalSetting_Changed;
            panel.Children.Add(CreateSettingsCard(
                "\uE7E8",
                "Launch at Windows startup",
                "Start Bins silently in the system tray when you sign in",
                _startupToggle));

            _showPinGuideToggle = FluentTheme.CreateToggleCheckBox(null, true);
            _showPinGuideToggle.Checked += GlobalSetting_Changed;
            _showPinGuideToggle.Unchecked += GlobalSetting_Changed;
            panel.Children.Add(CreateSettingsCard(
                "\uE718",
                "Show taskbar pinning guide",
                "Show instructions for locking taskbar location and hiding hover previews when adding a Bin",
                _showPinGuideToggle));

            scroll.Content = panel;
            return scroll;
        }

        private Border CreateSettingsCard(string glyph, string title, string description, UIElement rightControl)
        {
            var card = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = FluentTheme.CreateFluentIcon(glyph, 16, FluentTheme.TextSecondary);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13.5,
                Foreground = FluentTheme.TextPrimary
            });
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = FluentTheme.TextMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 16, 0)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            rightControl.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            Grid.SetColumn(rightControl, 2);
            grid.Children.Add(rightControl);

            card.Child = grid;
            return card;
        }

        private Border CreateFooterNavRow(
            string glyph,
            Brush iconBrush,
            string labelText,
            WorkspacePane targetPane,
            out Border pillBorder)
        {
            var row = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(0, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            pillBorder = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(1.5),
                Background = FluentTheme.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden
            };
            Grid.SetColumn(pillBorder, 0);
            rowGrid.Children.Add(pillBorder);

            var icon = FluentTheme.CreateFluentIcon(glyph, 14.5, iconBrush);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 11, 0));
            Grid.SetColumn(icon, 1);
            rowGrid.Children.Add(icon);

            var label = new TextBlock
            {
                Text = labelText,
                FontSize = 13,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 2);
            rowGrid.Children.Add(label);

            row.Child = rowGrid;
            row.MouseEnter += (_, __) =>
            {
                if (_activePane != targetPane)
                    row.Background = FluentTheme.SubtleHoverBg;
            };
            row.MouseLeave += (_, __) =>
            {
                if (_activePane != targetPane)
                    row.Background = Brushes.Transparent;
            };
            row.MouseLeftButtonUp += (_, __) =>
            {
                _editingItemId = null;
                _activePane = targetPane;
                RefreshFullUi();
            };

            return row;
        }

        private ScrollViewer BuildDonateView()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Visibility = Visibility.Collapsed
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(32, 26, 32, 24)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Donate",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Bins is free to use. If it helps keep your Windows 11 taskbar organized, consider supporting Piyush Agade's development with a tip.",
                FontSize = 13,
                Foreground = FluentTheme.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            panel.Children.Add(CreatePaymentMethodCard(
                badgeSymbol: "$",
                badgeFgHex: "#00D632",
                badgeBgHex: "#1E00D632",
                badgeBorderHex: "#4500D632",
                serviceName: "Cash App",
                handle: "$piyushagade",
                webUrl: "https://cash.app/$piyushagade",
                isPrimaryAction: true));

            panel.Children.Add(CreatePaymentMethodCard(
                badgeSymbol: "V",
                badgeFgHex: "#38A8FF",
                badgeBgHex: "#1E008CFF",
                badgeBorderHex: "#45008CFF",
                serviceName: "Venmo",
                handle: "@piyushagade",
                webUrl: "https://venmo.com/u/piyushagade",
                isPrimaryAction: true));

            scroll.Content = panel;
            return scroll;
        }

        private Border CreatePaymentMethodCard(
            string badgeSymbol,
            string badgeFgHex,
            string badgeBgHex,
            string badgeBorderHex,
            string serviceName,
            string handle,
            string webUrl,
            bool isPrimaryAction)
        {
            var card = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(8),
                Background = FluentTheme.BrushFromHex(badgeBgHex),
                BorderBrush = FluentTheme.BrushFromHex(badgeBorderHex),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = badgeSymbol,
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = FluentTheme.BrushFromHex(badgeFgHex),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = serviceName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary
            });
            textStack.Children.Add(new TextBlock
            {
                Text = handle,
                FontSize = 13,
                Foreground = FluentTheme.AccentBrush,
                Margin = new Thickness(0, 2, 16, 0)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var copyBtn = FluentTheme.CreateButton("Copy", padding: new Thickness(11, 6, 12, 6), iconGlyph: "\uE8C8");
            copyBtn.Margin = new Thickness(0, 0, 8, 0);
            copyBtn.ToolTip = $"Copy {handle} to clipboard";
            copyBtn.Click += (_, __) =>
            {
                try
                {
                    Clipboard.SetText(handle);
                    if (copyBtn.Content is StackPanel sp && sp.Children.Count >= 2 &&
                        sp.Children[0] is TextBlock glyphTb && sp.Children[1] is TextBlock labelTb)
                    {
                        glyphTb.Text = "\uE73E";
                        glyphTb.Foreground = FluentTheme.SuccessBrush;
                        labelTb.Text = "Copied";

                        var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                        resetTimer.Tick += (ts, te) =>
                        {
                            resetTimer.Stop();
                            glyphTb.Text = "\uE8C8";
                            glyphTb.Foreground = FluentTheme.TextPrimary;
                            labelTb.Text = "Copy";
                        };
                        resetTimer.Start();
                    }
                }
                catch { }
            };
            actions.Children.Add(copyBtn);

            var openBtn = FluentTheme.CreateButton($"Open {serviceName}", isPrimary: isPrimaryAction, padding: new Thickness(12, 6, 13, 6), iconGlyph: "\uE8A7");
            openBtn.ToolTip = webUrl;
            openBtn.Click += (_, __) => OpenExternalUrl(webUrl);
            actions.Children.Add(openBtn);

            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            card.Child = grid;
            return card;
        }

        private ScrollViewer BuildAboutView()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Visibility = Visibility.Collapsed
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(32, 26, 32, 24)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "About Bins",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 18)
            });

            // Hero identity card
            var heroCard = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var heroGrid = new Grid();
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var appIconTile = new Border
            {
                Width = 48,
                Height = 48,
                Background = FluentTheme.BrushFromHex("#14FFFFFF"),
                BorderBrush = FluentTheme.SurfaceStroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7),
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            using (var mosaicBmp = Win32Interop.CreateGroupMosaicBitmap(null))
            {
                var appImg = new Image
                {
                    Source = IconService.ConvertBitmapToBitmapSource(mosaicBmp)
                };
                RenderOptions.SetBitmapScalingMode(appImg, BitmapScalingMode.HighQuality);
                appIconTile.Child = appImg;
            }
            Grid.SetColumn(appIconTile, 0);
            heroGrid.Children.Add(appIconTile);

            var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = "Bins for Windows 11",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            var versionBadge = new Border
            {
                Background = FluentTheme.BrushFromHex("#18FFFFFF"),
                BorderBrush = FluentTheme.SurfaceStroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 1, 7, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = AppVersion.Display,
                    FontSize = 11.5,
                    Foreground = FluentTheme.TextSecondary
                }
            };
            titleRow.Children.Add(versionBadge);
            heroText.Children.Add(titleRow);

            heroText.Children.Add(new TextBlock
            {
                Text = "Group apps, folders, and shortcuts into clean customizable mosaic icons on the Windows 11 taskbar.",
                FontSize = 12.5,
                Foreground = FluentTheme.TextMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 16, 0)
            });
            Grid.SetColumn(heroText, 1);
            heroGrid.Children.Add(heroText);

            var supportBtn = FluentTheme.CreateButton("Donate", padding: new Thickness(12, 6, 13, 6), iconGlyph: "\uEB51");
            supportBtn.VerticalAlignment = VerticalAlignment.Center;
            supportBtn.Click += (_, __) =>
            {
                _activePane = WorkspacePane.Donate;
                RefreshFullUi();
            };
            Grid.SetColumn(supportBtn, 2);
            heroGrid.Children.Add(supportBtn);

            heroCard.Child = heroGrid;
            panel.Children.Add(heroCard);

            // Details card (WinUI 3 grouped rows)
            var detailsCard = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            var detailsStack = new StackPanel();
            detailsStack.Children.Add(CreateAboutDetailRow("Author", "Piyush Agade", showTopDivider: false));
            detailsStack.Children.Add(CreateAboutDetailRow("Architecture", "Windows 11 x64  •  WPF & Win32 Shell / DWM Interop", showTopDivider: true));
            detailsStack.Children.Add(CreateAboutDataFolderRow());
            detailsCard.Child = detailsStack;
            panel.Children.Add(detailsCard);

            scroll.Content = panel;
            return scroll;
        }

        private UIElement CreateAboutDetailRow(string label, string value, bool showTopDivider)
        {
            var wrapper = new StackPanel();
            if (showTopDivider)
            {
                wrapper.Children.Add(new Border
                {
                    Height = 1,
                    Background = FluentTheme.DividerBrush
                });
            }

            var grid = new Grid { Margin = new Thickness(18, 12, 18, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = FluentTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var val = new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);

            wrapper.Children.Add(grid);
            return wrapper;
        }

        private UIElement CreateAboutDataFolderRow()
        {
            var wrapper = new StackPanel();
            wrapper.Children.Add(new Border
            {
                Height = 1,
                Background = FluentTheme.DividerBrush
            });

            var grid = new Grid { Margin = new Thickness(18, 10, 18, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = "Configuration folder",
                FontSize = 13,
                Foreground = FluentTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var pathText = new TextBlock
            {
                Text = _configService.AppDataFolder,
                FontSize = 12.5,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(pathText, 1);
            grid.Children.Add(pathText);

            var openFolderBtn = FluentTheme.CreateButton("Open folder", padding: new Thickness(10, 5, 11, 5), iconGlyph: "\uE8B7");
            openFolderBtn.Click += (_, __) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{_configService.AppDataFolder}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            Grid.SetColumn(openFolderBtn, 2);
            grid.Children.Add(openFolderBtn);

            wrapper.Children.Add(grid);
            return wrapper;
        }

        private static void OpenExternalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void RebuildSidebar()
        {
            _binsListPanel.Children.Clear();
            var bins = _configService.Current?.Bins ?? new List<BinGroup>();

            foreach (BinGroup bin in bins)
            {
                bool isSelected = _activePane == WorkspacePane.Bin && _selectedBin != null &&
                                  string.Equals(bin.Id, _selectedBin.Id, StringComparison.OrdinalIgnoreCase);

                var row = new Border
                {
                    Background = isSelected ? FluentTheme.CardSelectedBg : Brushes.Transparent,
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(0, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 2),
                    Cursor = Cursors.Hand
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var pill = new Border
                {
                    Width = 3,
                    Height = 16,
                    CornerRadius = new CornerRadius(1.5),
                    Background = FluentTheme.AccentBrush,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = isSelected ? Visibility.Visible : Visibility.Hidden
                };
                Grid.SetColumn(pill, 0);
                grid.Children.Add(pill);

                var icon = new Image
                {
                    Width = 22,
                    Height = 22,
                    Source = _iconService.CreateBinImageSource(bin),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                Grid.SetColumn(icon, 1);
                grid.Children.Add(icon);

                var nameText = new TextBlock
                {
                    Text = bin.Name,
                    FontSize = 13,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = FluentTheme.TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameText, 2);
                grid.Children.Add(nameText);

                bool binPinned = ShortcutService.IsBinPinnedOnTaskbar(bin);
                if (bin.IsEnabledOnTaskbar || binPinned)
                {
                    var pinGlyph = FluentTheme.CreateFluentIcon("\uE718", 10.5, binPinned ? FluentTheme.AccentBrush : FluentTheme.TextMuted);
                    pinGlyph.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
                    pinGlyph.SetValue(FrameworkElement.ToolTipProperty, binPinned ? "Pinned on Taskbar" : "On Taskbar");
                    Grid.SetColumn(pinGlyph, 3);
                    grid.Children.Add(pinGlyph);
                }

                row.Child = grid;

                row.MouseEnter += (_, __) =>
                {
                    if (!isSelected)
                        row.Background = FluentTheme.SubtleHoverBg;
                };
                row.MouseLeave += (_, __) =>
                {
                    if (!isSelected)
                        row.Background = Brushes.Transparent;
                };
                row.MouseLeftButtonUp += (_, __) =>
                {
                    _editingItemId = null;
                    _activePane = WorkspacePane.Bin;
                    _selectedBin = bin;
                    RefreshFullUi();
                };

                _binsListPanel.Children.Add(row);
            }

            ApplyFooterNavState(_donateNavRow, _donateNavPill, _activePane == WorkspacePane.Donate);
            ApplyFooterNavState(_settingsNavRow, _settingsNavPill, _activePane == WorkspacePane.Settings);
            ApplyFooterNavState(_aboutNavRow, _aboutNavPill, _activePane == WorkspacePane.About);
        }

        private static void ApplyFooterNavState(Border row, Border pill, bool active)
        {
            if (row != null)
                row.Background = active ? FluentTheme.CardSelectedBg : Brushes.Transparent;
            if (pill != null)
                pill.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        }

        private void RebuildRightPane()
        {
            _donateView.Visibility = _activePane == WorkspacePane.Donate ? Visibility.Visible : Visibility.Collapsed;
            _settingsView.Visibility = _activePane == WorkspacePane.Settings ? Visibility.Visible : Visibility.Collapsed;
            _aboutView.Visibility = _activePane == WorkspacePane.About ? Visibility.Visible : Visibility.Collapsed;

            if (_activePane != WorkspacePane.Bin)
            {
                _binWorkspaceView.Visibility = Visibility.Collapsed;
                _emptyBinsView.Visibility = Visibility.Collapsed;
                UpdateFlyoutExpanderVisual();
                return;
            }

            if (_selectedBin == null)
            {
                _binWorkspaceView.Visibility = Visibility.Collapsed;
                _emptyBinsView.Visibility = Visibility.Visible;
                UpdateFlyoutExpanderVisual();
                return;
            }

            _emptyBinsView.Visibility = Visibility.Collapsed;
            _binWorkspaceView.Visibility = Visibility.Visible;

            _headerBinIcon.Source = _iconService.CreateBinImageSource(_selectedBin);

            if (_binNameBox.Text != _selectedBin.Name)
            {
                _binNameBox.Text = _selectedBin.Name;
            }

            int count = _selectedBin.Items?.Count ?? 0;
            string itemWord = count == 1 ? "1 item" : $"{count} items";
            bool isPinned = ShortcutService.IsBinPinnedOnTaskbar(_selectedBin);
            if (isPinned)
            {
                _binMetaSubtitle.Text = $"{itemWord}  •  Pinned on taskbar";
                _binMetaSubtitle.ToolTip = "Taskbar position is saved across restarts and hover window preview is disabled";
            }
            else if (_selectedBin.IsEnabledOnTaskbar)
            {
                _binMetaSubtitle.Text = $"{itemWord}  •  On taskbar (unpinned)";
                _binMetaSubtitle.ToolTip = "Right-click this Bin on your taskbar and select 'Pin to taskbar' so Windows saves its location and hides hover previews.";
            }
            else
            {
                _binMetaSubtitle.Text = itemWord;
                _binMetaSubtitle.ToolTip = null;
            }

            _addToTaskbarBtn.Visibility = (_selectedBin.IsEnabledOnTaskbar || isPinned)
                ? Visibility.Collapsed
                : Visibility.Visible;
            _pinGuideBtn.Visibility = (_selectedBin.IsEnabledOnTaskbar && !isPinned)
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Taskbar icon mode segmented control (inside Floating Flyout Options sidebar)
            bool isMosaic = _selectedBin.IconMode == BinIconMode.GridMosaic;
            ApplySegmentVisual(_gridSegmentBtn, _gridSegmentText, isMosaic);
            ApplySegmentVisual(_customSegmentBtn, _customSegmentText, !isMosaic);
            _customIconRowWrapper.Visibility = !isMosaic ? Visibility.Visible : Visibility.Collapsed;
            if (!isMosaic && !string.IsNullOrWhiteSpace(_selectedBin.CustomIconPath))
            {
                _customIconPreviewImg.Source = _iconService.GetPathImageSource(_selectedBin.CustomIconPath, _selectedBin.CustomIconIndex);
                _customIconPreviewImg.Visibility = Visibility.Visible;
            }
            else
            {
                _customIconPreviewImg.Source = null;
                _customIconPreviewImg.Visibility = Visibility.Collapsed;
            }

            // Floating Flyout Options sidebar state
            _flyoutDrawerSubtitle.Text = _selectedBin.Name;
            UpdateFlyoutExpanderVisual();
            _columnsValueText.Text = _selectedBin.Columns.ToString();
            _rowsValueText.Text = _selectedBin.MaxRows <= 0 ? "Auto" : _selectedBin.MaxRows.ToString();
            _paddingValueText.Text = $"{_selectedBin.ItemPadding} px";

            ApplySegmentVisual(_densityCompactBtn, _densityCompactText, _selectedBin.Density == BinFlyoutDensity.Compact);
            ApplySegmentVisual(_densityNormalBtn, _densityNormalText, _selectedBin.Density == BinFlyoutDensity.Normal);
            ApplySegmentVisual(_densitySpaciousBtn, _densitySpaciousText, _selectedBin.Density == BinFlyoutDensity.Spacious);

            bool hasFlyoutIcon = !string.IsNullOrWhiteSpace(_selectedBin.FlyoutIconPath);
            if (hasFlyoutIcon)
            {
                _flyoutIconPreviewImg.Source = _iconService.GetPathImageSource(_selectedBin.FlyoutIconPath, _selectedBin.FlyoutIconIndex);
                _flyoutIconPreviewImg.Visibility = Visibility.Visible;
                _flyoutIconPlaceholderGlyph.Visibility = Visibility.Collapsed;
                _flyoutIconBtnLabel.Text = "Change...";
                _clearFlyoutIconBtn.Visibility = Visibility.Visible;
            }
            else
            {
                _flyoutIconPreviewImg.Source = null;
                _flyoutIconPreviewImg.Visibility = Visibility.Collapsed;
                _flyoutIconPlaceholderGlyph.Visibility = Visibility.Visible;
                _flyoutIconBtnLabel.Text = "Set icon...";
                _clearFlyoutIconBtn.Visibility = Visibility.Collapsed;
            }

            _showTitleToggle.IsChecked = _selectedBin.ShowFlyoutTitle;
            _showAddToggle.IsChecked = _selectedBin.ShowFlyoutAddButton;
            _showEditToggle.IsChecked = _selectedBin.ShowFlyoutEditButton;
            if (_hideRunningAppsBinToggle != null)
            {
                _hideRunningAppsBinToggle.IsChecked = _selectedBin.HideRunningAppsOnTaskbar;
            }

            RebuildItemsList();
        }

        private static void ApplySegmentVisual(Border pill, TextBlock label, bool active)
        {
            pill.Background = active ? FluentTheme.CardSelectedBg : Brushes.Transparent;
            label.Foreground = active ? FluentTheme.TextPrimary : FluentTheme.TextMuted;
            label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            if (pill.Tag is TextBlock iconTb)
            {
                iconTb.Foreground = active ? FluentTheme.AccentBrush : FluentTheme.TextMuted;
            }
        }

        private void RebuildItemsList()
        {
            _itemsListPanel.Children.Clear();
            if (_selectedBin == null)
                return;

            var items = _selectedBin.Items ?? new List<BinItem>();
            if (items.Count == 0)
            {
                var emptyBorder = new Border
                {
                    Background = FluentTheme.CardBg,
                    BorderBrush = FluentTheme.CardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(24, 48, 24, 48)
                };
                var st = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                st.Children.Add(FluentTheme.CreateFluentIcon("\uE710", 20, FluentTheme.TextMuted));
                st.Children.Add(new TextBlock
                {
                    Text = "Empty Bin",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FluentTheme.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 4)
                });
                st.Children.Add(new TextBlock
                {
                    Text = "Drag apps, folders, shortcuts, or URLs here, or click Add.",
                    FontSize = 12.5,
                    Foreground = FluentTheme.TextMuted,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                emptyBorder.Child = st;
                _itemsListPanel.Children.Add(emptyBorder);
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                BinItem item = items[i];
                int index = i;
                bool isEditing = !string.IsNullOrEmpty(_editingItemId) &&
                                 string.Equals(_editingItemId, item.Id, StringComparison.OrdinalIgnoreCase);

                var row = new Border
                {
                    Background = isEditing ? FluentTheme.CardSelectedBg : FluentTheme.CardBg,
                    BorderBrush = isEditing ? FluentTheme.AccentBrush : FluentTheme.CardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, isEditing ? 5 : 8, 10, isEditing ? 5 : 8),
                    Margin = new Thickness(0, 0, 0, 4),
                    ToolTip = item.TargetPath
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var img = new Image
                {
                    Width = 26,
                    Height = 26,
                    Source = _iconService.GetItemImageSource(item),
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                var iconButtonTile = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(3),
                    Margin = new Thickness(-3, 0, 9, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    ToolTip = "Click to change item icon",
                    Child = img
                };
                iconButtonTile.MouseEnter += (_, __) => iconButtonTile.Background = FluentTheme.SubtleHoverBg;
                iconButtonTile.MouseLeave += (_, __) => iconButtonTile.Background = Brushes.Transparent;
                iconButtonTile.MouseLeftButtonDown += (s, e) => e.Handled = true;
                iconButtonTile.MouseLeftButtonUp += (s, e) =>
                {
                    ChooseItemIcon(item);
                    e.Handled = true;
                };
                Grid.SetColumn(iconButtonTile, 0);
                grid.Children.Add(iconButtonTile);

                if (isEditing)
                {
                    bool editFinished = false;
                    var editBox = FluentTheme.CreateTextBox(item.Name, "Display name in Bins...");
                    editBox.Margin = new Thickness(0, 0, 8, 0);
                    editBox.VerticalAlignment = VerticalAlignment.Center;

                    Action commitEdit = () =>
                    {
                        if (editFinished)
                            return;
                        editFinished = true;
                        _editingItemId = null;

                        string trimmed = (editBox.Text ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !string.Equals(trimmed, item.Name, StringComparison.Ordinal))
                        {
                            item.Name = trimmed;
                            SaveAndSyncSelectedBin(bumpIcon: false);
                        }
                        else
                        {
                            RebuildItemsList();
                        }
                    };

                    Action cancelEdit = () =>
                    {
                        if (editFinished)
                            return;
                        editFinished = true;
                        _editingItemId = null;
                        RebuildItemsList();
                    };

                    editBox.KeyDown += (s, e) =>
                    {
                        if (e.Key == Key.Enter)
                        {
                            commitEdit();
                            e.Handled = true;
                        }
                        else if (e.Key == Key.Escape)
                        {
                            cancelEdit();
                            e.Handled = true;
                        }
                    };

                    var editActions = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var saveEditBtn = FluentTheme.CreateIconButton("\uE73E", "Save name", size: 28, iconSize: 12);
                    saveEditBtn.Focusable = false;
                    saveEditBtn.Click += (_, __) => commitEdit();
                    editActions.Children.Add(saveEditBtn);

                    var cancelEditBtn = FluentTheme.CreateIconButton("\uE711", "Cancel", size: 28, iconSize: 11);
                    cancelEditBtn.Focusable = false;
                    cancelEditBtn.Click += (_, __) => cancelEdit();
                    editActions.Children.Add(cancelEditBtn);

                    editBox.LostKeyboardFocus += (_, __) => commitEdit();
                    editBox.Loaded += (_, __) =>
                    {
                        editBox.Focus();
                        editBox.SelectAll();
                    };

                    Grid.SetColumn(editBox, 1);
                    grid.Children.Add(editBox);

                    Grid.SetColumn(editActions, 2);
                    grid.Children.Add(editActions);

                    row.Child = grid;
                    _itemsListPanel.Children.Add(row);
                    continue;
                }

                var nameBlock = new TextBlock
                {
                    Text = item.Name,
                    FontSize = 13.5,
                    Foreground = FluentTheme.TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameBlock, 1);
                grid.Children.Add(nameBlock);

                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0
                };

                var renameBtn = FluentTheme.CreateIconButton("\uE70F", "Rename in Bin", size: 28, iconSize: 11.5);
                renameBtn.Click += (_, __) => BeginInlineItemRename(item);
                actions.Children.Add(renameBtn);

                var changeIconBtn = FluentTheme.CreateIconButton("\uEB9F", "Change icon", size: 28, iconSize: 11.5);
                changeIconBtn.Click += (_, __) => ChooseItemIcon(item);
                actions.Children.Add(changeIconBtn);

                var upBtn = FluentTheme.CreateIconButton("\uE74A", "Move up", size: 28, iconSize: 11.5);
                upBtn.IsEnabled = index > 0;
                upBtn.Click += (_, __) => MoveSelectedBinItem(index, index - 1);
                actions.Children.Add(upBtn);

                var downBtn = FluentTheme.CreateIconButton("\uE74B", "Move down", size: 28, iconSize: 11.5);
                downBtn.IsEnabled = index < items.Count - 1;
                downBtn.Click += (_, __) => MoveSelectedBinItem(index, index + 1);
                actions.Children.Add(downBtn);

                var itemMoreBtn = FluentTheme.CreateIconButton("\uE712", "More", size: 28, iconSize: 12);
                itemMoreBtn.Click += (_, __) => OpenItemContextMenu(itemMoreBtn, item, index);
                actions.Children.Add(itemMoreBtn);

                var removeBtn = FluentTheme.CreateIconButton("\uE711", "Remove", isDanger: true, size: 28, iconSize: 11);
                removeBtn.Click += (_, __) =>
                {
                    _selectedBin.Items.RemoveAt(index);
                    SaveAndSyncSelectedBin(bumpIcon: true);
                };
                actions.Children.Add(removeBtn);

                Grid.SetColumn(actions, 2);
                grid.Children.Add(actions);

                row.Child = grid;

                row.MouseEnter += (_, __) =>
                {
                    row.Background = FluentTheme.CardHoverBg;
                    actions.Opacity = 1;
                };
                row.MouseLeave += (_, __) =>
                {
                    row.Background = FluentTheme.CardBg;
                    actions.Opacity = 0;
                };
                row.MouseRightButtonUp += (s, e) =>
                {
                    OpenItemContextMenu(row, item, index);
                    e.Handled = true;
                };
                row.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        LauncherService.LaunchItem(item);
                        e.Handled = true;
                    }
                };

                _itemsListPanel.Children.Add(row);
            }
        }

        private void BeginInlineItemRename(BinItem item)
        {
            if (item == null)
                return;

            _editingItemId = item.Id;
            RebuildItemsList();
        }

        private void ChooseItemIcon(BinItem item)
        {
            if (item == null || _selectedBin == null)
                return;

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

            if (picker.ShowDialog() == true)
            {
                item.CustomIconPath = picker.SelectedIconPath ?? string.Empty;
                item.CustomIconIndex = picker.SelectedIconIndex;
                SaveAndSyncSelectedBin(bumpIcon: true);
            }
        }

        private void EditUrlItem(BinItem item)
        {
            if (item == null || _selectedBin == null)
                return;

            string previousPath = item.TargetPath;
            var dlg = new AddUrlDialogWindow(
                _iconService,
                item.TargetPath,
                item.Name,
                item.CustomIconPath,
                item.CustomIconIndex)
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true)
            {
                bool urlChanged = !string.Equals(previousPath, dlg.NormalizedUrl, StringComparison.OrdinalIgnoreCase);
                item.TargetPath = dlg.NormalizedUrl;
                item.Name = dlg.DisplayName;
                item.Kind = BinItemKind.WebUrl;
                item.CustomIconPath = dlg.CustomIconPath ?? string.Empty;
                item.CustomIconIndex = dlg.CustomIconIndex;

                SaveAndSyncSelectedBin(bumpIcon: true);

                if (string.IsNullOrWhiteSpace(item.CustomIconPath) && urlChanged)
                {
                    FetchFaviconAndRefresh(_selectedBin, item);
                }
            }
        }

        private void FetchFaviconAndRefresh(BinGroup owningBin, BinItem item)
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
                        _taskbarManager.RefreshBin(owningBin);
                        _popupWindow.RefreshIfShowing(owningBin);
                        RefreshFullUi();
                    }
                }));
            });
        }

        private void OpenItemContextMenu(FrameworkElement anchor, BinItem item, int index)
        {
            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;

            bool isUrl = item.Kind == BinItemKind.WebUrl || BinItem.IsWebUrl(item.TargetPath);
            if (isUrl)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Open in browser", () => LauncherService.LaunchItem(item), iconGlyph: "\uE774"));
                menu.Items.Add(FluentTheme.CreateMenuItem("Edit URL...", () => EditUrlItem(item), iconGlyph: "\uE71B"));
                menu.Items.Add(FluentTheme.CreateMenuItem("Copy URL", () =>
                {
                    try { Clipboard.SetText(item.TargetPath ?? string.Empty); } catch { }
                }, iconGlyph: "\uE8C8"));
            }
            else
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Open", () => LauncherService.LaunchItem(item), iconGlyph: "\uE8A7"));
                if (item.Kind != BinItemKind.Folder)
                {
                    menu.Items.Add(FluentTheme.CreateMenuItem("Run as administrator", () => LauncherService.LaunchItem(item, runAsAdmin: true), iconGlyph: "\uE7EF"));
                }
                menu.Items.Add(FluentTheme.CreateMenuItem("Open file location", () => LauncherService.OpenItemLocation(item), iconGlyph: "\uE8B7"));
            }

            menu.Items.Add(FluentTheme.CreateMenuSeparator());
            menu.Items.Add(FluentTheme.CreateMenuItem("Rename", () => BeginInlineItemRename(item), iconGlyph: "\uE70F"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Change icon...", () => ChooseItemIcon(item), iconGlyph: "\uEB9F"));

            if (!string.IsNullOrWhiteSpace(item.CustomIconPath))
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Reset default icon", () =>
                {
                    item.CustomIconPath = string.Empty;
                    item.CustomIconIndex = 0;
                    SaveAndSyncSelectedBin(bumpIcon: true);
                }, iconGlyph: "\uE777"));
            }
            else if (isUrl)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Fetch website icon", () =>
                {
                    FetchFaviconAndRefresh(_selectedBin, item);
                }, iconGlyph: "\uE774"));
            }

            string defaultName = BinItem.DeriveDisplayName(item.TargetPath);
            if (!string.IsNullOrWhiteSpace(defaultName) &&
                !string.Equals(item.Name, defaultName, StringComparison.Ordinal))
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Reset default name", () =>
                {
                    item.Name = defaultName;
                    SaveAndSyncSelectedBin(bumpIcon: false);
                }, iconGlyph: "\uE777"));
            }

            menu.Items.Add(FluentTheme.CreateMenuItem("Remove", () =>
            {
                _selectedBin?.Items.RemoveAt(index);
                SaveAndSyncSelectedBin(bumpIcon: true);
            }, isDanger: true, iconGlyph: "\uE74D"));

            menu.IsOpen = true;
        }

        private void AddToTaskbar_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null || _selectedBin.IsEnabledOnTaskbar)
                return;

            _selectedBin.IsEnabledOnTaskbar = true;
            SaveAndSyncSelectedBin(bumpIcon: false);
            ShowTaskbarPinGuide(_selectedBin, forceShow: false);
        }

        private void ShowTaskbarPinGuide(BinGroup bin, bool forceShow)
        {
            if (bin == null)
                return;

            if (!forceShow)
            {
                if (_configService.Current?.HidePinToTaskbarPrompt == true)
                    return;

                if (ShortcutService.IsBinPinnedOnTaskbar(bin))
                    return;
            }

            if (_activePinGuideDialog != null && _activePinGuideDialog.IsLoaded)
            {
                _activePinGuideDialog.UpdateForBin(bin);
                _activePinGuideDialog.Activate();
                return;
            }

            var dlg = new TaskbarPinGuideDialog(_configService, _iconService, _taskbarManager, bin)
            {
                Owner = IsVisible ? this : null,
                WindowStartupLocation = IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
            };
            dlg.SuppressionPreferenceChanged += () =>
            {
                if (_showPinGuideToggle != null && _configService.Current != null)
                {
                    _updatingUi = true;
                    try
                    {
                        _showPinGuideToggle.IsChecked = !_configService.Current.HidePinToTaskbarPrompt;
                    }
                    finally
                    {
                        _updatingUi = false;
                    }
                }
            };
            dlg.Closed += (_, __) =>
            {
                if (ReferenceEquals(_activePinGuideDialog, dlg))
                {
                    _activePinGuideDialog = null;
                }
            };

            _activePinGuideDialog = dlg;
            dlg.Show();
        }

        private void BinMoreMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null || !(sender is FrameworkElement btn))
                return;

            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;

            bool selectedPinned = ShortcutService.IsBinPinnedOnTaskbar(_selectedBin);
            if (selectedPinned)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Unpin from Taskbar...", () =>
                {
                    ContentDialogWindow.ShowMessage(
                        this,
                        "Pinned on Taskbar",
                        $"To remove \"{_selectedBin.Name}\" from the taskbar, right-click its icon on your Windows 11 Taskbar and select \"Unpin from taskbar\".",
                        "Got it");
                }, iconGlyph: "\uE77A"));
            }
            else if (_selectedBin.IsEnabledOnTaskbar)
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("How to Pin & Lock Position...", () =>
                {
                    ShowTaskbarPinGuide(_selectedBin, forceShow: true);
                }, iconGlyph: "\uE718"));

                menu.Items.Add(FluentTheme.CreateMenuItem("Remove from Taskbar", () =>
                {
                    _selectedBin.IsEnabledOnTaskbar = false;
                    SaveAndSyncSelectedBin(bumpIcon: false);
                }, iconGlyph: "\uE77A"));
            }
            else
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Add to Taskbar", () =>
                {
                    _selectedBin.IsEnabledOnTaskbar = true;
                    SaveAndSyncSelectedBin(bumpIcon: false);
                    ShowTaskbarPinGuide(_selectedBin, forceShow: false);
                }, iconGlyph: "\uE718"));
            }

            menu.Items.Add(FluentTheme.CreateMenuItem("Set Flyout Icon...", () => ChooseFlyoutIcon_Click(null, null), iconGlyph: "\uEB9F"));
            if (!string.IsNullOrWhiteSpace(_selectedBin.FlyoutIconPath))
            {
                menu.Items.Add(FluentTheme.CreateMenuItem("Remove Flyout Icon", () =>
                {
                    _selectedBin.FlyoutIconPath = string.Empty;
                    _selectedBin.FlyoutIconIndex = 0;
                    SaveAndSyncSelectedBin(bumpIcon: false);
                }, iconGlyph: "\uE711"));
            }

            menu.Items.Add(FluentTheme.CreateMenuItem("Export Shortcut (.lnk)", () => ExportShortcut_Click(null, null), iconGlyph: "\uE8A7"));
            menu.Items.Add(FluentTheme.CreateMenuSeparator());
            menu.Items.Add(FluentTheme.CreateMenuItem("Delete Bin", () => DeleteSelectedBin_Click(null, null), isDanger: true, iconGlyph: "\uE74D"));

            menu.IsOpen = true;
        }

        private void AddMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null || !(sender is FrameworkElement btn))
                return;

            var menu = FluentTheme.CreateDarkContextMenu();
            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;

            menu.Items.Add(FluentTheme.CreateMenuItem("Installed App (Start Menu)...", () => AddFromStartMenu_Click(null, null), iconGlyph: "\uE737"));
            menu.Items.Add(FluentTheme.CreateMenuItem("App or Shortcut...", () => AddAppOrShortcut_Click(null, null), iconGlyph: "\uE7C3"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Folder...", () => AddFolder_Click(null, null), iconGlyph: "\uE8B7"));
            menu.Items.Add(FluentTheme.CreateMenuItem("Website / URL...", () => AddUrl_Click(null, null), iconGlyph: "\uE774"));

            menu.IsOpen = true;
        }

        private void CreateNewBin_Click(object sender, RoutedEventArgs e)
        {
            int nextNum = (_configService.Current?.Bins?.Count ?? 0) + 1;
            var newBin = new BinGroup
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = $"Bin {nextNum}",
                IconMode = BinIconMode.GridMosaic,
                Columns = 4,
                IsEnabledOnTaskbar = false,
                HideRunningAppsOnTaskbar = _configService.Current?.HideRunningAppsOnTaskbar ?? false
            };

            _configService.Current.Bins.Add(newBin);
            _activePane = WorkspacePane.Bin;
            _selectedBin = newBin;
            _configService.Save(notify: false);
            RefreshFullUi();

            _binNameBox.Focus();
            _binNameBox.SelectAll();
        }

        private void DeleteSelectedBin_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            bool confirmed = ContentDialogWindow.ShowConfirm(
                this,
                $"Delete \"{_selectedBin.Name}\"?",
                "This removes the Bin from Bins. Your apps, folders, and shortcuts on disk will not be affected.",
                primaryText: "Delete",
                cancelText: "Cancel",
                isDestructive: true);

            if (!confirmed)
                return;

            BinGroup toDelete = _selectedBin;
            toDelete.IsEnabledOnTaskbar = false;
            _taskbarManager.RefreshBin(toDelete);

            _configService.Current.Bins.Remove(toDelete);
            _selectedBin = _configService.Current.Bins.FirstOrDefault();
            _configService.Save(notify: false);
            _runningAppsManager?.SyncRunningWindows();
            RefreshFullUi();
        }

        private void BinNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingUi || _selectedBin == null)
                return;

            string trimmed = (_binNameBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(trimmed) && trimmed != _selectedBin.Name)
            {
                _selectedBin.Name = trimmed;
                _flyoutDrawerSubtitle.Text = trimmed;
                _configService.Save(notify: false);
                _taskbarManager.RefreshBin(_selectedBin);
                RebuildSidebar();
            }
        }

        private void SetIconMode(BinIconMode mode)
        {
            if (_selectedBin == null)
                return;

            if (mode == BinIconMode.CustomIcon)
            {
                if (string.IsNullOrWhiteSpace(_selectedBin.CustomIconPath) || _selectedBin.IconMode == BinIconMode.CustomIcon)
                {
                    var picker = new IconPickerWindow(
                        _iconService,
                        _selectedBin.CustomIconPath,
                        _selectedBin.CustomIconIndex,
                        allowClear: false,
                        dialogTitle: "Choose Taskbar Icon")
                    {
                        Owner = this
                    };
                    if (picker.ShowDialog() == true)
                    {
                        _selectedBin.CustomIconPath = picker.SelectedIconPath;
                        _selectedBin.CustomIconIndex = picker.SelectedIconIndex;
                    }
                    else if (string.IsNullOrWhiteSpace(_selectedBin.CustomIconPath))
                    {
                        return;
                    }
                }
            }

            _selectedBin.IconMode = mode;
            SaveAndSyncSelectedBin(bumpIcon: true);
        }

        private void ChooseCustomIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            var picker = new IconPickerWindow(
                _iconService,
                _selectedBin.CustomIconPath,
                _selectedBin.CustomIconIndex,
                allowClear: false,
                dialogTitle: "Choose Taskbar Icon")
            {
                Owner = this
            };

            if (picker.ShowDialog() == true)
            {
                _selectedBin.CustomIconPath = picker.SelectedIconPath;
                _selectedBin.CustomIconIndex = picker.SelectedIconIndex;
                _selectedBin.IconMode = BinIconMode.CustomIcon;
                SaveAndSyncSelectedBin(bumpIcon: true);
            }
        }

        private void ChooseFlyoutIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            var picker = new IconPickerWindow(
                _iconService,
                _selectedBin.FlyoutIconPath,
                _selectedBin.FlyoutIconIndex,
                allowClear: !string.IsNullOrWhiteSpace(_selectedBin.FlyoutIconPath),
                dialogTitle: "Choose Bin Flyout Icon")
            {
                Owner = this
            };

            if (picker.ShowDialog() == true)
            {
                _selectedBin.FlyoutIconPath = picker.SelectedIconPath;
                _selectedBin.FlyoutIconIndex = picker.SelectedIconIndex;
                SaveAndSyncSelectedBin(bumpIcon: false);
            }
        }

        private void AdjustColumns(int delta)
        {
            if (_selectedBin == null)
                return;

            int next = Math.Max(1, Math.Min(10, _selectedBin.Columns + delta));
            if (next != _selectedBin.Columns)
            {
                _selectedBin.Columns = next;
                SaveAndSyncSelectedBin(bumpIcon: false);
            }
        }

        private void AdjustMaxRows(int delta)
        {
            if (_selectedBin == null)
                return;

            int next = Math.Max(0, Math.Min(12, _selectedBin.MaxRows + delta));
            if (next != _selectedBin.MaxRows)
            {
                _selectedBin.MaxRows = next;
                SaveAndSyncSelectedBin(bumpIcon: false);
            }
        }

        private void SetDensity(BinFlyoutDensity density)
        {
            if (_selectedBin == null || _selectedBin.Density == density)
                return;

            _selectedBin.Density = density;
            SaveAndSyncSelectedBin(bumpIcon: false);
        }

        private void AdjustItemPadding(int delta)
        {
            if (_selectedBin == null)
                return;

            int next = Math.Max(0, Math.Min(20, _selectedBin.ItemPadding + delta));
            if (next != _selectedBin.ItemPadding)
            {
                _selectedBin.ItemPadding = next;
                SaveAndSyncSelectedBin(bumpIcon: false);
            }
        }

        private void FlyoutHeaderVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingUi || _selectedBin == null)
                return;

            _selectedBin.ShowFlyoutTitle = _showTitleToggle.IsChecked == true;
            _selectedBin.ShowFlyoutAddButton = _showAddToggle.IsChecked == true;
            _selectedBin.ShowFlyoutEditButton = _showEditToggle.IsChecked == true;
            SaveAndSyncSelectedBin(bumpIcon: false);
        }

        private void HideRunningAppsBin_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingUi || _selectedBin == null)
                return;

            bool hide = _hideRunningAppsBinToggle.IsChecked == true;
            _selectedBin.HideRunningAppsOnTaskbar = hide;

            var allBins = _configService.Current?.Bins;
            if (_configService.Current != null && allBins != null && allBins.Count > 0)
            {
                if (allBins.All(b => b.HideRunningAppsOnTaskbar))
                    _configService.Current.HideRunningAppsOnTaskbar = true;
                else if (allBins.All(b => !b.HideRunningAppsOnTaskbar))
                    _configService.Current.HideRunningAppsOnTaskbar = false;
            }

            _configService.Save(notify: false);
            _runningAppsManager?.SyncRunningWindows(forceReassert: hide);
            RefreshFullUi();
        }

        private void MoveSelectedBinItem(int fromIndex, int toIndex)
        {
            if (_selectedBin == null || fromIndex < 0 || toIndex < 0 ||
                fromIndex >= _selectedBin.Items.Count || toIndex >= _selectedBin.Items.Count)
                return;

            BinItem item = _selectedBin.Items[fromIndex];
            _selectedBin.Items.RemoveAt(fromIndex);
            _selectedBin.Items.Insert(toIndex, item);
            SaveAndSyncSelectedBin(bumpIcon: true);
        }

        private void AddFromStartMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            var picker = new StartMenuPickerWindow(_iconService) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedApps.Count > 0)
            {
                foreach (var app in picker.SelectedApps)
                {
                    var item = BinItem.FromPath(app.ShortcutPath);
                    item.Name = app.Name;
                    _selectedBin.Items.Add(item);
                }
                SaveAndSyncSelectedBin(bumpIcon: true);
            }
        }

        private void AddAppOrShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            var dlg = new OpenFileDialog
            {
                Title = "Select Applications, Shortcuts, or Files",
                Multiselect = true,
                DereferenceLinks = false,
                Filter = "Applications & Shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog(this) == true && dlg.FileNames.Length > 0)
            {
                AddPathsToSelectedBin(dlg.FileNames);
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Choose a folder to add to this Bin";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    AddPathsToSelectedBin(new[] { dlg.SelectedPath });
                }
            }
        }

        private void AddUrl_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            var dlg = new AddUrlDialogWindow(_iconService)
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true)
            {
                BinGroup targetBin = _selectedBin;
                BinItem urlItem = BinItem.FromUrl(
                    dlg.NormalizedUrl,
                    dlg.DisplayName,
                    dlg.CustomIconPath,
                    dlg.CustomIconIndex);
                targetBin.Items.Add(urlItem);
                SaveAndSyncSelectedBin(bumpIcon: true);

                if (string.IsNullOrWhiteSpace(urlItem.CustomIconPath))
                {
                    FetchFaviconAndRefresh(targetBin, urlItem);
                }
            }
        }

        private void AddPathsToSelectedBin(IEnumerable<string> paths)
        {
            if (_selectedBin == null || paths == null)
                return;

            bool any = false;
            foreach (string path in paths)
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    _selectedBin.Items.Add(BinItem.FromPath(path));
                    any = true;
                }
            }

            if (any)
            {
                SaveAndSyncSelectedBin(bumpIcon: true);
            }
        }

        private void OpenPopupPreview()
        {
            if (_selectedBin == null)
                return;

            Win32Interop.GetCursorPos(out Win32Interop.POINT pt);
            _popupWindow.ShowForBin(_selectedBin, new System.Drawing.Point(pt.X, pt.Y));
        }

        private void ExportShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBin == null)
                return;

            try
            {
                string icoPath = _iconService.EnsureBinIcoFile(_selectedBin);
                string lnkPath = ShortcutService.CreateBinShortcut(_selectedBin, icoPath, _configService.ShortcutsFolder);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{lnkPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ContentDialogWindow.ShowMessage(
                    this,
                    "Export Shortcut",
                    $"Failed to create shortcut:\n{ex.Message}");
            }
        }

        private void GlobalSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingUi || _configService.Current == null)
                return;

            _configService.Current.ClosePopupOnLaunch = _closeOnLaunchToggle.IsChecked == true;
            _configService.Current.MinimizeToTrayOnClose = _minimizeToTrayToggle.IsChecked == true;
            _configService.Current.LaunchAtStartup = _startupToggle.IsChecked == true;
            if (_showPinGuideToggle != null)
            {
                _configService.Current.HidePinToTaskbarPrompt = _showPinGuideToggle.IsChecked != true;
            }

            bool globalHide = _hideRunningAppsGlobalToggle?.IsChecked == true;
            if (ReferenceEquals(sender, _hideRunningAppsGlobalToggle) ||
                globalHide != _configService.Current.HideRunningAppsOnTaskbar)
            {
                _configService.Current.HideRunningAppsOnTaskbar = globalHide;
                if (_configService.Current.Bins != null)
                {
                    foreach (BinGroup bin in _configService.Current.Bins)
                    {
                        bin.HideRunningAppsOnTaskbar = globalHide;
                    }
                }
                _configService.Save(notify: false);
                _runningAppsManager?.SyncRunningWindows(forceReassert: globalHide);
                RefreshFullUi();
                return;
            }

            _configService.Save(notify: false);
        }

        private void SaveAndSyncSelectedBin(bool bumpIcon)
        {
            if (_selectedBin == null)
                return;

            if (bumpIcon)
            {
                _selectedBin.IconRevision++;
            }

            _configService.Save(notify: false);
            _taskbarManager.RefreshBin(_selectedBin);
            _runningAppsManager?.SyncRunningWindows();
            _popupWindow.RefreshIfShowing(_selectedBin);
            RefreshFullUi();
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

        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (_activePane == WorkspacePane.Bin &&
                _selectedBin != null &&
                (e.Data.GetDataPresent(DataFormats.FileDrop) || TryExtractDroppedWebUrl(e.Data, out _)))
            {
                e.Effects = DragDropEffects.Copy;
                _dropHighlightOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            _dropHighlightOverlay.Visibility = Visibility.Collapsed;
            if (_activePane == WorkspacePane.Bin && _selectedBin != null)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                    files.Length > 0)
                {
                    AddPathsToSelectedBin(files);
                }
                else if (TryExtractDroppedWebUrl(e.Data, out string droppedUrl))
                {
                    BinGroup targetBin = _selectedBin;
                    BinItem urlItem = BinItem.FromUrl(droppedUrl);
                    targetBin.Items.Add(urlItem);
                    SaveAndSyncSelectedBin(bumpIcon: true);
                    FetchFaviconAndRefresh(targetBin, urlItem);
                }
            }
            e.Handled = true;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_activePinGuideDialog != null)
            {
                try { _activePinGuideDialog.Close(); } catch { }
                _activePinGuideDialog = null;
            }

            if (!AllowRealClose && (_configService.Current?.MinimizeToTrayOnClose ?? true))
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
