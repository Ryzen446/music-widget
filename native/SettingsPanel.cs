using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MusicWidget.NativeApp
{
    internal sealed class SettingsPanel : Grid
    {
        private static readonly string[] ShortcutActions = { "quit", "toggle", "volumeUp", "volumeDown", "pause", "next" };
        private static readonly string[] ShortcutLabels = { "quit", "hideShortcut", "volumeUp", "volumeDown", "playPause", "next" };
        private static readonly string[] PositionIds = { "TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomCenter", "BottomRight" };
        private static readonly string[] ColorPresets = { "#B9C7E8", "#8AA9FF", "#C499F2", "#F0A3B9", "#F2C59D", "#8DD4C5" };
        private static readonly string[] SpecialKeys = { "keyEnter", "keySpace", "keyPageUp", "keyPageDown", "keyEnd", "keyHome", "keyInsert", "keyDelete" };
        private static readonly uint[] SpecialKeyCodes = { 13, 32, 33, 34, 35, 36, 45, 46 };
        private readonly WidgetWindow window;
        private readonly ScrollViewer scroll;
        private StackPanel shortcuts;
        private TextBlock shortcutError;
        private ContentControl sleepControl;
        private int displayedSleep = -1;
        private Button recording;
        private string recordAction;
        private uint keyDown, modifiersDown;
        private WidgetSettings Settings { get { return window.Model.Settings; } }

        internal static FrameworkElement Build(WidgetWindow window)
        {
            return new SettingsPanel(window);
        }

        private SettingsPanel(WidgetWindow w)
        {
            window = w;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            Children.Add(w.Glass.Header(w.T("settings"), null, delegate { w.Navigate("player"); }));
            scroll = PanelKit.Scroll(null);
            Grid.SetRow(scroll, 1);
            Children.Add(scroll);
            Loaded += delegate { window.Model.StateChanged += RefreshSleep; };
            Unloaded += delegate { window.Model.StateChanged -= RefreshSleep; CancelRecording(false); };
            Render();
        }

        private void Render()
        {
            CancelRecording(false);
            double offset = scroll.VerticalOffset;
            var body = new StackPanel { Margin = new Thickness(0, 0, 4, 6) };
            string[] languages = { "auto", "fr", "en" };
            string[] languageKeys = { "languageAuto", "languageFr", "languageEn" };
            body.Children.Add(CaptionChoice("language", languages, languageKeys, Settings.Language, delegate(string value)
            {
                Settings.Language = value;
                window.ApplySettings(true);
            }));

            var sizeOptions = new StackPanel();
            var placementOptions = new StackPanel();
            string[] designs = { "Minimalist", "Compact" };
            string[] designLabels = { window.T("minimal"), window.T("compact") };
            body.Children.Add(PanelKit.Section(window, window.T("layout"),
                new GlassSegments(window, window.T("playerLayout"), designLabels, Array.IndexOf(designs, Settings.DesignMode), index =>
                {
                    Settings.DesignMode = designs[index];
                    window.Model.ApplySettings();
                    UpdateLayoutDetails(sizeOptions, placementOptions, true);
                })));
            body.Children.Add(sizeOptions);
            BuildAura(body);
            body.Children.Add(placementOptions);
            UpdateLayoutDetails(sizeOptions, placementOptions, false);

            string[] speeds = { "0.75", "1", "1.25", "1.5", "2" };
            string[] labels = speeds.Select(speed => Double.Parse(speed, CultureInfo.InvariantCulture).ToString("0.##", CultureInfo.CurrentCulture) + "×").ToArray();
            var speedSection = PanelKit.Section(window, window.T("speed"),
                Choice(speeds, labels, Settings.PlaybackSpeed.ToString("0.##", CultureInfo.InvariantCulture),
                    delegate(string value) { Settings.PlaybackSpeed = Double.Parse(value, CultureInfo.InvariantCulture); ApplyAndRender(); }));
            speedSection.Margin = new Thickness(0, 0, 7, 17);
            sleepControl = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            displayedSleep = -1;
            RefreshSleep();
            var sleepSection = PanelKit.Section(window, window.T("sleep"), sleepControl);
            sleepSection.Margin = new Thickness(7, 0, 0, 17);
            body.Children.Add(PanelKit.Split(speedSection, sleepSection));
            body.Children.Add(Switch("alwaysOnTop", "alwaysOnTopHint", Settings.AlwaysOnTop,
                delegate(bool value) { Settings.AlwaysOnTop = value; window.ApplySettings(false); }));
            body.Children.Add(Switch("reducedMotion", "reducedMotionHint", Settings.ReducedMotion,
                delegate(bool value) { Settings.ReducedMotion = value; window.ApplySettings(false); }));
            body.Children.Add(Switch("shuffle", null, Settings.IsShuffle,
                delegate(bool value) { Settings.IsShuffle = value; window.ApplySettings(false); }));
            body.Children.Add(CaptionChoice("repeat", new[] { "0", "1", "2" }, new[] { "repeatList", "repeatTrack", "repeatOff" },
                Settings.RepeatMode.ToString(CultureInfo.InvariantCulture),
                delegate(string value) { Settings.RepeatMode = Int32.Parse(value, CultureInfo.InvariantCulture); ApplyAndRender(); }));
            BuildShortcuts(body);
            scroll.Content = body;
            scroll.ScrollToVerticalOffset(offset);
        }

        private void ApplyAndRender()
        {
            window.ApplySettings(false);
            Render();
        }

        private FrameworkElement CaptionChoice(string caption, string[] values, string[] keys, string current, Action<string> select)
        {
            return PanelKit.Section(window, window.T(caption), Choice(values, keys.Select(key => window.T(key)).ToArray(),
                current, select, matchWidth: caption == "playerPosition"));
        }

        private FrameworkElement Choice(string[] values, string[] labels, string current, Action<string> select, string fallback = null, bool matchWidth = false)
        {
            var entries = new List<PanelMenuItem>();
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                entries.Add(new PanelMenuItem(labels[i], value == current ? "✓" : null,
                    delegate { if (value != current) select(value); }));
            }
            int index = Array.IndexOf(values, current);
            return PanelKit.Choice(window, index >= 0 ? labels[index] : fallback ?? labels[0], entries, matchWidth);
        }

        private void RefreshSleep()
        {
            int remaining = window.Model.SleepMinutes;
            if (remaining == displayedSleep || sleepControl == null) return;
            displayedSleep = remaining;
            sleepControl.Content = Choice(new[] { "0", "15", "30", "60" }, new[] { window.T("off"), "15 min", "30 min", "60 min" },
                remaining.ToString(CultureInfo.InvariantCulture), delegate(string value)
                {
                    window.Model.SetSleep(Int32.Parse(value, CultureInfo.InvariantCulture));
                    RefreshSleep();
                }, remaining.ToString(CultureInfo.CurrentCulture) + " min");
        }

        private FrameworkElement Segments(string labelKey, string first, string second, bool firstSelected, Action<bool> select)
        {
            return new GlassSegments(window, window.T(labelKey), window.T(first), window.T(second),
                firstSelected ? 0 : 1, value => select(value == 0));
        }

        private void UpdateLayoutDetails(StackPanel size, StackPanel placement, bool animate)
        {
            size.Children.Clear();
            placement.Children.Clear();
            bool compact = Settings.DesignMode == "Compact";
            if (compact) BuildSize(size);
            else placement.Children.Add(CaptionChoice("playerPosition", PositionIds, PositionIds.Select(id => "position" + id).ToArray(),
                Settings.MinimalPosition, delegate(string value)
                {
                    Settings.MinimalPosition = value;
                    window.Model.ApplySettings();
                    UpdateLayoutDetails(size, placement, false);
                }));
            if (animate) PanelKit.Enter(compact ? size : placement, !window.Motion, !compact);
        }

        private void BuildSize(StackPanel body)
        {
            var area = new StackPanel();
            var number = window.Glass.Text(Percentage(Settings.SizeScale), 11, true);
            number.HorizontalAlignment = HorizontalAlignment.Right;
            var range = window.Glass.Slider(80, 150, Math.Round(Settings.SizeScale * 100), delegate(double value)
            {
                Settings.SizeScale = Math.Round(value / 5) * .05;
                number.Text = Percentage(Settings.SizeScale);
                window.Model.ApplySettings();
            });
            range.TickFrequency = 5;
            range.IsSnapToTickEnabled = true;
            AutomationProperties.SetName(range, window.T("playerSize"));
            area.Children.Add(number);
            area.Children.Add(range);
            var description = window.Glass.Text(window.T("resizeHint"), 11);
            description.TextWrapping = TextWrapping.Wrap;
            description.Foreground = PanelKit.SubtleInk;
            description.Margin = new Thickness(0, 7, 0, 0);
            area.Children.Add(description);
            body.Children.Add(PanelKit.Section(window, window.T("playerSize"), area));
        }

        private static string Percentage(double value) { return Math.Round(value * 100).ToString(CultureInfo.CurrentCulture) + " %"; }

        private void BuildAura(StackPanel body)
        {
            var area = new StackPanel { Margin = new Thickness(0, 0, 0, 19) };
            var heading = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
            var dot = new Ellipse { Width = 9, Height = 9, Fill = PanelKit.Tint(window.Glass.Aura, 255), Margin = new Thickness(0, 0, 3, 0) };
            DockPanel.SetDock(dot, Dock.Right);
            heading.Children.Add(dot);
            var label = window.Glass.Text(window.T("glassAura"), 11, true);
            label.Foreground = PanelKit.SubtleInk;
            heading.Children.Add(label);
            area.Children.Add(heading);
            var details = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            bool fixedColor = Settings.AuraMode != "RGB";
            details.Content = fixedColor ? BuildPicker(dot) : BuildRgbPreview();
            area.Children.Add(Segments("auraMode", "fixedColor", "rgb", fixedColor, delegate(bool fixedMode)
            {
                Settings.AuraMode = fixedMode ? "Static" : "RGB";
                window.ApplySettings(false);
                dot.Fill = PanelKit.Tint(window.Glass.Aura, 255);
                var content = fixedMode ? BuildPicker(dot) : BuildRgbPreview();
                details.Content = content;
                PanelKit.Enter(content, !window.Motion, fixedMode);
            }));
            area.Children.Add(details);
            body.Children.Add(area);
        }

        private FrameworkElement BuildRgbPreview()
        {
            var preview = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 16, 0, 1) };
            var swatch = window.Glass.Button("", null);
            swatch.Width = swatch.Height = 52;
            swatch.Margin = new Thickness(0, 0, 14, 0);
            swatch.Focusable = swatch.IsTabStop = swatch.IsHitTestVisible = false;
            DockPanel.SetDock(swatch, Dock.Left);
            preview.Children.Add(swatch);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(window.Glass.Text(window.T("rgbHint"), 11));
            var hint = window.Glass.Text(window.T("rgbDuration"), 10);
            hint.Foreground = PanelKit.SubtleInk;
            hint.Margin = new Thickness(0, 4, 0, 0);
            copy.Children.Add(hint);
            preview.Children.Add(copy);
            return preview;
        }

        private FrameworkElement BuildPicker(Ellipse dot)
        {
            var picker = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            Color initial;
            if (!PanelHsv.TryParse(Settings.AuraColor, out initial)) initial = window.Glass.Aura;
            PanelHsv hsv = PanelHsv.FromColor(initial, 0);
            var plane = new PanelColorPlane();
            plane.Value = hsv;
            plane.Height = 137;
            AutomationProperties.SetName(plane, window.T("saturationBrightness"));
            picker.Children.Add(plane);
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 13, 0, 2) };
            var degrees = window.Glass.Text(String.Format(CultureInfo.CurrentCulture, "{0:0}°", hsv.Hue), 11, true);
            DockPanel.SetDock(degrees, Dock.Right);
            row.Children.Add(degrees);
            row.Children.Add(window.Glass.Text(window.T("hue"), 11, true));
            picker.Children.Add(row);
            var hue = new PanelHue { Minimum = 0, Maximum = 359, Value = Math.Round(hsv.Hue), Height = 22 };
            AutomationProperties.SetName(hue, window.T("hue"));
            picker.Children.Add(hue);

            var details = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 13, 0, 0) };
            var current = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Background = PanelKit.Tint(initial, 255),
                BorderThickness = new Thickness(1), BorderBrush = PanelKit.ColorBrush(146, 255, 255, 255),
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Bottom
            };
            DockPanel.SetDock(current, Dock.Left);
            details.Children.Add(current);
            var hexSettings = new StackPanel();
            var hexLabel = window.Glass.Text(window.T("hexColor"), 11, true);
            hexLabel.Margin = new Thickness(0, 0, 0, 7);
            hexSettings.Children.Add(hexLabel);
            var field = window.Glass.Field("#RRGGBB", PanelHsv.ToHex(initial));
            field.MaxLength = 7;
            field.Height = 39;
            AutomationProperties.SetName(field, window.T("hexColor"));
            hexSettings.Children.Add(field);
            details.Children.Add(hexSettings);
            picker.Children.Add(details);

            bool updating = false;
            Action<PanelHsv> refresh = delegate(PanelHsv value)
            {
                updating = true;
                hsv = value;
                Color color = hsv.Color();
                hue.Value = Math.Round(hsv.Hue);
                plane.Value = hsv;
                degrees.Text = String.Format(CultureInfo.CurrentCulture, "{0:0}°", hsv.Hue);
                field.Text = PanelHsv.ToHex(color);
                current.Background = dot.Fill = PanelKit.Tint(color, 255);
                window.Glass.SetAura(color);
                updating = false;
            };
            Action commit = delegate
            {
                string color = PanelHsv.ToHex(hsv.Color());
                if (String.Equals(Settings.AuraColor, color, StringComparison.OrdinalIgnoreCase)) return;
                Settings.AuraColor = color;
                window.ApplySettings(false);
            };
            Action commitField = delegate
            {
                Color color;
                if (!PanelHsv.TryParse(field.Text, out color)) color = hsv.Color();
                refresh(PanelHsv.FromColor(color, hsv.Hue));
                commit();
            };
            plane.Changed += refresh;
            plane.Committed += commit;
            hue.ValueChanged += delegate { if (!updating) refresh(new PanelHsv(hue.Value, hsv.Saturation, hsv.Brightness)); };
            hue.Committed += commit;
            field.LostKeyboardFocus += delegate { commitField(); };
            field.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { args.Handled = true; commitField(); } };

            var choices = new UniformGrid { Columns = ColorPresets.Length, Margin = new Thickness(0, 15, 0, 0) };
            for (int i = 0; i < ColorPresets.Length; i++)
            {
                string hex = ColorPresets[i];
                Color color;
                PanelHsv.TryParse(hex, out color);
                var button = window.Glass.IconButton(null, window.T("auraValue", "color", hex),
                    delegate { refresh(PanelHsv.FromColor(color, hsv.Hue)); commit(); }, 30);
                button.SetSwatch(color);
                button.HorizontalAlignment = HorizontalAlignment.Center;
                button.VerticalAlignment = VerticalAlignment.Center;
                choices.Children.Add(button);
            }
            picker.Children.Add(choices);
            return picker;
        }

        private FrameworkElement Switch(string labelKey, string hintKey, bool enabled, Action<bool> change)
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 16) };
            var toggle = new PanelToggle(() => window.Motion) { IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, window.T(labelKey));
            toggle.Checked += delegate { change(true); };
            toggle.Unchecked += delegate { change(false); };
            DockPanel.SetDock(toggle, Dock.Right);
            row.Children.Add(toggle);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(window.Glass.Text(window.T(labelKey), 12, true));
            if (hintKey != null)
            {
                var hint = window.Glass.Text(window.T(hintKey), 10);
                hint.Foreground = PanelKit.SubtleInk;
                hint.Margin = new Thickness(0, 3, 0, 0);
                copy.Children.Add(hint);
            }
            row.Children.Add(copy);
            return row;
        }

        private void BuildShortcuts(StackPanel body)
        {
            var title = window.Glass.Text(window.T("shortcuts"), 11, true);
            title.Foreground = PanelKit.SubtleInk;
            title.Margin = new Thickness(0, 0, 0, 9);
            body.Children.Add(title);
            var hint = window.Glass.Text(window.T("shortcutsHint"), 11);
            hint.Foreground = PanelKit.SubtleInk;
            hint.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(hint);
            shortcuts = new StackPanel { Margin = new Thickness(0, 11, 0, 10) };
            body.Children.Add(shortcuts);
            shortcutError = window.Glass.Text("", 11, true);
            shortcutError.TextWrapping = TextWrapping.Wrap;
            shortcutError.Foreground = PanelKit.DangerInk;
            shortcutError.Visibility = Visibility.Collapsed;
            body.Children.Add(shortcutError);
            UpdateShortcutRows();
        }

        private void UpdateShortcutRows()
        {
            shortcuts.Children.Clear();
            for (int i = 0; i < ShortcutActions.Length; i++)
            {
                string action = ShortcutActions[i], label = window.T(ShortcutLabels[i]);
                ShortcutBinding binding;
                Settings.Shortcuts.TryGetValue(action, out binding);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7), LastChildFill = true };
                var clear = window.Glass.IconButton("close", window.T("clearShortcut", "action", label), delegate
                {
                    CancelRecording(false);
                    ReportShortcut(window.ChangeBinding(action, 0, 0));
                    UpdateShortcutRows();
                }, 27);
                clear.IsEnabled = binding != null && binding.key != 0;
                clear.Margin = new Thickness(7, 0, 0, 0);
                DockPanel.SetDock(clear, Dock.Right);
                row.Children.Add(clear);
                Button edit = null;
                edit = window.Glass.Button(FormatShortcut(binding), delegate { StartRecording(action, edit); });
                edit.Height = 29;
                edit.MinHeight = 29;
                edit.MinWidth = 102;
                edit.Padding = new Thickness(8, 0, 8, 0);
                edit.FontSize = 10;
                var editName = window.T("editShortcut", "action", label);
                AutomationProperties.SetName(edit, editName);
                window.Glass.SetTip(edit, editName);
                edit.PreviewKeyDown += delegate(object sender, KeyEventArgs args) { RecordDown(edit, args); };
                edit.PreviewKeyUp += delegate(object sender, KeyEventArgs args) { RecordUp(edit, args); };
                edit.LostKeyboardFocus += delegate { if (recording == edit) CancelRecording(true); };
                DockPanel.SetDock(edit, Dock.Right);
                row.Children.Add(edit);
                var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                var caption = window.Glass.Text(label, 11);
                caption.TextWrapping = TextWrapping.Wrap;
                left.Children.Add(caption);
                if (window.ShortcutInactive(action))
                {
                    var inactive = window.Glass.Text(window.T("shortcutInactive"), 10);
                    inactive.Foreground = PanelKit.DangerInk;
                    left.Children.Add(inactive);
                }
                row.Children.Add(left);
                shortcuts.Children.Add(row);
            }
        }

        private void StartRecording(string action, Button button)
        {
            if (recording == button) { CancelRecording(true); return; }
            CancelRecording(true);
            recordAction = action;
            recording = button;
            window.SetShortcutCapture(true);
            button.Content = window.T("shortcutPress");
            button.Focus();
            ReportShortcut(null);
        }

        private void CancelRecording(bool restore)
        {
            if (recording == null) return;
            Button previous = recording;
            string action = recordAction;
            recording = null;
            recordAction = null;
            keyDown = modifiersDown = 0;
            window.SetShortcutCapture(false);
            if (restore)
            {
                ShortcutBinding binding;
                Settings.Shortcuts.TryGetValue(action, out binding);
                previous.Content = FormatShortcut(binding);
                ReportShortcut(null);
            }
        }

        private void RecordDown(Button button, KeyEventArgs args)
        {
            if (button != recording) return;
            Key key = args.Key == Key.System ? args.SystemKey : args.Key;
            if (key == Key.Tab) return;
            args.Handled = true;
            if (key == Key.Escape) { CancelRecording(true); return; }
            if (args.IsRepeat || IsModifier(key)) return;
            var held = Keyboard.Modifiers;
            uint code = (uint)KeyInterop.VirtualKeyFromKey(key);
            if ((held & ModifierKeys.Windows) != 0 || (held & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 || !AllowedKey(code))
            {
                keyDown = modifiersDown = 0;
                ReportShortcut("shortcutInvalid");
                return;
            }
            keyDown = code;
            modifiersDown = 0;
            if ((held & ModifierKeys.Alt) != 0) modifiersDown |= 1;
            if ((held & ModifierKeys.Control) != 0) modifiersDown |= 2;
            if ((held & ModifierKeys.Shift) != 0) modifiersDown |= 4;
            ReportShortcut(null);
        }

        private void RecordUp(Button button, KeyEventArgs args)
        {
            if (button != recording || keyDown == 0) return;
            Key key = args.Key == Key.System ? args.SystemKey : args.Key;
            if ((uint)KeyInterop.VirtualKeyFromKey(key) != keyDown) return;
            args.Handled = true;
            uint modifiers = modifiersDown, code = keyDown;
            string action = recordAction;
            CancelRecording(false);
            ReportShortcut(window.ChangeBinding(action, modifiers, code));
            UpdateShortcutRows();
        }

        private void ReportShortcut(string error)
        {
            if (shortcutError == null) return;
            shortcutError.Text = String.IsNullOrEmpty(error) ? "" : window.T(error);
            shortcutError.Visibility = String.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static bool IsModifier(Key key)
        {
            return key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;
        }

        private static bool AllowedKey(uint key)
        {
            return (key >= 48 && key <= 57) || (key >= 65 && key <= 90) || (key >= 112 && key <= 135 && key != 123) ||
                (key >= 37 && key <= 40) || SpecialKeyCodes.Contains(key);
        }

        private string FormatShortcut(ShortcutBinding binding)
        {
            if (binding == null || binding.key == 0) return window.T("shortcutNone");
            var parts = new List<string>();
            if ((binding.modifiers & 2) != 0) parts.Add("Ctrl");
            if ((binding.modifiers & 1) != 0) parts.Add("Alt");
            if ((binding.modifiers & 4) != 0) parts.Add(window.T("keyShift"));
            uint key = binding.key;
            int named = Array.IndexOf(SpecialKeyCodes, key);
            if (named >= 0) parts.Add(window.T(SpecialKeys[named]));
            else if (key >= 37 && key <= 40) parts.Add(new[] { "←", "↑", "→", "↓" }[key - 37]);
            else if (key >= 112) parts.Add("F" + (key - 111));
            else parts.Add(((char) key).ToString());
            return String.Join(" + ", parts);
        }
    }
}
