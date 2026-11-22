using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Mini = MusicWidget.NativeMini;
using Forms = System.Windows.Forms;

namespace MusicWidget.NativeApp
{
    internal sealed partial class WidgetWindow : Window
    {
        private static readonly Color[] Spectrum = {
            Color.FromRgb(138, 169, 255), Color.FromRgb(196, 153, 242), Color.FromRgb(240, 163, 185),
            Color.FromRgb(242, 197, 157), Color.FromRgb(141, 212, 197)
        };
        private readonly Dictionary<string, Dictionary<string, string>> words = new Dictionary<string, Dictionary<string, string>>();
        private readonly DispatcherTimer auraTimer;
        private readonly DispatcherTimer barsTimer;
        private readonly Stopwatch rgbTime = new Stopwatch();
        private Mini.CardMaterial shell;
        private Canvas glassHost;
        private Grid paneLayer;
        private Rect paneBounds;
        private Rect hostBounds;
        private OnlinePanel online;
        private LibraryPanel library;
        private bool libraryEditor;
        private IntPtr handle;
        private bool acrylic;
        private bool inMove;
        private string language;
        private string renderedDesign;
        private Size lastPlayerSize;
        private double lastPlayerRadius;
        private string lastColor;
        private string currentPane = "player";

        internal PlayerModel Model { get; private set; }
        internal GlassUi Glass { get; private set; }
        internal string Pane { get { return currentPane; } }
        internal bool AddReturnsToLibrary { get; private set; }
        internal bool AcrylicActive { get { return acrylic; } }
        internal IntPtr Hwnd { get { return handle; } }
        internal bool Motion { get { return !Model.Settings.ReducedMotion && SystemParameters.ClientAreaAnimation; } }
        private double PlayerCornerRadius { get { return currentPane == "player" && Model.Settings.DesignMode == "Compact" ?
            8 * Model.Settings.SizeScale : 8; } }

        internal WidgetWindow(PlayerModel model)
        {
            Model = model;
            ResolveLanguage();
            Title = "Music Widget";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = Mini.Glass.Typeface;
            Foreground = Mini.Glass.InkBrush;
            SetValue(Mini.Glass.KeyboardFocusVisibleProperty, false);
            Topmost = model.Settings.AlwaysOnTop;
            Glass = new GlassUi(this, ReadAura());
            auraTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(333) };
            auraTimer.Tick += (sender, args) => UpdateAura();
            barsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(55) };
            barsTimer.Tick += (sender, args) => UpdateBars(Model.ReadVisualizer());
            SourceInitialized += (sender, args) => ConfigureGlass();
            SizeChanged += (sender, args) => ApplyRegion();
            IsVisibleChanged += (sender, args) => UpdateActivity();
            MouseEnter += (sender, args) => UpdateActivity();
            MouseLeave += (sender, args) => UpdateActivity();
            Activated += (sender, args) => { if (removalConfirmation != null) removalConfirmation.FocusCancel(); UpdateActivity(); };
            Deactivated += (sender, args) => { if (library != null) library.CancelDrag(); DismissFloater(false); UpdateActivity(); };
            AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(HandlePointer), true);
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HandleFocusKey), true);
            PreviewKeyDown += HandleKeys;
            Model.StateChanged += OnStateChanged;
            Model.PlaybackUpdated += RefreshPlayback;
            Closed += (sender, args) =>
            {
                Model.StateChanged -= OnStateChanged;
                Model.PlaybackUpdated -= RefreshPlayback;
                if (online != null) online.Dispose();
                CloseRemoval();
                CloseFloater();
                EndDesignTransition(false);
                auraTimer.Stop();
                barsTimer.Stop();
                DisposeWindowsIntegration();
            };
            RefreshPane();
        }

        private void ConfigureGlass()
        {
            handle = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(handle);
            if (source != null && source.CompositionTarget != null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                source.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool processed) =>
                {
                    if (msg == 0x02E0)
                        Dispatcher.BeginInvoke(new Action(() => { ApplyWindowBounds(); ApplyRegion(); }));
                    return IntPtr.Zero;
                });
            }
            acrylic = !SystemParameters.HighContrast && NativeBackdrop.Apply(handle, true) >= 0 &&
                NativeBackdrop.TrySetPlayerAcrylic(handle, true);
            if (shell != null) shell.SetBackdrop(acrylic);
            ApplyWindowBounds();
            ApplyRegion();
        }

        private void ResolveLanguage()
        {
            var preference = Model.Settings.Language;
            language = preference == "fr" || preference == "en" ? preference :
                (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en");
            if (words.ContainsKey(language)) return;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("localization." + language + ".json"))
            using (var reader = new StreamReader(stream))
                words[language] = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }

        internal string T(string key)
        {
            string value;
            return words[language].TryGetValue(key, out value) ? value : key;
        }

        internal string T(string key, string field, string value)
        {
            return T(key).Replace("{" + field + "}", value ?? "");
        }

        internal void Navigate(string pane)
        {
            if (pane != "player" && pane != "library" && pane != "settings" && pane != "add")
                throw new ArgumentException("Unknown pane.", "pane");
            if (currentPane == pane) return;
            bool back = pane == "player" || (pane == "library" && currentPane == "add");
            if (currentPane == "add" && online != null) online.OnNavigateAway();
            CloseFloater();
            if (shortcutCapture) SetShortcutCapture(false);
            currentPane = pane;
            libraryEditor = false;
            RefreshPane(back ? -1 : 1);
        }

        internal void OpenAddMusic()
        {
            if (currentPane == "add") return;
            AddReturnsToLibrary = Pane == "library";
            Navigate("add");
            online.ShowTab(true);
        }

        internal void GoBack()
        {
            if (currentPane == "library" && library != null && library.TryGoBack()) return;
            Navigate(currentPane == "add" && AddReturnsToLibrary ? "library" : "player");
        }

        internal void SetLibraryEditor(bool open)
        {
            if (libraryEditor == open) return;
            libraryEditor = open;
            ApplyWindowBounds();
        }

        private void RefreshPane(int direction = 0)
        {
            CloseRemoval();
            CloseFloater();
            EndDesignTransition();
            string previousDesign = renderedDesign;
            var previousSize = lastPlayerSize;
            double previousRadius = lastPlayerRadius;
            bool changeDesign = currentPane == "player" && previousDesign != null &&
                previousDesign != Model.Settings.DesignMode && IsVisible && Motion;
            DetachPlaybackControls();
            Content = null; // release the reusable online panel before attaching it again
            if (online != null)
            {
                var previousParent = VisualTreeHelper.GetParent(online) as Panel;
                if (previousParent != null) previousParent.Children.Remove(online);
            }
            ApplyWindowBounds(false);
            if (currentPane != "player") Glass.LoadPaneStyles();
            library = null;
            FrameworkElement view;
            switch (currentPane)
            {
                case "settings": view = SettingsPanel.Build(this); break;
                case "library": view = library = LibraryPanel.Build(this); break;
                case "add":
                    if (online == null) online = new OnlinePanel(this);
                    view = online;
                    break;
                default: view = BuildPlayer(); break;
            }
            paneLayer = new Grid { ClipToBounds = true, Background = Brushes.Transparent };
            shell = Glass.Card(acrylic, PlayerCornerRadius);
            paneLayer.Children.Add(shell);
            if (currentPane != "player") view.Margin = new Thickness(22, 23, 22, 20);
            paneLayer.Children.Add(view);
            glassHost = new Canvas();
            glassHost.Children.Add(paneLayer);
            Content = glassHost;
            bool morphing = changeDesign && BeginDesignTransition(previousSize, previousRadius, view);
            if (!morphing) ApplyHostBounds();
            if ((direction != 0 || morphing) && IsVisible)
            {
                if (!morphing) PanelKit.Enter(view, !Motion, direction < 0);
                if (Mini.Glass.ShowKeyboardFocus(this))
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (view.IsVisible && IsActive) view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                    }));
            }
            RefreshPlayback();
            UpdateActivity();
        }

        internal void ApplySettings(bool rebuild = true)
        {
            Model.ApplySettings();
            string oldLanguage = language;
            ResolveLanguage();
            Topmost = Model.Settings.AlwaysOnTop;
            UpdateAura();
            if (oldLanguage != language && online != null) online.RefreshLanguage();
            if (rebuild) RefreshPane();
            else
            {
                ApplyWindowBounds();
                UpdateActivity();
            }
        }

        private void OnStateChanged()
        {
            if (currentPane == "player" && renderedDesign != Model.Settings.DesignMode)
            {
                RefreshPane();
                return;
            }
            RefreshPlayback();
            UpdateActivity();
        }

        private Color ReadAura()
        {
            return (Color)ColorConverter.ConvertFromString(Model.Settings.AuraColor);
        }

        private void UpdateAura()
        {
            if (Model.Settings.AuraMode == "RGB" && Motion)
            {
                double step = rgbTime.Elapsed.TotalSeconds % 40 / 8;
                int index = (int)step;
                Glass.SetAura(Mini.Glass.Mix(Spectrum[(index + 1) % Spectrum.Length], Spectrum[index], step - index));
                lastColor = null;
            }
            else if (lastColor != Model.Settings.AuraColor)
            {
                lastColor = Model.Settings.AuraColor;
                Glass.SetAura(ReadAura());
            }
        }

        private void UpdateActivity()
        {
            Mini.Glass.MotionEnabled = Motion;
            bool animate = IsVisible && Motion && (IsMouseOver || IsActive);
            if (animate && Model.Settings.AuraMode == "RGB")
            {
                if (!rgbTime.IsRunning) rgbTime.Start();
                if (!auraTimer.IsEnabled) auraTimer.Start();
            }
            else
            {
                rgbTime.Stop();
                auraTimer.Stop();
            }
            bool showAudio = IsVisible && Motion && Model.IsPlaying && !Model.Settings.IsMuted &&
                Model.Settings.LocalVolume > 0 && currentPane == "player" && renderedDesign == "Compact";
            Model.SetVisualizerActive(showAudio);
            if (showAudio) { if (!barsTimer.IsEnabled) barsTimer.Start(); }
            else if (barsTimer.IsEnabled)
            {
                barsTimer.Stop();
                UpdateBars(null);
            }
            UpdateMarquee(animate);
        }

        private void ApplyWindowBounds(bool applyHost = true)
        {
            if (inMove) return;
            bool compact = Model.Settings.DesignMode == "Compact";
            double scale = Model.Settings.SizeScale;
            double width, height, left, top;
            if (currentPane == "player")
            {
                width = compact ? 400 * scale : 350;
                height = compact ? 260 * scale : 52;
            }
            else
            {
                width = currentPane == "settings" || libraryEditor ? 420 : 440;
                height = libraryEditor ? 320 : currentPane == "settings" ? 600 : 620;
            }
            Rect area = WorkArea(compact ? Model.Settings.CompactScreen : Model.Settings.MinimalScreen);
            width = Math.Min(area.Width, width);
            height = Math.Min(area.Height, height);
            if (compact)
            {
                left = Model.Settings.CompactLeft ?? area.Right - width - 16;
                top = Model.Settings.CompactTop ?? area.Top + 16;
            }
            else
            {
                string position = Model.Settings.MinimalPosition;
                left = position.EndsWith("Left", StringComparison.Ordinal) ? area.Left + 16 :
                    position.EndsWith("Right", StringComparison.Ordinal) ? area.Right - width - 16 : area.Left + (area.Width - width) / 2;
                top = position.StartsWith("Bottom", StringComparison.Ordinal) ? area.Bottom - height - 16 : area.Top + 16;
            }
            var next = new Rect(Limit(left, area.Left, area.Right - width), Limit(top, area.Top, area.Bottom - height), width, height);
            Topmost = Model.Settings.AlwaysOnTop;
            if (next != paneBounds)
            {
                CloseFloater();
                EndDesignTransition();
            }
            paneBounds = next;
            if (currentPane == "player")
            {
                lastPlayerSize = next.Size;
                lastPlayerRadius = PlayerCornerRadius;
            }
            if (shell != null) shell.SetRadius(PlayerCornerRadius);
            if (applyHost) ApplyHostBounds();
        }

        private Rect WorkArea(string monitor)
        {
            if (string.IsNullOrEmpty(monitor)) return SystemParameters.WorkArea;
            foreach (var screen in Forms.Screen.AllScreens)
                if (string.Equals(screen.DeviceName, monitor, StringComparison.OrdinalIgnoreCase))
                    return ScreenBounds(screen);
            return SystemParameters.WorkArea;
        }

        private Rect ScreenBounds(Forms.Screen screen)
        {
            var rect = screen.WorkingArea;
            uint x, y;
            double sx = NativeBackdrop.GetWindowDpiScale(handle);
            double sy = sx;
            try
            {
                var monitor = MonitorFromPoint(new NativePoint { X = rect.Left + rect.Width / 2, Y = rect.Top + rect.Height / 2 }, 2);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out x, out y) == 0)
                {
                    sx = x / 96.0;
                    sy = y / 96.0;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return new Rect(rect.Left / sx, rect.Top / sy, rect.Width / sx, rect.Height / sy);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X; internal int Y; }
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        private static double Limit(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void DragCompact(MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || e.Handled) return;
            CloseFloater();
            inMove = true;
            try { DragMove(); }
            catch (InvalidOperationException) { }
            finally { inMove = false; }
            var screen = Forms.Screen.FromHandle(handle);
            var area = ScreenBounds(screen);
            Model.Settings.CompactScreen = screen.DeviceName;
            Model.Settings.CompactLeft = Limit(Left, area.Left, area.Right - ActualWidth);
            Model.Settings.CompactTop = Limit(Top, area.Top, area.Bottom - ActualHeight);
            Model.ApplySettings();
            ApplyWindowBounds();
        }

        internal void Reveal()
        {
            Show();
            WindowState = WindowState.Normal;
            ApplyWindowBounds();
        }

        internal void HidePlayer()
        {
            CloseRemoval();
            CloseFloater();
            EndDesignTransition();
            if (online != null) online.OnNavigateAway();
            if (shortcutCapture) SetShortcutCapture(false);
            Hide();
        }

        private void HandleKeys(object sender, KeyEventArgs e)
        {
            if (shortcutCapture || e.Handled) return;
            if (removalConfirmation != null)
            {
                if (e.Key == Key.Escape || e.Key == Key.System && e.SystemKey == Key.F4)
                {
                    e.Handled = true;
                    removalConfirmation.Dismiss(false);
                }
                return;
            }
            if (e.Key == Key.Escape)
            {
                if (library != null && library.CancelDrag())
                {
                    e.Handled = true;
                    return;
                }
                if (floaterSurface != null) DismissFloater(true);
                else if (Pane == "add" && online != null && online.TryDismiss()) { }
                else if (Pane != "player") GoBack();
                else return;
                e.Handled = true;
                return;
            }
            if (e.OriginalSource is TextBoxBase || e.OriginalSource is Slider || e.OriginalSource is ComboBox || e.OriginalSource is Thumb) return;
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (control && e.Key == Key.L) Navigate("library");
            else if (e.Key == Key.M) Model.ToggleMute();
            else if (e.Key == Key.Space && !(e.OriginalSource is ButtonBase)) PlayPause();
            else if (e.Key == Key.Right && control) Next();
            else if (e.Key == Key.Left && control) Previous();
            else if (e.Key == Key.Right && currentPane == "player") Model.Seek(Model.Position + 5);
            else if (e.Key == Key.Left && currentPane == "player") Model.Seek(Model.Position - 5);
            else return;
            e.Handled = true;
        }

        private void PlayPause() { if (online != null) online.StopPreview(false); Model.TogglePlayPause(); }
        private void Next() { if (online != null) online.StopPreview(false); Model.Next(); }
        private void Previous() { if (online != null) online.StopPreview(false); Model.Previous(); }
    }
}
