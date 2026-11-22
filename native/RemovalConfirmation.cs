using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Mini = MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal sealed partial class WidgetWindow
    {
        private RemovalConfirmation removalConfirmation;

        internal void ConfirmRemoval(FrameworkElement trigger, string title, string summary, string item, Action confirmed)
        {
            if (removalConfirmation != null || library == null || !IsVisible) return;
            CloseFloater();
            library.CancelDrag();
            var layer = paneLayer;
            var dialog = new RemovalConfirmation(this, library, title, summary, item, accepted =>
            {
                CloseRemoval();
                if (accepted) confirmed();
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (removalConfirmation != null || paneLayer != layer || !IsActive) return;
                    if (!trigger.IsVisible || !trigger.Focus())
                        layer.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }));
            });
            removalConfirmation = dialog;
            layer.Children.Add(dialog);
            dialog.Open();
        }

        private void CloseRemoval()
        {
            var dialog = removalConfirmation;
            if (dialog == null) return;
            removalConfirmation = null;
            dialog.Reset();
            paneLayer.Children.Remove(dialog);
        }
    }

    internal sealed class RemovalConfirmation : Grid
    {
        private static readonly KeySpline ExitEase = new KeySpline(.36, 0, .58, 1);
        private static readonly Brush Shade = PanelKit.ColorBrush(255, 7, 8, 13);
        private static readonly Brush DeleteFill = Mini.Glass.Gradient(Color.FromArgb(174, 151, 47, 57),
            Color.FromArgb(159, 88, 24, 40), new Point(0, 0), new Point(.72, 1));
        private static readonly Brush DeleteEdge = Mini.Glass.Gradient(Color.FromArgb(210, 255, 179, 168),
            Color.FromArgb(100, 233, 104, 108), new Point(0, 0), new Point(.65, 1));
        private readonly WidgetWindow window;
        private readonly UIElement background;
        private readonly Effect previousEffect;
        private readonly BlurEffect blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
        private readonly Border scrim = new Border { Background = Shade, Opacity = 0 };
        private readonly Border card;
        private readonly Button cancel, delete;
        private readonly ScaleTransform scale = new ScaleTransform(.96, .96);
        private readonly TranslateTransform lift = new TranslateTransform(0, 7);
        private readonly Action<bool> closed;
        private bool closing;

        internal RemovalConfirmation(WidgetWindow window, UIElement background, string title, string summary, string item, Action<bool> closed)
        {
            this.window = window;
            this.background = background;
            this.closed = closed;
            previousEffect = background.Effect;
            AutomationProperties.SetName(this, title);
            FocusManager.SetIsFocusScope(this, true);
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.Cycle);

            scrim.MouseLeftButtonDown += (sender, args) => { args.Handled = true; Dismiss(false); };
            Children.Add(scrim);
            var body = new StackPanel();
            var heading = window.Glass.Text(title, 17, true);
            heading.TextWrapping = TextWrapping.Wrap;
            heading.VerticalAlignment = VerticalAlignment.Center;
            var badge = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
                BorderBrush = DeleteEdge, BorderThickness = new Thickness(.7), Background = DeleteFill,
                Margin = new Thickness(0, 0, 12, 0), Child = window.Glass.Glyph("trash", 23, PanelKit.DangerInk)
            };
            var header = new DockPanel();
            DockPanel.SetDock(badge, Dock.Left);
            header.Children.Add(badge);
            header.Children.Add(heading);
            body.Children.Add(header);

            var name = window.Glass.Text(item, 12, true);
            name.TextWrapping = TextWrapping.Wrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            name.LineHeight = 17;
            name.MaxHeight = 34;
            var itemSurface = PanelKit.Track(name, 11);
            itemSurface.Padding = new Thickness(11, 7, 11, 7);
            itemSurface.Margin = new Thickness(0, 14, 0, 9);
            body.Children.Add(itemSurface);

            var description = window.Glass.Hint(summary, 12);
            description.TextWrapping = TextWrapping.Wrap;
            description.LineHeight = 17;
            body.Children.Add(description);

            cancel = window.Glass.Button(window.T("cancel"), () => Dismiss(false));
            delete = window.Glass.Button(window.T("delete"), () => Dismiss(true), "trash", 12, 15);
            delete.Background = DeleteFill;
            delete.BorderBrush = DeleteEdge;
            cancel.Height = delete.Height = 38;
            cancel.Margin = new Thickness(0, 0, 5, 0);
            delete.Margin = new Thickness(5, 0, 0, 0);
            var actions = PanelKit.Split(cancel, delete);
            actions.Margin = new Thickness(0, 18, 0, 0);
            body.Children.Add(actions);

            card = window.Glass.Flyout(body, 24, new Thickness(20));
            card.MaxWidth = 366;
            card.Margin = new Thickness(20, 0, 20, 0);
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Opacity = 0;
            card.RenderTransformOrigin = new Point(.5, .5);
            var motion = new TransformGroup();
            motion.Children.Add(scale);
            motion.Children.Add(lift);
            card.RenderTransform = motion;
            Children.Add(card);
            PreviewKeyDown += (sender, args) =>
            {
                if (args.Key != Key.Enter) return;
                args.Handled = true;
                Dismiss(delete.IsKeyboardFocused);
            };
        }

        internal void Open()
        {
            background.IsHitTestVisible = false;
            if (!SystemParameters.HighContrast) background.Effect = blur;
            Animate(true, false, null);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusCancel));
        }

        internal void FocusCancel()
        {
            if (!closing && IsLoaded && window.IsActive && !IsKeyboardFocusWithin) cancel.Focus();
        }

        internal void Dismiss(bool accepted)
        {
            if (closing) return;
            closing = true;
            card.IsHitTestVisible = false;
            Animate(false, accepted, () => closed(accepted));
        }

        private void Animate(bool opening, bool accepted, Action completed)
        {
            int duration = opening ? 210 : 160;
            var ease = opening ? Mini.Glass.Ease : ExitEase;
            double opacity = opening ? 1 : 0;
            double size = opening ? 1 : accepted ? .94 : .97;
            double y = opening ? 0 : accepted ? 3 : 6;
            var fade = window.Motion ? Mini.Glass.Transition(card.Opacity, opacity, duration, ease) : null;
            if (fade != null && completed != null) fade.Completed += (sender, args) => completed();
            card.Opacity = opacity;
            card.BeginAnimation(OpacityProperty, fade);
            var shade = window.Motion ? Mini.Glass.Transition(scrim.Opacity, opening ? .26 : 0, duration, ease) : null;
            scrim.Opacity = opening ? .26 : 0;
            scrim.BeginAnimation(OpacityProperty, shade);
            Move(scale, ScaleTransform.ScaleXProperty, size, duration, ease);
            Move(scale, ScaleTransform.ScaleYProperty, size, duration, ease);
            Move(lift, TranslateTransform.YProperty, y, duration, ease);
            if (background.Effect == blur) Move(blur, BlurEffect.RadiusProperty, opening ? 6 : 0, duration, ease);
            if (fade == null && completed != null) completed();
        }

        private void Move(Animatable target, DependencyProperty property, double value, int duration, KeySpline ease)
        {
            var animation = window.Motion ? Mini.Glass.Transition((double)target.GetValue(property), value, duration, ease) : null;
            target.SetValue(property, value);
            target.BeginAnimation(property, animation);
        }

        internal void Reset()
        {
            closing = true;
            card.BeginAnimation(OpacityProperty, null);
            scrim.BeginAnimation(OpacityProperty, null);
            blur.BeginAnimation(BlurEffect.RadiusProperty, null);
            background.Effect = previousEffect;
            background.IsHitTestVisible = true;
        }
    }
}
