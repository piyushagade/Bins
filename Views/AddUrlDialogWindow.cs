using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;

namespace WinBins.Views
{
    public class AddUrlDialogWindow : Window
    {
        private readonly IconService _iconService;
        private readonly TextBox _urlBox;
        private readonly TextBox _nameBox;
        private readonly TextBlock _validationText;
        private readonly Image _iconPreviewImg;
        private readonly Button _resetIconBtn;

        public string NormalizedUrl { get; private set; }
        public string DisplayName { get; private set; }
        public string CustomIconPath { get; private set; }
        public int CustomIconIndex { get; private set; }

        public AddUrlDialogWindow(
            IconService iconService,
            string initialUrl = null,
            string initialName = null,
            string initialIconPath = null,
            int initialIconIndex = 0)
        {
            _iconService = iconService;
            CustomIconPath = initialIconPath ?? string.Empty;
            CustomIconIndex = initialIconIndex;

            bool isEdit = !string.IsNullOrWhiteSpace(initialUrl);

            Title = isEdit ? "Edit Website / URL" : "Add Website / URL";
            Width = 468;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.SingleBorderWindow;
            Background = FluentTheme.WindowBg;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            ShowInTaskbar = false;

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Interop.ApplyWindows11WindowStyling(hwnd, captionColorRef: 0x00202020);
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                }
            };

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };

            root.Children.Add(new TextBlock
            {
                Text = "Website URL",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _urlBox = FluentTheme.CreateTextBox(initialUrl ?? string.Empty, "https://example.com");
            _urlBox.TextChanged += (_, __) =>
            {
                if (_validationText.Visibility == Visibility.Visible)
                    _validationText.Visibility = Visibility.Collapsed;
            };
            _urlBox.KeyDown += Input_KeyDown;
            root.Children.Add(_urlBox);

            _validationText = new TextBlock
            {
                Text = "Please enter a valid web address (e.g., https://github.com).",
                FontSize = 12,
                Foreground = FluentTheme.DangerBrush,
                Margin = new Thickness(2, 5, 0, 0),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_validationText);

            root.Children.Add(new TextBlock
            {
                Text = "Display name (optional)",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                Margin = new Thickness(0, 14, 0, 6)
            });

            _nameBox = FluentTheme.CreateTextBox(initialName ?? string.Empty, "Auto-filled from domain if left blank");
            _nameBox.KeyDown += Input_KeyDown;
            root.Children.Add(_nameBox);

            // Icon chooser row
            var iconCard = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 14, 0, 0)
            };

            var iconRow = new Grid();
            iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _iconPreviewImg = new Image
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(_iconPreviewImg, BitmapScalingMode.HighQuality);
            Grid.SetColumn(_iconPreviewImg, 0);
            iconRow.Children.Add(_iconPreviewImg);

            var iconTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            iconTextStack.Children.Add(new TextBlock
            {
                Text = "Shortcut icon",
                FontSize = 13,
                Foreground = FluentTheme.TextPrimary
            });
            iconTextStack.Children.Add(new TextBlock
            {
                Text = "Auto-fetches site icon, or choose a custom one",
                FontSize = 11.5,
                Foreground = FluentTheme.TextMuted,
                Margin = new Thickness(0, 1, 10, 0)
            });
            Grid.SetColumn(iconTextStack, 1);
            iconRow.Children.Add(iconTextStack);

            var iconBtnGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _resetIconBtn = FluentTheme.CreateIconButton("\uE777", "Use automatic site icon", size: 28, iconSize: 12);
            _resetIconBtn.Margin = new Thickness(0, 0, 4, 0);
            _resetIconBtn.Click += (_, __) =>
            {
                CustomIconPath = string.Empty;
                CustomIconIndex = 0;
                UpdateIconPreview();
            };
            iconBtnGroup.Children.Add(_resetIconBtn);

            var chooseIconBtn = FluentTheme.CreateButton("Choose...", padding: new Thickness(10, 5, 11, 5), iconGlyph: "\uEB9F");
            chooseIconBtn.Click += ChooseIcon_Click;
            iconBtnGroup.Children.Add(chooseIconBtn);

            Grid.SetColumn(iconBtnGroup, 2);
            iconRow.Children.Add(iconBtnGroup);
            iconCard.Child = iconRow;
            root.Children.Add(iconCard);

            UpdateIconPreview();

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var cancelBtn = FluentTheme.CreateButton("Cancel");
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (_, __) =>
            {
                DialogResult = false;
                Close();
            };

            var saveBtn = FluentTheme.CreateButton(isEdit ? "Save" : "Add URL", isPrimary: true);
            saveBtn.Click += (_, __) => Confirm();

            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(saveBtn);
            root.Children.Add(btnRow);

            Content = root;

            Loaded += (_, __) =>
            {
                _urlBox.Focus();
                _urlBox.SelectAll();
            };
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
                e.Handled = true;
            }
        }

        private void ChooseIcon_Click(object sender, RoutedEventArgs e)
        {
            var picker = new IconPickerWindow(
                _iconService,
                CustomIconPath,
                CustomIconIndex,
                allowClear: !string.IsNullOrWhiteSpace(CustomIconPath),
                dialogTitle: "Choose URL Shortcut Icon",
                clearButtonLabel: "Automatic Icon",
                clearButtonGlyph: "\uE777")
            {
                Owner = this
            };

            if (picker.ShowDialog() == true)
            {
                CustomIconPath = picker.SelectedIconPath ?? string.Empty;
                CustomIconIndex = picker.SelectedIconIndex;
                UpdateIconPreview();
            }
        }

        private void UpdateIconPreview()
        {
            if (!string.IsNullOrWhiteSpace(CustomIconPath))
            {
                _iconPreviewImg.Source = _iconService.GetPathImageSource(CustomIconPath, CustomIconIndex);
                _resetIconBtn.Visibility = Visibility.Visible;
            }
            else
            {
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                _iconPreviewImg.Source = _iconService.GetPathImageSource(Path.Combine(sys32, "imageres.dll"), -25);
                _resetIconBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void Confirm()
        {
            if (!BinItem.TryNormalizeWebUrl(_urlBox.Text, out string normalized))
            {
                _validationText.Visibility = Visibility.Visible;
                _urlBox.Focus();
                return;
            }

            NormalizedUrl = normalized;
            string customTitle = (_nameBox.Text ?? string.Empty).Trim();
            DisplayName = !string.IsNullOrWhiteSpace(customTitle)
                ? customTitle
                : BinItem.DeriveUrlDisplayName(normalized);

            DialogResult = true;
            Close();
        }
    }
}
