using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal sealed class PanelMenuItem
    {
        internal readonly string Text;
        internal readonly string Glyph;
        internal readonly Action Select;
        internal readonly bool Danger;
        internal readonly string Icon;

        internal PanelMenuItem(string text, string glyph, Action select, bool danger = false, string icon = null)
        {
            Text = text;
            Glyph = glyph;
            Select = select;
            Danger = danger;
            Icon = icon;
        }
    }

    internal static class PanelKit
    {
        internal static readonly Brush SubtleInk = ColorBrush(255, 191, 201, 216);
        internal static readonly Brush DangerInk = ColorBrush(255, 255, 125, 114);

        internal static Brush ColorBrush(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        internal static Brush Tint(Color aura, byte alpha)
        {
            return ColorBrush(alpha, aura.R, aura.G, aura.B);
        }

        internal static Border Track(UIElement child, double radius = 22)
        {
            var track = new Border { Child = child };
            StyleTrack(track, radius);
            return track;
        }

        internal static void StyleTrack(Border track, double radius = 22)
        {
            track.CornerRadius = new CornerRadius(radius);
            track.BorderThickness = new Thickness(1);
            track.BorderBrush = ControlGlass.WellEdge;
            track.Background = ControlGlass.WellFill;
        }

        internal static Grid Split(params FrameworkElement[] children)
        {
            var row = new Grid();
            for (int i = 0; i < children.Length; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(children[i], i);
                row.Children.Add(children[i]);
            }
            return row;
        }

        internal static StackPanel Section(WidgetWindow window, string label, FrameworkElement content)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            var caption = window.Glass.Text(label, 11, true);
            caption.Margin = new Thickness(0, 0, 0, 10);
            caption.Foreground = SubtleInk;
            panel.Children.Add(caption);
            panel.Children.Add(content);
            return panel;
        }

        internal static ScrollViewer Scroll(UIElement contents)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = contents
            };
        }

        internal static FrameworkElement Choice(WidgetWindow window, string selected, IList<PanelMenuItem> choices, bool matchWidth = false)
        {
            Button trigger = null;
            trigger = window.Glass.Button(selected, delegate { Menu(window, trigger, choices, matchWidth); });
            trigger.Height = 36;
            trigger.HorizontalAlignment = HorizontalAlignment.Stretch;
            trigger.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var content = new DockPanel { LastChildFill = true };
            var glyph = window.Glass.Text("›", 15);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 2, 0);
            DockPanel.SetDock(glyph, Dock.Right);
            content.Children.Add(glyph);
            var label = window.Glass.Text(selected, 12, true);
            label.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(label);
            trigger.Content = content;
            return trigger;
        }

        internal static void Menu(WidgetWindow window, FrameworkElement target, IList<PanelMenuItem> entries, bool matchWidth = false)
        {
            if (target == null || entries.Count == 0) return;
            var rows = new StackPanel();
            foreach (var entry in entries)
            {
                rows.Children.Add(window.Glass.MenuRow(entry.Text, entry.Icon, delegate
                {
                    window.DismissFloater(true);
                    entry.Select();
                }, mark: entry.Glyph, ink: entry.Danger ? DangerInk : null));
            }
            var menu = window.Glass.Flyout(Scroll(rows), 26, new Thickness(6));
            menu.MaxHeight = 340;
            menu.Width = matchWidth ? target.ActualWidth : 208;
            window.ToggleFloater(target, menu, focusMenu: true);
        }

        internal static void Enter(FrameworkElement element, bool reduced, bool back = false)
        {
            if (reduced || !SystemParameters.ClientAreaAnimation) return;
            var shift = new TranslateTransform();
            element.RenderTransform = shift;
            var duration = TimeSpan.FromMilliseconds(190);
            var easing = new KeySpline(.2, .75, .25, 1);
            element.BeginAnimation(UIElement.OpacityProperty, MotionBetween(.48, 1, duration, easing));
            shift.BeginAnimation(TranslateTransform.XProperty, MotionBetween(back ? -8 : 8, 0, duration, easing));
            shift.BeginAnimation(TranslateTransform.YProperty, MotionBetween(3, 0, duration, easing));
        }

        internal static void AnimatePopover(FrameworkElement element, bool above, bool rightAligned, bool opening, Action completed)
        {
            var previous = element.RenderTransform as ScaleTransform;
            double startX = previous != null ? previous.ScaleX : opening ? .91 : 1;
            double startY = previous != null ? previous.ScaleY : opening ? .85 : 1;
            double opacity = previous == null && opening ? .12 : element.Opacity;
            double distance = opening ? (1 - opacity) / .88 : opacity;
            var duration = TimeSpan.FromMilliseconds((opening ? 185 : 160) * Math.Max(.45, Math.Min(1, distance)));
            var easing = opening ? new KeySpline(.16, 1, .3, 1) : new KeySpline(.58, .03, .82, .75);
            var scale = new ScaleTransform(startX, startY,
                element.Width * (rightAligned ? 1 : .5), above ? element.Height : 0);
            var fade = MotionBetween(opacity, opening ? 1 : 0, duration,
                opening ? easing : new KeySpline(.25, .1, .4, 1), FillBehavior.HoldEnd);
            fade.Completed += (sender, args) =>
            {
                if (element.RenderTransform != scale) return;
                if (opening) ResetPopoverAnimation(element);
                completed();
            };
            element.RenderTransform = scale;
            // Scale the entire surface so the glass lip, icons and text move together.
            double end = opening ? 1 : .06;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, MotionBetween(startX, end, duration, easing, FillBehavior.HoldEnd));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, MotionBetween(startY, end, duration, easing, FillBehavior.HoldEnd));
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        internal static void ResetPopoverAnimation(FrameworkElement element)
        {
            element.ClearValue(UIElement.RenderTransformProperty);
            element.BeginAnimation(UIElement.OpacityProperty, null);
        }

        private static DoubleAnimationUsingKeyFrames MotionBetween(double from, double to, TimeSpan duration, KeySpline easing,
            FillBehavior fill = FillBehavior.Stop)
        {
            var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = fill, Duration = duration };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), easing));
            return animation;
        }
    }
}
