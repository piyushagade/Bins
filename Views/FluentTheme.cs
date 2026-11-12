using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using WinBins.Models;

namespace WinBins.Views
{
    public static class FluentTheme
    {
        private sealed class ThemeBrushSlot : INotifyPropertyChanged
        {
            private Brush _brush;

            public Brush Brush
            {
                get => _brush;
                set
                {
                    if (!ReferenceEquals(_brush, value))
                    {
                        _brush = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
                    }
                }
            }

            public ThemeBrushSlot(Brush initial)
            {
                _brush = initial;
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private static readonly Dictionary<SolidColorBrush, ThemeBrushSlot> _slots =
            new Dictionary<SolidColorBrush, ThemeBrushSlot>();

        public static readonly FontFamily BodyFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily DisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        // Mutable SolidColorBrushes for direct FrameworkElement properties + paired ThemeBrushSlots for ControlTemplates/Styles
        public static readonly SolidColorBrush ShellBg = RegisterThemeBrush("#202020");
        public static readonly SolidColorBrush WindowBg = RegisterThemeBrush("#202020");
        public static readonly SolidColorBrush SidebarBg = RegisterThemeBrush("#202020");
        public static readonly SolidColorBrush LayerBg = RegisterThemeBrush("#272727");
        public static readonly SolidColorBrush PopupBg = RegisterThemeBrush("#2C2C2C");
        public static readonly SolidColorBrush FlyoutBg = RegisterThemeBrush("#2C2C2C");

        public static readonly SolidColorBrush CardBg = RegisterThemeBrush("#0DFFFFFF");
        public static readonly SolidColorBrush CardHoverBg = RegisterThemeBrush("#15FFFFFF");
        public static readonly SolidColorBrush CardSelectedBg = RegisterThemeBrush("#18FFFFFF");
        public static readonly SolidColorBrush SubtleHoverBg = RegisterThemeBrush("#12FFFFFF");
        public static readonly SolidColorBrush SubtlePressedBg = RegisterThemeBrush("#0AFFFFFF");
        public static readonly SolidColorBrush InputBg = RegisterThemeBrush("#12FFFFFF");
        public static readonly SolidColorBrush SegmentedTrackBg = RegisterThemeBrush("#191919");

        public static readonly SolidColorBrush SurfaceStroke = RegisterThemeBrush("#14FFFFFF");
        public static readonly SolidColorBrush CardBorder = RegisterThemeBrush("#12FFFFFF");
        public static readonly SolidColorBrush SubtleBorder = RegisterThemeBrush("#1EFFFFFF");
        public static readonly SolidColorBrush DividerBrush = RegisterThemeBrush("#14FFFFFF");
        public static readonly SolidColorBrush SwitchTrackBorder = RegisterThemeBrush("#8AFFFFFF");

        public static readonly SolidColorBrush AccentBrush = RegisterThemeBrush("#60CDFF");
        public static readonly SolidColorBrush AccentHoverBrush = RegisterThemeBrush("#78D6FF");
        public static readonly SolidColorBrush AccentPressedBrush = RegisterThemeBrush("#4CC2FF");
        public static readonly SolidColorBrush AccentInkBrush = RegisterThemeBrush("#001420");
        public static readonly SolidColorBrush DangerBrush = RegisterThemeBrush("#FF99A4");
        public static readonly SolidColorBrush SuccessBrush = RegisterThemeBrush("#6CCB5F");

        public static readonly SolidColorBrush TextPrimary = RegisterThemeBrush("#FFFFFF");
        public static readonly SolidColorBrush TextSecondary = RegisterThemeBrush("#C5C5C5");
        public static readonly SolidColorBrush TextMuted = RegisterThemeBrush("#8A8A8A");

        public static bool IsLightThemeActive { get; private set; }
        public static int ActiveCaptionColorRef => IsLightThemeActive ? 0x00F3F3F3 : 0x00202020;

        public static event Action ThemeChanged;

        private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static SolidColorBrush RegisterThemeBrush(string hex)
        {
            var publicBrush = new SolidColorBrush(ParseColor(hex));
            _slots[publicBrush] = new ThemeBrushSlot(BrushFromHex(hex));
            return publicBrush;
        }

        private static void SetThemeBrush(SolidColorBrush publicBrush, string hex)
        {
            publicBrush.Color = ParseColor(hex);
            if (_slots.TryGetValue(publicBrush, out ThemeBrushSlot slot))
            {
                slot.Brush = BrushFromHex(hex);
            }
        }

        private static Binding BindBrush(SolidColorBrush scb)
        {
            return new Binding(nameof(ThemeBrushSlot.Brush))
            {
                Source = _slots[scb],
                Mode = BindingMode.OneWay
            };
        }

        private static void SetFactoryBrush(FrameworkElementFactory factory, DependencyProperty prop, Brush brush)
        {
            if (brush is SolidColorBrush scb && _slots.ContainsKey(scb))
            {
                factory.SetBinding(prop, BindBrush(scb));
            }
            else
            {
                factory.SetValue(prop, brush);
            }
        }

        private static Setter CreateBrushSetter(DependencyProperty prop, Brush brush, string targetName = null)
        {
            object val = (brush is SolidColorBrush scb && _slots.ContainsKey(scb))
                ? (object)BindBrush(scb)
                : brush;
            return targetName == null
                ? new Setter(prop, val)
                : new Setter(prop, val, targetName);
        }

        public static SolidColorBrush BrushFromHex(string hex)
        {
            var brush = new SolidColorBrush(ParseColor(hex));
            brush.Freeze();
            return brush;
        }

        public static bool IsWindowsAppLightMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false))
                {
                    if (key != null)
                    {
                        int val = Convert.ToInt32(key.GetValue("AppsUseLightTheme", 0));
                        return val != 0;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static void ApplyTheme(BinThemeMode mode, bool useAcrylicFlyout = true)
        {
            bool useLight = mode == BinThemeMode.Light ||
                            (mode == BinThemeMode.System && IsWindowsAppLightMode());

            IsLightThemeActive = useLight;

            if (useLight)
            {
                SetThemeBrush(ShellBg, "#F3F3F3");
                SetThemeBrush(WindowBg, "#F3F3F3");
                SetThemeBrush(SidebarBg, "#F3F3F3");
                SetThemeBrush(LayerBg, "#FBFBFB");
                SetThemeBrush(PopupBg, useAcrylicFlyout ? "#CCF9F9F9" : "#F9F9F9");
                SetThemeBrush(FlyoutBg, "#F9F9F9");

                SetThemeBrush(CardBg, "#B8FFFFFF");
                SetThemeBrush(CardHoverBg, "#09000000");
                SetThemeBrush(CardSelectedBg, "#10000000");
                SetThemeBrush(SubtleHoverBg, "#0B000000");
                SetThemeBrush(SubtlePressedBg, "#14000000");
                SetThemeBrush(InputBg, "#FFFFFF");
                SetThemeBrush(SegmentedTrackBg, "#EBEBEB");

                SetThemeBrush(SurfaceStroke, "#16000000");
                SetThemeBrush(CardBorder, "#12000000");
                SetThemeBrush(SubtleBorder, "#20000000");
                SetThemeBrush(DividerBrush, "#14000000");
                SetThemeBrush(SwitchTrackBorder, "#72000000");

                SetThemeBrush(AccentBrush, "#005FB8");
                SetThemeBrush(AccentHoverBrush, "#196EC0");
                SetThemeBrush(AccentPressedBrush, "#00529E");
                SetThemeBrush(AccentInkBrush, "#FFFFFF");
                SetThemeBrush(DangerBrush, "#C42B1C");
                SetThemeBrush(SuccessBrush, "#0F7B0F");

                SetThemeBrush(TextPrimary, "#1A1A1A");
                SetThemeBrush(TextSecondary, "#484848");
                SetThemeBrush(TextMuted, "#6E6E6E");
            }
            else
            {
                SetThemeBrush(ShellBg, "#202020");
                SetThemeBrush(WindowBg, "#202020");
                SetThemeBrush(SidebarBg, "#202020");
                SetThemeBrush(LayerBg, "#272727");
                SetThemeBrush(PopupBg, useAcrylicFlyout ? "#BF282828" : "#2C2C2C");
                SetThemeBrush(FlyoutBg, "#2C2C2C");

                SetThemeBrush(CardBg, "#0DFFFFFF");
                SetThemeBrush(CardHoverBg, "#15FFFFFF");
                SetThemeBrush(CardSelectedBg, "#18FFFFFF");
                SetThemeBrush(SubtleHoverBg, "#12FFFFFF");
                SetThemeBrush(SubtlePressedBg, "#0AFFFFFF");
                SetThemeBrush(InputBg, "#12FFFFFF");
                SetThemeBrush(SegmentedTrackBg, "#191919");

                SetThemeBrush(SurfaceStroke, "#14FFFFFF");
                SetThemeBrush(CardBorder, "#12FFFFFF");
                SetThemeBrush(SubtleBorder, "#1EFFFFFF");
                SetThemeBrush(DividerBrush, "#14FFFFFF");
                SetThemeBrush(SwitchTrackBorder, "#8AFFFFFF");

                SetThemeBrush(AccentBrush, "#60CDFF");
                SetThemeBrush(AccentHoverBrush, "#78D6FF");
                SetThemeBrush(AccentPressedBrush, "#4CC2FF");
                SetThemeBrush(AccentInkBrush, "#001420");
                SetThemeBrush(DangerBrush, "#FF99A4");
                SetThemeBrush(SuccessBrush, "#6CCB5F");

                SetThemeBrush(TextPrimary, "#FFFFFF");
                SetThemeBrush(TextSecondary, "#C5C5C5");
                SetThemeBrush(TextMuted, "#8A8A8A");
            }

            ThemeChanged?.Invoke();
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
                                        <Border x:Name='Pill' Background='#48888888' CornerRadius='3' Margin='1.5,2,1.5,2'/>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property='IsMouseOver' Value='True'>
                                                <Setter TargetName='Pill' Property='Background' Value='#80888888'/>
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
            SetFactoryBrush(border, Border.BackgroundProperty, FlyoutBg);
            SetFactoryBrush(border, Border.BorderBrushProperty, SubtleBorder);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(presenter);
            template.VisualTree = border;

            style.Setters.Add(CreateBrushSetter(Control.ForegroundProperty, TextPrimary));
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
            SetFactoryBrush(border, Border.BackgroundProperty, FlyoutBg);
            SetFactoryBrush(border, Border.BorderBrushProperty, SubtleBorder);
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

        public static Style CreateWin11MenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            var template = new ControlTemplate(typeof(MenuItem));

            var outerGrid = new FrameworkElementFactory(typeof(Grid));

            var border = new FrameworkElementFactory(typeof(Border), "MenuRow");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 7, 12, 7));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(1));

            var grid = new FrameworkElementFactory(typeof(Grid));
            var colIcon = new FrameworkElementFactory(typeof(ColumnDefinition));
            colIcon.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var colText = new FrameworkElementFactory(typeof(ColumnDefinition));
            colText.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var colArrow = new FrameworkElementFactory(typeof(ColumnDefinition));
            colArrow.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(colIcon);
            grid.AppendChild(colText);
            grid.AppendChild(colArrow);

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

            var subArrow = new FrameworkElementFactory(typeof(TextBlock), "SubmenuChevron");
            subArrow.SetValue(TextBlock.TextProperty, "\uE76C");
            subArrow.SetValue(TextBlock.FontFamilyProperty, IconFont);
            subArrow.SetValue(TextBlock.FontSizeProperty, 10.0);
            SetFactoryBrush(subArrow, TextBlock.ForegroundProperty, TextMuted);
            subArrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            subArrow.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 0, 0));
            subArrow.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            subArrow.SetValue(Grid.ColumnProperty, 2);
            grid.AppendChild(subArrow);

            border.AppendChild(grid);
            outerGrid.AppendChild(border);

            // Cascading submenu popup (used by Folder Peek and nested submenus)
            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
            popup.SetValue(Popup.HorizontalOffsetProperty, 2.0);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsSubmenuOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

            var popBorder = new FrameworkElementFactory(typeof(Border));
            SetFactoryBrush(popBorder, Border.BackgroundProperty, FlyoutBg);
            SetFactoryBrush(popBorder, Border.BorderBrushProperty, SubtleBorder);
            popBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            popBorder.SetValue(Border.PaddingProperty, new Thickness(4));

            var popScroll = new FrameworkElementFactory(typeof(ScrollViewer));
            popScroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            popScroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            popScroll.SetValue(FrameworkElement.MaxHeightProperty, 380.0);

            var subItemsHost = new FrameworkElementFactory(typeof(StackPanel));
            subItemsHost.SetValue(Panel.IsItemsHostProperty, true);
            subItemsHost.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
            popScroll.AppendChild(subItemsHost);
            popBorder.AppendChild(popScroll);
            popup.AppendChild(popBorder);
            outerGrid.AppendChild(popup);

            template.VisualTree = outerGrid;

            var noIconTrigger = new Trigger { Property = MenuItem.IconProperty, Value = null };
            noIconTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "IconHost"));
            template.Triggers.Add(noIconTrigger);

            var hasItemsTrigger = new Trigger { Property = ItemsControl.HasItemsProperty, Value = true };
            hasItemsTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SubmenuChevron"));
            template.Triggers.Add(hasItemsTrigger);

            var hoverTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, SubtleHoverBg, "MenuRow"));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(Control.FontFamilyProperty, BodyFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(CreateBrushSetter(Control.ForegroundProperty, TextPrimary));
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
            SetFactoryBrush(borderFactory, Border.BackgroundProperty, normalBg);
            SetFactoryBrush(borderFactory, Border.BorderBrushProperty, borderBrush);
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenter);

            template.VisualTree = borderFactory;

            var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, hoverBg, "rootBorder"));
            template.Triggers.Add(mouseOverTrigger);

            var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, pressedBg, "rootBorder"));
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
                SetFactoryBrush(watermark, TextBlock.ForegroundProperty, TextMuted);
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
            SetFactoryBrush(focusLine, Border.BackgroundProperty, SubtleBorder);
            outerGrid.AppendChild(focusLine);

            template.VisualTree = outerGrid;

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focusTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, InputBg, "tbBorder"));
            focusTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, AccentBrush, "FocusLine"));
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
            hoverTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, SubtleHoverBg, "TitleBorder"));
            template.Triggers.Add(hoverTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focusTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, InputBg, "TitleBorder"));
            focusTrigger.Setters.Add(CreateBrushSetter(Border.BorderBrushProperty, SurfaceStroke, "TitleBorder"));
            focusTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, AccentBrush, "TitleFocusLine"));
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
            SetFactoryBrush(track, Border.BackgroundProperty, SubtleHoverBg);
            SetFactoryBrush(track, Border.BorderBrushProperty, SwitchTrackBorder);
            track.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var thumb = new FrameworkElementFactory(typeof(Border), "SwitchThumb");
            thumb.SetValue(FrameworkElement.WidthProperty, 12.0);
            thumb.SetValue(FrameworkElement.HeightProperty, 12.0);
            thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            SetFactoryBrush(thumb, Border.BackgroundProperty, TextSecondary);
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
            checkedTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, AccentBrush, "SwitchTrack"));
            checkedTrigger.Setters.Add(CreateBrushSetter(Border.BorderBrushProperty, AccentBrush, "SwitchTrack"));
            checkedTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, AccentInkBrush, "SwitchThumb"));
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
            SetFactoryBrush(box, Border.BackgroundProperty, SubtleHoverBg);
            SetFactoryBrush(box, Border.BorderBrushProperty, SwitchTrackBorder);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var checkGlyph = new FrameworkElementFactory(typeof(TextBlock), "CheckGlyph");
            checkGlyph.SetValue(TextBlock.TextProperty, "\uE73E");
            checkGlyph.SetValue(TextBlock.FontFamilyProperty, IconFont);
            checkGlyph.SetValue(TextBlock.FontSizeProperty, 11.0);
            checkGlyph.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            SetFactoryBrush(checkGlyph, TextBlock.ForegroundProperty, AccentInkBrush);
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
            hoverTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, CardHoverBg, "CheckBoxBorder"));
            template.Triggers.Add(hoverTrigger);

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(CreateBrushSetter(Border.BackgroundProperty, AccentBrush, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(CreateBrushSetter(Border.BorderBrushProperty, AccentBrush, "CheckBoxBorder"));
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

        public static readonly Tuple<string, string>[] PresetBinAccents =
        {
            Tuple.Create("Default", string.Empty),
            Tuple.Create("Blue", "#3B82F6"),
            Tuple.Create("Cyan", "#06B6D4"),
            Tuple.Create("Emerald", "#10B981"),
            Tuple.Create("Amber", "#F59E0B"),
            Tuple.Create("Rose", "#F43F5E"),
            Tuple.Create("Purple", "#8B5CF6"),
            Tuple.Create("Pink", "#EC4899")
        };

        public static Brush GetEffectiveAccentBrush(string accentColorHex)
        {
            if (string.IsNullOrWhiteSpace(accentColorHex))
                return AccentBrush;

            try
            {
                string clean = accentColorHex.Trim();
                if (!clean.StartsWith("#", StringComparison.Ordinal))
                    clean = "#" + clean;
                return BrushFromHex(clean);
            }
            catch
            {
                return AccentBrush;
            }
        }

        public static Brush GetTintedPillBgBrush(string accentColorHex, byte alpha = 42)
        {
            if (string.IsNullOrWhiteSpace(accentColorHex))
                return SubtleHoverBg;

            try
            {
                string clean = accentColorHex.Trim();
                if (!clean.StartsWith("#", StringComparison.Ordinal))
                    clean = "#" + clean;
                var baseCol = (Color)ColorConverter.ConvertFromString(clean);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, baseCol.R, baseCol.G, baseCol.B));
                brush.Freeze();
                return brush;
            }
            catch
            {
                return SubtleHoverBg;
            }
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
            SetFactoryBrush(line, Border.BackgroundProperty, DividerBrush);
            template.VisualTree = line;
            sep.Template = template;
            return sep;
        }
    }
}
