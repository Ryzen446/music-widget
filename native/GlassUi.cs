using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Condition = System.Windows.Condition;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal class CapsuleBorder : Border
    {
        public CapsuleBorder() { }

        protected virtual bool Raised { get { return false; } }

        protected override void OnRender(DrawingContext drawing)
        {
            var bounds = new Rect(RenderSize);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            double radius = Math.Min(bounds.Width, bounds.Height) / 2;
            var solid = Background as SolidColorBrush;
            if (Raised && Background != null && (solid == null || solid.Color.A != 0))
            {
                ControlGlass.Draw(drawing, bounds, radius, Background, BorderBrush == Glass.InkBrush,
                    edge: BorderBrush == Brushes.Transparent ? null : BorderBrush);
                return;
            }
            drawing.DrawRoundedRectangle(Background, null, bounds, radius, radius);
            double stroke = BorderThickness.Left;
            if (BorderBrush == null || BorderBrush == Brushes.Transparent || stroke <= 0 || radius <= stroke / 2) return;
            bounds.Inflate(-stroke / 2, -stroke / 2);
            radius -= stroke / 2;
            drawing.DrawRoundedRectangle(null, new Pen(BorderBrush, stroke), bounds, radius, radius);
        }
    }

    internal sealed class RaisedCapsule : CapsuleBorder
    {
        public RaisedCapsule() { }
        protected override bool Raised { get { return true; } }
    }

    internal sealed class GlassUi
    {
        private readonly WidgetWindow window;
        private readonly List<WeakReference> surfaces = new List<WeakReference>();
        internal const string Rest = "MusicWidget.Rest";
        private const string Hover = "MusicWidget.Hover";
        private const string Press = "MusicWidget.Press";
        private const string MenuHover = "MusicWidget.MenuHover";
        private const string Selected = "MusicWidget.Selected";
        private const string PopupMaterial = "MusicWidget.PopupMaterial";
        private const string AcrylicPopupMaterial = "MusicWidget.AcrylicPopupMaterial";
        private const string AcrylicVolumeMaterial = "MusicWidget.AcrylicVolumeMaterial";
        internal const string VisualizerBrushKey = "MusicWidget.Visualizer";
        private static readonly Brush Muted = Glass.Solid(Color.FromRgb(205, 213, 226));
        private readonly Style buttonStyle;
        private static readonly Style inputStyle = CreateInputStyle();
        private static readonly ControlTemplate tipTemplate = new ControlTemplate(typeof(ToolTip))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
        };
        internal Color Aura { get; private set; }

        internal GlassUi(WidgetWindow window, Color aura)
        {
            this.window = window;
            buttonStyle = CreateButtonStyle();
            SetAura(aura);
        }

        internal void SetAura(Color aura)
        {
            var resources = window.Resources;
            if (resources.Contains(Rest) && Aura == aura) return;
            Aura = aura;
            // WPF can freeze the shared brushes when it seals a control template.
            resources[Rest] = ControlGlass.Fill(aura);
            resources[Hover] = Glass.Gradient(Glass.Alpha(Glass.Mix(aura, Colors.White, .65), 30),
                Glass.Alpha(aura, 22), new Point(0, 0), new Point(1, 1));
            resources[Press] = Glass.Solid(Glass.Alpha(Glass.Mix(aura, Colors.White, .45), 25));
            resources[MenuHover] = Glass.Solid(Glass.Mix(aura, Color.FromArgb(18, 255, 255, 255), .13));
            resources[Selected] = ControlGlass.Fill(aura, true);
            resources[VisualizerBrushKey] = Glass.Gradient(Colors.White, Glass.Alpha(aura, 204), new Point(0, 0), new Point(0, 1));
            Color tint = Glass.Mix(aura, Color.FromRgb(21, 16, 24), .26);
            resources[PopupMaterial] = PopoverTint(tint);
            // Match the effective tint over the already composited desktop,
            // with a lighter edge on the menu and a softer well for volume.
            Color menu = Glass.Mix(aura, tint, .04);
            resources[AcrylicPopupMaterial] = AcrylicTint(menu, 157, .035, .01);
            Color volume = Glass.Mix(aura, Color.FromRgb(17, 16, 17), .26);
            resources[AcrylicVolumeMaterial] = AcrylicTint(volume, 167, .018, .024);
            for (int index = surfaces.Count - 1; index >= 0; --index)
            {
                var element = surfaces[index].Target;
                if (element == null) { surfaces.RemoveAt(index); continue; }
                var card = element as CardMaterial;
                if (card != null) card.SetAura(aura);
                var round = element as GlassButton;
                if (round != null) round.SetAura(aura);
                var range = element as GlassRange;
                if (range != null) range.SetAura(aura);
            }
        }

        private static Brush PopoverTint(Color tint)
        {
            var glass = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(.85, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Glass.Alpha(Glass.Mix(Colors.White, tint, .14), 245), 0),
                    new GradientStop(Glass.Alpha(Glass.Mix(Colors.White, tint, .035), 240), .34),
                    new GradientStop(Glass.Alpha(tint, 246), 1)
                }
            };
            glass.Freeze();
            return glass;
        }

        private static Brush AcrylicTint(Color tint, byte density, double topLight, double bottomLight)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection {
                    new GradientStop(Glass.Alpha(Glass.Mix(Colors.White, tint, topLight), density), 0),
                    new GradientStop(Glass.Alpha(tint, density), .28),
                    new GradientStop(Glass.Alpha(tint, density), .7),
                    new GradientStop(Glass.Alpha(Glass.Mix(Colors.White, tint, bottomLight), density), 1)
                }
            };
            brush.Freeze();
            return brush;
        }

        internal void LoadPaneStyles()
        {
            if (window.Resources.Contains(typeof(ScrollBar))) return;
            using (var stream = typeof(GlassUi).Assembly.GetManifestResourceStream("styles.scrollbars.xaml"))
                window.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream));
        }

        internal CardMaterial Card(bool backdrop, double radius = 8)
        {
            var material = new CardMaterial(Aura, null, radius);
            material.SetBackdrop(backdrop);
            surfaces.Add(new WeakReference(material));
            return material;
        }

        internal TextBlock Text(string text, double size = 12, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, FontFamily = Glass.Typeface,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Glass.InkBrush, VerticalAlignment = VerticalAlignment.Center
            };
        }

        internal TextBlock Hint(string text, double size = 11)
        {
            var label = Text(text, size);
            label.Foreground = Muted;
            label.TextWrapping = TextWrapping.Wrap;
            return label;
        }

        internal FrameworkElement Glyph(string key, double size = 16, Brush ink = null)
        {
            Geometry data = GlassButton.GetIcon(key);
            var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
            if (data != null)
            {
                canvas.Children.Add(new Path
                {
                    Data = data, Stroke = ink ?? Glass.InkBrush, StrokeThickness = 1.7,
                    Fill = key == "play" ? ink ?? Glass.InkBrush : null, StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
                });
            }
            return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
        }

        internal Button Button(string text, Action action, string icon = null, double labelSize = 12, double iconSize = 14)
        {
            var copy = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (icon != null)
            {
                var glyph = Glyph(icon, iconSize);
                glyph.Margin = new Thickness(0, 0, text.Length == 0 ? 0 : 8, 0);
                copy.Children.Add(glyph);
            }
            copy.Children.Add(Text(text, labelSize, true));
            var button = new InteractiveGlassButton { Style = buttonStyle, Content = copy };
            button.SetResourceReference(Control.BackgroundProperty, Rest);
            button.SetResourceReference(InteractiveGlassButton.HoverBrushProperty, Hover);
            button.SetResourceReference(InteractiveGlassButton.PressBrushProperty, Press);
            AutomationProperties.SetName(button, text);
            if (action != null) button.Click += (sender, args) => action();
            return button;
        }

        internal Button Chip(string text, bool active, Action action, string icon = null, double labelSize = 12)
        {
            var button = (InteractiveGlassButton)Button(text, action, icon, labelSize);
            button.FillOnly = true;
            if (active)
            {
                button.SetResourceReference(Control.BackgroundProperty, Selected);
            }
            else button.Background = button.BorderBrush = Brushes.Transparent;
            button.MinHeight = 34;
            return button;
        }

        internal Border SelectionIndicator()
        {
            var indicator = new RaisedCapsule
            {
                IsHitTestVisible = false, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(.7)
            };
            indicator.SetResourceReference(Border.BackgroundProperty, Selected);
            return indicator;
        }

        internal GlassButton IconButton(string icon, string label, Action action, double size = 34)
        {
            var button = new GlassButton(Aura, icon, icon == "volume" || icon == "muted" ? 13 : 16)
            {
                Width = size, Height = size
            };
            SetTip(button, label, window.Pane == "player" ? 10 : 11);
            AutomationProperties.SetName(button, label);
            if (action != null) button.Click += (sender, args) => action();
            surfaces.Add(new WeakReference(button));
            return button;
        }

        internal void SetTip(FrameworkElement element, string text, double size = 11)
        {
            var tip = element.ToolTip as ToolTip;
            if (tip == null || tip.Template != tipTemplate)
            {
                var label = Text(text, size);
                label.MaxWidth = 332;
                label.Margin = new Thickness(4);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 1, BlurRadius = 7, Opacity = .95
                };
                tip = new ToolTip { Template = tipTemplate, Content = label, HasDropShadow = false, IsHitTestVisible = false };
                element.ToolTip = tip;
                ToolTipService.SetInitialShowDelay(element, 640);
                ToolTipService.SetPlacement(element, PlacementMode.Top);
            }
            else ((TextBlock)tip.Content).Text = text;
        }

        internal TextBox Field(string placeholder, string text = "")
        {
            var field = new TextBox
            {
                Style = inputStyle, Tag = placeholder, Text = text, MinHeight = 39,
                FontFamily = Glass.Typeface, FontSize = 12, Foreground = Glass.InkBrush,
                CaretBrush = Glass.InkBrush, SelectionBrush = Glass.InkBrush,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(14, 6, 14, 6)
            };
            AutomationProperties.SetName(field, placeholder);
            return field;
        }

        internal GlassRange Slider(double min, double max, double value, Action<double> changed)
        {
            return Range(min, max, value, changed, false);
        }

        internal GlassRange Range(double min, double max, double value, Action<double> changed, bool edgeProgress)
        {
            var slider = new GlassRange(Aura, edgeProgress)
            {
                Minimum = min, Maximum = max, Value = value,
                SmallChange = (max - min) / 100.0, LargeChange = (max - min) / 10.0
            };
            if (changed != null) slider.ValueChanged += (sender, args) => changed(args.NewValue);
            surfaces.Add(new WeakReference(slider));
            return slider;
        }

        internal Grid Header(string text, string subtitle = null, Action back = null)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 23) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var arrow = IconButton("back", window.T(window.Pane == "add" && window.AddReturnsToLibrary ? "backLibrary" : "backPlayer"),
                back ?? new Action(window.GoBack), 44);
            arrow.VerticalAlignment = VerticalAlignment.Top;
            header.Children.Add(arrow);
            var title = new StackPanel { Margin = new Thickness(10, 3, 8, 0) };
            title.Children.Add(Text(text, 21, true));
            if (!string.IsNullOrEmpty(subtitle))
            {
                var label = Hint(subtitle);
                label.Margin = new Thickness(0, 5, 0, 0);
                title.Children.Add(label);
            }
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var close = IconButton("close", window.T("closePanel"), () => window.Navigate("player"), 44);
            close.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(close, 2);
            header.Children.Add(close);
            return header;
        }

        internal Border Popover(FrameworkElement child, int radius, bool embedded = false)
        {
            var border = new RaisedCard
            {
                Child = child, Padding = new Thickness(6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius)
            };
            border.SetResourceReference(Border.BackgroundProperty, embedded ? Rest : PopupMaterial);
            return border;
        }

        private sealed class RaisedCard : Border
        {
            protected override void OnRender(DrawingContext drawing)
            {
                ControlGlass.Draw(drawing, new Rect(RenderSize), CornerRadius.TopLeft, Background, edge: BorderBrush);
            }
        }

        internal Border Flyout(FrameworkElement child, double radius, Thickness padding, bool volume = false)
        {
            var border = new FlyoutFrame
            {
                Child = child, CornerRadius = new CornerRadius(radius), Padding = padding, ClipToBounds = true
            };
            var material = volume ? AcrylicVolumeMaterial : AcrylicPopupMaterial;
            border.SetResourceReference(Border.BackgroundProperty, window.AcrylicActive ? material : PopupMaterial);
            return border;
        }

        private sealed class FlyoutFrame : Border
        {
            internal Brush Backdrop { get; set; }

            protected override void OnRender(DrawingContext drawing)
            {
                GlassFrame.Draw(drawing, RenderSize, CornerRadius.TopLeft, Background, true, backdrop: Backdrop);
            }
        }

        internal static void SetFlyoutBackdrop(FrameworkElement surface, Brush backdrop)
        {
            var frame = surface as FlyoutFrame;
            if (frame == null || frame.Backdrop == backdrop) return;
            frame.Backdrop = backdrop;
            frame.InvalidateVisual();
        }

        internal Button MenuRow(string label, string icon, Action action, bool child = false, string mark = null, Brush ink = null)
        {
            bool selected = mark == "\u2713";
            if (selected) mark = null;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(icon != null || !String.IsNullOrEmpty(mark) ? 24 : 0) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null)
            {
                var glyph = Glyph(icon, 15, ink);
                glyph.HorizontalAlignment = HorizontalAlignment.Left;
                grid.Children.Add(glyph);
            }
            else if (!String.IsNullOrEmpty(mark))
            {
                var indicator = Text(mark, 14);
                if (ink != null) indicator.Foreground = ink;
                grid.Children.Add(indicator);
            }
            var text = Text(label, 12, true);
            if (ink != null) text.Foreground = ink;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (child || selected)
            {
                var arrow = Glyph(selected ? "check" : "chevron", 12);
                arrow.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(arrow, 2);
                grid.Children.Add(arrow);
            }
            var button = (InteractiveGlassButton)Button(label, action);
            button.FillOnly = true;
            button.SetResourceReference(InteractiveGlassButton.HoverBrushProperty, MenuHover);
            button.Content = grid;
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.MinHeight = 40;
            button.Padding = new Thickness(12, 0, 12, 0);
            return button;
        }

        private Style CreateButtonStyle()
        {
            var style = new Style(typeof(Button));
            var outer = new FrameworkElementFactory(typeof(RaisedCapsule), "Face");
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(.7));
            outer.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            outer.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            var layers = new FrameworkElementFactory(typeof(Grid));
            layers.AppendChild(new FrameworkElementFactory(typeof(GlassButtonLight), "InteractionLight"));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            layers.AppendChild(presenter);
            outer.AppendChild(layers);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = outer };
            var focus = new MultiTrigger();
            focus.Conditions.Add(new Condition(UIElement.IsKeyboardFocusedProperty, true));
            focus.Conditions.Add(new Condition(Glass.KeyboardFocusVisibleProperty, true));
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Glass.InkBrush, "Face"));
            template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .43, "Face"));
            template.Triggers.Add(disabled);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 0, 14, 0)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            return style;
        }

        private static Style CreateInputStyle()
        {
            var style = new Style(typeof(TextBox));
            var outer = new FrameworkElementFactory(typeof(CapsuleBorder), "FieldBorder");
            outer.SetValue(Border.BackgroundProperty, ControlGlass.WellFill);
            outer.SetValue(Border.BorderBrushProperty, ControlGlass.WellEdge);
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var layers = new FrameworkElementFactory(typeof(Grid));
            var watermark = new FrameworkElementFactory(typeof(TextBlock), "Watermark");
            watermark.SetBinding(TextBlock.TextProperty, new Binding("Tag") { RelativeSource = RelativeSource.TemplatedParent });
            watermark.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            watermark.SetValue(TextBlock.ForegroundProperty, Muted);
            watermark.SetValue(UIElement.OpacityProperty, .64);
            watermark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            watermark.SetValue(UIElement.IsHitTestVisibleProperty, false);
            watermark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            layers.AppendChild(watermark);
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            // TextBox transfers Padding to its ScrollViewer itself.
            layers.AppendChild(host);
            outer.AppendChild(layers);
            var template = new ControlTemplate(typeof(TextBox)) { VisualTree = outer };
            var empty = new Trigger { Property = TextBox.TextProperty, Value = "" };
            empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Watermark"));
            template.Triggers.Add(empty);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Glass.InkBrush, "FieldBorder"));
            template.Triggers.Add(focus);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            return style;
        }
    }
}
