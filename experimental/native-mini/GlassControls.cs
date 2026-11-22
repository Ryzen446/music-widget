using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MusicWidget.NativeMini
{
    internal static class Glass
    {
        internal static readonly Color Ink = Color.FromRgb(247, 246, 241);
        internal static readonly Brush InkBrush = Solid(Ink);
        internal static readonly FontFamily Typeface = LoadTypeface();
        internal static bool MotionEnabled = true;

        private static FontFamily LoadTypeface()
        {
            const string fallback = "Segoe UI Variable Text, Segoe UI";
            var assembly = typeof(Glass).Assembly;
            if (assembly.GetManifestResourceInfo(assembly.GetName().Name + ".g.resources") == null)
                return new FontFamily(fallback);
            var baseUri = new Uri("pack://application:,,,/" + assembly.GetName().Name + ";component/");
            return new FontFamily(baseUri, "./fonts/#Inter, " + fallback);
        }
        internal static readonly DependencyProperty KeyboardFocusVisibleProperty = DependencyProperty.RegisterAttached(
            "KeyboardFocusVisible", typeof(bool), typeof(Glass),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        internal static bool ShowKeyboardFocus(DependencyObject element)
        {
            return (bool)element.GetValue(KeyboardFocusVisibleProperty);
        }

        internal static SolidColorBrush Solid(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        internal static Color Alpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        internal static Color Mix(Color first, Color second, double weight)
        {
            // CSS color-mix(in srgb) uses premultiplied sRGB when colors differ in alpha.
            double a = first.A * weight;
            double b = second.A * (1 - weight);
            double sum = a + b;
            if (sum == 0) return Colors.Transparent;
            return Color.FromArgb((byte)Math.Round(sum),
                (byte)Math.Round((first.R * a + second.R * b) / sum),
                (byte)Math.Round((first.G * a + second.G * b) / sum),
                (byte)Math.Round((first.B * a + second.B * b) / sum));
        }

        internal static LinearGradientBrush Gradient(Color top, Color bottom, Point start, Point end)
        {
            var brush = new LinearGradientBrush(top, bottom, start, end);
            brush.Freeze();
            return brush;
        }

        internal static Brush PopupFill(Color aura)
        {
            Color tint = Mix(aura, Color.FromRgb(21, 16, 24), 0.26);
            return Gradient(Mix(Colors.White, tint, 0.11), Alpha(tint, 248),
                new Point(0, 0), new Point(1, 1));
        }

        internal static ControlTemplate EmptyTemplate(Type type)
        {
            var hitTarget = new FrameworkElementFactory(typeof(Border));
            hitTarget.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            return new ControlTemplate(type) { VisualTree = hitTarget };
        }

        internal static DoubleAnimationUsingKeyFrames Transition(double from, double to, int milliseconds,
            KeySpline curve)
        {
            var duration = TimeSpan.FromMilliseconds(milliseconds);
            var motion = new DoubleAnimationUsingKeyFrames { Duration = duration, FillBehavior = FillBehavior.Stop };
            motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            motion.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), curve));
            return motion;
        }

        internal static readonly KeySpline Ease = Curve(.22, 1, .36, 1);
        internal static readonly KeySpline Spring = Curve(.34, 1.4, .64, 1);
        internal static readonly Brush HeldLens = Solid(Color.FromArgb(115, 255, 255, 255));

        internal static void AnimateLens(UIElement grip, DependencyProperty property, bool pressed, int milliseconds)
        {
            bool animate = grip.IsVisible && MotionEnabled && SystemParameters.ClientAreaAnimation;
            double target = pressed && animate ? 1 : 0;
            double current = (double)grip.GetValue(property);
            grip.SetValue(property, target);
            grip.BeginAnimation(property, animate && current != target ? Transition(current, target, milliseconds, Spring) : null);
        }

        internal static Rect LensBounds(Rect original, double amount, double expandX, double expandY)
        {
            original.Inflate(original.Width * amount * expandX / 2, original.Height * amount * expandY / 2);
            return original;
        }
        private static KeySpline Curve(double x1, double y1, double x2, double y2)
        {
            var spline = new KeySpline(x1, y1, x2, y2);
            spline.Freeze();
            return spline;
        }
    }

    internal static class GlassFrame
    {
        private static readonly Brush UpperLight = Glass.Gradient(Color.FromArgb(18, 255, 255, 255),
            Colors.Transparent, new Point(0, 0), new Point(0, 1));
        private static readonly Brush LeftLight = Glass.Gradient(Color.FromArgb(13, 255, 255, 255),
            Colors.Transparent, new Point(0, 0), new Point(1, 0));
        private static readonly Brush LowerLight = Glass.Gradient(Color.FromArgb(8, 236, 229, 255),
            Colors.Transparent, new Point(0, 1), new Point(0, 0));
        private static readonly Pen ClearRim = CreateRim(.18, .467);

        private static class Frosted
        {
            internal static readonly Pen Rim = CreateRim(.17, .416);
        }

        private static Pen CreateRim(double baseAlpha, double counter)
        {
            // A small, shared angular light map preserves the two highlights of
            // the source rim. The rounded outline itself remains a vector path.
            const int length = 65;
            const int center = length / 2;
            var pixels = new byte[length * length * 4];
            for (int y = 0; y < length; ++y)
            for (int x = 0; x < length; ++x)
            {
                double angle = (Math.Atan2(y - center, x - center) * 180 / Math.PI + 180) % 360;
                double upper = Math.Max(0, 1 - Math.Abs(angle - 45) / 45);
                double lower = Math.Max(0, 1 - Math.Abs(angle - 225) / 45) * counter;
                int index = (y * length + x) * 4;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = 255;
                pixels[index + 3] = (byte)Math.Round(255 * (baseAlpha + (1 - baseAlpha) * (upper + lower)));
            }
            var bitmap = BitmapSource.Create(length, length, 96, 96, PixelFormats.Bgra32, null, pixels, length * 4);
            bitmap.Freeze();
            var light = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            light.Freeze();
            var pen = new Pen(light, 1);
            pen.Freeze();
            return pen;
        }

        internal static void Draw(DrawingContext dc, Size size, double radius, Brush material, bool frosted)
        {
            var bounds = new Rect(size);
            if (bounds.Width <= 1 || bounds.Height <= 1) return;
            radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
            dc.DrawRoundedRectangle(material, null, bounds, radius, radius);
            dc.PushClip(new RectangleGeometry(bounds, radius, radius));
            double band = Math.Min(frosted ? 13 : 10, Math.Min(bounds.Width, bounds.Height) / 2);
            dc.DrawRectangle(UpperLight, null, new Rect(0, 0, bounds.Width, band));
            dc.DrawRectangle(LeftLight, null, new Rect(0, 0, band, bounds.Height));
            dc.DrawRectangle(LowerLight, null, new Rect(0, bounds.Height - band, bounds.Width, band));
            dc.Pop();
            var edge = new Rect(.5, .5, bounds.Width - 1, bounds.Height - 1);
            double edgeRadius = Math.Max(0, radius - .5);
            dc.DrawRoundedRectangle(null, frosted ? Frosted.Rim : ClearRim, edge, edgeRadius, edgeRadius);
        }
    }

    internal sealed class CardMaterial : FrameworkElement
    {
        internal static readonly DependencyProperty AnimatedBoundsProperty = DependencyProperty.Register(
            "AnimatedBounds", typeof(Rect), typeof(CardMaterial),
            new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly Brush fallback;
        private Brush tint;
        private bool useBackdrop;
        private double radius;

        internal CardMaterial(Color aura, Color? referenceBackground, double radius = 8)
        {
            IsHitTestVisible = false;
            this.radius = radius;
            fallback = Glass.Solid(referenceBackground ?? Color.FromRgb(22, 24, 33));
            SetAura(aura);
        }

        internal void SetBackdrop(bool enabled)
        {
            useBackdrop = enabled;
            InvalidateVisual();
        }

        internal void SetRadius(double value)
        {
            if (radius == value) return;
            radius = value;
            InvalidateVisual();
        }

        internal void SetAura(Color aura)
        {
            tint = Glass.Solid(Glass.Alpha(aura, 36));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            var animated = (Rect)GetValue(AnimatedBoundsProperty);
            var size = animated.IsEmpty ? RenderSize : animated.Size;
            if (!animated.IsEmpty) drawing.PushTransform(new TranslateTransform(animated.X, animated.Y));
            if (!useBackdrop) drawing.DrawRoundedRectangle(fallback, null, new Rect(size), radius, radius);
            GlassFrame.Draw(drawing, size, radius, tint, false);
            if (!animated.IsEmpty) drawing.Pop();
        }
    }

    internal class InteractiveGlassButton : Button
    {
        internal static readonly DependencyProperty HoverBrushProperty = DependencyProperty.Register(
            "HoverBrush", typeof(Brush), typeof(InteractiveGlassButton), new FrameworkPropertyMetadata(null, Repaint));
        internal static readonly DependencyProperty PressBrushProperty = DependencyProperty.Register(
            "PressBrush", typeof(Brush), typeof(InteractiveGlassButton), new FrameworkPropertyMetadata(null, Repaint));
        private static readonly DependencyProperty TintProperty = DependencyProperty.Register(
            "Tint", typeof(double), typeof(InteractiveGlassButton), new FrameworkPropertyMetadata(0.0, Repaint));
        private static readonly DependencyProperty GlintProperty = DependencyProperty.Register(
            "Glint", typeof(double), typeof(InteractiveGlassButton), new FrameworkPropertyMetadata(0.0, Repaint));
        private static readonly Brush Specular = CreateSpecular();
        private static readonly KeySpline MenuEase = new KeySpline(.25, .1, .25, 1);
        private Point pointer = new Point(.5, 0);
        private RectangleGeometry glintClip;
        private readonly ScaleTransform pressTransform = new ScaleTransform();

        // Menu and segmented rows use just a fill. Their label and hit area stay fixed.
        internal bool FillOnly { get; set; }

        internal InteractiveGlassButton()
        {
            RenderTransformOrigin = new Point(.5, .5);
            RenderTransform = pressTransform;
            Loaded += (sender, args) => UpdateInteraction(false);
            Unloaded += (sender, args) => ClearInteraction();
            IsEnabledChanged += (sender, args) => UpdateInteraction(false);
        }

        private static Brush CreateSpecular()
        {
            var brush = new RadialGradientBrush(Color.FromArgb(122, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
            brush.Freeze();
            return brush;
        }

        private static void Repaint(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ((InteractiveGlassButton)sender).InvalidateInteraction();
        }

        private void InvalidateInteraction()
        {
            var light = GetTemplateChild("InteractionLight") as GlassButtonLight;
            if (light != null) light.InvalidateVisual();
            else InvalidateVisual();
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            SetPointer(e.GetPosition(this));
            UpdateInteraction(true);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            UpdateInteraction(true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetPointer(e.GetPosition(this));
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            if (IsLoaded) UpdateInteraction(true);
        }

        private void SetPointer(Point location)
        {
            if (FillOnly || !IsEnabled || !Glass.MotionEnabled || !SystemParameters.ClientAreaAnimation ||
                !(IsMouseOver || IsPressed) || ActualWidth <= 0 || ActualHeight <= 0) return;
            var next = new Point(Math.Round(Math.Max(0, Math.Min(1, location.X / ActualWidth)), 3),
                Math.Round(Math.Max(0, Math.Min(1, location.Y / ActualHeight)), 3));
            if (pointer == next) return;
            pointer = next;
            InvalidateInteraction();
        }

        private void UpdateInteraction(bool animate)
        {
            animate = animate && IsVisible && Glass.MotionEnabled && SystemParameters.ClientAreaAnimation;
            bool hover = IsEnabled && IsMouseOver;
            bool pressed = IsEnabled && IsPressed && !FillOnly;
            SetInteraction(TintProperty, pressed ? 2 : hover ? 1 : 0, FillOnly ? 140 : 220, FillOnly ? MenuEase : Glass.Ease, animate);
            if (FillOnly) return;
            if (!Glass.MotionEnabled || !SystemParameters.ClientAreaAnimation) pointer = new Point(.5, 0);
            SetInteraction(GlintProperty, pressed ? 1.4 : hover ? 1 : 0, 320, Glass.Ease, animate);
            double current = pressTransform.ScaleX;
            double next = pressed && animate ? .965 : 1;
            pressTransform.ScaleX = pressTransform.ScaleY = next;
            var motion = animate && current != next ? Glass.Transition(current, next, 420, Glass.Spring) : null;
            pressTransform.BeginAnimation(ScaleTransform.ScaleXProperty, motion);
            pressTransform.BeginAnimation(ScaleTransform.ScaleYProperty, motion);
        }

        private void ClearInteraction()
        {
            SetInteraction(TintProperty, 0, 0, Glass.Ease, false);
            SetInteraction(GlintProperty, 0, 0, Glass.Ease, false);
            pressTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            pressTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            pressTransform.ScaleX = pressTransform.ScaleY = 1;
        }

        private void SetInteraction(DependencyProperty property, double value, int duration, KeySpline easing, bool animate)
        {
            if ((double)GetAnimationBaseValue(property) == value)
            {
                if (!animate) BeginAnimation(property, null);
                return;
            }
            var previous = (double)GetValue(property);
            SetValue(property, value);
            BeginAnimation(property, animate ? Glass.Transition(previous, value, duration, easing) : null);
        }

        internal void DrawInteraction(DrawingContext dc, Rect bounds, double radius)
        {
            if (!IsEnabled || bounds.Width <= 0 || bounds.Height <= 0) return;
            var tint = (double)GetValue(TintProperty);
            DrawTint(dc, HoverBrushProperty, bounds, radius, Math.Min(1, tint));
            DrawTint(dc, PressBrushProperty, bounds, radius, Math.Max(0, tint - 1));
            var glint = (double)GetValue(GlintProperty);
            if (glint <= 0 || FillOnly || SystemParameters.HighContrast) return;
            var origin = new Point(bounds.X + bounds.Width * pointer.X, bounds.Y + bounds.Height * pointer.Y);
            var farX = Math.Max(origin.X - bounds.Left, bounds.Right - origin.X);
            var farY = Math.Max(origin.Y - bounds.Top, bounds.Bottom - origin.Y);
            var reach = .62 * Math.Sqrt(farX * farX + farY * farY);
            if (glintClip == null || glintClip.Rect != bounds || glintClip.RadiusX != radius)
            {
                glintClip = new RectangleGeometry(bounds, radius, radius);
                glintClip.Freeze();
            }
            dc.PushClip(glintClip);
            dc.PushOpacity(Math.Min(1, glint / 1.4));
            dc.DrawRectangle(Specular, null, new Rect(origin.X - reach, origin.Y - reach, reach * 2, reach * 2));
            dc.Pop();
            dc.Pop();
        }

        private void DrawTint(DrawingContext dc, DependencyProperty property, Rect bounds, double radius, double alpha)
        {
            if (alpha <= 0) return;
            var brush = (Brush)GetValue(property);
            if (brush == null) return;
            dc.PushOpacity(alpha);
            dc.DrawRoundedRectangle(brush, null, bounds, radius, radius);
            dc.Pop();
        }
    }

    internal sealed class GlassButtonLight : FrameworkElement
    {
        public GlassButtonLight() { IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var owner = TemplatedParent as InteractiveGlassButton;
            if (owner != null)
                owner.DrawInteraction(dc, new Rect(RenderSize), Math.Min(ActualHeight, ActualWidth) / 2);
        }
    }

    internal sealed class GlassButton : InteractiveGlassButton
    {
        private static readonly ControlTemplate ButtonTemplate = Glass.EmptyTemplate(typeof(Button));
        private static readonly Dictionary<string, Geometry> Icons = DefineIcons();
        private Brush restingFill;
        private readonly Pen rim;
        private readonly Pen glyphStroke;
        private readonly double glyphSize;
        private string glyph;
        private Brush swatchFill;

        internal GlassButton(Color aura, string glyph, double glyphSize)
        {
            this.glyph = glyph;
            this.glyphSize = glyphSize;
            Template = ButtonTemplate;
            Width = Height = 28;
            Cursor = Cursors.Hand;
            Style = null;
            FocusVisualStyle = null;
            SetAura(aura);
            rim = new Pen(Glass.Gradient(Color.FromArgb(194, 255, 255, 255),
                Color.FromArgb(54, 255, 255, 255), new Point(0.1, 0.05), new Point(0.85, 1)), 0.72);
            rim.Freeze();
            glyphStroke = GlyphPen(Glass.InkBrush);
            IsKeyboardFocusedChanged += (sender, args) => InvalidateVisual();
        }

        private static Pen GlyphPen(Brush brush)
        {
            var pen = new Pen(brush, 1.7)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }

        internal void SetIcon(string value)
        {
            if (value == glyph) return;
            glyph = value;
            InvalidateVisual();
        }

        internal void SetSwatch(Color color)
        {
            swatchFill = Glass.Solid(color);
            InvalidateVisual();
        }

        internal void SetAura(Color aura)
        {
            Color rest = Glass.Alpha(Glass.Mix(aura, Color.FromRgb(218, 230, 255), 0.40), 62);
            restingFill = Glass.Gradient(Glass.Mix(Glass.Alpha(Colors.White, 96), rest, 0.35),
                rest, new Point(0.15, 0.02), new Point(0.83, 0.95));
            SetValue(HoverBrushProperty, Glass.Solid(Glass.Alpha(Glass.Mix(aura, Colors.White, .65), 27)));
            SetValue(PressBrushProperty, Glass.Solid(Glass.Alpha(Glass.Mix(aura, Colors.White, .45), 25)));
            InvalidateVisual();
        }

        private static Dictionary<string, Geometry> DefineIcons()
        {
            // Original application paths from frontend/src/Icons.tsx, on its 24×24 viewbox.
            var paths = new Dictionary<string, string> {
                { "play", "M8,5 V19 L19,12 Z" }, { "pause", "M8,5 V19 M16,5 V19" },
                { "next", "M5,5 L16,12 L5,19 Z M19,5 V19" },
                { "volume", "M3,9 H7 L12,5 V19 L7,15 H3 Z M16,8 C19,10 19,14 16,16 M19,5 C24,9 24,15 19,19" },
                { "muted", "M3,9 H7 L12,5 V19 L7,15 H3 Z M17,9 L22,15 M22,9 L17,15" },
                { "prev", "M19 5 8 12l11 7ZM5 5v14" },
                { "close", "m6 6 12 12M6 18 18 6" }, { "back", "m14 6-6 6 6 6" },
                { "chevron", "m8 5 7 7-7 7" }, { "plus", "M12 5v14M5 12h14" },
                { "settings", "M9 3h6l1 3 3 1 2 5-2 5-3 1-1 3H9l-1-3-3-1-2-5 2-5 3-1ZM9 12a3 3 0 1 0 6 0 3 3 0 1 0-6 0" },
                { "library", "M4 4v16M9 4v16M14 4v16m4-15 3 14" },
                { "shuffle", "M3 6h3l12 12h3m-4-4 4 4-4 4M3 18h3L18 6h3m-4-4 4 4-4 4" },
                { "repeat", "m17 2 4 4-4 4M3 11V8a2 2 0 0 1 2-2h16M7 22l-4-4 4-4m14-1v3a2 2 0 0 1-2 2H3" },
                { "hide", "M5 12h14" }, { "upload", "M12 16V3m-5 5 5-5 5 5M4 16v5h16v-5" },
                { "link", "M10 13a4 4 0 0 0 6 0l4-4a4 4 0 0 0-6-6l-2 2M14 11a4 4 0 0 0-6 0l-4 4a4 4 0 0 0 6 6l2-2" },
                { "check", "m5 12 4 4L19 6" },
                { "trash", "M4 7h16 M9 7V5a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2 M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12 M10 11v6 M14 11v6" },
                { "edit", "M4 15.5V20h4.5L20 8.5a2.1 2.1 0 0 0 0-3l-1.5-1.5a2.1 2.1 0 0 0-3 0ZM13.5 6l4.5 4.5" },
                { "note", "M9 18V5l11-2v13M9 9l11-2M9 18a3 2 0 1 1-6 0 3 2 0 1 1 6 0m11-2a3 2 0 1 1-6 0 3 2 0 1 1 6 0" },
                { "search", "M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0" },
                { "download", "M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4" },
                { "stop", "M6 6h12v12H6Z" }, { "resize", "M6 18 18 6 M12 18l6-6" }
            };
            var result = new Dictionary<string, Geometry>();
            foreach (var path in paths)
            {
                var geometry = Geometry.Parse(path.Value);
                geometry.Freeze();
                result.Add(path.Key, geometry);
            }
            return result;
        }

        internal static Geometry GetIcon(string key)
        {
            Geometry result;
            return key != null && Icons.TryGetValue(key, out result) ? result : null;
        }

        protected override void OnRender(DrawingContext drawing)
        {
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = Math.Min(ActualWidth, ActualHeight) / 2 - 0.45;
            if (r <= 0) return;
            drawing.PushOpacity(IsEnabled ? 1 : 0.45);
            var ink = Glass.InkBrush;
            drawing.DrawEllipse(restingFill, rim, center, r, r);
            var bounds = new Rect(center.X - r, center.Y - r, r * 2, r * 2);
            DrawInteraction(drawing, bounds, r);
            if (IsKeyboardFocused && Glass.ShowKeyboardFocus(this))
                drawing.DrawEllipse(null, new Pen(ink, .8), center, r - 1.45, r - 1.45);
            if (swatchFill != null)
            {
                drawing.DrawEllipse(swatchFill, null, center, 9, 9);
                drawing.Pop();
                return;
            }
            double scale = glyphSize / 24.0;
            drawing.PushTransform(new MatrixTransform(scale, 0, 0, scale,
                center.X - glyphSize / 2, center.Y - glyphSize / 2));
            if (glyph == "more")
            {
                foreach (int x in new[] { 5, 12, 19 })
                    drawing.DrawEllipse(ink, null, new Point(x, 12), 1.8, 1.8);
            }
            else if (glyph == "grip")
            {
                for (int y = 5; y <= 19; y += 7)
                {
                    drawing.DrawEllipse(Brushes.White, null, new Point(8, y), 1.95, 1.95);
                    drawing.DrawEllipse(Brushes.White, null, new Point(16, y), 1.95, 1.95);
                }
            }
            else
            {
                Geometry shape = GetIcon(glyph);
                if (shape != null) drawing.DrawGeometry(glyph == "play" ? ink : null, glyphStroke, shape);
            }
            drawing.Pop();
            drawing.Pop();
        }
    }

    internal sealed class GlassRange : Slider
    {
        private const double LowVolume = .06;
        private const double LowTravel = .10;
        private static readonly ControlTemplate RangeTemplate = Glass.EmptyTemplate(typeof(Slider));
        private static readonly DependencyProperty LensProperty = DependencyProperty.Register("Lens", typeof(double), typeof(GlassRange),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly Brush Rail = Glass.Solid(Color.FromArgb(128, 16, 16, 25));
        private static readonly Brush RailInset = Glass.Gradient(Color.FromArgb(83, 0, 0, 0),
            Colors.Transparent, new Point(0, 0), new Point(0, 0.7));
        private static readonly Brush ProgressInset = Glass.Gradient(Color.FromArgb(184, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.45));
        private static readonly Brush Knob = Glass.Gradient(Color.FromArgb(140, 255, 255, 255),
            Color.FromArgb(77, 255, 255, 255), new Point(0, 0), new Point(1, 1));
        private static readonly Pen KnobEdge = CreateKnobEdge();
        private static readonly Brush SliderFill = Glass.Gradient(Color.FromArgb(170, 240, 248, 255),
            Color.FromArgb(139, 224, 231, 244), new Point(0, 0), new Point(0, 1));
        private Brush fill;
        private readonly bool progress;
        private double displayed;
        private double from;
        private double target;
        private double dragOffset;
        private DateTime started;

        internal GlassRange(Color aura, bool progress)
        {
            this.progress = progress;
            SetAura(aura);
            Template = RangeTemplate;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
            Minimum = 0;
            Maximum = 1;
            SmallChange = 0.02;
            LargeChange = 0.1;
            Height = progress ? 11 : 22;
            IsKeyboardFocusedChanged += (sender, args) => InvalidateVisual();
            Unloaded += (sender, args) =>
            {
                StopProgressAnimation();
                BeginAnimation(LensProperty, null);
                SetValue(LensProperty, 0.0);
            };
        }

        internal bool AnimateProgress { get; set; }
        internal bool PreciseLowEnd { get; set; }

        internal void SetAura(Color aura)
        {
            if (!progress) { fill = SliderFill; return; }
            var baseColor = Glass.Mix(aura, Color.FromRgb(244, 246, 255), 0.72);
            fill = Glass.Gradient(Glass.Mix(Colors.White, baseColor, 0.61), baseColor,
                new Point(0, 0), new Point(0, 0.75));
            InvalidateVisual();
        }

        private static Pen CreateKnobEdge()
        {
            var pen = new Pen(Glass.Gradient(Color.FromArgb(217, 255, 255, 255),
                Color.FromArgb(106, 255, 255, 255), new Point(0, 0), new Point(0, 1)), 1);
            pen.Freeze();
            return pen;
        }

        internal void StopProgressAnimation()
        {
            CompositionTarget.Rendering -= AnimateFrame;
            displayed = Value;
            InvalidateVisual();
        }

        private void AnimateFrame(object sender, EventArgs e)
        {
            double fraction = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / 160);
            displayed = from + (target - from) * fraction;
            if (fraction >= 1) CompositionTarget.Rendering -= AnimateFrame;
            InvalidateVisual();
        }

        protected override void OnValueChanged(double oldValue, double newValue)
        {
            base.OnValueChanged(oldValue, newValue);
            StopProgressAnimation();
            if (progress && AnimateProgress && IsVisible && Glass.MotionEnabled && SystemParameters.ClientAreaAnimation && Math.Abs(newValue - oldValue) < (Maximum - Minimum) / 15)
            {
                from = displayed = oldValue;
                target = newValue;
                started = DateTime.UtcNow;
                CompositionTarget.Rendering += AnimateFrame;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            double size = ActualWidth;
            double top = progress ? ActualHeight - 4 : (ActualHeight - 4) / 2;
            if (size <= 0) return;
            double fraction = VisualFraction(progress ? displayed : Value);
            double inset = progress ? 0 : Math.Min(14, size / 2);
            double filled = Math.Max(0, size - 2 * inset) * fraction;
            drawing.DrawRoundedRectangle(Rail, null, new Rect(0, top, size, 4), progress ? 0 : 2, 2);
            if (progress)
                drawing.DrawRectangle(RailInset, null, new Rect(0, top, size, 4));
            double fillWidth = progress ? filled : filled + inset;
            if (fillWidth > 0)
            {
                drawing.DrawRoundedRectangle(fill, null, new Rect(0, top, fillWidth, 4), progress ? 0 : 2, 2);
                if (progress)
                    drawing.DrawRectangle(ProgressInset, null, new Rect(0, top, fillWidth, 4));
            }
            if (IsKeyboardFocused && Glass.ShowKeyboardFocus(this))
                drawing.DrawLine(new Pen(Glass.InkBrush, 0.65), new Point(0, top - 0.5), new Point(size, top - 0.5));
            if (!progress)
            {
                double lens = Math.Max(0, (double)GetValue(LensProperty));
                var thumb = Glass.LensBounds(new Rect(filled, top - 6.5, 2 * inset, 17), lens, .3, .45);
                drawing.DrawRoundedRectangle(IsMouseCaptured ? Glass.HeldLens : Knob, KnobEdge, thumb, thumb.Height / 2, thumb.Height / 2);
            }
        }

        private void SelectPoint(MouseEventArgs args)
        {
            double inset = progress ? 0 : Math.Min(14, ActualWidth / 2);
            double fraction = Math.Max(0, Math.Min(1,
                (args.GetPosition(this).X - dragOffset - inset) / Math.Max(1, ActualWidth - 2 * inset)));
            if (PreciseLowEnd)
                fraction = fraction <= LowTravel ? fraction * LowVolume / LowTravel :
                    LowVolume + (fraction - LowTravel) * (1 - LowVolume) / (1 - LowTravel);
            double value = Minimum + fraction * (Maximum - Minimum);
            Value = PreciseLowEnd ? Math.Round(value, MidpointRounding.AwayFromZero) : value;
        }

        private double VisualFraction(double value)
        {
            double fraction = Math.Max(0, Math.Min(1, (value - Minimum) / Math.Max(.001, Maximum - Minimum)));
            if (!PreciseLowEnd) return fraction;
            return fraction <= LowVolume ? fraction * LowTravel / LowVolume :
                LowTravel + (fraction - LowVolume) * (1 - LowTravel) / (1 - LowVolume);
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!IsEnabled) return;
            Focus();
            dragOffset = 0;
            bool onThumb = false;
            if (!progress)
            {
                double inset = Math.Min(14, ActualWidth / 2);
                double center = inset + (ActualWidth - 2 * inset) * VisualFraction(Value);
                double distance = e.GetPosition(this).X - center;
                onThumb = Math.Abs(distance) <= inset;
                if (onThumb) dragOffset = distance;
            }
            if (CaptureMouse() && !progress) Glass.AnimateLens(this, LensProperty, true, 360);
            if (!onThumb) SelectPoint(e);
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SelectPoint(e);
            base.OnPreviewMouseMove(e);
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (!progress) Glass.AnimateLens(this, LensProperty, false, 360);
        }
    }
}
