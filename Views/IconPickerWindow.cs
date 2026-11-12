using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinBins.Interop;
using WinBins.Services;

namespace WinBins.Views
{
    public class IconPickerWindow : Window
    {
        private readonly IconService _iconService;

        public string SelectedIconPath { get; private set; }
        public int SelectedIconIndex { get; private set; }

        public IconPickerWindow(
            IconService iconService,
            string currentPath,
            int currentIndex,
            bool allowClear = false,
            string dialogTitle = null,
            string clearButtonLabel = "No Icon",
            string clearButtonGlyph = "\uE711")
        {
            _iconService = iconService;
            SelectedIconPath = currentPath;
            SelectedIconIndex = currentIndex;

            Title = dialogTitle ?? "Choose Icon";
            Width = 520;
            Height = 470;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = FluentTheme.LayerBg;
            Foreground = FluentTheme.TextPrimary;
            FontFamily = FluentTheme.BodyFont;
            ShowInTaskbar = false;

            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Interop.ApplyWindows11WindowStyling(hwnd, captionColorRef: 0x00202020);
            };

            var root = new Grid { Margin = new Thickness(20, 16, 20, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = dialogTitle ?? "Select an icon or browse a custom file",
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(titleBlock, 0);
            root.Children.Add(titleBlock);

            var topBtnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 14)
            };

            var browseBtn = FluentTheme.CreateButton("Browse File...", isPrimary: true, iconGlyph: "\uE8E5");
            browseBtn.Click += BrowseCustomFile_Click;
            topBtnRow.Children.Add(browseBtn);

            if (allowClear)
            {
                var clearBtn = FluentTheme.CreateButton(clearButtonLabel ?? "No Icon", iconGlyph: clearButtonGlyph ?? "\uE711");
                clearBtn.Margin = new Thickness(8, 0, 0, 0);
                clearBtn.Click += (_, __) =>
                {
                    SelectedIconPath = string.Empty;
                    SelectedIconIndex = 0;
                    DialogResult = true;
                    Close();
                };
                topBtnRow.Children.Add(clearBtn);
            }

            Grid.SetRow(topBtnRow, 1);
            root.Children.Add(topBtnRow);

            var galleryBorder = new Border
            {
                Background = FluentTheme.SidebarBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10)
            };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var grid = new UniformGrid { Columns = 4 };

            List<PresetIconOption> presets = IconService.GetWindows11PresetIcons();
            foreach (PresetIconOption preset in presets)
            {
                var card = new Border
                {
                    Background = FluentTheme.CardBg,
                    BorderBrush = FluentTheme.CardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 10, 8, 10),
                    Margin = new Thickness(4),
                    Cursor = Cursors.Hand
                };

                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var img = new Image
                {
                    Width = 40,
                    Height = 40,
                    Source = _iconService.GetPathImageSource(preset.ResourcePath, preset.ResourceIndex),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                stack.Children.Add(img);

                stack.Children.Add(new TextBlock
                {
                    Text = preset.Label,
                    FontSize = 11.5,
                    Foreground = FluentTheme.TextSecondary,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                card.Child = stack;
                card.MouseEnter += (_, __) =>
                {
                    card.Background = FluentTheme.CardHoverBg;
                    card.BorderBrush = FluentTheme.AccentBrush;
                };
                card.MouseLeave += (_, __) =>
                {
                    card.Background = FluentTheme.CardBg;
                    card.BorderBrush = FluentTheme.CardBorder;
                };
                card.MouseLeftButtonUp += (_, __) =>
                {
                    SelectedIconPath = preset.ResourcePath;
                    SelectedIconIndex = preset.ResourceIndex;
                    DialogResult = true;
                    Close();
                };

                grid.Children.Add(card);
            }

            scroll.Content = grid;
            galleryBorder.Child = scroll;
            Grid.SetRow(galleryBorder, 2);
            root.Children.Add(galleryBorder);

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var cancelBtn = FluentTheme.CreateButton("Cancel");
            cancelBtn.Click += (_, __) => { DialogResult = false; Close(); };
            footer.Children.Add(cancelBtn);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
        }

        private void BrowseCustomFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Custom Icon or Image",
                Filter = "Icons, Images & Executables (*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog(this) == true)
            {
                SelectedIconPath = dlg.FileName;
                SelectedIconIndex = 0;
                DialogResult = true;
                Close();
            }
        }
    }
}
