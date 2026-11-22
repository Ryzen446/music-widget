using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal sealed class GlassSegments : Border
    {
        private static readonly DependencyProperty SelectionProgressProperty = DependencyProperty.Register(
            "SelectionProgress", typeof(double), typeof(GlassSegments),
            new PropertyMetadata(0.0, (owner, args) => ((GlassSegments)owner).UpdateIndicator()));
        private readonly WidgetWindow window;
        private readonly Border indicator;
        private readonly TranslateTransform shift = new TranslateTransform();
        private readonly ScaleTransform elasticity = new ScaleTransform();
        private readonly Grid segments = new Grid();
        private readonly Button[] buttons;
        private int selected;

        internal GlassSegments(WidgetWindow owner, string label, string first, string second, int initial,
            Action<int> change, string firstIcon = null, string secondIcon = null)
            : this(owner, label, new[] { first, second }, initial, change, new[] { firstIcon, secondIcon }) { }

        internal GlassSegments(WidgetWindow owner, string label, string[] labels, int initial,
            Action<int> change, string[] icons = null)
        {
            window = owner;
            selected = initial;
            PanelKit.StyleTrack(this);
            Child = segments;
            AutomationProperties.SetName(this, label);
            indicator = window.Glass.SelectionIndicator();
            indicator.Margin = new Thickness(2);
            indicator.RenderTransformOrigin = new Point(.5, .5);
            var motion = new TransformGroup();
            motion.Children.Add(elasticity);
            motion.Children.Add(shift);
            indicator.RenderTransform = motion;
            segments.Children.Add(indicator);
            buttons = new Button[labels.Length];
            for (int i = 0; i < buttons.Length; ++i)
            {
                int index = i;
                segments.ColumnDefinitions.Add(new ColumnDefinition());
                buttons[i] = window.Glass.Chip(labels[i], false, () => Select(index, change),
                    icons == null ? null : icons[i], labels.Length > 2 ? 11 : 12);
                if (labels.Length > 2) buttons[i].Padding = new Thickness(5, 0, 5, 0);
                buttons[i].Margin = new Thickness(2);
                buttons[i].Height = 34;
                Grid.SetColumn(buttons[i], i);
                segments.Children.Add(buttons[i]);
            }
            SetPosition(selected, false);
            segments.SizeChanged += (sender, args) => UpdateIndicator();
        }

        private void Select(int value, Action<int> change)
        {
            if (selected == value) return;
            selected = value;
            SetPosition(value, true);
            change(value);
        }

        internal void EnterFrom(int previous)
        {
            if (previous == selected) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!IsVisible) return;
                SetPosition(previous, false);
                SetPosition(selected, true);
                if (Glass.ShowKeyboardFocus(window) && window.IsActive) buttons[selected].Focus();
            }));
        }

        private void SetPosition(int index, bool animate)
        {
            double old = (double)GetValue(SelectionProgressProperty);
            SetValue(SelectionProgressProperty, (double)index);
            BeginAnimation(SelectionProgressProperty, animate && window.Motion && old != index
                ? Glass.Transition(old, index, 520, Glass.Spring) : null);
        }

        private void UpdateIndicator()
        {
            double progress = (double)GetValue(SelectionProgressProperty);
            double position = Math.Max(0, Math.Min(buttons.Length - 1, progress));
            shift.X = position * segments.ColumnDefinitions[0].ActualWidth;
            double overshoot = Math.Abs(progress - position);
            elasticity.ScaleX = 1 - overshoot * .28;
            elasticity.ScaleY = 1 + overshoot * .14;
        }
    }
}
