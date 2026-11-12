using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WinBins.Interop;

namespace WinBins.Views
{
    public class RenameDialogWindow : Window
    {
        private readonly TextBox _inputBox;
        public string ResultText { get; private set; }

        public RenameDialogWindow(string title, string prompt, string currentValue)
        {
            Title = title;
            Width = 380;
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
                Win32Interop.ApplyWindows11WindowStyling(hwnd);
            };

            var root = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };

            root.Children.Add(new TextBlock
            {
                Text = prompt,
                FontSize = 13.5,
                Foreground = FluentTheme.TextSecondary,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _inputBox = FluentTheme.CreateTextBox(currentValue ?? string.Empty, "Enter name...");
            _inputBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Confirm();
                }
                else if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
            root.Children.Add(_inputBox);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancelBtn = FluentTheme.CreateButton("Cancel");
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (_, __) => { DialogResult = false; Close(); };

            var saveBtn = FluentTheme.CreateButton("Save", isPrimary: true);
            saveBtn.Click += (_, __) => Confirm();

            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(saveBtn);
            root.Children.Add(btnRow);

            Content = root;

            Loaded += (_, __) =>
            {
                _inputBox.Focus();
                _inputBox.SelectAll();
            };
        }

        private void Confirm()
        {
            string trimmed = (_inputBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
                return;

            ResultText = trimmed;
            DialogResult = true;
            Close();
        }
    }
}
