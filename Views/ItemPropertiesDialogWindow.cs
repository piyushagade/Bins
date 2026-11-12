using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Views
{
    public class ItemPropertiesDialogWindow : Window
    {
        private static readonly int[] DelayStepsMs = { 0, 250, 500, 1000, 2000, 3000, 5000 };

        private readonly BinItem _item;
        private readonly TextBox _nameBox;
        private readonly TextBox _targetBox;
        private readonly TextBox _argsBox;
        private readonly TextBox _workDirBox;
        private readonly TextBox _sectionBox;
        private readonly CheckBox _adminToggle;
        private readonly CheckBox _openAllToggle;

        private BinWindowStyle _selectedWindowStyle;
        private int _selectedDelayMs;

        private readonly Border _winNormalBtn;
        private readonly TextBlock _winNormalTxt;
        private readonly Border _winMaxBtn;
        private readonly TextBlock _winMaxTxt;
        private readonly Border _winMinBtn;
        private readonly TextBlock _winMinTxt;
        private readonly TextBlock _delayValueText;

        public ItemPropertiesDialogWindow(BinItem item, IEnumerable<string> existingSections = null)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _selectedWindowStyle = item.WindowStyle;
            _selectedDelayMs = item.OpenAllDelayMs;

            bool isSystem = item.Kind == BinItemKind.SystemCommand || BinItem.IsSystemCommand(item.TargetPath);
            bool isPowerShellSys = isSystem && string.Equals(BinItem.GetSystemActionKey(item.TargetPath), "powershell", StringComparison.OrdinalIgnoreCase);
            bool isWeb = item.Kind == BinItemKind.WebUrl || BinItem.IsWebUrl(item.TargetPath);
            bool isPackaged = item.Kind == BinItemKind.PackagedApp || BinItem.IsPackagedApp(item.TargetPath);

            Title = $"Properties — {item.Name}";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.SingleBorderWindow;
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

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                }
            };

            var rootBorder = new Border
            {
                Background = FluentTheme.WindowBg,
                Padding = new Thickness(22, 16, 22, 20)
            };
            var root = new StackPanel();

            // 1. Display Name
            root.Children.Add(CreateFieldLabel("Display name", topMargin: 0));
            _nameBox = FluentTheme.CreateTextBox(item.Name ?? string.Empty, "Item display name");
            root.Children.Add(_nameBox);

            // 2. Target Path (editable for apps/files/folders/urls, read-only badge for system/packaged)
            root.Children.Add(CreateFieldLabel(isWeb ? "Website URL" : "Target path"));
            var targetGrid = new Grid();
            targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _targetBox = FluentTheme.CreateTextBox(item.TargetPath ?? string.Empty, "Path or command");
            if (isSystem || isPackaged)
            {
                _targetBox.IsReadOnly = true;
                _targetBox.Opacity = 0.75;
            }
            Grid.SetColumn(_targetBox, 0);
            targetGrid.Children.Add(_targetBox);

            if (!isSystem && !isWeb && !isPackaged)
            {
                var browseBtn = FluentTheme.CreateButton("Browse...", padding: new Thickness(10, 6, 10, 6));
                browseBtn.Margin = new Thickness(8, 0, 0, 0);
                browseBtn.Click += BrowseTarget_Click;
                Grid.SetColumn(browseBtn, 1);
                targetGrid.Children.Add(browseBtn);
            }
            root.Children.Add(targetGrid);

            // 3. Command-line arguments / PowerShell command
            if (!isWeb && !isPackaged && (!isSystem || isPowerShellSys))
            {
                root.Children.Add(CreateFieldLabel(isPowerShellSys ? "PowerShell command" : "Command-line arguments (optional)"));
                _argsBox = FluentTheme.CreateTextBox(
                    item.Arguments ?? string.Empty,
                    isPowerShellSys ? "e.g. Get-Process | Out-GridView" : "e.g. --profile \"Work\"");
                root.Children.Add(_argsBox);

                // 4. Working directory
                root.Children.Add(CreateFieldLabel("Start in directory (optional)"));
                var wdGrid = new Grid();
                wdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                wdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _workDirBox = FluentTheme.CreateTextBox(item.WorkingDirectory ?? string.Empty, "Defaults to target folder");
                Grid.SetColumn(_workDirBox, 0);
                wdGrid.Children.Add(_workDirBox);

                var wdBrowseBtn = FluentTheme.CreateButton("Folder...", padding: new Thickness(10, 6, 10, 6));
                wdBrowseBtn.Margin = new Thickness(8, 0, 0, 0);
                wdBrowseBtn.Click += BrowseWorkDir_Click;
                Grid.SetColumn(wdBrowseBtn, 1);
                wdGrid.Children.Add(wdBrowseBtn);

                root.Children.Add(wdGrid);
            }

            // 5. Category / Section Tab
            root.Children.Add(CreateFieldLabel("Category / Tab inside Bin (optional)"));
            _sectionBox = FluentTheme.CreateTextBox(item.Section ?? string.Empty, "e.g. IDEs, Browsers, Docs (leave blank for default)");
            root.Children.Add(_sectionBox);

            var distinctSections = (existingSections ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            if (distinctSections.Count > 0)
            {
                var chipsWrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (string sec in distinctSections)
                {
                    string captured = sec;
                    var chip = FluentTheme.CreateButton(captured, isSubtle: false, padding: new Thickness(9, 3, 9, 3));
                    chip.FontSize = 11.5;
                    chip.Margin = new Thickness(0, 0, 6, 4);
                    chip.Click += (_, __) => _sectionBox.Text = captured;
                    chipsWrap.Children.Add(chip);
                }
                root.Children.Add(chipsWrap);
            }

            // 6. Options Card (Window State, Run as Admin, Workspace Open All + Delay)
            var optionsCard = new Border
            {
                Background = FluentTheme.CardBg,
                BorderBrush = FluentTheme.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 14, 0, 0)
            };
            var optionsStack = new StackPanel();

            if (!isWeb && !isPackaged && (!isSystem || isPowerShellSys))
            {
                // Window state segmented row
                var segTrack = new Border
                {
                    Background = FluentTheme.SegmentedTrackBg,
                    BorderBrush = FluentTheme.CardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(2)
                };
                var segRow = new StackPanel { Orientation = Orientation.Horizontal };
                _winNormalBtn = CreateSegOption("Normal", out _winNormalTxt, () => SetWindowStyleOption(BinWindowStyle.Normal));
                _winMaxBtn = CreateSegOption("Maximized", out _winMaxTxt, () => SetWindowStyleOption(BinWindowStyle.Maximized));
                _winMinBtn = CreateSegOption("Minimized", out _winMinTxt, () => SetWindowStyleOption(BinWindowStyle.Minimized));
                segRow.Children.Add(_winNormalBtn);
                segRow.Children.Add(_winMaxBtn);
                segRow.Children.Add(_winMinBtn);
                segTrack.Child = segRow;

                optionsStack.Children.Add(CreateOptionRow("Run window", segTrack, showBottomBorder: true));

                // Always Run as Admin row
                _adminToggle = FluentTheme.CreateToggleCheckBox(null, item.RunAsAdmin);
                optionsStack.Children.Add(CreateOptionRow("Always run as administrator", _adminToggle, showBottomBorder: true));
            }

            // Include in Open All Workspace row
            _openAllToggle = FluentTheme.CreateToggleCheckBox(null, item.IncludeInOpenAll);
            optionsStack.Children.Add(CreateOptionRow("Include in \"Open all\" workspace", _openAllToggle, showBottomBorder: true));

            // Launch delay stepper row
            var delayStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var minusBtn = FluentTheme.CreateIconButton("\uE738", "Decrease delay", size: 26, iconSize: 10);
            minusBtn.Click += (_, __) => StepDelay(-1);
            _delayValueText = new TextBlock
            {
                MinWidth = 58,
                TextAlignment = TextAlignment.Center,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            var plusBtn = FluentTheme.CreateIconButton("\uE710", "Increase delay", size: 26, iconSize: 10);
            plusBtn.Click += (_, __) => StepDelay(1);
            delayStack.Children.Add(minusBtn);
            delayStack.Children.Add(_delayValueText);
            delayStack.Children.Add(plusBtn);

            optionsStack.Children.Add(CreateOptionRow("Workspace launch delay", delayStack, showBottomBorder: false));
            optionsCard.Child = optionsStack;
            root.Children.Add(optionsCard);

            UpdateWindowStyleVisuals();
            UpdateDelayText();

            // Footer buttons
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var cancelBtn = FluentTheme.CreateButton("Cancel", padding: new Thickness(16, 6, 16, 6));
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (_, __) => { DialogResult = false; Close(); };

            var saveBtn = FluentTheme.CreateButton("Save", isPrimary: true, padding: new Thickness(20, 6, 20, 6));
            saveBtn.Click += Save_Click;

            footer.Children.Add(cancelBtn);
            footer.Children.Add(saveBtn);
            root.Children.Add(footer);

            rootBorder.Child = root;
            Content = rootBorder;
        }

        private static TextBlock CreateFieldLabel(string text, double topMargin = 12)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FluentTheme.TextSecondary,
                Margin = new Thickness(0, topMargin, 0, 5)
            };
        }

        private static UIElement CreateOptionRow(string title, UIElement control, bool showBottomBorder)
        {
            var container = new Border
            {
                BorderBrush = FluentTheme.DividerBrush,
                BorderThickness = showBottomBorder ? new Thickness(0, 0, 0, 1) : new Thickness(0),
                Padding = new Thickness(12, 9, 12, 9)
            };
            var dock = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(control, Dock.Right);
            dock.Children.Add(control);
            dock.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                Foreground = FluentTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            container.Child = dock;
            return container;
        }

        private static Border CreateSegOption(string label, out TextBlock tb, Action onClick)
        {
            tb = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                Foreground = FluentTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            var b = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 4, 9, 4),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = tb
            };
            b.MouseLeftButtonDown += (s, e) =>
            {
                onClick?.Invoke();
                e.Handled = true;
            };
            return b;
        }

        private void SetWindowStyleOption(BinWindowStyle style)
        {
            _selectedWindowStyle = style;
            UpdateWindowStyleVisuals();
        }

        private void UpdateWindowStyleVisuals()
        {
            if (_winNormalBtn == null)
                return;

            ApplySegState(_winNormalBtn, _winNormalTxt, _selectedWindowStyle == BinWindowStyle.Normal);
            ApplySegState(_winMaxBtn, _winMaxTxt, _selectedWindowStyle == BinWindowStyle.Maximized);
            ApplySegState(_winMinBtn, _winMinTxt, _selectedWindowStyle == BinWindowStyle.Minimized);
        }

        private static void ApplySegState(Border b, TextBlock tb, bool active)
        {
            b.Background = active ? FluentTheme.CardSelectedBg : Brushes.Transparent;
            tb.Foreground = active ? FluentTheme.TextPrimary : FluentTheme.TextSecondary;
            tb.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private void StepDelay(int delta)
        {
            int idx = 0;
            for (int i = 0; i < DelayStepsMs.Length; i++)
            {
                if (DelayStepsMs[i] <= _selectedDelayMs)
                    idx = i;
            }
            int next = Math.Max(0, Math.Min(DelayStepsMs.Length - 1, idx + delta));
            _selectedDelayMs = DelayStepsMs[next];
            UpdateDelayText();
        }

        private void UpdateDelayText()
        {
            if (_delayValueText == null)
                return;
            _delayValueText.Text = _selectedDelayMs <= 0
                ? "None"
                : (_selectedDelayMs >= 1000 ? $"{_selectedDelayMs / 1000.0:0.#} s" : $"{_selectedDelayMs} ms");
        }

        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Target File or Application",
                Filter = "All Files (*.*)|*.*|Applications (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd",
                DereferenceLinks = false
            };
            if (dlg.ShowDialog(this) == true)
            {
                _targetBox.Text = dlg.FileName;
            }
        }

        private void BrowseWorkDir_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select starting directory",
                ShowNewFolderButton = false
            })
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    _workDirBox.Text = dlg.SelectedPath;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string trimmedName = (_nameBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                _nameBox.Focus();
                return;
            }

            _item.Name = trimmedName;
            if (!_targetBox.IsReadOnly && !string.IsNullOrWhiteSpace(_targetBox.Text))
            {
                _item.TargetPath = _targetBox.Text.Trim();
                _item.Kind = BinItem.DetectKind(_item.TargetPath);
            }

            if (_argsBox != null)
            {
                _item.Arguments = (_argsBox.Text ?? string.Empty).Trim();
            }

            if (_workDirBox != null)
            {
                _item.WorkingDirectory = (_workDirBox.Text ?? string.Empty).Trim();
            }

            _item.Section = (_sectionBox.Text ?? string.Empty).Trim();
            _item.WindowStyle = _selectedWindowStyle;
            if (_adminToggle != null)
            {
                _item.RunAsAdmin = _adminToggle.IsChecked == true;
            }
            _item.IncludeInOpenAll = _openAllToggle.IsChecked == true;
            _item.OpenAllDelayMs = _selectedDelayMs;

            DialogResult = true;
            Close();
        }
    }
}
