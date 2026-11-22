using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MusicWidget.NativeMini
{
    internal sealed class LaunchOptions
    {
        internal string ReadyFile;
        internal string StateFile;
        internal string Title;
        internal bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
        internal bool AutoPlay;
        internal bool NoTray;
        internal Color Aura = Color.FromRgb(220, 228, 239);
        internal Color? Background;
        internal double? Left;
        internal double? Top;
        internal double Progress;
        internal readonly List<string> Tracks = new List<string>();

        private static Color ReadColor(string value)
        {
            if (value.Length != 7 || value[0] != '#') throw new FormatException("Use a #RRGGBB color.");
            return (Color)ColorConverter.ConvertFromString(value);
        }

        private static double ReadNumber(string value)
        {
            double number = double.Parse(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(number) || double.IsInfinity(number))
                throw new FormatException("Use a finite number.");
            return number;
        }

        internal static LaunchOptions Parse(string[] args)
        {
            var options = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (name == "--play") { options.AutoPlay = true; continue; }
                if (name == "--no-tray") { options.NoTray = true; continue; }
                if (i + 1 == args.Length) throw new ArgumentException("Missing value for " + name);
                string value = args[++i];
                switch (name)
                {
                    case "--ready-file": options.ReadyFile = Path.GetFullPath(value); break;
                    case "--state-file": options.StateFile = Path.GetFullPath(value); break;
                    case "--track": options.Tracks.Add(Path.GetFullPath(value)); break;
                    case "--title": options.Title = value; break;
                    case "--aura": options.Aura = ReadColor(value); break;
                    case "--background": options.Background = ReadColor(value); break;
                    case "--left": options.Left = ReadNumber(value); break;
                    case "--top": options.Top = ReadNumber(value); break;
                    case "--progress": options.Progress = Math.Max(0, Math.Min(1, ReadNumber(value))); break;
                    case "--language":
                        if (value != "en" && value != "fr") throw new ArgumentException("Use en or fr.");
                        options.French = value == "fr";
                        break;
                    default: throw new ArgumentException("Unknown option " + name);
                }
            }
            return options;
        }
    }

    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var startupClock = Stopwatch.StartNew();
            try
            {
                var options = LaunchOptions.Parse(args);
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                return app.Run(new MiniPlayer(options, startupClock));
            }
            catch (Exception error)
            {
                for (int i = 0; i + 1 < args.Length; i++)
                {
                    if (args[i] != "--ready-file") continue;
                    File.WriteAllText(args[i + 1] + ".error", error.ToString(), new UTF8Encoding(false));
                    return 1;
                }
                MessageBox.Show(error.Message, "Music Widget Native Mini", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }

    internal sealed class MiniPlayer : Window
    {
        private const int PlayerWidth = 350;
        private const int PlayerHeight = 52;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly LaunchOptions options;
        private readonly Stopwatch startupClock;
        private readonly MediaPlayer player = new MediaPlayer();
        private readonly DispatcherTimer progressClock;
        private readonly List<string> tracks = new List<string>();
        private readonly Forms.NotifyIcon tray;
        private readonly System.Drawing.Icon trayIcon;
        private CardMaterial material;
        private TextBlock titleLabel;
        private GlassButton playButton;
        private GlassButton volumeButton;
        private GlassButton optionsButton;
        private GlassRange progress;
        private Popup popup;
        private IntPtr handle;
        private bool acrylic;
        private bool ready;
        private bool mediaReady;
        private bool seeking;
        private bool playing;
        private bool muted;
        private double volume = 0.2;
        private int trackIndex = -1;
        private string mediaFailure;

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        private static extern int DwmFlush();

        internal MiniPlayer(LaunchOptions options, Stopwatch startupClock)
        {
            this.options = options;
            this.startupClock = startupClock;
            Title = "Music Widget Native Mini";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Transparent;
            Width = PlayerWidth;
            Height = PlayerHeight;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            Left = options.Left ?? (SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - PlayerWidth) / 2);
            Top = options.Top ?? (SystemParameters.WorkArea.Top + 16);
            tracks.AddRange(options.Tracks);
            CreateView();
            SourceInitialized += InitializeBackdrop;
            ContentRendered += FirstFrame;
            IsVisibleChanged += (sender, args) => UpdateProgressTimer();
            PreviewKeyDown += HandleKey;
            player.Volume = volume;
            player.MediaOpened += (sender, args) =>
            {
                mediaReady = true;
                mediaFailure = null;
                progress.IsEnabled = player.NaturalDuration.HasTimeSpan;
                UpdateProgress();
                WriteState();
            };
            player.MediaEnded += (sender, args) => OpenTrack((trackIndex + 1) % tracks.Count, true);
            player.MediaFailed += (sender, args) =>
            {
                mediaFailure = args.ErrorException == null ? "Media failed." : args.ErrorException.Message;
                mediaReady = false;
                progress.IsEnabled = false;
                SetPlaying(false);
            };
            progressClock = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };
            progressClock.Tick += (sender, args) => UpdateProgress();
            if (!options.NoTray)
            {
                trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add(Local("Afficher le widget", "Show widget"), null, (sender, args) => Show());
                menu.Items.Add(Local("Quitter", "Exit"), null, (sender, args) => Close());
                tray = new Forms.NotifyIcon
                {
                    Icon = trayIcon, Text = Title, ContextMenuStrip = menu, Visible = true
                };
                tray.DoubleClick += (sender, args) => Show();
            }
            Closed += (sender, args) =>
            {
                progressClock.Stop();
                player.Close();
                if (tray != null)
                {
                    tray.Visible = false;
                    tray.ContextMenuStrip.Dispose();
                    tray.Dispose();
                    if (trayIcon != null) trayIcon.Dispose();
                }
            };
        }

        private string Local(string french, string english)
        {
            return options.French ? french : english;
        }

        private string GetTitle()
        {
            if (trackIndex >= 0) return Path.GetFileNameWithoutExtension(tracks[trackIndex]);
            if (options.Title != null) return options.Title;
            return tracks.Count == 0 ? Local("Votre musique, ici.", "Your music, here.") :
                Path.GetFileNameWithoutExtension(tracks[0]);
        }

        private void CreateView()
        {
            var card = new Grid
            {
                Background = Brushes.Transparent,
                Clip = new RectangleGeometry(new Rect(0, 0, PlayerWidth, PlayerHeight), 8, 8)
            };
            material = new CardMaterial(options.Aura, options.Background);
            material.SetBackdrop(acrylic);
            Grid.SetRowSpan(material, 2);
            card.Children.Add(material);
            card.RowDefinitions.Add(new RowDefinition());
            card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11) });
            var controls = new Grid { Margin = new Thickness(12, 3, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            controls.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++)
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

            titleLabel = MakeText(GetTitle(), 12);
            titleLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            titleLabel.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(titleLabel, titleLabel.Text);
            controls.Children.Add(titleLabel);
            volumeButton = AddButton(controls, 1, muted || volume == 0 ? "muted" : "volume", 13,
                Local("Régler le volume", "Adjust volume"), ShowVolume);
            playButton = AddButton(controls, 2, playing ? "pause" : "play", 16, PlayLabel(), TogglePlayback);
            AddButton(controls, 3, "next", 16, Local("Morceau suivant", "Next track"), NextTrack);
            optionsButton = AddButton(controls, 4, "more", 16, "Options", ShowMenu);
            card.Children.Add(controls);

            progress = new GlassRange(options.Aura, true) { IsEnabled = mediaReady, Value = options.Progress };
            AutomationProperties.SetName(progress, Local("Position de lecture", "Playback position"));
            progress.ValueChanged += (sender, args) =>
            {
                if (seeking || !mediaReady || !player.NaturalDuration.HasTimeSpan) return;
                player.Position = TimeSpan.FromTicks((long)(player.NaturalDuration.TimeSpan.Ticks * args.NewValue));
                WriteState();
            };
            Grid.SetRow(progress, 1);
            card.Children.Add(progress);
            Content = card;
        }

        private GlassButton AddButton(Grid row, int column, string glyph, int glyphSize, string name, Action action)
        {
            var button = new GlassButton(options.Aura, glyph, glyphSize) { HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
            button.Click += (sender, args) => action();
            Grid.SetColumn(button, column);
            row.Children.Add(button);
            return button;
        }

        private TextBlock MakeText(string text, double size)
        {
            return new TextBlock
            {
                Text = text, FontFamily = Glass.Typeface, FontWeight = FontWeights.SemiBold, FontSize = size,
                Foreground = Glass.InkBrush
            };
        }

        private void InitializeBackdrop(object sender, EventArgs args)
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            handle = source.Handle;
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var transparency = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency", 1);
            bool useBackdrop = !options.Background.HasValue && !SystemParameters.HighContrast && !Equals(transparency, 0);
            if (useBackdrop && NativeBackdrop.Apply(handle, true) >= 0)
                acrylic = NativeBackdrop.TrySetPlayerAcrylic(handle, true);
            material.SetBackdrop(acrylic || (useBackdrop && NativeBackdrop.LastAppliedGlass));
            UpdateShape();
            source.DpiChanged += (from, change) => Dispatcher.BeginInvoke(new Action(UpdateShape), DispatcherPriority.Loaded);
        }

        private void UpdateShape()
        {
            if (handle != IntPtr.Zero)
                NativeBackdrop.SetGlassRegion(handle, NativeBackdrop.GetWindowDpiScale(handle),
                    new double[] { 0, 0, PlayerWidth, PlayerHeight, 8 }, null);
        }

        private void FirstFrame(object sender, EventArgs args)
        {
            if (ready) return;
            ready = true;
            if (options.ReadyFile != null)
            {
                int composition = DwmFlush();
                string report = string.Format(CultureInfo.InvariantCulture,
                    "{{\"pid\":{0},\"elapsedMs\":{1:F2},\"utc\":\"{2:O}\",\"hwnd\":{3},\"dwmFlush\":{4},\"acrylic\":{5},\"backdropType\":{6},\"width\":{7},\"height\":{8},\"regionResult\":{9}}}",
                    Process.GetCurrentProcess().Id, startupClock.Elapsed.TotalMilliseconds, DateTime.UtcNow,
                    handle.ToInt64(), composition, acrylic ? "true" : "false", NativeBackdrop.GetBackdropType(handle), ActualWidth, ActualHeight,
                    NativeBackdrop.LastRegionResult);
                WriteFile(options.ReadyFile, report);
            }
            if (tracks.Count > 0) OpenTrack(0, options.AutoPlay);
            else WriteState();
        }

        private static void WriteFile(string path, string text)
        {
            // Readers never observe half a readiness or state message.
            string pending = path + ".tmp";
            File.WriteAllText(pending, text, Utf8);
            if (File.Exists(path)) File.Replace(pending, path, null);
            else File.Move(pending, path);
        }

        private static string JsonString(string value)
        {
            if (value == null) return "null";
            var result = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') result.Append('\\').Append(c);
                else if (c < ' ') result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }

        private void WriteState()
        {
            if (options.StateFile == null) return;
            double duration = mediaReady && player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0;
            WriteFile(options.StateFile, string.Format(CultureInfo.InvariantCulture,
                "{{\"title\":{0},\"playing\":{1},\"durationSeconds\":{2:F3},\"positionSeconds\":{3:F3},\"volume\":{4:F2},\"muted\":{5},\"error\":{6}}}",
                JsonString(GetTitle()), playing ? "true" : "false", duration, mediaReady ? player.Position.TotalSeconds : 0,
                volume, muted ? "true" : "false", JsonString(mediaFailure)));
        }

        private void OpenTrack(int index, bool start)
        {
            if (index < 0 || index >= tracks.Count) return;
            trackIndex = index;
            mediaReady = false;
            mediaFailure = null;
            player.Close();
            titleLabel.Text = GetTitle();
            ToolTipService.SetToolTip(titleLabel, titleLabel.Text);
            progress.IsEnabled = false;
            seeking = true;
            progress.Value = 0;
            seeking = false;
            player.Open(new Uri(tracks[index], UriKind.Absolute));
            SetPlaying(start);
        }

        private string PlayLabel()
        {
            return playing ? Local("Mettre en pause", "Pause") : Local("Lancer la lecture", "Play");
        }

        private void SetPlaying(bool value)
        {
            playing = value;
            if (playing) player.Play();
            else player.Pause();
            playButton.SetIcon(playing ? "pause" : "play");
            AutomationProperties.SetName(playButton, PlayLabel());
            ToolTipService.SetToolTip(playButton, PlayLabel());
            UpdateProgressTimer();
            WriteState();
        }

        private void TogglePlayback()
        {
            if (trackIndex >= 0) SetPlaying(!playing);
            else if (tracks.Count > 0) OpenTrack(0, true);
            else AddTracks();
        }

        private void NextTrack()
        {
            if (tracks.Count > 0) OpenTrack((trackIndex + 1) % tracks.Count, playing || trackIndex < 0);
        }

        private void UpdateProgressTimer()
        {
            if (progressClock == null) return;
            if (IsVisible && playing) progressClock.Start();
            else progressClock.Stop();
        }

        private void UpdateProgress()
        {
            if (!mediaReady || !player.NaturalDuration.HasTimeSpan || progress.IsMouseCaptured) return;
            double duration = player.NaturalDuration.TimeSpan.TotalSeconds;
            if (duration <= 0) return;
            seeking = true;
            progress.Value = Math.Min(1, player.Position.TotalSeconds / duration);
            seeking = false;
        }

        private void HandleKey(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape && popup != null && popup.IsOpen)
            {
                popup.IsOpen = false;
                args.Handled = true;
            }
        }

        private void AddTracks()
        {
            ClosePopup();
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = Local("Fichiers audio", "Audio files") + "|*.mp3;*.wav;*.m4a;*.flac;*.aac;*.wma;*.ogg"
            };
            if (dialog.ShowDialog(this) != true) return;
            int first = tracks.Count;
            tracks.AddRange(dialog.FileNames);
            if (trackIndex < 0) OpenTrack(first, false);
        }

        private Popup OpenPopup(FrameworkElement target, FrameworkElement contents, int width, int radius)
        {
            ClosePopup();
            var border = new Border
            {
                Width = width, Child = contents, Padding = new Thickness(7),
                CornerRadius = new CornerRadius(radius), Background = Glass.PopupFill(options.Aura),
                BorderBrush = Glass.Gradient(Color.FromArgb(162, 255, 255, 255), Color.FromArgb(31, 255, 255, 255),
                    new Point(0, 0), new Point(1, 1)),
                BorderThickness = new Thickness(0.65)
            };
            var anchor = target.PointToScreen(new Point());
            double dpi = NativeBackdrop.GetWindowDpiScale(handle);
            bool openAbove = anchor.Y + 250 * dpi > SystemParameters.WorkArea.Bottom * dpi;
            popup = new Popup
            {
                PlacementTarget = target, Placement = openAbove ? PlacementMode.Top : PlacementMode.Bottom,
                HorizontalOffset = target == volumeButton ? (target.ActualWidth - width) / 2 : target.ActualWidth - width,
                VerticalOffset = openAbove ? -7 : 7,
                AllowsTransparency = true, StaysOpen = false, Child = border
            };
            if (SystemParameters.ClientAreaAnimation) popup.PopupAnimation = PopupAnimation.Fade;
            popup.IsOpen = true;
            return popup;
        }

        private void ClosePopup()
        {
            if (popup != null) popup.IsOpen = false;
        }

        private void ShowVolume()
        {
            if (popup != null && popup.IsOpen && popup.PlacementTarget == volumeButton) { ClosePopup(); return; }
            var contents = new StackPanel();
            var label = MakeText(Local("Volume", "Volume"), 11);
            label.Margin = new Thickness(4, 0, 0, 5);
            contents.Children.Add(label);
            var controls = new DockPanel { LastChildFill = true };
            var mute = new GlassButton(options.Aura, muted ? "muted" : "volume", 13);
            var level = MakeText(VolumeLabel(), 10);
            level.VerticalAlignment = VerticalAlignment.Center;
            level.Margin = new Thickness(7, 0, 0, 0);
            DockPanel.SetDock(level, Dock.Right);
            controls.Children.Add(level);
            mute.Width = mute.Height = 24;
            mute.Margin = new Thickness(0, 0, 7, 0);
            AutomationProperties.SetName(mute, Local("Activer ou couper le son", "Toggle mute"));
            DockPanel.SetDock(mute, Dock.Left);
            controls.Children.Add(mute);
            var slider = new GlassRange(options.Aura, false) { Value = volume, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(slider, Local("Volume", "Volume"));
            controls.Children.Add(slider);
            contents.Children.Add(controls);
            Action refresh = () =>
            {
                player.IsMuted = muted;
                player.Volume = volume;
                level.Text = VolumeLabel();
                mute.SetIcon(muted || volume == 0 ? "muted" : "volume");
                volumeButton.SetIcon(muted || volume == 0 ? "muted" : "volume");
                WriteState();
            };
            mute.Click += (sender, args) => { muted = !muted; refresh(); };
            slider.ValueChanged += (sender, args) => { volume = args.NewValue; muted = false; refresh(); };
            OpenPopup(volumeButton, contents, 205, 18);
            slider.Focus();
        }

        private string VolumeLabel()
        {
            return muted ? Local("Muet", "Muted") : Math.Round(volume * 100).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void ShowMenu()
        {
            if (popup != null && popup.IsOpen) { ClosePopup(); return; }
            var stack = new StackPanel();
            MenuRow(stack, Local("Apparence et écoute", "Appearance and playback"), ShowAppearance);
            MenuRow(stack, Local("Bibliothèque", "Library"), ShowTracks);
            MenuRow(stack, Local("Morceau suivant", "Next track"), NextTrack);
            MenuRow(stack, Local("Ajouter de la musique", "Add music"), AddTracks);
            MenuRow(stack, Local("Masquer le widget", "Hide widget"), () =>
            {
                if (tray != null) Hide();
                else Close();
            });
            OpenPopup(optionsButton, stack, 205, 26);
        }

        private void MenuRow(StackPanel container, string name, Action command)
        {
            var content = new Border
            {
                Padding = new Thickness(12, 0, 10, 0), CornerRadius = new CornerRadius(15),
                Background = Brushes.Transparent,
                Child = MakeText(name, 12)
            };
            ((TextBlock)content.Child).VerticalAlignment = VerticalAlignment.Center;
            var template = new FrameworkElementFactory(typeof(ContentPresenter));
            var row = new Button
            {
                Height = 39, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch, Content = content, Cursor = Cursors.Hand,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = template }
            };
            AutomationProperties.SetName(row, name);
            row.MouseEnter += (sender, args) => content.Background = Glass.Solid(Color.FromArgb(28, 255, 255, 255));
            row.MouseLeave += (sender, args) => content.Background = Brushes.Transparent;
            row.Click += (sender, args) => { ClosePopup(); command(); };
            container.Children.Add(row);
        }

        private void ShowAppearance()
        {
            var stack = new StackPanel();
            MenuRow(stack, Topmost ? Local("Désactiver le premier plan", "Turn off always on top") :
                Local("Toujours au premier plan", "Always on top"), () => Topmost = !Topmost);
            OpenPopup(optionsButton, stack, 205, 18);
        }

        private void ShowTracks()
        {
            var stack = new StackPanel();
            if (tracks.Count == 0) MenuRow(stack, Local("Ajouter de la musique", "Add music"), AddTracks);
            for (int i = 0; i < tracks.Count; i++)
            {
                int index = i;
                MenuRow(stack, Path.GetFileNameWithoutExtension(tracks[i]), () => OpenTrack(index, true));
            }
            OpenPopup(optionsButton, new ScrollViewer { MaxHeight = 230, Content = stack }, 230, 18);
        }
    }
}
