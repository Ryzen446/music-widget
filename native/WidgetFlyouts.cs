using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mini = MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal sealed partial class WidgetWindow
    {
        private static readonly DependencyProperty DesignProgressProperty = DependencyProperty.Register(
            "DesignProgress", typeof(double), typeof(WidgetWindow), new PropertyMetadata(0.0));
        private FrameworkElement floaterAnchor;
        private FrameworkElement floaterSurface;
        private Rect floaterBounds;
        private Rect floaterAnchorBounds;
        private double floaterRadius;
        private double floaterScale = 1;
        private bool floaterClosing;
        private bool floaterAbove;
        private bool floaterRightAligned;
        private FrameworkElement floaterPreferredFocus;
        private bool isMenu;
        private Rect lastPaneRegion = Rect.Empty;
        private Rect lastFloaterRegion = Rect.Empty;
        private Size lastRegionHostSize;
        private double lastRegionDpi;
        private double lastPaneRadius;
        private Mini.CardMaterial animatedFrame;
        private Rect transitionHost;
        private Rect designStart, designEnd;
        private double designStartRadius, animatedRadius;
        private FrameworkElement animatedPlayer;
        private TranslateTransform animatedPlayerShift;

        private void ApplyHostBounds()
        {
            hostBounds = paneBounds;
            if (animatedFrame != null) hostBounds.Union(transitionHost);
            if (floaterSurface != null) hostBounds.Union(floaterBounds);
            if (paneLayer != null)
            {
                paneLayer.Width = paneBounds.Width;
                paneLayer.Height = paneBounds.Height;
                Canvas.SetLeft(paneLayer, paneBounds.X - hostBounds.X);
                Canvas.SetTop(paneLayer, paneBounds.Y - hostBounds.Y);
            }
            if (floaterSurface != null)
            {
                Canvas.SetLeft(floaterSurface, floaterBounds.X - hostBounds.X);
                Canvas.SetTop(floaterSurface, floaterBounds.Y - hostBounds.Y);
            }
            Left = hostBounds.X;
            Top = hostBounds.Y;
            Width = hostBounds.Width;
            Height = hostBounds.Height;
            ApplyRegion();
        }

        private void ApplyRegion()
        {
            if (handle == IntPtr.Zero || paneBounds.Width < 1 || paneBounds.Height < 1) return;
            var pane = new Rect(paneBounds.X - hostBounds.X, paneBounds.Y - hostBounds.Y, paneBounds.Width, paneBounds.Height);
            var floater = FloaterVisualBounds();
            if (!floater.IsEmpty) floater.Offset(-hostBounds.X, -hostBounds.Y);
            double dpi = NativeBackdrop.GetWindowDpiScale(handle);
            if (animatedFrame != null)
                pane = RoundRegion((Rect)animatedFrame.GetValue(Mini.CardMaterial.AnimatedBoundsProperty), dpi);
            var hostSize = new Size(ActualWidth, ActualHeight);
            floater = RoundRegion(floater, dpi);
            double paneRadius = animatedFrame == null ? PlayerCornerRadius : animatedRadius;
            if (pane == lastPaneRegion && floater == lastFloaterRegion && hostSize == lastRegionHostSize &&
                dpi == lastRegionDpi && paneRadius == lastPaneRadius) return;
            double radius = floaterSurface != null && floaterSurface.RenderTransform is ScaleTransform
                ? floaterRadius * ((ScaleTransform)floaterSurface.RenderTransform).ScaleY : floaterRadius;
            if (NativeBackdrop.SetGlassRegion(handle, dpi, SurfaceRect(pane, paneRadius),
                floater.IsEmpty ? null : SurfaceRect(floater, radius)) != 0) return;
            lastPaneRegion = pane;
            lastFloaterRegion = floater;
            lastRegionHostSize = hostSize;
            lastRegionDpi = dpi;
            lastPaneRadius = paneRadius;
        }

        private Rect FloaterVisualBounds()
        {
            if (floaterSurface == null) return Rect.Empty;
            var scale = floaterSurface.RenderTransform as ScaleTransform;
            if (scale == null) return floaterBounds;
            var rect = scale.TransformBounds(new Rect(0, 0, floaterSurface.Width, floaterSurface.Height));
            rect.Scale(floaterScale, floaterScale);
            rect.Offset(floaterBounds.X, floaterBounds.Y);
            return rect;
        }

        private static Rect RoundRegion(Rect bounds, double dpi)
        {
            if (bounds.IsEmpty) return bounds;
            double left = Math.Floor(bounds.Left * dpi) / dpi;
            double top = Math.Floor(bounds.Top * dpi) / dpi;
            return new Rect(left, top, Math.Ceiling(bounds.Right * dpi) / dpi - left, Math.Ceiling(bounds.Bottom * dpi) / dpi - top);
        }

        private static double[] SurfaceRect(Rect bounds, double radius)
        {
            return new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height, radius };
        }

        private bool BeginDesignTransition(Size previousSize, double previousRadius, FrameworkElement view)
        {
            bool compact = Model.Settings.DesignMode == "Compact";
            var area = WorkArea(compact ? Model.Settings.CompactScreen : Model.Settings.MinimalScreen);
            if (previousSize.Width <= 0 || previousSize.Height <= 0) return false;
            double initialWidth = Math.Min(area.Width, previousSize.Width);
            double initialHeight = Math.Min(area.Height, previousSize.Height);
            double edge = paneBounds.Left - area.Left < 24 ? 0 : area.Right - paneBounds.Right < 24 ? 1 : .5;
            bool atBottom = area.Bottom - paneBounds.Bottom < 24;
            var initial = new Rect(
                Limit(paneBounds.X + (paneBounds.Width - initialWidth) * edge, area.Left, area.Right - initialWidth),
                Limit(atBottom ? paneBounds.Bottom - initialHeight : paneBounds.Y, area.Top, area.Bottom - initialHeight),
                initialWidth, initialHeight);
            if (initial == paneBounds) return false;

            transitionHost = Rect.Union(initial, paneBounds);
            designStart = initial;
            designEnd = paneBounds;
            designStart.Offset(-transitionHost.X, -transitionHost.Y);
            designEnd.Offset(-transitionHost.X, -transitionHost.Y);
            designStartRadius = previousRadius;
            var frame = animatedFrame = shell;
            paneLayer.Children.Remove(frame);
            frame.Width = transitionHost.Width;
            frame.Height = transitionHost.Height;
            glassHost.Children.Insert(0, frame);
            animatedPlayer = view;
            animatedPlayerShift = new TranslateTransform();
            view.RenderTransform = animatedPlayerShift;
            SetValue(DesignProgressProperty, 0.0);
            UpdateDesignFrame();
            ApplyHostBounds();

            var transition = Mini.Glass.Transition(0, 1, 265, new System.Windows.Media.Animation.KeySpline(.22, .75, .26, 1));
            transition.Completed += (sender, args) =>
            {
                if (animatedFrame == frame) EndDesignTransition();
            };
            BeginAnimation(DesignProgressProperty, transition);
            CompositionTarget.Rendering += RenderDesignRegion;
            return true;
        }

        private void UpdateDesignFrame()
        {
            double progress = (double)GetValue(DesignProgressProperty);
            var bounds = new Rect(
                Interpolate(designStart.X, designEnd.X, progress), Interpolate(designStart.Y, designEnd.Y, progress),
                Interpolate(designStart.Width, designEnd.Width, progress), Interpolate(designStart.Height, designEnd.Height, progress));
            animatedRadius = Math.Round(Interpolate(designStartRadius, PlayerCornerRadius, progress), 2);
            animatedFrame.SetValue(Mini.CardMaterial.AnimatedBoundsProperty, bounds);
            animatedFrame.SetRadius(animatedRadius);

            // Delay the full reveal until there is room for the incoming controls.
            double reveal = Limit((progress - .19) / .68, 0, 1);
            animatedPlayer.Opacity = reveal * reveal * (3 - 2 * reveal);
            animatedPlayerShift.X = (bounds.X - designEnd.X) * .5;
            animatedPlayerShift.Y = bounds.Y - designEnd.Y + 5 * (1 - progress);
        }

        private static double Interpolate(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }

        private void RenderDesignRegion(object sender, EventArgs args)
        {
            UpdateDesignFrame();
            ApplyRegion();
        }

        private void EndDesignTransition(bool restore = true)
        {
            var frame = animatedFrame;
            if (frame == null) return;
            var view = animatedPlayer;
            animatedFrame = null;
            animatedPlayer = null;
            animatedPlayerShift = null;
            CompositionTarget.Rendering -= RenderDesignRegion;
            BeginAnimation(DesignProgressProperty, null);
            ClearValue(DesignProgressProperty);
            frame.ClearValue(Mini.CardMaterial.AnimatedBoundsProperty);
            if (!restore) return;
            view.Opacity = 1;
            view.RenderTransform = Transform.Identity;
            frame.SetRadius(PlayerCornerRadius);
            glassHost.Children.Remove(frame);
            frame.ClearValue(WidthProperty);
            frame.ClearValue(HeightProperty);
            paneLayer.Children.Insert(0, frame);
            ApplyHostBounds();
        }

        internal void CloseFloater()
        {
            if (floaterSurface == null) return;
            var surface = floaterSurface;
            var trigger = floaterAnchor;
            floaterSurface = floaterAnchor = floaterPreferredFocus = null;
            floaterClosing = false;
            trigger.Unloaded -= AnchorUnloaded;
            CompositionTarget.Rendering -= RenderFloaterRegion;
            PanelKit.ResetPopoverAnimation(surface);
            glassHost.Children.Remove(surface);
            ApplyHostBounds();
        }

        internal void DismissFloater(bool restoreFocus)
        {
            if (floaterSurface == null || floaterClosing) return;
            var trigger = floaterAnchor;
            var surface = floaterSurface;
            if (surface.IsMouseCaptureWithin) Mouse.Capture(null);
            if ((restoreFocus || surface.IsKeyboardFocusWithin) && trigger.IsVisible && IsActive) trigger.Focus();
            if (!Motion || !IsVisible || Pane != "player")
            {
                CloseFloater();
                return;
            }
            floaterClosing = true;
            surface.IsHitTestVisible = false;
            KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.None);
            AnimateFloater(false);
        }

        private bool ToggleExistingFloater(FrameworkElement anchor)
        {
            if (floaterSurface == null || floaterAnchor != anchor) return false;
            if (floaterClosing)
            {
                floaterClosing = false;
                floaterSurface.IsHitTestVisible = true;
                KeyboardNavigation.SetTabNavigation(floaterSurface, KeyboardNavigationMode.Cycle);
                if (Motion) AnimateFloater(true);
                else
                {
                    PanelKit.ResetPopoverAnimation(floaterSurface);
                    ApplyRegion();
                }
                FocusFloater();
            }
            else DismissFloater(true);
            return true;
        }

        internal void ToggleFloater(FrameworkElement anchor, FrameworkElement surface, bool rightAligned = true,
            bool focusMenu = false, FrameworkElement preferredFocus = null)
        {
            EndDesignTransition();
            if (ToggleExistingFloater(anchor)) return;
            CloseFloater();
            if (!anchor.IsVisible || paneLayer == null) return;
            var area = WorkArea(Model.Settings.DesignMode == "Compact" ? Model.Settings.CompactScreen : Model.Settings.MinimalScreen);
            var target = anchor.TransformToAncestor(paneLayer).TransformBounds(new Rect(anchor.RenderSize));
            target.Offset(paneBounds.X, paneBounds.Y);
            var border = surface as Border;
            double radius = border == null ? 0 : border.CornerRadius.TopLeft;
            double zoom = Pane == "player" && renderedDesign == "Compact" ? Model.Settings.SizeScale : 1;
            double gap = Pane == "player" ? 8 : 5;
            if (Pane == "player" && renderedDesign == "Compact" && focusMenu)
                gap = -17 * zoom;
            double belowRoom = Math.Max(0, area.Bottom - target.Bottom - gap);
            double aboveRoom = Math.Max(0, target.Top - area.Top - gap);

            // Attach first so measure resolves the window's fonts, palette and templates.
            glassHost.Children.Add(surface);
            surface.UseLayoutRounding = UseLayoutRounding;
            if (zoom != 1) surface.LayoutTransform = new ScaleTransform(zoom, zoom);
            surface.MaxHeight = Math.Min(surface.MaxHeight, Math.Max(belowRoom, aboveRoom) / zoom);
            surface.MaxWidth = Math.Min(surface.MaxWidth, area.Width / zoom);
            surface.Measure(new Size(area.Width, Math.Max(belowRoom, aboveRoom)));
            var size = surface.DesiredSize;
            bool above = size.Height > belowRoom && aboveRoom > belowRoom;
            double width = Math.Max(1, size.Width), height = Math.Max(1, size.Height);
            double left = rightAligned ? target.Right - width : target.Left + (target.Width - width) / 2;
            left = Limit(left, area.Left, area.Right - width);
            double top = above ? target.Top - gap - height : target.Bottom + gap;
            floaterBounds = new Rect(left, Limit(top, area.Top, area.Bottom - height), width, height);
            floaterAnchorBounds = target;
            floaterAnchor = anchor;
            floaterSurface = surface;
            floaterRadius = radius * zoom;
            floaterScale = zoom;
            floaterAbove = above;
            floaterRightAligned = rightAligned;
            floaterPreferredFocus = preferredFocus;
            isMenu = focusMenu;
            anchor.Unloaded += AnchorUnloaded;
            KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.Cycle);
            surface.Width = width / zoom;
            surface.Height = height / zoom;
            if (Motion) AnimateFloater(true);
            ApplyHostBounds();
            FocusFloater();
        }

        private void FocusFloater()
        {
            if (!Mini.Glass.ShowKeyboardFocus(this)) return;
            var surface = floaterSurface;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (floaterSurface != surface || floaterClosing || !IsActive) return;
                if (floaterPreferredFocus == null || !floaterPreferredFocus.Focus())
                    surface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }));
        }

        private void AnimateFloater(bool opening)
        {
            var surface = floaterSurface;
            PanelKit.AnimatePopover(surface, floaterAbove, floaterRightAligned, opening, () =>
                {
                    if (floaterSurface != surface) return;
                    if (floaterClosing) CloseFloater();
                    else
                    {
                        CompositionTarget.Rendering -= RenderFloaterRegion;
                        ApplyRegion();
                    }
                });
            CompositionTarget.Rendering -= RenderFloaterRegion;
            CompositionTarget.Rendering += RenderFloaterRegion;
        }

        private void AnchorUnloaded(object sender, RoutedEventArgs args) { CloseFloater(); }

        private void RenderFloaterRegion(object sender, EventArgs args)
        {
            ApplyRegion();
        }

        private void HandlePointer(object sender, MouseButtonEventArgs args)
        {
            EndDesignTransition();
            SetValue(Mini.Glass.KeyboardFocusVisibleProperty, false);
            if (floaterSurface == null || floaterAnchor.IsMouseOver || floaterSurface.IsMouseCaptureWithin) return;
            var point = args.GetPosition(glassHost);
            point.Offset(hostBounds.X, hostBounds.Y);
            if (floaterAnchorBounds.Contains(point) && args.ChangedButton == MouseButton.Left)
            {
                if (floaterClosing) ToggleExistingFloater(floaterAnchor);
                else DismissFloater(true);
                args.Handled = true;
            }
            else if (!FloaterVisualBounds().Contains(point)) DismissFloater(false);
        }

        private void HandleFocusKey(object sender, KeyEventArgs args)
        {
            SetValue(Mini.Glass.KeyboardFocusVisibleProperty, true);
            if (args.Handled || shortcutCapture || floaterSurface == null || floaterClosing) return;
            if (args.Key == Key.Escape)
            {
                DismissFloater(true);
                args.Handled = true;
                return;
            }
            if (!isMenu && args.Key != Key.Tab) return;
            if (args.Key != Key.Up && args.Key != Key.Down && args.Key != Key.Home && args.Key != Key.End && args.Key != Key.Tab) return;
            bool reverse = args.Key == Key.Up || args.Key == Key.End ||
                (args.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            bool inside = floaterSurface.IsKeyboardFocusWithin;
            if (!inside || args.Key == Key.Home || args.Key == Key.End)
                floaterSurface.MoveFocus(new TraversalRequest(reverse ? FocusNavigationDirection.Last : FocusNavigationDirection.First));
            else
            {
                var focus = Keyboard.FocusedElement as UIElement;
                if (focus != null) focus.MoveFocus(new TraversalRequest(reverse ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            }
            args.Handled = true;
        }
    }
}
