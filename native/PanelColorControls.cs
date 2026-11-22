using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal struct PanelHsv
    {
        internal readonly double Hue, Saturation, Brightness;

        internal PanelHsv(double hue, double saturation, double brightness)
        {
            Hue = Math.Max(0, Math.Min(359, hue));
            Saturation = Clamp(saturation);
            Brightness = Clamp(brightness);
        }

        internal static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }

        internal static PanelHsv FromColor(Color color, double previousHue)
        {
            double red = color.R / 255.0, green = color.G / 255.0, blue = color.B / 255.0;
            double max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));
            double difference = max - min, hue = previousHue;
            if (difference != 0)
            {
                if (max == red) hue = ((green - blue) / difference + 6) % 6 * 60;
                else if (max == green) hue = ((blue - red) / difference + 2) * 60;
                else hue = ((red - green) / difference + 4) * 60;
            }
            return new PanelHsv(hue, max == 0 ? 0 : difference / max, max);
        }

        internal Color Color()
        {
            return System.Windows.Media.Color.FromRgb(Channel(5), Channel(3), Channel(1));
        }

        private byte Channel(double offset)
        {
            double k = (offset + Hue / 60) % 6;
            double strength = Math.Max(0, Math.Min(k, Math.Min(4 - k, 1)));
            return (byte)Math.Round(255 * Brightness * (1 - Saturation * strength), MidpointRounding.AwayFromZero);
        }

        internal static bool TryParse(string input, out Color color)
        {
            color = Colors.Transparent;
            string value = (input ?? "").Trim().TrimStart('#');
            uint rgb;
            if (value.Length != 6 || !UInt32.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb)) return false;
            color = System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        internal static string ToHex(Color color)
        {
            return String.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }

    internal sealed class PanelColorPlane : FrameworkElement
    {
        private static readonly Brush ValueShade = Gradient(Colors.Transparent, Colors.Black, new Point(0, 0), new Point(0, 1));
        private static readonly Pen Edge = Stroke(PanelKit.ColorBrush(94, 255, 255, 255), .9);
        private static readonly Pen CursorStroke = Stroke(Colors.White, 1.85);
        private static readonly Pen CursorShadow = Stroke(PanelKit.ColorBrush(130, 2, 3, 11), 3);
        private PanelHsv value;
        private Brush hue;
        internal event Action<PanelHsv> Changed;
        internal event Action Committed;

        internal PanelColorPlane()
        {
            Focusable = true;
            KeyboardNavigation.SetIsTabStop(this, true);
            Cursor = Cursors.Cross;
            IsKeyboardFocusedChanged += delegate { InvalidateVisual(); };
        }

        internal PanelHsv Value
        {
            get { return value; }
            set
            {
                if (hue == null || this.value.Hue != value.Hue)
                {
                    Color pure = new PanelHsv(value.Hue, 1, 1).Color();
                    hue = Gradient(Colors.White, pure, new Point(0, 0), new Point(1, 0));
                }
                this.value = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawing)
        {
            if (ActualWidth < 4 || ActualHeight < 4) return;
            var rect = new Rect(.5, .5, ActualWidth - 1, ActualHeight - 1);
            drawing.DrawRoundedRectangle(hue, null, rect, 12, 12);
            drawing.DrawRoundedRectangle(ValueShade, IsKeyboardFocused && MusicWidget.NativeMini.Glass.ShowKeyboardFocus(this) ? CursorStroke : Edge, rect, 12, 12);
            var selected = new Point(Bound(value.Saturation * ActualWidth, 6, ActualWidth - 6),
                Bound((1 - value.Brightness) * ActualHeight, 6, ActualHeight - 6));
            drawing.DrawEllipse(null, CursorShadow, new Point(selected.X, selected.Y + 1), 6, 6);
            drawing.DrawEllipse(PanelKit.Tint(value.Color(), 255), CursorStroke, selected, 6, 6);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            e.Handled = true;
            Focus();
            CaptureMouse();
            Pick(e.GetPosition(this));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            e.Handled = true;
            Pick(e.GetPosition(this));
            ReleaseMouseCapture();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            Commit();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            double saturation = value.Saturation, brightness = value.Brightness;
            switch (e.Key)
            {
                case Key.Left: saturation -= .02; break;
                case Key.Right: saturation += .02; break;
                case Key.Up: brightness += .02; break;
                case Key.Down: brightness -= .02; break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true;
            Change(new PanelHsv(value.Hue, saturation, brightness));
            Commit();
        }

        private void Pick(Point p)
        {
            if (ActualWidth > 0 && ActualHeight > 0)
                Change(new PanelHsv(value.Hue, p.X / ActualWidth, 1 - p.Y / ActualHeight));
        }

        private void Change(PanelHsv next)
        {
            Value = next;
            var handler = Changed;
            if (handler != null) handler(next);
        }

        private void Commit()
        {
            var handler = Committed;
            if (handler != null) handler();
        }

        private static double Bound(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        internal static Brush Gradient(Color a, Color b, Point start, Point end)
        {
            var brush = new LinearGradientBrush(a, b, start, end);
            brush.Freeze();
            return brush;
        }

        internal static Pen Stroke(Color color, double width)
        {
            return Stroke(PanelKit.Tint(color, color.A), width);
        }

        internal static Pen Stroke(Brush brush, double width)
        {
            var pen = new Pen(brush, width);
            pen.Freeze();
            return pen;
        }
    }

    internal sealed class PanelHue : Slider
    {
        private static readonly DependencyProperty LensProperty = DependencyProperty.Register("Lens", typeof(double), typeof(PanelHue),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly Brush Rainbow = MakeRainbow();
        private static readonly Brush HueProgress = PanelKit.ColorBrush(82, 247, 249, 255);
        private static readonly Brush ThumbFill = PanelColorPlane.Gradient(Color.FromArgb(187, 255, 255, 255),
            Color.FromArgb(70, 247, 249, 255), new Point(0, 0), new Point(.9, 1));
        private static readonly Pen ThumbEdge = PanelColorPlane.Stroke(Color.FromArgb(184, 255, 255, 255), .8);
        internal event Action Committed;

        internal PanelHue()
        {
            Background = Brushes.Transparent;
            var hitTarget = new FrameworkElementFactory(typeof(Border));
            hitTarget.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            Template = new ControlTemplate(typeof(Slider)) { VisualTree = hitTarget };
            IsMoveToPointEnabled = true;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
            SmallChange = 1;
            LargeChange = 10;
            ValueChanged += delegate { InvalidateVisual(); };
            IsKeyboardFocusedChanged += delegate { InvalidateVisual(); };
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 29 || ActualHeight < 16) return;
            double center = ActualHeight * .5;
            double amount = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;
            dc.DrawRoundedRectangle(Rainbow, null, new Rect(0, center - 4, ActualWidth, 8), 2, 2);
            if (amount > 0)
                dc.DrawRectangle(HueProgress, null, new Rect(0, center - 3, ActualWidth * amount, 6));
            double x = (ActualWidth - 28) * amount;
            var thumb = Glass.LensBounds(new Rect(x, center - 8.5, 28, 17), Math.Max(0, (double)GetValue(LensProperty)), .3, .45);
            dc.DrawRoundedRectangle(IsMouseCaptured ? Glass.HeldLens : ThumbFill, ThumbEdge, thumb, thumb.Height / 2, thumb.Height / 2);
            if (IsKeyboardFocused && Glass.ShowKeyboardFocus(this))
                dc.DrawRoundedRectangle(null, ThumbEdge, new Rect(.5, center - 5, ActualWidth - 1, 10), 4, 4);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            if (CaptureMouse()) Glass.AnimateLens(this, LensProperty, true, 360);
            Read(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Read(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            Read(e.GetPosition(this).X);
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            Glass.AnimateLens(this, LensProperty, false, 360);
            Commit();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                case Key.Up:
                case Key.Down:
                case Key.PageDown:
                case Key.PageUp:
                case Key.Home:
                case Key.End:
                    Commit(); break;
            }
        }

        private void Read(double x)
        {
            if (ActualWidth > 0)
                Value = Math.Round(Minimum + (Maximum - Minimum) * PanelHsv.Clamp(x / ActualWidth));
        }

        private void Commit()
        {
            var handler = Committed;
            if (handler != null) handler();
        }

        private static Brush MakeRainbow()
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            Color[] colors = { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red };
            for (int i = 0; i < colors.Length; i++) gradient.GradientStops.Add(new GradientStop(colors[i], i / 6.0));
            gradient.Freeze();
            return gradient;
        }
    }

    internal sealed class PanelToggle : ToggleButton
    {
        private static readonly DependencyProperty ThumbProgressProperty = DependencyProperty.Register(
            "ThumbProgress", typeof(double), typeof(PanelToggle), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty LensProperty = DependencyProperty.Register("Lens", typeof(double), typeof(PanelToggle),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly Brush Knob = PanelColorPlane.Gradient(Color.FromArgb(203, 255, 255, 255),
            Color.FromArgb(110, 219, 231, 255), new Point(0, 0), new Point(.8, 1));
        private static readonly Pen WellEdge = CreateWellEdge();
        private static readonly Pen Highlight = PanelColorPlane.Stroke(Color.FromArgb(184, 255, 255, 255), .9);

        internal PanelToggle(Func<bool> motion)
        {
            SetResourceReference(BackgroundProperty, GlassUi.Rest);
            Width = 50;
            Height = 28;
            Cursor = Cursors.Hand;
            FocusVisualStyle = null;
            var hitTarget = new FrameworkElementFactory(typeof(Border));
            hitTarget.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            Template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = hitTarget };
            Checked += delegate { MoveThumb(1, motion()); };
            Unchecked += delegate { MoveThumb(0, motion()); };
            IsKeyboardFocusedChanged += delegate { InvalidateVisual(); };
        }

        private void MoveThumb(double target, bool animate)
        {
            var start = (double)GetValue(ThumbProgressProperty);
            SetValue(ThumbProgressProperty, target);
            if (IsLoaded && animate)
                BeginAnimation(ThumbProgressProperty, Glass.Transition(start, target, 460, Glass.Spring));
            else BeginAnimation(ThumbProgressProperty, null);
            InvalidateVisual();
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            if (IsLoaded) Glass.AnimateLens(this, LensProperty, IsPressed, 320);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 4 || ActualHeight < 4) return;
            bool focused = IsKeyboardFocused && Glass.ShowKeyboardFocus(this);
            var bounds = new Rect(RenderSize);
            if (IsChecked == true) ControlGlass.Draw(dc, bounds, 14, Background, focused, lift: false);
            else
            {
                bounds.Inflate(-.5, -.5);
                dc.DrawRoundedRectangle(ControlGlass.WellFill, focused ? Highlight : WellEdge, bounds, 14, 14);
            }
            var thumb = Glass.LensBounds(new Rect(3 + (ActualWidth - 36) * (double)GetValue(ThumbProgressProperty), 3, 30, ActualHeight - 6),
                Math.Max(0, (double)GetValue(LensProperty)), .22, .30);
            ControlGlass.Draw(dc, thumb, thumb.Height / 2, IsPressed ? Glass.HeldLens : Knob);
        }

        private static Pen CreateWellEdge()
        {
            var pen = new Pen(ControlGlass.WellEdge, 1);
            pen.Freeze();
            return pen;
        }
    }
}
