using System;
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
    /// Minimal Windows 11 (WinUI 3 ContentDialog-style) guide shown when adding a Bin to the taskbar,
    /// explaining the 1-step "Pin to taskbar" action and live-updating as soon as the user pins it.
    /// </summary>
    public class TaskbarPinGuideDialog : Window
    {
        private readonly ConfigService _configService;
        private readonly IconService _iconService;
        private readonly TaskbarBinHostManager _taskbarManager;

        private BinGroup _bin;
        private readonly Image _binIconImage;
        private readonly TextBlock _titleBlock;
        private readonly TextBlock _introBlock;
        private readonly Border _stepCard;
        private readonly TextBlock _stepGlyph;
        private readonly TextBlock _stepTitle;
        private readonly TextBlock _stepDetail;
        private readonly CheckBox _dontShowAgainCheck;
        private readonly Button _primaryBtn;

        public event Action SuppressionPreferenceChanged;

        public TaskbarPinGuideDialog(
            ConfigService configService,
            IconService iconService,
            TaskbarBinHostManager taskbarManager,
            BinGroup bin)
        {
            _configService = configService;
            _iconService = iconService;
            _taskbarManager = taskbarManager;
            _bin = bin;

            Title = "Pin Bin to Taskbar";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            Background = FluentTheme.LayerBg;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;

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
                Win32Interop.ApplyWindows11WindowStyling(hwnd, forceDarkMode: !FluentTheme.IsLightThemeActive);
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter)
                {
                    Close();
                    e.Handled = true;
                }
            };

            var outerBorder = new Border
            {
                Background = FluentTheme.LayerBg,
                BorderBrush = FluentTheme.SubtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Body
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // --- Dialog Body ---
            var bodyStack = new StackPanel
            {
                Margin = new Thickness(24, 22, 24, 20),
                Background = Brushes.Transparent
            };
            bodyStack.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            // Header row with Bin icon + Title + Close X
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBadge = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(7),
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.SurfaceStroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _binIconImage = new Image { Width = 26, Height = 26 };
            RenderOptions.SetBitmapScalingMode(_binIconImage, BitmapScalingMode.HighQuality);
            iconBadge.Child = _binIconImage;
            Grid.SetColumn(iconBadge, 0);
            headerGrid.Children.Add(iconBadge);

            _titleBlock = new TextBlock
            {
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 17.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_titleBlock, 1);
            headerGrid.Children.Add(_titleBlock);

            var closeBtn = FluentTheme.CreateIconButton("\uE711", "Close", size: 28, iconSize: 11);
            closeBtn.Margin = new Thickness(8, -4, -6, 0);
            closeBtn.Click += (_, __) => Close();
            Grid.SetColumn(closeBtn, 2);
            headerGrid.Children.Add(closeBtn);

            bodyStack.Children.Add(headerGrid);

            _introBlock = new TextBlock
            {
                FontSize = 13,
                Foreground = FluentTheme.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Margin = new Thickness(0, 0, 0, 14)
            };
            bodyStack.Children.Add(_introBlock);

            // Highlighted 1-Step Instruction / Live Status Card
            _stepCard = new Border
            {
                Background = FluentTheme.BrushFromHex("#1460CDFF"),
                BorderBrush = FluentTheme.BrushFromHex("#3860CDFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var stepGrid = new Grid();
            stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _stepGlyph = new TextBlock
            {
                Text = "\uE718",
                FontFamily = FluentTheme.IconFont,
                FontSize = 16,
                Foreground = FluentTheme.AccentBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 12, 0)
            };
            Grid.SetColumn(_stepGlyph, 0);
            stepGrid.Children.Add(_stepGlyph);

            var stepTextStack = new StackPanel();
            _stepTitle = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                TextWrapping = TextWrapping.Wrap
            };
            stepTextStack.Children.Add(_stepTitle);

            _stepDetail = new TextBlock
            {
                FontSize = 12,
                Foreground = FluentTheme.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            stepTextStack.Children.Add(_stepDetail);

            Grid.SetColumn(stepTextStack, 1);
            stepGrid.Children.Add(stepTextStack);
            _stepCard.Child = stepGrid;
            bodyStack.Children.Add(_stepCard);

            // Why consider pinning?
            bodyStack.Children.Add(new TextBlock
            {
                Text = "Why pin your bin?",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextMuted,
                Margin = new Thickness(0, 0, 0, 8)
            });

            bodyStack.Children.Add(CreateBenefitRow(
                "\uE840",
                "Remembers taskbar location",
                "Windows 11 only saves rearranged taskbar positions across restarts for pinned icons."));

            bodyStack.Children.Add(CreateBenefitRow(
                "\uED1A",
                "Hides window hover preview",
                "Once pinned, Bins removes the temporary background window so hovering won't pop up a preview card.",
                isLast: true));

            Grid.SetRow(bodyStack, 0);
            rootGrid.Children.Add(bodyStack);

            // --- WinUI 3 Dialog Footer Strip ---
            var footerBorder = new Border
            {
                Background = FluentTheme.ShellBg,
                BorderBrush = FluentTheme.DividerBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                Padding = new Thickness(24, 14, 24, 14)
            };

            var footerDock = new DockPanel { LastChildFill = true };

            _primaryBtn = FluentTheme.CreateButton("Got it", isPrimary: true, padding: new Thickness(22, 6, 22, 6));
            _primaryBtn.MinWidth = 88;
            _primaryBtn.Click += (_, __) => Close();
            DockPanel.SetDock(_primaryBtn, Dock.Right);
            footerDock.Children.Add(_primaryBtn);

            bool currentlyHidden = _configService.Current?.HidePinToTaskbarPrompt ?? false;
            _dontShowAgainCheck = FluentTheme.CreateTickCheckBox("Don't show again", currentlyHidden);
            _dontShowAgainCheck.VerticalAlignment = VerticalAlignment.Center;
            _dontShowAgainCheck.Checked += DontShowAgain_Changed;
            _dontShowAgainCheck.Unchecked += DontShowAgain_Changed;
            footerDock.Children.Add(_dontShowAgainCheck);

            footerBorder.Child = footerDock;
            Grid.SetRow(footerBorder, 1);
            rootGrid.Children.Add(footerBorder);

            outerBorder.Child = rootGrid;
            Content = outerBorder;

            if (_taskbarManager != null)
            {
                _taskbarManager.TaskbarPinStateChanged += OnTaskbarPinStateChanged;
            }

            Closed += (_, __) =>
            {
                if (_taskbarManager != null)
                {
                    _taskbarManager.TaskbarPinStateChanged -= OnTaskbarPinStateChanged;
                }
            };

            UpdateForBin(bin);
        }

        public void UpdateForBin(BinGroup bin)
        {
            if (bin == null)
                return;

            _bin = bin;
            _binIconImage.Source = _iconService.CreateBinImageSource(bin);

            string name = string.IsNullOrWhiteSpace(bin.Name) ? "Bin" : bin.Name;
            bool isPinned = ShortcutService.IsBinPinnedOnTaskbar(bin);

            if (isPinned)
            {
                _titleBlock.Text = $"\"{name}\" is pinned";
                _introBlock.Text = $"\"{name}\" is now pinned directly to your Windows 11 Taskbar.";

                _stepCard.Background = FluentTheme.BrushFromHex("#146CCB5F");
                _stepCard.BorderBrush = FluentTheme.BrushFromHex("#406CCB5F");
                _stepGlyph.Text = "\uE73E";
                _stepGlyph.Foreground = FluentTheme.SuccessBrush;
                _stepTitle.Text = "All set — taskbar location locked";
                _stepDetail.Text = "You can drag this icon anywhere on your taskbar. Windows will remember its spot and won't show a window preview on hover.";
                _primaryBtn.Content = "Done";
            }
            else
            {
                _titleBlock.Text = $"Pin \"{name}\" to taskbar";
                _introBlock.Text = $"\"{name}\" has been placed on your taskbar. Because Windows 11 blocks apps from pinning icons automatically, complete one quick step:";

                _stepCard.Background = FluentTheme.BrushFromHex("#1460CDFF");
                _stepCard.BorderBrush = FluentTheme.BrushFromHex("#3860CDFF");
                _stepGlyph.Text = "\uE718";
                _stepGlyph.Foreground = FluentTheme.AccentBrush;
                _stepTitle.Text = $"Right-click \"{name}\" on your taskbar → Pin to taskbar";
                _stepDetail.Text = "You can do this right now while this window is open, then drag the icon to your preferred spot.";
                _primaryBtn.Content = "Got it";
            }
        }

        private void OnTaskbarPinStateChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_bin != null)
                {
                    UpdateForBin(_bin);
                }
            }));
        }

        private void DontShowAgain_Changed(object sender, RoutedEventArgs e)
        {
            if (_configService.Current == null)
                return;

            bool hide = _dontShowAgainCheck.IsChecked == true;
            if (_configService.Current.HidePinToTaskbarPrompt != hide)
            {
                _configService.Current.HidePinToTaskbarPrompt = hide;
                _configService.Save(notify: false);
                SuppressionPreferenceChanged?.Invoke();
            }
        }

        private static Grid CreateBenefitRow(string glyph, string title, string description, bool isLast = false)
        {
            var grid = new Grid
            {
                Margin = new Thickness(2, 0, 0, isLast ? 0 : 10)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = FluentTheme.IconFont,
                FontSize = 14,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 11, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = FluentTheme.TextMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });

            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);
            return grid;
        }
    }
}
