using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MusicWidget.NativeApp
{
    internal sealed partial class LibraryPanel
    {
        private TrackDrag drag;

        private void StartDrag(PanelLibraryRow row, DisplayTrack item, double grabY)
        {
            if (drag != null || !item.Reorder || row.ActualHeight <= 0) return;
            var scroll = FirstVisual<ScrollViewer>(list);
            if (scroll == null || !rowsHost.CaptureMouse()) return;
            drag = new TrackDrag(this, row, item, scroll, grabY);
        }

        internal bool CancelDrag()
        {
            if (drag == null) return false;
            drag.Finish(false);
            return true;
        }

        private static IEnumerable<PanelLibraryRow> RealizedRows(ListBox list)
        {
            var panel = FirstVisual<VirtualizingStackPanel>(list);
            if (panel == null) yield break;
            foreach (UIElement container in panel.Children)
            {
                var row = FirstVisual<PanelLibraryRow>(container);
                if (row != null && row.Display != null) yield return row;
            }
        }

        private sealed class TrackDrag
        {
            private readonly LibraryPanel library;
            private readonly DisplayTrack source;
            private readonly ScrollViewer scroll;
            private readonly Grid area;
            private readonly Canvas overlay;
            private readonly TranslateTransform motion = new TranslateTransform();
            private readonly DispatcherTimer edgeScroll;
            private readonly double rowHeight;
            private readonly double rowPitch;
            private readonly double slotOrigin;
            private readonly double grabY;
            private int destination;
            private double edgeSpeed;

            internal TrackDrag(LibraryPanel library, PanelLibraryRow row, DisplayTrack source, ScrollViewer scroll, double grabY)
            {
                this.library = library;
                this.source = source;
                this.scroll = scroll;
                this.grabY = grabY;
                area = library.rowsHost;
                rowHeight = row.ActualHeight;
                rowPitch = rowHeight + row.Margin.Top + row.Margin.Bottom;
                var origin = row.TranslatePoint(new Point(), area);
                // Compute against the unshifted slots so an animating row never changes the drop target.
                slotOrigin = origin.Y - row.ReorderOffset + scroll.VerticalOffset - source.Index * rowPitch;
                destination = source.Index;
                var preview = new PanelLibraryRow
                {
                    DataContext = source, Width = row.ActualWidth, Height = rowHeight,
                    Margin = new Thickness(0), IsHitTestVisible = false
                };
                overlay = new Canvas { IsHitTestVisible = false, RenderTransform = motion };
                Canvas.SetLeft(preview, origin.X);
                overlay.Children.Add(preview);
                area.Children.Add(overlay);

                edgeScroll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
                edgeScroll.Tick += (sender, args) => scroll.ScrollToVerticalOffset(scroll.VerticalOffset + edgeSpeed);
                scroll.ScrollChanged += OnScroll;
                area.PreviewMouseMove += OnMove;
                area.PreviewMouseLeftButtonUp += OnRelease;
                area.PreviewMouseWheel += OnWheel;
                area.LostMouseCapture += OnCaptureLost;
                area.Cursor = Cursors.Arrow;
                library.list.IsHitTestVisible = false;
                Update(Mouse.GetPosition(area), true);
            }

            private void Update(Point point, bool refreshRows = false)
            {
                double top = point.Y - grabY;
                motion.Y = Math.Max(0, Math.Min(top, area.ActualHeight - rowHeight));
                double index = (top + scroll.VerticalOffset - slotOrigin) / rowPitch;
                int next = Math.Max(0, Math.Min(source.ListedTracks.Count - 1, (int)Math.Round(index)));
                if (refreshRows || next != destination)
                {
                    destination = next;
                    PositionRows(true);
                }

                const double edge = 35;
                edgeSpeed = point.Y < edge && scroll.VerticalOffset > 0 ? -Math.Min(18, (edge - point.Y) * .5) :
                    point.Y > area.ActualHeight - edge && scroll.VerticalOffset < scroll.ScrollableHeight
                        ? Math.Min(18, (point.Y - area.ActualHeight + edge) * .5) : 0;
                if (edgeSpeed == 0) edgeScroll.Stop();
                else if (!edgeScroll.IsEnabled) edgeScroll.Start();
            }

            private void PositionRows(bool dragging)
            {
                bool animate = library.window.Motion;
                foreach (var row in RealizedRows(library.list))
                {
                    int index = row.Display.Index;
                    row.Opacity = dragging && index == source.Index ? 0 : 1;
                    double offset = 0;
                    if (dragging && source.Index < index && index <= destination) offset = -rowPitch;
                    if (dragging && destination <= index && index < source.Index) offset = rowPitch;
                    row.ShiftTo(offset, animate);
                }
            }

            private void OnMove(object sender, MouseEventArgs args)
            {
                args.Handled = true;
                if (args.LeftButton != MouseButtonState.Pressed) Finish(false);
                else Update(args.GetPosition(area));
            }

            private void OnRelease(object sender, MouseButtonEventArgs args)
            {
                args.Handled = true;
                Point point = args.GetPosition(area);
                Update(point);
                Finish(new Rect(area.RenderSize).Contains(point));
            }

            private void OnWheel(object sender, MouseWheelEventArgs args)
            {
                args.Handled = true;
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - args.Delta / 120.0 * rowPitch * 3);
            }

            private void OnScroll(object sender, ScrollChangedEventArgs args)
            {
                if (args.VerticalChange != 0) Update(Mouse.GetPosition(area), true);
            }

            private void OnCaptureLost(object sender, MouseEventArgs args)
            {
                if (!area.IsMouseCaptured) Finish(false);
            }

            internal void Finish(bool drop)
            {
                library.drag = null;
                edgeScroll.Stop();
                scroll.ScrollChanged -= OnScroll;
                area.PreviewMouseMove -= OnMove;
                area.PreviewMouseLeftButtonUp -= OnRelease;
                area.PreviewMouseWheel -= OnWheel;
                area.LostMouseCapture -= OnCaptureLost;
                if (area.IsMouseCaptured) area.ReleaseMouseCapture();
                area.ClearValue(CursorProperty);
                library.list.IsHitTestVisible = true;
                double top = area.TranslatePoint(new Point(0, motion.Y), library.list).Y;
                area.Children.Remove(overlay);
                if (drop && destination != source.Index && library.CommitReorder(source.Track.Name,
                    source.ListedTracks[destination].Name, destination > source.Index, top)) return;
                PositionRows(false);
            }
        }
    }
}
