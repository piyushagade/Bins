using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinBins.Views
{
    public static class FluentTheme
    {
        public static readonly FontFamily BodyFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily DisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        // Authentic Windows 11 (WinUI 3 Dark) Neutral Palette
        public static readonly SolidColorBrush ShellBg = BrushFromHex("#202020");
        public static readonly SolidColorBrush WindowBg = BrushFromHex("#202020");
        public static readonly SolidColorBrush SidebarBg = BrushFromHex("#202020");
        public static readonly SolidColorBrush LayerBg = BrushFromHex("#272727");
        public static readonly SolidColorBrush PopupBg = BrushFromHex("#2C2C2C");
        public static readonly SolidColorBrush FlyoutBg = BrushFromHex("#2C2C2C");

        public static readonly SolidColorBrush CardBg = BrushFromHex("#0DFFFFFF");
        public static readonly SolidColorBrush CardHoverBg = BrushFromHex("#15FFFFFF");
        public static readonly SolidColorBrush CardSelectedBg = BrushFromHex("#18FFFFFF");
        public static readonly SolidColorBrush SubtleHoverBg = BrushFromHex("#12FFFFFF");
        public static readonly SolidColorBrush SubtlePressedBg = BrushFromHex("#0AFFFFFF");
        public static readonly SolidColorBrush InputBg = BrushFromHex("#12FFFFFF");
        public static readonly SolidColorBrush SegmentedTrackBg = BrushFromHex("#191919");

        public static readonly SolidColorBrush SurfaceStroke = BrushFromHex("#14FFFFFF");
        public static readonly SolidColorBrush CardBorder = BrushFromHex("#12FFFFFF");
        public static readonly SolidColorBrush SubtleBorder = BrushFromHex("#1EFFFFFF");
        public static readonly SolidColorBrush DividerBrush = BrushFromHex("#14FFFFFF");

        // Windows 11 Dark Theme Accent (#60CDFF fill with dark ink, #0078D4 alternative)
        public static readonly SolidColorBrush AccentBrush = BrushFromHex("#60CDFF");
        public static readonly SolidColorBrush AccentHoverBrush = BrushFromHex("#78D6FF");
        public static readonly SolidColorBrush AccentPressedBrush = BrushFromHex("#4CC2FF");
        public static readonly SolidColorBrush AccentInkBrush = BrushFromHex("#001420");
        public static readonly SolidColorBrush DangerBrush = BrushFromHex("#FF99A4");
        public static readonly SolidColorBrush SuccessBrush = BrushFromHex("#6CCB5F");

        public static readonly SolidColorBrush TextPrimary = BrushFromHex("#FFFFFF");
        public static readonly SolidColorBrush TextSecondary = BrushFromHex("#C5C5C5");
        public static readonly SolidColorBrush TextMuted = BrushFromHex("#8A8A8A");

        public static SolidColorBrush BrushFromHex(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Installs sleek Windows 11 ScrollBar, ContextMenu, MenuItem, Separator, and ToolTip styles application-wide.
        /// </summary>
        public static void ApplyGlobalResources(ResourceDictionary resources)
        {
            if (resources == null)
                return;

            resources[typeof(ScrollBar)] = CreateWin11ScrollBarStyle();
            resources[typeof(ToolTip)] = CreateWin11ToolTipStyle();
            resources[typeof(ContextMenu)] = CreateWin11ContextMenuStyle();
            resources[typeof(MenuItem)] = CreateWin11MenuItemStyle();
        }

        private static Style CreateWin11ScrollBarStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ScrollBar}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ScrollBar}'>
                <Grid Background='Transparent' Width='8'>
                    <Track x:Name='PART_Track' IsDirectionReversed='True'>
                        <Track.DecreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb>
                                <Thumb.Template>
                                    <ControlTemplate TargetType='{x:Type Thumb}'>
                                        <Border x:Name='Pill' Background='#38FFFFFF' CornerRadius='3' Margin='1.5,2,1.5,2'/>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property='IsMouseOver' Value='True'>
                                                <Setter TargetName='Pill' Property='Background' Value='#66FFFFFF'/>
                                                <Setter TargetName='Pill' Property='Margin' Value='1,2,1,2'/>
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
                        </Track.IncreaseRepeatButton>
                    </Track>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static Style CreateWin11ToolTipStyle()
        {
            var style = new Style(typeof(ToolTip));
            var template = new ControlTemplate(typeof(ToolTip));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, FlyoutBg);
            border.SetValue(Border.BorderBrushProperty, SubtleBorder);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(presenter);
            template.VisualTree = border;

            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, BodyFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style CreateWin11ContextMenuStyle()
        {
            var style = new Style(typeof(ContextMenu));
            var template = new ControlTemplate(typeof(ContextMenu));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, FlyoutBg);
            border.SetValue(Border.BorderBrushProperty, SubtleBorder);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(4));

            var presenter = new FrameworkElementFactory(typeof(StackPanel));
            presenter.SetValue(Panel.IsItemsHostProperty, true);
            presenter.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
            border.AppendChild(presenter);

            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(ContextMenu.HasDropShadowProperty, true));
            return style;
        }

        private static Style CreateWin11MenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            var template = new ControlTemplate(typeof(MenuItem));

            var border = new FrameworkElementFactory(typeof(Border), "MenuRow");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 7, 14, 7));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(1));

            var grid = new FrameworkElementFactory(typeof(Grid));
            var colIcon = new FrameworkElementFactory(typeof(ColumnDefinition));
            colIcon.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var colText = new FrameworkElementFactory(typeof(ColumnDefinition));
            colText.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            grid.AppendChild(colIcon);
            grid.AppendChild(colText);

            var iconPresenter = new FrameworkElementFactory(typeof(ContentPresenter), "IconHost");
            iconPresenter.SetValue(ContentPresenter.ContentSourceProperty, "Icon");
            iconPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            iconPresenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            iconPresenter.SetValue(Grid.ColumnProperty, 0);
            grid.AppendChild(iconPresenter);

            var headerPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            headerPresenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            headerPresenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            headerPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            headerPresenter.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(headerPresenter);

            border.AppendChild(grid);
            template.VisualTree = border;

            var noIconTrigger = new Trigger { Property = MenuItem.IconProperty, Value = null };
            noIconTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "IconHost"));
            template.Triggers.Add(noIconTrigger);

            var hoverTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, SubtleHoverBg, "MenuRow"));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(Control.FontFamilyProperty, BodyFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        public static UIElement CreateFluentIcon(string glyph, double size = 14, Brush foreground = null)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = IconFont,
                FontSize = size,
                Foreground = foreground ?? TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        /// <summary>
        /// Creates a clean Windows 11 WinUI 3 Button (Accent, Standard, or Subtle/Ghost), optionally with a Fluent icon glyph.
        /// </summary>
        public static Button CreateButton(
            string text,
            bool isPrimary = false,
            bool isSubtle = false,
            bool isDanger = false,
            Thickness? padding = null,
            string iconGlyph = null,
            bool isDangerSolid = false)
        {
            Brush fg = isDangerSolid
                ? TextPrimary
                : (isPrimary
                    ? AccentInkBrush
                    : (isDanger ? DangerBrush : TextPrimary));

            object contentObj;
            if (!string.IsNullOrEmpty(iconGlyph) && !string.IsNullOrEmpty(text))
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock
                {
                    Text = iconGlyph,
                    FontFamily = IconFont,
                    FontSize = 13,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = text,
                    FontFamily = BodyFont,
                    FontSize = 13,
                    FontWeight = (isPrimary || isDangerSolid) ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center
                });
                contentObj = sp;
            }
            else if (!string.IsNullOrEmpty(iconGlyph))
            {
                contentObj = CreateFluentIcon(iconGlyph, 13.5, fg);
            }
            else
            {
                contentObj = text;
            }

            var btn = new Button
            {
                Content = contentObj,
                FontFamily = BodyFont,
                FontSize = 13,
                FontWeight = (isPrimary || isDangerSolid) ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = fg,
                Cursor = Cursors.Hand,
                Padding = padding ?? new Thickness(12, 6, 12, 6)
            };

            Brush normalBg = isDangerSolid
                ? BrushFromHex("#C42B1C")
                : (isPrimary
                    ? AccentBrush
                    : (isSubtle ? Brushes.Transparent : CardBg));
            Brush hoverBg = isDangerSolid
                ? BrushFromHex("#D6392A")
                : (isPrimary
                    ? AccentHoverBrush
                    : (isSubtle ? SubtleHoverBg : CardHoverBg));
            Brush pressedBg = isDangerSolid
                ? BrushFromHex("#A92315")
                : (isPrimary
                    ? AccentPressedBrush
                    : SubtlePressedBg);
            Brush borderBrush = (isPrimary || isDangerSolid)
                ? BrushFromHex("#28FFFFFF")
                : (isSubtle ? Brushes.Transparent : SurfaceStroke);

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border), "rootBorder");
            borderFactory.SetValue(Border.BackgroundProperty, normalBg);
            borderFactory.SetValue(Border.BorderBrushProperty, borderBrush);
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenter);

            template.VisualTree = borderFactory;

            var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "rootBorder"));
            template.Triggers.Add(mouseOverTrigger);

            var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, pressedBg, "rootBorder"));
            template.Triggers.Add(pressedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35, "rootBorder"));
            template.Triggers.Add(disabledTrigger);

            btn.Template = template;
            return btn;
        }

        public static Button CreateIconButton(string glyph, string tooltip = null, bool isDanger = false, double size = 30, double iconSize = 13)
        {
            var btn = CreateButton(
                text: null,
                isSubtle: true,
                isDanger: isDanger,
                padding: new Thickness(0),
                iconGlyph: glyph);
            btn.Width = size;
            btn.Height = size;
            if (btn.Content is TextBlock tb)
            {
                tb.FontSize = iconSize;
            }
            if (!string.IsNullOrEmpty(tooltip))
            {
                btn.ToolTip = tooltip;
            }
            return btn;
        }

        /// <summary>
        /// Creates a WinUI 3 TextBox with subtle fill and bottom accent underline on focus.
        /// </summary>
        public static TextBox CreateTextBox(string initialText = "", string placeholder = "")
        {
            var tb = new TextBox
            {
                Text = initialText ?? string.Empty,
                FontFamily = BodyFont,
                FontSize = 13.5,
                Foreground = TextPrimary,
                CaretBrush = AccentBrush,
                Background = InputBg,
                BorderBrush = SurfaceStroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var template = new ControlTemplate(typeof(TextBox));
            var outerGrid = new FrameworkElementFactory(typeof(Grid));

            var border = new FrameworkElementFactory(typeof(Border), "tbBorder");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));

            var innerGrid = new FrameworkElementFactory(typeof(Grid));
            var scrollHost = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            scrollHost.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));
            innerGrid.AppendChild(scrollHost);

            if (!string.IsNullOrEmpty(placeholder))
            {
                var watermark = new FrameworkElementFactory(typeof(TextBlock), "WatermarkText");
                watermark.SetValue(TextBlock.TextProperty, placeholder);
                watermark.SetValue(TextBlock.ForegroundProperty, TextMuted);
                watermark.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 10, 0));
                watermark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                watermark.SetValue(UIElement.IsHitTestVisibleProperty, false);
                watermark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
                innerGrid.AppendChild(watermark);

                var emptyTrigger = new Trigger { Property = TextBox.TextProperty, Value = string.Empty };
                emptyTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "WatermarkText"));
                template.Triggers.Add(emptyTrigger);
            }

            border.AppendChild(innerGrid);
            outerGrid.AppendChild(border);

            // WinUI 3 bottom accent focus bar
            var focusLine = new FrameworkElementFactory(typeof(Border), "FocusLine");
            focusLine.SetValue(FrameworkElement.HeightProperty, 2.0);
            focusLine.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            focusLine.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 0, 5, 5));
            focusLine.SetValue(Border.BackgroundProperty, BrushFromHex("#3AFFFFFF"));
            outerGrid.AppendChild(focusLine);

            template.VisualTree = outerGrid;

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Border.BackgroundProperty, BrushFromHex("#191919"), "tbBorder"));
            focusTrigger.Setters.Add(new Setter(Border.BackgroundProperty, AccentBrush, "FocusLine"));
            template.Triggers.Add(focusTrigger);

            tb.Template = template;
            return tb;
        }

        /// <summary>
        /// Creates an inline borderless title TextBox that looks like clean static Display typography until hovered or clicked.
        /// </summary>
        public static TextBox CreateInlineTitleTextBox(string initialText = "")
        {
            var tb = new TextBox
            {
                Text = initialText ?? string.Empty,
                FontFamily = DisplayFont,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                CaretBrush = AccentBrush,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 8, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Click to rename Bin"
            };

            var template = new ControlTemplate(typeof(TextBox));
            var outerGrid = new FrameworkElementFactory(typeof(Grid));

            var border = new FrameworkElementFactory(typeof(Border), "TitleBorder");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));

            var scrollHost = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            border.AppendChild(scrollHost);
            outerGrid.AppendChild(border);

            var focusLine = new FrameworkElementFactory(typeof(Border), "TitleFocusLine");
            focusLine.SetValue(FrameworkElement.HeightProperty, 2.0);
            focusLine.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            focusLine.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
            focusLine.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            outerGrid.AppendChild(focusLine);

            template.VisualTree = outerGrid;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, SubtleHoverBg, "TitleBorder"));
            template.Triggers.Add(hoverTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Border.BackgroundProperty, BrushFromHex("#191919"), "TitleBorder"));
            focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, SurfaceStroke, "TitleBorder"));
            focusTrigger.Setters.Add(new Setter(Border.BackgroundProperty, AccentBrush, "TitleFocusLine"));
            template.Triggers.Add(focusTrigger);

            tb.Template = template;
            return tb;
        }

        /// <summary>
        /// Creates a WinUI 3 ToggleSwitch (with optional label or standalone switch for right-aligned SettingsCards).
        /// </summary>
        public static CheckBox CreateToggleCheckBox(string label, bool isChecked)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = isChecked,
                FontFamily = BodyFont,
                FontSize = 13,
                Foreground = TextPrimary,
                Cursor = Cursors.Hand,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var template = new ControlTemplate(typeof(CheckBox));
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            stack.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var track = new FrameworkElementFactory(typeof(Border), "SwitchTrack");
            track.SetValue(FrameworkElement.WidthProperty, 40.0);
            track.SetValue(FrameworkElement.HeightProperty, 20.0);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            track.SetValue(Border.BackgroundProperty, BrushFromHex("#14FFFFFF"));
            track.SetValue(Border.BorderBrushProperty, BrushFromHex("#8AFFFFFF"));
            track.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var thumb = new FrameworkElementFactory(typeof(Border), "SwitchThumb");
            thumb.SetValue(FrameworkElement.WidthProperty, 12.0);
            thumb.SetValue(FrameworkElement.HeightProperty, 12.0);
            thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            thumb.SetValue(Border.BackgroundProperty, TextSecondary);
            thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            thumb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            thumb.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 3, 0));
            track.AppendChild(thumb);

            stack.AppendChild(track);

            if (!string.IsNullOrEmpty(label))
            {
                var content = new FrameworkElementFactory(typeof(ContentPresenter));
                content.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
                content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                stack.AppendChild(content);
            }

            template.VisualTree = stack;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, AccentBrush, "SwitchTrack"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "SwitchTrack"));
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, AccentInkBrush, "SwitchThumb"));
            checkedTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "SwitchThumb"));
            template.Triggers.Add(checkedTrigger);

            cb.Template = template;
            return cb;
        }

        /// <summary>
        /// Creates a standard Windows 11 WinUI 3 rounded 18x18 CheckBox with checkmark glyph.
        /// </summary>
        public static CheckBox CreateTickCheckBox(string label, bool isChecked)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = isChecked,
                FontFamily = BodyFont,
                FontSize = 12.5,
                Foreground = TextSecondary,
                Cursor = Cursors.Hand,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var template = new ControlTemplate(typeof(CheckBox));
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            stack.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var box = new FrameworkElementFactory(typeof(Border), "CheckBoxBorder");
            box.SetValue(FrameworkElement.WidthProperty, 18.0);
            box.SetValue(FrameworkElement.HeightProperty, 18.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            box.SetValue(Border.BackgroundProperty, BrushFromHex("#14FFFFFF"));
            box.SetValue(Border.BorderBrushProperty, BrushFromHex("#77FFFFFF"));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var checkGlyph = new FrameworkElementFactory(typeof(TextBlock), "CheckGlyph");
            checkGlyph.SetValue(TextBlock.TextProperty, "\uE73E");
            checkGlyph.SetValue(TextBlock.FontFamilyProperty, IconFont);
            checkGlyph.SetValue(TextBlock.FontSizeProperty, 11.0);
            checkGlyph.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            checkGlyph.SetValue(TextBlock.ForegroundProperty, AccentInkBrush);
            checkGlyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkGlyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkGlyph.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(checkGlyph);

            stack.AppendChild(box);

            if (!string.IsNullOrEmpty(label))
            {
                var content = new FrameworkElementFactory(typeof(ContentPresenter));
                content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
                content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                stack.AppendChild(content);
            }

            template.VisualTree = stack;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, BrushFromHex("#22FFFFFF"), "CheckBoxBorder"));
            template.Triggers.Add(hoverTrigger);

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, AccentBrush, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));
            template.Triggers.Add(checkedTrigger);

            cb.Template = template;
            return cb;
        }

        public static ContextMenu CreateDarkContextMenu()
        {
            return new ContextMenu();
        }

        public static MenuItem CreateMenuItem(string header, Action onClick, bool isDanger = false, string iconGlyph = null)
        {
            var fg = isDanger ? DangerBrush : TextPrimary;
            var mi = new MenuItem
            {
                Header = header,
                Foreground = fg
            };

            if (!string.IsNullOrEmpty(iconGlyph))
            {
                mi.Icon = CreateFluentIcon(iconGlyph, 13.5, fg);
            }

            mi.Click += (_, __) => onClick?.Invoke();
            return mi;
        }

        public static Separator CreateMenuSeparator()
        {
            var sep = new Separator
            {
                Margin = new Thickness(6, 4, 6, 4)
            };
            var template = new ControlTemplate(typeof(Separator));
            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(FrameworkElement.HeightProperty, 1.0);
            line.SetValue(Border.BackgroundProperty, DividerBrush);
            template.VisualTree = line;
            sep.Template = template;
            return sep;
        }
    }
}
