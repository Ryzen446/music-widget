using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MusicWidget.NativeApp
{
    public sealed class LocalTrack
    {
        public string Name { get; private set; }
        public string FullPath { get; private set; }
        public string Title { get; private set; }

        internal LocalTrack(string path)
        {
            FullPath = path;
            Name = Path.GetFileName(path);
            Title = Path.GetFileNameWithoutExtension(path);
        }
    }

    public sealed class MusicPlaylist
    {
        public string Name { get; private set; }
        public List<string> Tracks { get; private set; }

        internal MusicPlaylist(string name, IEnumerable<string> tracks)
        {
            Name = name;
            Tracks = new List<string>(tracks);
        }
    }

    public sealed class ShortcutBinding
    {
        public uint modifiers { get; set; }
        public uint key { get; set; }
    }

    public sealed class WidgetSettings
    {
        public string Language { get; set; }
        public Dictionary<string, ShortcutBinding> Shortcuts { get; set; }
        public double LocalVolume { get; set; }
        public bool IsMuted { get; set; }
        public int ThemeIndex { get; set; }
        public int AppearanceVersion { get; set; }
        public string ThemeName { get; set; }
        public string DesignMode { get; set; }
        public string AuraColor { get; set; }
        public string AuraMode { get; set; }
        public double SizeScale { get; set; }
        public string MinimalPosition { get; set; }
        public string MinimalScreen { get; set; }
        public double? CompactLeft { get; set; }
        public double? CompactTop { get; set; }
        public string CompactScreen { get; set; }
        public bool ReducedMotion { get; set; }
        public bool AlwaysOnTop { get; set; }
        public double PlaybackSpeed { get; set; }
        public bool IsShuffle { get; set; }
        public int RepeatMode { get; set; }
        public string ActivePlaylist { get; set; }
        public string PlaybackPlaylist { get; set; }
        public string CurrentTrackName { get; set; }
        public double CurrentTrackPositionSeconds { get; set; }

        public WidgetSettings()
        {
            Language = "auto";
            Shortcuts = ShortcutDefaults();
            LocalVolume = .2;
            DesignMode = "Minimalist";
            AuraColor = "#DCE4EF";
            AuraMode = "Static";
            SizeScale = 1;
            MinimalPosition = "TopCenter";
            MinimalScreen = String.Empty;
            CompactScreen = String.Empty;
            AlwaysOnTop = true;
            PlaybackSpeed = 1;
            ActivePlaylist = PlayerModel.All;
            SetFixedAppearance();
        }

        internal void Normalize()
        {
            if (Language != "en" && Language != "fr") Language = "auto";
            if (DesignMode != "Compact") DesignMode = "Minimalist";
            if (AuraMode != "RGB") AuraMode = "Static";
            if (AuraColor == null || AuraColor.Length != 7 || AuraColor[0] != '#' ||
                AuraColor.Skip(1).Any(character => !Uri.IsHexDigit(character))) AuraColor = "#DCE4EF";
            AuraColor = AuraColor.ToUpperInvariant();
            LocalVolume = Clamp(LocalVolume, 0, 1, .2);
            SizeScale = Math.Round(Clamp(SizeScale, .8, 1.5, 1), 3);
            RepeatMode = Math.Max(0, Math.Min(2, RepeatMode));
            if (PlaybackSpeed != .75 && PlaybackSpeed != 1 && PlaybackSpeed != 1.25 &&
                PlaybackSpeed != 1.5 && PlaybackSpeed != 2) PlaybackSpeed = 1;
            switch (MinimalPosition)
            {
                case "TopLeft":
                case "TopCenter":
                case "TopRight":
                case "BottomLeft":
                case "BottomCenter":
                case "BottomRight": break;
                default: MinimalPosition = "TopCenter"; break;
            }
            if ((CompactLeft.HasValue && !IsFinite(CompactLeft.Value)) ||
                (CompactTop.HasValue && !IsFinite(CompactTop.Value))) CompactLeft = CompactTop = null;
            CurrentTrackPositionSeconds = Clamp(CurrentTrackPositionSeconds, 0, TimeSpan.MaxValue.TotalSeconds - 1, 0);
            RestoreShortcuts();
            SetFixedAppearance();
        }

        private void SetFixedAppearance()
        {
            ThemeIndex = 1;
            ThemeName = "Glass";
            AppearanceVersion = 4;
        }

        private void RestoreShortcuts()
        {
            var defaults = ShortcutDefaults();
            if (Shortcuts != null)
            {
                foreach (string action in defaults.Keys.ToArray())
                {
                    ShortcutBinding entry;
                    if (Shortcuts.TryGetValue(action, out entry) && IsValidShortcut(entry)) defaults[action] = entry;
                }
            }
            Shortcuts = defaults;
        }

        private static Dictionary<string, ShortcutBinding> ShortcutDefaults()
        {
            return new Dictionary<string, ShortcutBinding>(StringComparer.Ordinal)
            {
                { "quit", new ShortcutBinding { modifiers = 7, key = 81 } },
                { "toggle", new ShortcutBinding { modifiers = 6, key = 77 } },
                { "volumeUp", new ShortcutBinding { modifiers = 7, key = 38 } },
                { "volumeDown", new ShortcutBinding { modifiers = 7, key = 40 } },
                { "pause", new ShortcutBinding { modifiers = 7, key = 32 } },
                { "next", new ShortcutBinding { modifiers = 7, key = 39 } }
            };
        }

        private static bool IsValidShortcut(ShortcutBinding binding)
        {
            if (binding == null) return true;
            uint modifiers = binding.modifiers;
            if (modifiers < 1 || modifiers > 7 || modifiers == 4) return false;
            uint key = binding.key;
            return key >= 48 && key <= 57 || key >= 65 && key <= 90 ||
                key >= 112 && key <= 135 && key != 123 || key == 13 || key == 32 ||
                key >= 33 && key <= 40 || key == 45 || key == 46;
        }

        internal static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        internal static double Clamp(double value, double minimum, double maximum, double fallback)
        {
            return IsFinite(value) ? Math.Max(minimum, Math.Min(maximum, value)) : fallback;
        }
    }

    internal sealed class ModelPersistence
    {
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        private readonly string settingsPath;
        private readonly string playlistsPath;
        private Dictionary<string, object> existingSettings;

        public ModelPersistence(string directory)
        {
            settingsPath = Path.Combine(directory, "settings.json");
            playlistsPath = Path.Combine(directory, "playlists.json");
        }

        public WidgetSettings ReadSettings()
        {
            WidgetSettings settings = null;
            try
            {
                if (File.Exists(settingsPath))
                {
                    string text = File.ReadAllText(settingsPath, Encoding.UTF8);
                    existingSettings = serializer.Deserialize<Dictionary<string, object>>(text);
                    settings = serializer.Deserialize<WidgetSettings>(text);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            settings = settings ?? new WidgetSettings();
            settings.Normalize();
            return settings;
        }

        public List<MusicPlaylist> ReadPlaylists()
        {
            var results = new List<MusicPlaylist>();
            try
            {
                if (!File.Exists(playlistsPath)) return results;
                var records = serializer.Deserialize<PlaylistRecord[]>(File.ReadAllText(playlistsPath, Encoding.UTF8));
                var names = new HashSet<string>(Names);
                foreach (PlaylistRecord entry in records ?? new PlaylistRecord[0])
                {
                    if (entry == null || String.IsNullOrWhiteSpace(entry.name) ||
                        Names.Equals(entry.name, PlayerModel.All) || !names.Add(entry.name)) continue;
                    var tracks = (entry.tracks ?? new string[0]).Where(PlayerModel.IsFileName).Distinct(Names);
                    results.Add(new MusicPlaylist(entry.name, tracks));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            return results;
        }

        public void WriteSettings(WidgetSettings settings)
        {
            var current = serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(settings));
            var merged = existingSettings == null ? new Dictionary<string, object>() : new Dictionary<string, object>(existingSettings);
            merged.Remove("TaskbarPosition");
            merged.Remove("TaskbarLeft");
            foreach (var pair in current) merged[pair.Key] = pair.Value;
            AtomicWrite(settingsPath, serializer.Serialize(merged));
            existingSettings = merged;
        }

        public void WritePlaylists(IEnumerable<MusicPlaylist> playlists)
        {
            var entries = playlists.Select(playlist => new PlaylistRecord
            {
                name = playlist.Name, tracks = playlist.Tracks.ToArray()
            }).ToArray();
            AtomicWrite(playlistsPath, serializer.Serialize(entries));
        }

        private static void AtomicWrite(string target, string content)
        {
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private sealed class PlaylistRecord
        {
            public string name { get; set; }
            public string[] tracks { get; set; }
        }
    }
}
