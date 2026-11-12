using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using WinBins.Interop;

namespace WinBins.Views
{
    /// <summary>
    /// Authentic Windows 11 (WinUI 3 ContentDialog) modal popup for confirmations and alerts.
    /// </summary>
    public class ContentDialogWindow : Window
    {
        public ContentDialogWindow(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText = null,
            bool isDestructive = false)
        {
            Title = title ?? "Bins";
            Width = 400;
            SizeToContent = SizeToContent.Height;
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

            bool hasTwoButtons = !string.IsNullOrEmpty(closeButtonText);

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    DialogResult = true;
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
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Content body
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Command bar footer

            // --- WinUI 3 ContentDialog Body ---
            var bodyStack = new StackPanel
            {
                Margin = new Thickness(24, 22, 24, 22),
                Background = Brushes.Transparent
            };
            bodyStack.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            bodyStack.Children.Add(new TextBlock
            {
                Text = title ?? string.Empty,
                FontFamily = FluentTheme.DisplayFont,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            bodyStack.Children.Add(new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = 13.5,
                Foreground = FluentTheme.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            });

            Grid.SetRow(bodyStack, 0);
            rootGrid.Children.Add(bodyStack);

            // --- WinUI 3 ContentDialog Command Space Footer ---
            var footerBorder = new Border
            {
                Background = FluentTheme.ShellBg,
                BorderBrush = FluentTheme.DividerBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                Padding = new Thickness(24, 16, 24, 16)
            };

            if (hasTwoButtons)
            {
                var buttonGrid = new UniformGrid { Columns = 2 };

                var primaryBtn = FluentTheme.CreateButton(
                    primaryButtonText ?? "OK",
                    isPrimary: !isDestructive,
                    isDangerSolid: isDestructive,
                    padding: new Thickness(16, 7, 16, 7));
                primaryBtn.Height = 32;
                primaryBtn.Margin = new Thickness(0, 0, 4, 0);
                primaryBtn.Click += (_, __) =>
                {
                    DialogResult = true;
                    Close();
                };

                var cancelBtn = FluentTheme.CreateButton(
                    closeButtonText,
                    padding: new Thickness(16, 7, 16, 7));
                cancelBtn.Height = 32;
                cancelBtn.Margin = new Thickness(4, 0, 0, 0);
                cancelBtn.Click += (_, __) =>
                {
                    DialogResult = false;
                    Close();
                };

                buttonGrid.Children.Add(primaryBtn);
                buttonGrid.Children.Add(cancelBtn);
                footerBorder.Child = buttonGrid;
            }
            else
            {
                var singleBtn = FluentTheme.CreateButton(
                    primaryButtonText ?? "OK",
                    isPrimary: true,
                    padding: new Thickness(24, 7, 24, 7));
                singleBtn.Height = 32;
                singleBtn.MinWidth = 110;
                singleBtn.HorizontalAlignment = HorizontalAlignment.Right;
                singleBtn.Click += (_, __) =>
                {
                    DialogResult = true;
                    Close();
                };
                footerBorder.Child = singleBtn;
            }

            Grid.SetRow(footerBorder, 1);
            rootGrid.Children.Add(footerBorder);

            outerBorder.Child = rootGrid;
            Content = outerBorder;
        }

        public static bool ShowConfirm(
            Window owner,
            string title,
            string message,
            string primaryText = "Confirm",
            string cancelText = "Cancel",
            bool isDestructive = false)
        {
            var dlg = new ContentDialogWindow(title, message, primaryText, cancelText, isDestructive);
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                dlg.Owner = owner;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            return dlg.ShowDialog() == true;
        }

        public static void ShowMessage(
            Window owner,
            string title,
            string message,
            string buttonText = "OK")
        {
            var dlg = new ContentDialogWindow(title, message, buttonText, closeButtonText: null, isDestructive: false);
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                dlg.Owner = owner;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dlg.ShowDialog();
        }
    }
}
