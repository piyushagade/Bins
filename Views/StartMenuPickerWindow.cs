using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinBins.Interop;
using WinBins.Services;

namespace WinBins.Views
{
    public enum AppPickerMode
    {
        StartMenuAndStore,
        TaskbarRunningAndPinned
    }

    public class StartMenuPickerWindow : Window
    {
        private sealed class PickerListBox : ListBox
        {
            private sealed class LightweightListBoxPeer : FrameworkElementAutomationPeer
            {
                public LightweightListBoxPeer(PickerListBox owner) : base(owner)
                {
                }

                protected override List<AutomationPeer> GetChildrenCore() => null;
            }

            protected override AutomationPeer OnCreateAutomationPeer()
            {
                return new LightweightListBoxPeer(this);
            }
        }

        private sealed class AppRowItem : INotifyPropertyChanged
        {
            private readonly StartMenuPickerWindow _owner;
            private bool _isSelected;
            private ImageSource _icon;
            private int _iconRequested;

            public DiscoveredAppEntry Entry { get; }
            public string Key { get; }
            public string Name => Entry.Name;
            public string Category => Entry.Category;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            public ImageSource Icon
            {
                get
                {
                    if (_icon != null)
                        return _icon;

                    if (_owner.TryGetInstantIcon(Entry.ShortcutPath, out BitmapSource cached))
                    {
                        _icon = cached;
                        return _icon;
                    }

                    if (Interlocked.Exchange(ref _iconRequested, 1) == 0)
                    {
                        _owner.EnqueueIconLoad(this);
                    }

                    return _owner.PlaceholderIcon;
                }
            }

            public AppRowItem(StartMenuPickerWindow owner, DiscoveredAppEntry entry, string key)
            {
                _owner = owner;
                Entry = entry;
                Key = key;
            }

            public void SetLoadedIcon(BitmapSource loaded)
            {
                if (loaded == null || ReferenceEquals(_icon, loaded))
                    return;

                _icon = loaded;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly IconService _iconService;
        private readonly AppPickerMode _mode;
        private readonly BitmapSource _placeholderIcon;
        private readonly HashSet<string> _selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly TextBox _searchBox;
        private readonly PickerListBox _listBox;
        private readonly TextBlock _statusText;
        private readonly Button _addBtn;

        private readonly object _iconQueueLock = new object();
        private readonly Stack<AppRowItem> _iconLoadStack = new Stack<AppRowItem>();
        private readonly AutoResetEvent _iconQueueSignal = new AutoResetEvent(false);
        private readonly Thread _iconWorkerThread;
        private volatile bool _isClosed;

        private List<AppRowItem> _allRows = new List<AppRowItem>();
        private bool _isLoadingApps;

        public List<DiscoveredAppEntry> SelectedApps { get; private set; } = new List<DiscoveredAppEntry>();
        internal ImageSource PlaceholderIcon => _placeholderIcon;

        public StartMenuPickerWindow(IconService iconService, AppPickerMode mode = AppPickerMode.StartMenuAndStore)
        {
            _iconService = iconService;
            _mode = mode;
            _placeholderIcon = _iconService.GetDefaultPlaceholderImageSource();

            bool isTaskbarMode = _mode == AppPickerMode.TaskbarRunningAndPinned;
            Title = isTaskbarMode ? "Add Running & Pinned Taskbar Items" : "Add Installed & Store Apps";
            Width = 540;
            Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = FluentTheme.WindowBg;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            ShowInTaskbar = false;
            FluentTheme.ApplyGlobalResources(Resources);

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Interop.ApplyWindows11WindowStyling(
                    hwnd,
                    forceDarkMode: !FluentTheme.IsLightThemeActive,
                    captionColorRef: FluentTheme.ActiveCaptionColorRef);
            };

            var root = new Grid { Margin = new Thickness(20, 16, 20, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = isTaskbarMode
                    ? "Select taskbar apps & folders to add to this Bin"
                    : "Select applications to add to this Bin",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            _searchBox = FluentTheme.CreateTextBox(
                "",
                isTaskbarMode
                    ? "Filter running & pinned taskbar apps or folders..."
                    : "Search Start Menu & Microsoft Store apps...");
            _searchBox.Margin = new Thickness(0, 0, 0, 12);
            _searchBox.TextChanged += (_, __) => ApplyFilter();
            Grid.SetRow(_searchBox, 1);
            root.Children.Add(_searchBox);

            var listBorder = new Border
            {
                Background = FluentTheme.SidebarBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6)
            };

            var listHostGrid = new Grid();

            _listBox = new PickerListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                ItemTemplate = BuildRowDataTemplate(),
                ItemContainerStyle = BuildRowContainerStyle(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_listBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(_listBox, true);
            VirtualizingPanel.SetIsVirtualizing(_listBox, true);
            VirtualizingPanel.SetVirtualizationMode(_listBox, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(_listBox, ScrollUnit.Pixel);

            _statusText = new TextBlock
            {
                Text = isTaskbarMode
                    ? "Scanning running & pinned taskbar items..."
                    : "Loading installed & Store apps...",
                FontSize = 13,
                Foreground = FluentTheme.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20),
                IsHitTestVisible = false
            };

            listHostGrid.Children.Add(_listBox);
            listHostGrid.Children.Add(_statusText);
            listBorder.Child = listHostGrid;
            Grid.SetRow(listBorder, 2);
            root.Children.Add(listBorder);

            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var cancelBtn = FluentTheme.CreateButton("Cancel");
            cancelBtn.IsCancel = true;
            cancelBtn.Click += (_, __) => { DialogResult = false; Close(); };

            _addBtn = FluentTheme.CreateButton("Add Selected", isPrimary: true);
            _addBtn.IsEnabled = false;
            _addBtn.IsDefault = true;
            _addBtn.Margin = new Thickness(8, 0, 0, 0);
            _addBtn.Click += (_, __) =>
            {
                SelectedApps = _allRows
                    .Where(r => _selectedKeys.Contains(r.Key))
                    .Select(r => r.Entry)
                    .ToList();
                DialogResult = true;
                Close();
            };

            var btnGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnGroup.Children.Add(cancelBtn);
            btnGroup.Children.Add(_addBtn);
            DockPanel.SetDock(btnGroup, Dock.Right);
            footer.Children.Add(btnGroup);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;

            _iconWorkerThread = new Thread(IconWorkerLoop)
            {
                IsBackground = true,
                Name = "Bins.PickerIconWorker"
            };
            _iconWorkerThread.SetApartmentState(ApartmentState.STA);
            _iconWorkerThread.Start();

            Closed += (_, __) =>
            {
                _isClosed = true;
                try { _iconQueueSignal.Set(); } catch { }
            };

            Loaded += (_, __) =>
            {
                _searchBox.Focus();
                BeginLoadApps();
            };
        }

        private void BeginLoadApps()
        {
            if (_mode == AppPickerMode.StartMenuAndStore)
            {
                List<DiscoveredAppEntry> cached = LauncherService.TryGetCachedStartMenuApps();
                if (cached != null)
                {
                    PopulateDiscoveredApps(cached);
                    return;
                }
            }

            _isLoadingApps = true;
            _statusText.Visibility = Visibility.Visible;

            var discoveryThread = new Thread(() =>
            {
                List<DiscoveredAppEntry> discovered;
                try
                {
                    discovered = _mode == AppPickerMode.TaskbarRunningAndPinned
                        ? LauncherService.DiscoverTaskbarAppsAndFolders()
                        : LauncherService.DiscoverStartMenuApps();
                }
                catch
                {
                    discovered = new List<DiscoveredAppEntry>();
                }

                if (_isClosed)
                    return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_isClosed)
                        return;
                    _isLoadingApps = false;
                    PopulateDiscoveredApps(discovered);
                }), DispatcherPriority.Normal);
            })
            {
                IsBackground = true,
                Name = "Bins.AppDiscoveryWorker"
            };
            discoveryThread.SetApartmentState(ApartmentState.STA);
            discoveryThread.Start();
        }

        private void PopulateDiscoveredApps(List<DiscoveredAppEntry> entries)
        {
            var rows = new List<AppRowItem>(entries?.Count ?? 0);
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    DiscoveredAppEntry e = entries[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.ShortcutPath))
                        continue;

                    string key = string.IsNullOrWhiteSpace(e.Arguments)
                        ? e.ShortcutPath
                        : (e.ShortcutPath + "|" + e.Arguments);

                    var row = new AppRowItem(this, e, key)
                    {
                        IsSelected = _selectedKeys.Contains(key)
                    };
                    rows.Add(row);
                }
            }

            _allRows = rows;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_isLoadingApps)
                return;

            string query = (_searchBox.Text ?? string.Empty).Trim();
            List<AppRowItem> matches;

            if (string.IsNullOrEmpty(query))
            {
                matches = _allRows;
            }
            else
            {
                var prefixMatches = new List<AppRowItem>();
                var nameMatches = new List<AppRowItem>();
                var categoryMatches = new List<AppRowItem>();

                for (int i = 0; i < _allRows.Count; i++)
                {
                    AppRowItem row = _allRows[i];
                    string name = row.Name ?? string.Empty;
                    int nameIdx = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (nameIdx == 0)
                    {
                        prefixMatches.Add(row);
                    }
                    else if (nameIdx > 0)
                    {
                        nameMatches.Add(row);
                    }
                    else if (!string.IsNullOrEmpty(row.Category) &&
                             row.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        categoryMatches.Add(row);
                    }
                }

                matches = new List<AppRowItem>(prefixMatches.Count + nameMatches.Count + categoryMatches.Count);
                matches.AddRange(prefixMatches);
                matches.AddRange(nameMatches);
                matches.AddRange(categoryMatches);
            }

            _listBox.ItemsSource = matches;

            if (matches.Count == 0)
            {
                _statusText.Text = string.IsNullOrEmpty(query)
                    ? (_mode == AppPickerMode.TaskbarRunningAndPinned
                        ? "No running or pinned non-Bin apps or folders were found on the taskbar."
                        : "No installed or Store applications were found.")
                    : $"No matches for \"{query}\".";
                _statusText.Visibility = Visibility.Visible;
            }
            else
            {
                _statusText.Visibility = Visibility.Collapsed;
            }

            UpdateAddButton();
        }

        private void ToggleRowSelection(AppRowItem row)
        {
            if (row == null)
                return;

            if (_selectedKeys.Contains(row.Key))
            {
                _selectedKeys.Remove(row.Key);
                row.IsSelected = false;
            }
            else
            {
                _selectedKeys.Add(row.Key);
                row.IsSelected = true;
            }

            UpdateAddButton();
        }

        private void UpdateAddButton()
        {
            int n = _selectedKeys.Count;
            _addBtn.IsEnabled = n > 0;
            bool isTaskbar = _mode == AppPickerMode.TaskbarRunningAndPinned;
            string noun = isTaskbar ? "Item" : "App";
            _addBtn.Content = n > 0 ? $"Add {n} {noun}{(n == 1 ? "" : "s")}" : "Add Selected";
        }

        private bool TryGetInstantIcon(string path, out BitmapSource cached)
        {
            return _iconService.TryGetCachedPickerImageSource(path, out cached);
        }

        private void EnqueueIconLoad(AppRowItem row)
        {
            if (_isClosed || row == null)
                return;

            lock (_iconQueueLock)
            {
                _iconLoadStack.Push(row);
            }

            try { _iconQueueSignal.Set(); } catch { }
        }

        private void IconWorkerLoop()
        {
            while (!_isClosed)
            {
                AppRowItem next = null;
                lock (_iconQueueLock)
                {
                    if (_iconLoadStack.Count > 0)
                    {
                        next = _iconLoadStack.Pop();
                    }
                }

                if (next == null)
                {
                    try { _iconQueueSignal.WaitOne(250); } catch { break; }
                    continue;
                }

                if (_isClosed)
                    break;

                BitmapSource bmp = null;
                try
                {
                    bmp = _iconService.GetPickerImageSource(next.Entry.ShortcutPath);
                }
                catch
                {
                }

                if (_isClosed)
                    break;

                if (bmp != null)
                {
                    AppRowItem capturedRow = next;
                    BitmapSource capturedBmp = bmp;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_isClosed)
                        {
                            capturedRow.SetLoadedIcon(capturedBmp);
                        }
                    }), DispatcherPriority.Background);
                }
            }
        }

        private Style BuildRowContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(UIElement.FocusableProperty, false));

            var template = new ControlTemplate(typeof(ListBoxItem));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            template.VisualTree = presenter;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            style.Setters.Add(new EventSetter(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnRowItemMouseLeftButtonUp)));
            return style;
        }

        private void OnRowItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem lbi && lbi.DataContext is AppRowItem row)
            {
                ToggleRowSelection(row);
                e.Handled = true;
            }
        }

        private DataTemplate BuildRowDataTemplate()
        {
            var template = new DataTemplate(typeof(AppRowItem));

            var rowBorder = new FrameworkElementFactory(typeof(Border), "RowBorder");
            rowBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            rowBorder.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            rowBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            rowBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            rowBorder.SetValue(Border.PaddingProperty, new Thickness(10, 7, 10, 7));
            rowBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 3));
            rowBorder.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);

            var grid = new FrameworkElementFactory(typeof(Grid));
            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            grid.AppendChild(col0);
            grid.AppendChild(col1);
            grid.AppendChild(col2);

            // WinUI 3 rounded tick box
            var checkBoxBorder = new FrameworkElementFactory(typeof(Border), "CheckBorder");
            checkBoxBorder.SetValue(Grid.ColumnProperty, 0);
            checkBoxBorder.SetValue(FrameworkElement.WidthProperty, 18.0);
            checkBoxBorder.SetValue(FrameworkElement.HeightProperty, 18.0);
            checkBoxBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            checkBoxBorder.SetValue(Border.BackgroundProperty, FluentTheme.SubtleHoverBg);
            checkBoxBorder.SetValue(Border.BorderBrushProperty, FluentTheme.SwitchTrackBorder);
            checkBoxBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            checkBoxBorder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBoxBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 11, 0));

            var checkGlyph = new FrameworkElementFactory(typeof(TextBlock), "CheckGlyph");
            checkGlyph.SetValue(TextBlock.TextProperty, "\uE73E");
            checkGlyph.SetValue(TextBlock.FontFamilyProperty, FluentTheme.IconFont);
            checkGlyph.SetValue(TextBlock.FontSizeProperty, 11.0);
            checkGlyph.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            checkGlyph.SetValue(TextBlock.ForegroundProperty, FluentTheme.AccentInkBrush);
            checkGlyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkGlyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkGlyph.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            checkBoxBorder.AppendChild(checkGlyph);
            grid.AppendChild(checkBoxBorder);

            // Async-loaded icon
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetValue(Grid.ColumnProperty, 1);
            img.SetValue(FrameworkElement.WidthProperty, 28.0);
            img.SetValue(FrameworkElement.HeightProperty, 28.0);
            img.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
            img.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            img.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
            img.SetBinding(Image.SourceProperty, new Binding(nameof(AppRowItem.Icon)) { Mode = BindingMode.OneWay });
            grid.AppendChild(img);

            // App name + category
            var textStack = new FrameworkElementFactory(typeof(StackPanel));
            textStack.SetValue(Grid.ColumnProperty, 2);
            textStack.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var nameBlock = new FrameworkElementFactory(typeof(TextBlock));
            nameBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(AppRowItem.Name)) { Mode = BindingMode.OneWay });
            nameBlock.SetValue(TextBlock.FontSizeProperty, 13.5);
            nameBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
            nameBlock.SetValue(TextBlock.ForegroundProperty, FluentTheme.TextPrimary);
            nameBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            textStack.AppendChild(nameBlock);

            var catBlock = new FrameworkElementFactory(typeof(TextBlock));
            catBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(AppRowItem.Category)) { Mode = BindingMode.OneWay });
            catBlock.SetValue(TextBlock.FontSizeProperty, 11.5);
            catBlock.SetValue(TextBlock.ForegroundProperty, FluentTheme.TextMuted);
            catBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            textStack.AppendChild(catBlock);

            grid.AppendChild(textStack);
            rowBorder.AppendChild(grid);
            template.VisualTree = rowBorder;

            var hoverTrigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true,
                SourceName = "RowBorder"
            };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, FluentTheme.CardHoverBg, "RowBorder"));
            template.Triggers.Add(hoverTrigger);

            var selectedTrigger = new DataTrigger
            {
                Binding = new Binding(nameof(AppRowItem.IsSelected)),
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, FluentTheme.CardSelectedBg, "RowBorder"));
            selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, FluentTheme.AccentBrush, "RowBorder"));
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, FluentTheme.AccentBrush, "CheckBorder"));
            selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, FluentTheme.AccentBrush, "CheckBorder"));
            selectedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));
            template.Triggers.Add(selectedTrigger);

            return template;
        }
    }
}
