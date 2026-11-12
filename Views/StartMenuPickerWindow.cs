using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinBins.Interop;
using WinBins.Services;

namespace WinBins.Views
{
    public class StartMenuPickerWindow : Window
    {
        private readonly IconService _iconService;
        private readonly List<DiscoveredAppEntry> _allApps;
        private readonly HashSet<string> _selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly StackPanel _listPanel;
        private readonly TextBox _searchBox;
        private readonly Button _addBtn;

        public List<DiscoveredAppEntry> SelectedApps { get; private set; } = new List<DiscoveredAppEntry>();

        public StartMenuPickerWindow(IconService iconService)
        {
            _iconService = iconService;
            _allApps = LauncherService.DiscoverStartMenuApps();

            Title = "Add Installed Apps from Start Menu";
            Width = 540;
            Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = FluentTheme.WindowBg;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            ShowInTaskbar = false;

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Interop.ApplyWindows11WindowStyling(hwnd);
            };

            var root = new Grid { Margin = new Thickness(20, 16, 20, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Select applications to add to this Bin",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            _searchBox = FluentTheme.CreateTextBox("", "Search Start Menu apps...");
            _searchBox.Margin = new Thickness(0, 0, 0, 12);
            _searchBox.TextChanged += (_, __) => RebuildList();
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
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _listPanel = new StackPanel();
            scroll.Content = _listPanel;
            listBorder.Child = scroll;
            Grid.SetRow(listBorder, 2);
            root.Children.Add(listBorder);

            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var cancelBtn = FluentTheme.CreateButton("Cancel");
            cancelBtn.Click += (_, __) => { DialogResult = false; Close(); };

            _addBtn = FluentTheme.CreateButton("Add Selected", isPrimary: true);
            _addBtn.IsEnabled = false;
            _addBtn.Margin = new Thickness(8, 0, 0, 0);
            _addBtn.Click += (_, __) =>
            {
                SelectedApps = _allApps.Where(a => _selectedPaths.Contains(a.ShortcutPath)).ToList();
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

            Loaded += (_, __) =>
            {
                RebuildList();
                _searchBox.Focus();
            };
        }

        private void RebuildList()
        {
            _listPanel.Children.Clear();
            string query = (_searchBox.Text ?? string.Empty).Trim();

            IEnumerable<DiscoveredAppEntry> filtered = _allApps;
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(a =>
                    a.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (a.Category != null && a.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            foreach (DiscoveredAppEntry app in filtered.Take(120))
            {
                bool isSelected = _selectedPaths.Contains(app.ShortcutPath);

                var row = new Border
                {
                    Background = isSelected ? FluentTheme.CardSelectedBg : Brushes.Transparent,
                    BorderBrush = isSelected ? FluentTheme.AccentBrush : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 3),
                    Cursor = Cursors.Hand
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var cb = new CheckBox
                {
                    IsChecked = isSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsHitTestVisible = false
                };
                Grid.SetColumn(cb, 0);
                grid.Children.Add(cb);

                var img = new Image
                {
                    Width = 28,
                    Height = 28,
                    Source = _iconService.GetPathImageSource(app.ShortcutPath),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                Grid.SetColumn(img, 1);
                grid.Children.Add(img);

                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                textStack.Children.Add(new TextBlock
                {
                    Text = app.Name,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = FluentTheme.TextPrimary,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = app.Category,
                    FontSize = 11.5,
                    Foreground = FluentTheme.TextMuted,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(textStack, 2);
                grid.Children.Add(textStack);

                row.Child = grid;

                row.MouseEnter += (_, __) =>
                {
                    if (!_selectedPaths.Contains(app.ShortcutPath))
                        row.Background = FluentTheme.CardHoverBg;
                };
                row.MouseLeave += (_, __) =>
                {
                    if (!_selectedPaths.Contains(app.ShortcutPath))
                        row.Background = Brushes.Transparent;
                };
                row.MouseLeftButtonUp += (_, __) =>
                {
                    if (_selectedPaths.Contains(app.ShortcutPath))
                    {
                        _selectedPaths.Remove(app.ShortcutPath);
                        cb.IsChecked = false;
                        row.Background = FluentTheme.CardHoverBg;
                        row.BorderBrush = Brushes.Transparent;
                    }
                    else
                    {
                        _selectedPaths.Add(app.ShortcutPath);
                        cb.IsChecked = true;
                        row.Background = FluentTheme.CardSelectedBg;
                        row.BorderBrush = FluentTheme.AccentBrush;
                    }
                    UpdateAddButton();
                };

                _listPanel.Children.Add(row);
            }

            UpdateAddButton();
        }

        private void UpdateAddButton()
        {
            int n = _selectedPaths.Count;
            _addBtn.IsEnabled = n > 0;
            _addBtn.Content = n > 0 ? $"Add {n} App{(n == 1 ? "" : "s")}" : "Add Selected";
        }
    }
}
