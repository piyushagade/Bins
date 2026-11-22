using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;

namespace WinBins.Views
{
    /// <summary>
    /// Global Windows 11 Cross-Bin Omni-Search Launcher (Spotlight-style command bar searching across all Bins).
    /// </summary>
    public class OmniSearchWindow : Window
    {
        private sealed class SearchEntry
        {
            public BinGroup Bin;
            public BinItem Item;
            public List<RunningWindowEntry> RunningWindows;
            public Border RowBorder;
        }

        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly RunningAppsTaskbarManager _runningAppsManager;
        private readonly Action<BinGroup> _onOpenBinEditor;

        private readonly Border _outerBorder;
        private readonly TextBox _searchBox;
        private readonly StackPanel _resultsPanel;
        private readonly ScrollViewer _resultsScroll;
        private readonly TextBlock _emptyStateText;

        private readonly List<SearchEntry> _currentEntries = new List<SearchEntry>();
        private int _selectedIndex = 0;
        private bool _suppressTextChanged;

        public OmniSearchWindow(
            ConfigService configService,
            IconService iconService,
            RunningAppsTaskbarManager runningAppsManager,
            Action<BinGroup> onOpenBinEditor = null)
        {
            _configService = configService;
            _iconService = iconService;
            _runningAppsManager = runningAppsManager;
            _onOpenBinEditor = onOpenBinEditor;

            Title = "Bins Search";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Transparent;
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

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                ApplyDwmBackdrop(hwnd);
            };

            FluentTheme.ThemeChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ApplyDwmBackdrop(hwnd);
                    }
                }));
            };

            Deactivated += (_, __) => HideSearch();
            PreviewKeyDown += OnWindowPreviewKeyDown;

            _outerBorder = new Border
            {
                Background = FluentTheme.PopupBg,
                BorderBrush = FluentTheme.SubtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10)
            };

            var rootStack = new StackPanel();

            // Search input header row
            var headerBorder = new Border
            {
                BorderBrush = FluentTheme.DividerBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var searchDock = new DockPanel();
            var searchIcon = new TextBlock
            {
                Text = "\uE721",
                FontFamily = FluentTheme.IconFont,
                FontSize = 16,
                Foreground = FluentTheme.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            DockPanel.SetDock(searchIcon, Dock.Left);
            searchDock.Children.Add(searchIcon);

            var escBadge = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = "Esc",
                    FontSize = 11,
                    Foreground = FluentTheme.TextMuted
                }
            };
            DockPanel.SetDock(escBadge, Dock.Right);
            searchDock.Children.Add(escBadge);

            var searchInputGrid = new Grid();
            _searchBox = new TextBox
            {
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 15.5,
                Foreground = FluentTheme.TextPrimary,
                CaretBrush = FluentTheme.AccentBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var placeholderText = new TextBlock
            {
                Text = "Search apps, folders, websites, and actions across all Bins...",
                FontSize = 14.5,
                Foreground = FluentTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            _searchBox.TextChanged += (_, __) =>
            {
                placeholderText.Visibility = string.IsNullOrEmpty(_searchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                if (!_suppressTextChanged)
                {
                    RebuildResults(_searchBox.Text);
                }
            };
            searchInputGrid.Children.Add(placeholderText);
            searchInputGrid.Children.Add(_searchBox);
            searchDock.Children.Add(searchInputGrid);

            headerBorder.Child = searchDock;
            rootStack.Children.Add(headerBorder);

            // Results area
            _resultsPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
            _resultsScroll = new ScrollViewer
            {
                MaxHeight = 350,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _resultsPanel
            };
            rootStack.Children.Add(_resultsScroll);

            _emptyStateText = new TextBlock
            {
                Text = "No matching items found across your Bins.",
                FontSize = 13,
                Foreground = FluentTheme.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(16, 24, 16, 24),
                Visibility = Visibility.Collapsed
            };
            rootStack.Children.Add(_emptyStateText);

            // Footer hint strip
            var footerBorder = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.DividerBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 10, 10),
                Padding = new Thickness(14, 7, 14, 7)
            };
            var footerText = new TextBlock
            {
                Text = "↑↓ Navigate   •   Enter Open / Focus   •   Shift+Enter New Window   •   Ctrl+Shift+Enter Run as Admin",
                FontSize = 11,
                Foreground = FluentTheme.TextMuted
            };
            footerBorder.Child = footerText;
            rootStack.Children.Add(footerBorder);

            _outerBorder.Child = rootStack;
            Content = _outerBorder;
        }

        private void ApplyDwmBackdrop(IntPtr hwnd)
        {
            BinBackdropStyle backdrop = _configService.Current?.FlyoutBackdrop ?? BinBackdropStyle.Acrylic;
            int opacity = _configService.Current?.FlyoutOpacityPercent ?? 82;
            Win32Interop.ApplyFlyoutBackdropAndOpacity(
                hwnd,
                (int)backdrop,
                opacity,
                FluentTheme.IsLightThemeActive);
        }

        public void ToggleSearch()
        {
            if (IsVisible)
            {
                HideSearch();
            }
            else
            {
                ShowSearch();
            }
        }

        public void ShowSearch()
        {
            _suppressTextChanged = true;
            _searchBox.Text = string.Empty;
            _suppressTextChanged = false;

            RebuildResults(string.Empty);

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2.0;
            Top = workArea.Top + Math.Max(60, workArea.Height * 0.20);

            if (!IsVisible)
            {
                Show();
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyDwmBackdrop(hwnd);
                Win32Interop.SetForegroundWindow(hwnd);
            }

            Activate();
            _searchBox.Focus();
        }

        public void HideSearch()
        {
            if (IsVisible)
            {
                Hide();
            }
        }

        public void RebuildResults(string rawQuery)
        {
            _resultsPanel.Children.Clear();
            _currentEntries.Clear();
            _selectedIndex = 0;

            string query = (rawQuery ?? string.Empty).Trim();
            var bins = _configService.Current?.Bins ?? new List<BinGroup>();

            var scored = new List<Tuple<int, SearchEntry>>();
            foreach (BinGroup bin in bins)
            {
                if (bin == null)
                    continue;

                var combinedItems = new List<BinItem>();
                if (bin.Items != null)
                {
                    combinedItems.AddRange(bin.Items.Where(it => it != null));
                }
                if (!string.IsNullOrWhiteSpace(bin.LiveFolderPath) || bin.LiveFolderRecentMode)
                {
                    combinedItems.AddRange(LauncherService.GetLiveFolderItems(bin, 16));
                }

                foreach (BinItem item in combinedItems)
                {
                    int score = ScoreMatch(item, bin, query);
                    if (score <= 0)
                        continue;

                    var running = _runningAppsManager?.GetRunningWindowsForItem(item) ?? new List<RunningWindowEntry>();
                    scored.Add(Tuple.Create(score + (running.Count > 0 ? 15 : 0), new SearchEntry
                    {
                        Bin = bin,
                        Item = item,
                        RunningWindows = running
                    }));
                }
            }

            var ordered = string.IsNullOrEmpty(query)
                ? scored.Take(24).Select(t => t.Item2).ToList()
                : scored.OrderByDescending(t => t.Item1).Take(24).Select(t => t.Item2).ToList();

            if (ordered.Count == 0)
            {
                _resultsScroll.Visibility = Visibility.Collapsed;
                _emptyStateText.Visibility = Visibility.Visible;
                return;
            }

            _resultsScroll.Visibility = Visibility.Visible;
            _emptyStateText.Visibility = Visibility.Collapsed;

            for (int i = 0; i < ordered.Count; i++)
            {
                int idx = i;
                SearchEntry entry = ordered[i];
                Border row = CreateResultRow(entry, idx);
                entry.RowBorder = row;
                _currentEntries.Add(entry);
                _resultsPanel.Children.Add(row);
            }

            UpdateSelectionHighlight();
        }

        private static int ScoreMatch(BinItem item, BinGroup bin, string query)
        {
            if (string.IsNullOrEmpty(query))
                return 50;

            string name = item.Name ?? string.Empty;
            string binName = bin.Name ?? string.Empty;
            string sec = item.Section ?? string.Empty;
            string path = item.TargetPath ?? string.Empty;

            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 200;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return 140;
            if (sec.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return 95;
            if (binName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return 80;
            if (path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return 60;

            // Subsequence fuzzy match on item name (e.g. "vsc" -> "Visual Studio Code")
            int qIdx = 0;
            for (int i = 0; i < name.Length && qIdx < query.Length; i++)
            {
                if (char.ToLowerInvariant(name[i]) == char.ToLowerInvariant(query[qIdx]))
                    qIdx++;
            }
            return qIdx == query.Length ? 45 : 0;
        }

        private Border CreateResultRow(SearchEntry entry, int index)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 1, 0, 1),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconImg = new Image
            {
                Width = 28,
                Height = 28,
                Source = _iconService.GetItemImageSource(entry.Item),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.HighQuality);
            Grid.SetColumn(iconImg, 0);
            grid.Children.Add(iconImg);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new TextBlock
            {
                Text = entry.Item.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 290
            });

            if (entry.Item.RunAsAdmin)
            {
                nameRow.Children.Add(new TextBlock
                {
                    Text = "\uEA18",
                    FontFamily = FluentTheme.IconFont,
                    FontSize = 11,
                    Foreground = FluentTheme.AccentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "Always runs as administrator"
                });
            }
            textStack.Children.Add(nameRow);

            string sub = !string.IsNullOrWhiteSpace(entry.Item.Section)
                ? $"{entry.Item.Section}  •  {entry.Item.TargetPath}"
                : entry.Item.TargetPath;
            textStack.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = FluentTheme.TextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 330
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var rightBadges = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (entry.RunningWindows != null && entry.RunningWindows.Count > 0)
            {
                rightBadges.Children.Add(new Border
                {
                    Background = FluentTheme.CardBg,
                    BorderBrush = FluentTheme.CardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 6, 0),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new Border
                            {
                                Width = 6,
                                Height = 6,
                                CornerRadius = new CornerRadius(3),
                                Background = FluentTheme.SuccessBrush,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 5, 0)
                            },
                            new TextBlock
                            {
                                Text = entry.RunningWindows.Count > 1 ? $"{entry.RunningWindows.Count} open" : "Running",
                                FontSize = 10.5,
                                Foreground = FluentTheme.TextSecondary
                            }
                        }
                    }
                });
            }

            Brush binAccent = FluentTheme.GetEffectiveAccentBrush(entry.Bin.AccentColorHex);
            Brush binPillBg = FluentTheme.GetTintedPillBgBrush(entry.Bin.AccentColorHex, 36);
            rightBadges.Children.Add(new Border
            {
                Background = binPillBg,
                BorderBrush = string.IsNullOrWhiteSpace(entry.Bin.AccentColorHex) ? FluentTheme.CardBorder : binAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock
                {
                    Text = entry.Bin.Name,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = string.IsNullOrWhiteSpace(entry.Bin.AccentColorHex) ? FluentTheme.TextSecondary : binAccent
                }
            });

            Grid.SetColumn(rightBadges, 2);
            grid.Children.Add(rightBadges);

            row.Child = grid;

            row.MouseEnter += (_, __) =>
            {
                _selectedIndex = index;
                UpdateSelectionHighlight();
            };

            row.MouseLeftButtonUp += (_, e) =>
            {
                bool forceNew = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                bool asAdmin = (Keyboard.Modifiers & ModifierKeys.Control) != 0 && forceNew;
                ExecuteSelectedEntry(entry, forceNew, asAdmin);
                e.Handled = true;
            };

            return row;
        }

        private void UpdateSelectionHighlight()
        {
            for (int i = 0; i < _currentEntries.Count; i++)
            {
                Border row = _currentEntries[i].RowBorder;
                if (row == null)
                    continue;

                bool selected = (i == _selectedIndex);
                row.Background = selected ? FluentTheme.CardSelectedBg : Brushes.Transparent;
                row.BorderBrush = selected ? FluentTheme.AccentBrush : Brushes.Transparent;
                if (selected)
                {
                    row.BringIntoView();
                }
            }
        }

        private void ExecuteSelectedEntry(SearchEntry entry, bool forceNewInstance, bool runAsAdmin)
        {
            if (entry == null)
                return;

            HideSearch();

            if (!forceNewInstance && !runAsAdmin && entry.RunningWindows != null && entry.RunningWindows.Count > 0)
            {
                Win32Interop.ActivateAndRestoreWindow(entry.RunningWindows[0].Hwnd);
                return;
            }

            LauncherService.LaunchItem(entry.Item, runAsAdmin);
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideSearch();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && _currentEntries.Count > 0)
            {
                _selectedIndex = (_selectedIndex + 1) % _currentEntries.Count;
                UpdateSelectionHighlight();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up && _currentEntries.Count > 0)
            {
                _selectedIndex = (_selectedIndex - 1 + _currentEntries.Count) % _currentEntries.Count;
                UpdateSelectionHighlight();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && _selectedIndex >= 0 && _selectedIndex < _currentEntries.Count)
            {
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                ExecuteSelectedEntry(_currentEntries[_selectedIndex], forceNewInstance: shift, runAsAdmin: shift && ctrl);
                e.Handled = true;
            }
        }
    }
}
