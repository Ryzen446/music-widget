using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Condition = System.Windows.Condition;
using Mini = MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal sealed partial class WidgetWindow
    {
        private TextBlock songTitle;
        private TextBlock subtitle;
        private TextBlock time;
        private TextBlock speed;
        private TextBlock volumeLabel;
        private TextBlock popupVolumeLabel;
        private TextBlock repeatOne;
        private Mini.GlassRange seekBar;
        private Mini.GlassRange volumeBar;
        private Mini.GlassRange popupVolumeBar;
        private Mini.GlassButton playButton;
        private Mini.GlassButton muteButton;
        private Mini.GlassButton popupMuteButton;
        private Mini.GlassButton shuffleButton;
        private Mini.GlassButton repeatButton;
        private ScaleTransform[] meter;
        private Grid titleViewport;
        private TranslateTransform titleShift;
        private bool marqueeRunning;
        private bool isUpdatingPlayback;
        private bool? lastPlaying;
        private bool? lastMute;
        private double resizeScale;

        private FrameworkElement BuildPlayer()
        {
            renderedDesign = Model.Settings.DesignMode;
            return renderedDesign == "Compact" ? BuildCompact() : BuildMinimal();
        }

        private Grid BuildMinimal()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11) });
            var row = new Grid { Margin = new Thickness(12, 3, 12, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            for (int column = 0; column < 4; ++column)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

            titleViewport = new Grid { ClipToBounds = true };
            titleShift = new TranslateTransform();
            songTitle = Glass.Text("", 12, true);
            songTitle.RenderTransform = titleShift;
            songTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            songTitle.HorizontalAlignment = HorizontalAlignment.Left;
            titleViewport.Children.Add(songTitle);
            titleViewport.SizeChanged += (sender, e) =>
            {
                UpdateMarquee(false);
                UpdateMarquee(Motion && (IsMouseOver || IsActive));
            };
            row.Children.Add(titleViewport);

            const double buttonSize = 28;
            muteButton = Glass.IconButton("volume", T("adjustVolume"), () => ShowMiniVolume(muteButton), buttonSize);
            playButton = Glass.IconButton("play", T("play"), PlayPause, buttonSize);
            var next = Glass.IconButton("next", T("next"), Next, buttonSize);
            Mini.GlassButton options = null;
            options = Glass.IconButton("more", T("options"), () => ShowOptions(options), buttonSize);
            AddMiniButton(row, muteButton, 1);
            AddMiniButton(row, playButton, 2);
            AddMiniButton(row, next, 3);
            AddMiniButton(row, options, 4);
            row.MouseRightButtonUp += (sender, e) => { e.Handled = true; ShowOptions(options); };
            root.Children.Add(row);

            seekBar = Glass.Range(0, 1, 0, HandleSeek, true);
            AutomationProperties.SetName(seekBar, T("trackPosition"));
            Grid.SetRow(seekBar, 1);
            root.Children.Add(seekBar);
            return root;
        }

        private static void AddMiniButton(Grid row, Button button, int column)
        {
            Grid.SetColumn(button, column);
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(button);
        }

        private FrameworkElement BuildCompact()
        {
            var root = new Grid
            {
                Width = 400, Height = 260, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                LayoutTransform = new ScaleTransform(Model.Settings.SizeScale, Model.Settings.SizeScale)
            };
            var layout = new Grid { Margin = new Thickness(18, 17, 18, 12) };
            foreach (var height in new[] { 44.0, 25.0, 20.0, 58.0, 43.0, 33.0 })
                layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
            root.Children.Add(layout);

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            top.Background = Brushes.Transparent;
            top.Cursor = Cursors.SizeAll;
            top.MouseLeftButtonDown += (sender, e) => DragCompact(e);
            top.Children.Add(BuildVisualizer());
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            songTitle = Glass.Text("", 14, true);
            songTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            subtitle = Glass.Hint("", 10);
            subtitle.Margin = new Thickness(0, 5, 0, 0);
            words.Children.Add(songTitle);
            words.Children.Add(subtitle);
            Grid.SetColumn(words, 1);
            top.Children.Add(words);
            Mini.GlassButton options = null;
            options = Glass.IconButton("more", T("options"), () => ShowOptions(options), 28);
            options.HorizontalAlignment = HorizontalAlignment.Right;
            options.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(options, 2);
            top.Children.Add(options);
            top.MouseRightButtonUp += (sender, e) => { e.Handled = true; ShowOptions(options); };
            AddRow(layout, top, 0);

            time = Glass.Hint("", 10);
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            time.VerticalAlignment = VerticalAlignment.Bottom;
            AddRow(layout, time, 1);
            seekBar = Glass.Slider(0, 1, 0, HandleSeek);
            AutomationProperties.SetName(seekBar, T("trackPosition"));
            AddRow(layout, seekBar, 2);

            var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            transport.RenderTransform = new TranslateTransform(0, 5);
            shuffleButton = Glass.IconButton("shuffle", T("shuffle"), () => { Model.Settings.IsShuffle = !Model.Settings.IsShuffle; Model.ApplySettings(); }, 44);
            playButton = Glass.IconButton("play", T("play"), PlayPause, 45);
            repeatButton = Glass.IconButton("repeat", T("repeat"), () => { Model.Settings.RepeatMode = (Model.Settings.RepeatMode + 1) % 3; Model.ApplySettings(); }, 44);
            var repeatWrap = new Grid();
            repeatWrap.Children.Add(repeatButton);
            repeatOne = Glass.Text("1", 8);
            repeatOne.HorizontalAlignment = HorizontalAlignment.Left;
            repeatOne.VerticalAlignment = VerticalAlignment.Top;
            repeatOne.Margin = new Thickness(10, 6, 0, 0);
            repeatOne.IsHitTestVisible = false;
            repeatWrap.Children.Add(repeatOne);
            foreach (var control in new FrameworkElement[] {
                shuffleButton, Glass.IconButton("prev", T("previous"), Previous, 44), playButton,
                Glass.IconButton("next", T("next"), Next, 44), repeatWrap })
            {
                control.Margin = new Thickness(6, 0, 6, 0);
                transport.Children.Add(control);
            }
            AddRow(layout, transport, 3);
            var volume = BuildVolume(false);
            volume.RenderTransform = new TranslateTransform(0, 2);
            AddRow(layout, volume, 4);
            var footer = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
            Button library = Glass.Button(T("library"), () => Navigate("library"), "library", 10, 13);
            library.MinHeight = 25;
            library.Padding = new Thickness(14.4, 0, 14.4, 0);
            footer.Children.Add(library);
            speed = Glass.Hint("", 11);
            speed.Margin = new Thickness(3, 6, 26, 0);
            DockPanel.SetDock(speed, Dock.Right);
            footer.Children.Add(speed);
            AddRow(layout, footer, 5);

            var resize = new Thumb
            {
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 7, 7), Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Focusable = true,
                FocusVisualStyle = null
            };
            var resizeFace = new FrameworkElementFactory(typeof(Border), "ResizeFace");
            resizeFace.SetResourceReference(Border.BackgroundProperty, GlassUi.Rest);
            resizeFace.SetValue(Border.BorderBrushProperty, Mini.Glass.Gradient(Color.FromArgb(128, 255, 255, 255),
                Color.FromArgb(53, 255, 255, 255), new Point(0, 0), new Point(.9, 1)));
            resizeFace.SetValue(Border.BorderThicknessProperty, new Thickness(.6));
            resizeFace.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            var sizeGlyph = new FrameworkElementFactory(typeof(Path));
            sizeGlyph.SetValue(Path.DataProperty, Mini.GlassButton.GetIcon("resize"));
            sizeGlyph.SetValue(Path.StrokeProperty, Mini.Glass.InkBrush);
            sizeGlyph.SetValue(Path.StrokeThicknessProperty, 1.1);
            sizeGlyph.SetValue(Path.StretchProperty, Stretch.Uniform);
            sizeGlyph.SetValue(FrameworkElement.WidthProperty, 9.0);
            sizeGlyph.SetValue(FrameworkElement.HeightProperty, 9.0);
            sizeGlyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            sizeGlyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            resizeFace.AppendChild(sizeGlyph);
            var resizeTemplate = new ControlTemplate(typeof(Thumb)) { VisualTree = resizeFace };
            var resizeFocus = new MultiTrigger();
            resizeFocus.Conditions.Add(new Condition(IsKeyboardFocusedProperty, true));
            resizeFocus.Conditions.Add(new Condition(Mini.Glass.KeyboardFocusVisibleProperty, true));
            resizeFocus.Setters.Add(new Setter(Border.BorderBrushProperty, Mini.Glass.InkBrush, "ResizeFace"));
            resizeTemplate.Triggers.Add(resizeFocus);
            resize.Template = resizeTemplate;
            AutomationProperties.SetName(resize, T("resize"));
            resize.DragStarted += (sender, e) => resizeScale = Model.Settings.SizeScale;
            resize.DragDelta += (sender, e) =>
            {
                resizeScale = Limit(resizeScale + (e.HorizontalChange + e.VerticalChange * .7) * Model.Settings.SizeScale / 400, .8, 1.5);
                Model.Settings.SizeScale = resizeScale;
                ((ScaleTransform)root.LayoutTransform).ScaleX = ((ScaleTransform)root.LayoutTransform).ScaleY = resizeScale;
                ApplyWindowBounds();
            };
            resize.DragCompleted += (sender, e) => Model.ApplySettings();
            resize.KeyDown += (sender, e) =>
            {
                if (e.Key != Key.Right && e.Key != Key.Up && e.Key != Key.Left && e.Key != Key.Down) return;
                Model.Settings.SizeScale = Limit(Model.Settings.SizeScale + (e.Key == Key.Right || e.Key == Key.Up ? .05 : -.05), .8, 1.5);
                ApplySettings();
                e.Handled = true;
            };
            root.Children.Add(resize);
            return root;
        }

        private FrameworkElement BuildVisualizer()
        {
            var material = new Grid
            {
                Width = 43, Height = 43, HorizontalAlignment = HorizontalAlignment.Left
            };
            material.Children.Add(Glass.Card(true, 14));
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            meter = new ScaleTransform[5];
            for (int i = 0; i < meter.Length; ++i)
            {
                meter[i] = new ScaleTransform(1, .13);
                var bar = new Rectangle
                {
                    Width = 3.5, Height = 25, RadiusX = 1.75, RadiusY = 1.75, Margin = new Thickness(1.2, 0, 1.2, 0),
                    RenderTransformOrigin = new Point(.5, .5), RenderTransform = meter[i]
                };
                bar.SetResourceReference(Shape.FillProperty, GlassUi.VisualizerBrushKey);
                line.Children.Add(bar);
            }
            material.Children.Add(line);
            return material;
        }

        private static void AddRow(Grid layout, FrameworkElement child, int index)
        {
            Grid.SetRow(child, index);
            layout.Children.Add(child);
        }

        private Grid BuildVolume(bool popup)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(popup ? 30 : 36) });
            var button = Glass.IconButton("volume", T("mute"), Model.ToggleMute, 28);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(button);
            var slider = Glass.Slider(0, 100, Model.Settings.LocalVolume * 100, value =>
            {
                if (!isUpdatingPlayback) Model.SetVolume(value / 100);
            });
            slider.PreciseLowEnd = true;
            slider.TickFrequency = 1;
            slider.IsSnapToTickEnabled = true;
            slider.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(slider, T("volume"));
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            var label = Glass.Hint("", 10);
            Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(label, 2);
            row.Children.Add(label);
            if (popup) { popupVolumeBar = slider; popupVolumeLabel = label; popupMuteButton = button; }
            else { volumeBar = slider; volumeLabel = label; muteButton = button; }
            return row;
        }

        private void ShowMiniVolume(FrameworkElement anchor)
        {
            if (ToggleExistingFloater(anchor)) return;
            var group = new StackPanel { Width = 180 };
            var caption = Glass.Text(T("volume"), 11, true);
            caption.Height = 22;
            caption.Margin = new Thickness(2, 0, 0, 0);
            group.Children.Add(caption);
            group.Children.Add(BuildVolume(true));
            var slider = popupVolumeBar;
            group.Unloaded += (sender, args) =>
            {
                if (popupVolumeBar != slider) return;
                popupVolumeBar = null;
                popupVolumeLabel = null;
                popupMuteButton = null;
            };
            ToggleFloater(anchor, Glass.Flyout(group, 18, new Thickness(14, 14, 14, 13), volume: true), false, preferredFocus: slider);
            RefreshPlayback();
        }

        private void ShowOptions(FrameworkElement anchor)
        {
            if (ToggleExistingFloater(anchor)) return;
            var rows = new StackPanel { MinWidth = 220 };
            rows.Children.Add(Glass.MenuRow(T("appearance"), "settings", () => Navigate("settings"), true));
            rows.Children.Add(Glass.MenuRow(T("library"), "library", () => Navigate("library"), true));
            rows.Children.Add(Glass.MenuRow(T("next"), "next", () => { DismissFloater(false); Next(); }));
            rows.Children.Add(Glass.MenuRow(T("addMusic"), "plus", OpenAddMusic));
            rows.Children.Add(Glass.MenuRow(T("hideWidget"), "hide", HidePlayer));
            for (int index = 0; index < rows.Children.Count - 1; ++index)
                ((FrameworkElement)rows.Children[index]).Margin = new Thickness(0, 0, 0, 1);
            ToggleFloater(anchor, Glass.Flyout(rows, 26, new Thickness(6)), focusMenu: true);
        }

        private void HandleSeek(double value)
        {
            if (!isUpdatingPlayback && Model.MediaReady) Model.Seek(value);
        }

        private void RefreshPlayback()
        {
            if (Pane != "player" || songTitle == null) return;
            var settings = Model.Settings;
            var title = Model.CurrentTrack == null ? T("emptyTitle") : Model.CurrentTrack.Title;
            if (songTitle.Text != title)
            {
                UpdateMarquee(false);
                songTitle.Text = title;
                Glass.SetTip(songTitle, title, 10);
                AutomationProperties.SetName(songTitle, title);
                UpdateMarquee(Motion && (IsActive || IsMouseOver));
            }
            bool silent = settings.IsMuted || settings.LocalVolume <= 0;
            if (lastPlaying != Model.IsPlaying)
            {
                playButton.SetIcon(Model.IsPlaying ? "pause" : "play");
                UpdateHint(playButton, Model.IsPlaying ? "pause" : "play");
                lastPlaying = Model.IsPlaying;
            }
            if (lastMute != silent)
            {
                muteButton.SetIcon(silent ? "muted" : "volume");
                if (renderedDesign == "Compact") UpdateHint(muteButton, silent ? "unmute" : "mute");
                lastMute = silent;
            }
            if (popupMuteButton != null)
            {
                popupMuteButton.SetIcon(silent ? "muted" : "volume");
                UpdateHint(popupMuteButton, silent ? "unmute" : "mute");
            }
            if (subtitle != null)
            {
                subtitle.Text = !String.IsNullOrEmpty(Model.PlaybackError) ? T(Model.PlaybackError) :
                    Model.QueueCount == 0 ? T("localLibrary") :
                    T("trackLabel", "index", (Model.QueueIndex + 1).ToString(CultureInfo.CurrentCulture)).Replace("{count}", Model.QueueCount.ToString(CultureInfo.CurrentCulture));
                time.Text = ClockLabel(Model.Position) + " / " + ClockLabel(Model.Duration);
                speed.Text = settings.PlaybackSpeed.ToString("0.##", CultureInfo.CurrentCulture) + "\u00d7" +
                    (Model.SleepMinutes > 0 ? " \u00b7 " + T("sleepRemaining", "minutes", Model.SleepMinutes.ToString(CultureInfo.CurrentCulture)) : "");
                shuffleButton.Opacity = settings.IsShuffle ? 1 : .62;
                repeatButton.Opacity = settings.RepeatMode == 2 ? .62 : 1;
                repeatOne.Visibility = settings.RepeatMode == 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            isUpdatingPlayback = true;
            try
            {
                seekBar.IsEnabled = Model.MediaReady && Model.Duration > 0;
                seekBar.Maximum = Math.Max(1, Model.Duration);
                if (!seekBar.IsMouseCaptured)
                {
                    seekBar.AnimateProgress = Model.IsPlaying;
                    seekBar.Value = Limit(Model.Position, 0, seekBar.Maximum);
                }
                string volumeText = settings.IsMuted ? T("muted") : Math.Round(settings.LocalVolume * 100) + "%";
                if (volumeBar != null) { volumeBar.Value = settings.LocalVolume * 100; volumeLabel.Text = volumeText; }
                if (popupVolumeBar != null) { popupVolumeBar.Value = settings.LocalVolume * 100; popupVolumeLabel.Text = volumeText; }
            }
            finally { isUpdatingPlayback = false; }
        }

        private void UpdateHint(Mini.GlassButton button, string key)
        {
            string label = T(key);
            if (AutomationProperties.GetName(button) == label) return;
            AutomationProperties.SetName(button, label);
            Glass.SetTip(button, label, 10);
        }

        private static string ClockLabel(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? ((int)span.TotalHours).ToString() + span.ToString("\\:mm\\:ss") : span.ToString("m\\:ss");
        }

        private void UpdateBars(double[] bands)
        {
            if (meter == null) return;
            for (int i = 0; i < meter.Length; ++i)
                meter[i].ScaleY = bands == null || i >= bands.Length ? .13 : Limit(bands[i], .13, 1);
        }

        private void UpdateMarquee(bool play)
        {
            if (titleViewport == null || songTitle == null || titleShift == null || play == marqueeRunning) return;
            if (!play)
            {
                titleShift.BeginAnimation(TranslateTransform.XProperty, null);
                titleShift.X = 0;
                songTitle.TextTrimming = TextTrimming.CharacterEllipsis;
                songTitle.Width = Double.NaN;
                marqueeRunning = false;
                return;
            }
            if (titleViewport.ActualWidth <= 0 || String.IsNullOrEmpty(songTitle.Text)) return;
            var formatted = new FormattedText(songTitle.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Mini.Glass.Typeface, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), songTitle.FontSize, Mini.Glass.InkBrush,
                VisualTreeHelper.GetDpi(songTitle).PixelsPerDip);
            if (formatted.Width <= titleViewport.ActualWidth + 1) return;
            double difference = formatted.Width - titleViewport.ActualWidth + 3;
            songTitle.Width = formatted.Width + 3;
            songTitle.TextTrimming = TextTrimming.None;
            var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            double seconds = Math.Min(18, 2 + difference / 30);
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.9))));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-difference, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-difference, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds + .9))));
            titleShift.BeginAnimation(TranslateTransform.XProperty, animation);
            marqueeRunning = true;
        }

        private void DetachPlaybackControls()
        {
            UpdateMarquee(false);
            if (seekBar != null) seekBar.StopProgressAnimation();
            songTitle = subtitle = time = speed = volumeLabel = popupVolumeLabel = repeatOne = null;
            seekBar = volumeBar = popupVolumeBar = null;
            playButton = muteButton = popupMuteButton = shuffleButton = repeatButton = null;
            meter = null;
            titleViewport = null;
            titleShift = null;
            lastPlaying = lastMute = null;
        }
    }
}
