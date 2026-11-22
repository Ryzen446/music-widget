using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MusicWidget.NativeApp
{
    internal sealed partial class WidgetWindow
    {
        private static readonly string[] ActionIds = { "quit", "toggle", "volumeUp", "volumeDown", "pause", "next" };
        private readonly HashSet<string> inactive = new HashSet<string>(StringComparer.Ordinal);
        private GlobalHotKeyManager hotKeys;
        private bool shortcutCapture;
        private Forms.NotifyIcon tray;
        private MusicWidgetTrayMenu trayMenu;
        private Drawing.Icon trayIcon;
        private RegisteredWaitHandle revealRegistration;
        private bool portable;

        internal void InitializeWindowsIntegration(bool portableMode, EventWaitHandle reveal)
        {
            if (hotKeys != null || System.Windows.PresentationSource.FromVisual(this) == null) return;
            portable = portableMode;
            hotKeys = new GlobalHotKeyManager();
            hotKeys.Pressed += RunShortcut;
            RegisterShortcuts();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                trayIcon = (Drawing.Icon)new Drawing.Icon(stream).Clone();
            Drawing.Image logo;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.png"))
            using (var picture = Drawing.Image.FromStream(stream))
                logo = new Drawing.Bitmap(picture);
            trayMenu = new MusicWidgetTrayMenu(logo);
            var toggle = new Forms.ToolStripMenuItem();
            toggle.Click += (sender, args) => TogglePlayer();
            var auto = new Forms.ToolStripMenuItem();
            auto.Click += (sender, args) =>
            {
                try { SetAutostart(!File.Exists(StartupLink)); }
                catch (Exception error) { ShowSystemError(error.Message); }
            };
            var folder = new Forms.ToolStripMenuItem();
            folder.Click += (sender, args) =>
            {
                try { Process.Start(new ProcessStartInfo(Model.MusicDirectory) { UseShellExecute = true }); }
                catch (Exception error) { ShowSystemError(error.Message); }
            };
            var quit = new Forms.ToolStripMenuItem();
            quit.Click += (sender, args) => Close();
            trayMenu.Items.AddRange(new Forms.ToolStripItem[] { toggle, auto, folder, new Forms.ToolStripSeparator(), quit });
            trayMenu.Opening += (sender, args) =>
            {
                toggle.Text = T(IsVisible ? "hideWidget" : "showWidget");
                auto.Text = T("autostart");
                auto.Checked = File.Exists(StartupLink);
                folder.Text = T("openMusicFolder");
                quit.Text = T("quit");
                trayMenu.RefreshLayout();
            };
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Music Widget", Visible = true };
            tray.MouseUp += (sender, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left) TogglePlayer();
                else if (args.Button == Forms.MouseButtons.Right)
                    trayMenu.ShowAtCursor();
            };
            revealRegistration = ThreadPool.RegisterWaitForSingleObject(reveal,
                (state, timeout) => Dispatcher.BeginInvoke(new Action(Reveal), DispatcherPriority.Normal),
                null, Timeout.Infinite, false);
        }

        private void RegisterShortcuts()
        {
            if (hotKeys == null || shortcutCapture) return;
            inactive.Clear();
            for (int i = 0; i < ActionIds.Length; ++i)
            {
                ShortcutBinding binding;
                Model.Settings.Shortcuts.TryGetValue(ActionIds[i], out binding);
                if (!hotKeys.Apply(i, binding == null ? 0 : binding.modifiers, binding == null ? 0 : binding.key))
                    inactive.Add(ActionIds[i]);
            }
        }

        internal bool ShortcutInactive(string action) { return inactive.Contains(action); }

        internal void SetShortcutCapture(bool active)
        {
            if (active == shortcutCapture) return;
            shortcutCapture = active;
            if (hotKeys == null) return;
            if (active) hotKeys.UnregisterAll();
            else RegisterShortcuts();
        }

        internal string ChangeBinding(string action, uint modifiers, uint key)
        {
            int index = Array.IndexOf(ActionIds, action);
            if (index < 0) return "shortcutInvalid";
            if (key != 0)
            {
                bool validKey = (key >= 0x30 && key <= 0x39) || (key >= 0x41 && key <= 0x5a) ||
                    (key >= 0x60 && key <= 0x69) || (key >= 0x70 && key <= 0x7b) ||
                    (key >= 0x25 && key <= 0x28) || key == 0x20 || key == 0x21 || key == 0x22 || key == 0x24 ||
                    key == 0x23 || key == 0x2d || key == 0x2e || (key >= 0xba && key <= 0xc0) || (key >= 0xdb && key <= 0xde);
                if (!validKey || (modifiers & 3) == 0 || (modifiers & ~7U) != 0) return "shortcutInvalid";
                if (Model.Settings.Shortcuts.Any(entry => entry.Key != action && entry.Value != null &&
                    entry.Value.modifiers == modifiers && entry.Value.key == key))
                    return "shortcutDuplicate";
            }
            if (hotKeys != null && !hotKeys.Apply(index, modifiers, key)) return "shortcutUnavailable";
            if (hotKeys != null && shortcutCapture) hotKeys.Apply(index, 0, 0);
            Model.Settings.Shortcuts[action] = key == 0 ? null : new ShortcutBinding { modifiers = modifiers, key = key };
            Model.ApplySettings();
            inactive.Remove(action);
            return "";
        }

        private void RunShortcut(int id)
        {
            switch (id)
            {
                case 0: Close(); break;
                case 1: TogglePlayer(); break;
                case 2: Model.SetVolume(Limit(Model.Settings.LocalVolume + .05, 0, 1)); break;
                case 3: Model.SetVolume(Limit(Model.Settings.LocalVolume - .05, 0, 1)); break;
                case 4: PlayPause(); break;
                case 5: Next(); break;
            }
        }

        private void TogglePlayer() { if (IsVisible) HidePlayer(); else Reveal(); }

        private static string StartupLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Music Widget.lnk"); }
        }

        private void SetAutostart(bool enabled)
        {
            if (!enabled) { File.Delete(StartupLink); return; }
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            object shortcut = null;
            try
            {
                dynamic control = shell;
                shortcut = control.CreateShortcut(StartupLink);
                dynamic link = shortcut;
                link.TargetPath = Assembly.GetExecutingAssembly().Location;
                link.Arguments = portable ? "--portable" : "";
                link.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                link.Description = "Music Widget";
                link.Save();
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private void ShowSystemError(string error)
        {
            System.Windows.MessageBox.Show(this, error, "Music Widget", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }

        private void DisposeWindowsIntegration()
        {
            if (revealRegistration != null) revealRegistration.Unregister(null);
            if (hotKeys != null) hotKeys.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (trayMenu != null) trayMenu.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
        }
    }
}
