using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using MusicWidget.Visualization;

namespace MusicWidget.NativeApp
{
    public sealed class PlayerModel : IDisposable
    {
        public const string All = "ALL";
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(Names)
        { ".mp3", ".wav", ".m4a", ".flac", ".aac", ".wma", ".ogg" };
        private static readonly double[] SilentFrame = new double[5];

        private readonly ModelPersistence persistence;
        private readonly DispatcherTimer clock;
        private readonly DispatcherTimer saveTimer;
        private readonly Random random = new Random();
        private readonly List<string> playbackHistory = new List<string>();
        private readonly List<string> shuffleBag = new List<string>();
        private readonly List<LocalTrack> emptyTracks = new List<LocalTrack>();
        private Dictionary<string, LocalTrack> tracksByName = new Dictionary<string, LocalTrack>(Names);
        private Dictionary<string, List<LocalTrack>> playlistViews = new Dictionary<string, List<LocalTrack>>(Names);
        private bool disposed;
        private MediaPlayer player;
        private double pendingPosition;
        private double lastAudibleVolume = .2;
        private bool shuffleInitialized;
        private bool previousShuffle;
        private bool settingsDirty;
        private DateTime lastSave;
        private DateTime? sleepDeadline;
        private int sleepMinutesReported;
        private bool wantsVisualizer;
        private SessionAudioVisualizer sessionVisualizer;
        private TrackSpectrumReader fileSpectrum;
        private bool fileSpectrumActive;
        private DateTime nextSpectrumCheck;
        private LocalTrack previewTrack;
        private bool previewWillResume;

        public event Action StateChanged;
        public event Action PlaybackUpdated;
        public event Action LibraryChanged;
        internal event Action<string, string> PlaylistRenamed;
        public event Action<string> Problem;

        public string DataDirectory { get; private set; }
        public string MusicDirectory { get; private set; }
        public string ToolsDirectory { get; private set; }
        public WidgetSettings Settings { get; private set; }
        public List<LocalTrack> Library { get; private set; }
        public List<MusicPlaylist> Playlists { get; private set; }
        public string ActivePlaylist { get; private set; }
        public string PlaybackPlaylist { get; private set; }
        public LocalTrack CurrentTrack { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool MediaReady { get; private set; }
        public string PlaybackError { get; private set; }
        public double Duration { get; private set; }
        public int QueueCount { get { return Queue().Count; } }
        public int QueueIndex { get { return TrackIndex(Queue(), CurrentTrack); } }
        public bool IsMuted { get { return Settings.IsMuted || Settings.LocalVolume <= 0; } }
        public double Volume { get { return Settings.LocalVolume; } }
        public double Position { get { return MediaReady && player != null ? player.Position.TotalSeconds : pendingPosition; } }
        public int SleepMinutes
        {
            get
            {
                return sleepDeadline.HasValue ? Math.Max(0, (int)Math.Ceiling((sleepDeadline.Value - DateTime.UtcNow).TotalMinutes)) : 0;
            }
        }

        public PlayerModel(string dataDirectory)
            : this(dataDirectory, Path.Combine(dataDirectory, "musique"), Path.Combine(dataDirectory, "bin")) { }

        public PlayerModel(string dataDirectory, string musicDirectory, string toolsDirectory)
        {
            DataDirectory = Path.GetFullPath(dataDirectory);
            MusicDirectory = Path.GetFullPath(musicDirectory);
            ToolsDirectory = Path.GetFullPath(toolsDirectory);
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(MusicDirectory);
            persistence = new ModelPersistence(DataDirectory);
            Settings = persistence.ReadSettings();
            Playlists = persistence.ReadPlaylists();
            Library = new List<LocalTrack>();
            ActivePlaylist = ExistingPlaylistName(Settings.ActivePlaylist);
            PlaybackPlaylist = String.IsNullOrEmpty(Settings.PlaybackPlaylist) ? ActivePlaylist : ExistingPlaylistName(Settings.PlaybackPlaylist);
            lastAudibleVolume = Settings.LocalVolume > 0 ? Settings.LocalVolume : .2;
            previousShuffle = Settings.IsShuffle;
            clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            clock.Tick += OnClock;
            saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(850) };
            saveTimer.Tick += (sender, args) => { saveTimer.Stop(); SaveNow(); };
            lastSave = DateTime.UtcNow;

            ReadLibrary();
            LocalTrack savedTrack;
            if (Settings.CurrentTrackName != null && tracksByName.TryGetValue(Settings.CurrentTrackName, out savedTrack))
                OpenTrack(savedTrack, false, Settings.CurrentTrackPositionSeconds);
            else if (TracksFor(ActivePlaylist).Count != 0)
            {
                PlaybackPlaylist = ActivePlaylist;
                OpenTrack(TracksFor(ActivePlaylist)[0], false, 0);
            }
        }

        internal static bool IsFileName(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
                value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        public List<LocalTrack> TracksFor(string playlist)
        {
            if (String.IsNullOrEmpty(playlist) || Names.Equals(playlist, All)) return Library;
            List<LocalTrack> entries;
            return playlistViews.TryGetValue(playlist, out entries) ? entries : emptyTracks;
        }

        public void ApplySettings()
        {
            Settings.Normalize();
            if (Settings.LocalVolume > 0) lastAudibleVolume = Settings.LocalVolume;
            if (previousShuffle != Settings.IsShuffle)
            {
                ClearShuffle();
                previousShuffle = Settings.IsShuffle;
            }
            if (player != null)
            {
                player.SpeedRatio = Settings.PlaybackSpeed;
                player.Volume = IsMuted ? 0 : Settings.LocalVolume;
            }
            Changed();
        }

        public void SetVolume(double volume)
        {
            Settings.LocalVolume = WidgetSettings.Clamp(volume, 0, 1, Settings.LocalVolume);
            if (Settings.LocalVolume > 0) Settings.IsMuted = false;
            ApplySettings();
        }

        public void ToggleMute()
        {
            if (Settings.LocalVolume <= 0)
            {
                Settings.LocalVolume = lastAudibleVolume;
                Settings.IsMuted = false;
            }
            else Settings.IsMuted = !Settings.IsMuted;
            ApplySettings();
        }

        public void SetSleep(int minutes)
        {
            if (minutes != 15 && minutes != 30 && minutes != 60) minutes = 0;
            sleepDeadline = minutes == 0 ? (DateTime?)null : DateTime.UtcNow.AddMinutes(minutes);
            sleepMinutesReported = minutes;
            UpdateClock();
            Changed(false);
        }

        public bool Play(string trackName)
        {
            var selected = TracksFor(ActivePlaylist).FirstOrDefault(track => Names.Equals(track.Name, trackName));
            if (selected == null) return false;
            CancelPreviewResume();
            if (!Names.Equals(PlaybackPlaylist, ActivePlaylist))
            {
                PlaybackPlaylist = ActivePlaylist;
                ClearShuffle();
            }
            return PlayQueueEntry(selected, false);
        }

        public void TogglePlayPause()
        {
            CancelPreviewResume();
            if (IsPlaying) SetPlaying(false);
            else if (CurrentTrack == null)
            {
                var view = TracksFor(ActivePlaylist);
                if (view.Count == 0) return;
                PlaybackPlaylist = ActivePlaylist;
                ClearShuffle();
                PlayQueueEntry(view[0], false);
            }
            else if (PlaybackError != null || player == null) OpenTrack(CurrentTrack, true, 0);
            else SetPlaying(true);
        }

        public void Pause()
        {
            CancelPreviewResume();
            if (IsPlaying) SetPlaying(false);
        }

        public bool PreparePreview()
        {
            if (IsPlaying)
            {
                previewTrack = CurrentTrack;
                previewWillResume = true;
                SetPlaying(false);
            }
            return previewWillResume;
        }

        public void ResumeFromPreview(bool wasPlaying)
        {
            bool resume = wasPlaying && previewWillResume && CurrentTrack != null &&
                ReferenceEquals(previewTrack, CurrentTrack) && !IsPlaying;
            CancelPreviewResume();
            if (resume) SetPlaying(true);
        }

        private void CancelPreviewResume()
        {
            previewTrack = null;
            previewWillResume = false;
        }

        public void Seek(double seconds)
        {
            if (player == null || !WidgetSettings.IsFinite(seconds)) return;
            pendingPosition = Math.Max(0, seconds);
            if (MediaReady)
            {
                pendingPosition = Math.Min(Duration, pendingPosition);
                player.Position = TimeSpan.FromSeconds(pendingPosition);
            }
            Notify(PlaybackUpdated);
            ScheduleSave();
        }

        public void Next()
        {
            CancelPreviewResume();
            MoveNext(false);
        }

        public void Previous()
        {
            CancelPreviewResume();
            if (Position > 3) { Seek(0); return; }
            var queue = Queue();
            if (queue.Count == 0) return;
            if (Settings.IsShuffle)
            {
                while (playbackHistory.Count != 0)
                {
                    var previousName = playbackHistory[playbackHistory.Count - 1];
                    playbackHistory.RemoveAt(playbackHistory.Count - 1);
                    var previousTrack = queue.FirstOrDefault(track => Names.Equals(track.Name, previousName));
                    if (previousTrack == null) continue;
                    PlayQueueEntry(previousTrack, true);
                    return;
                }
            }
            int index = TrackIndex(queue, CurrentTrack);
            PlayQueueEntry(queue[index <= 0 ? queue.Count - 1 : index - 1], false);
        }

        private void MoveNext(bool automatic)
        {
            var queue = Queue();
            if (queue.Count == 0)
            {
                if (automatic) SetPlaying(false);
                return;
            }
            int index = TrackIndex(queue, CurrentTrack);
            if (Settings.IsShuffle)
            {
                if (shuffleBag.Count > 0)
                {
                    var queueNames = new HashSet<string>(queue.Select(track => track.Name), Names);
                    shuffleBag.RemoveAll(name => !queueNames.Contains(name));
                }
                if (shuffleBag.Count == 0)
                {
                    if (shuffleInitialized && automatic && Settings.RepeatMode == 2) { StopAtEnd(); return; }
                    foreach (var track in queue)
                        if (CurrentTrack == null || !Names.Equals(CurrentTrack.Name, track.Name)) shuffleBag.Add(track.Name);
                    for (int remaining = shuffleBag.Count - 1; remaining > 0; remaining--)
                    {
                        int choice = random.Next(remaining + 1);
                        var item = shuffleBag[remaining];
                        shuffleBag[remaining] = shuffleBag[choice];
                        shuffleBag[choice] = item;
                    }
                    shuffleInitialized = true;
                }
                if (shuffleBag.Count == 0)
                {
                    if (automatic && Settings.RepeatMode == 2) { StopAtEnd(); return; }
                    PlayQueueEntry(queue[Math.Max(0, index)], false);
                    return;
                }
                PlayQueueEntry(tracksByName[shuffleBag[0]], false);
                return;
            }
            if (automatic && Settings.RepeatMode == 2 && index == queue.Count - 1) { StopAtEnd(); return; }
            PlayQueueEntry(queue[(index + 1) % queue.Count], false);
        }

        private void StopAtEnd()
        {
            SetPlaying(false);
            Seek(0);
        }

        private bool PlayQueueEntry(LocalTrack selected, bool fromHistory)
        {
            var previous = CurrentTrack;
            if (!OpenTrack(selected, true, 0)) return false;
            if (!fromHistory && previous != null && !Names.Equals(previous.Name, selected.Name))
            {
                playbackHistory.Add(previous.Name);
                if (playbackHistory.Count > 100) playbackHistory.RemoveAt(0);
            }
            shuffleBag.RemoveAll(name => Names.Equals(name, selected.Name));
            return true;
        }

        private bool OpenTrack(LocalTrack track, bool autoPlay, double seek)
        {
            CloseTrack();
            PlaybackError = null;
            if (!File.Exists(track.FullPath))
            {
                FailPlayback("audioFileMissing");
                return false;
            }
            CurrentTrack = track;
            pendingPosition = Math.Max(0, seek);
            var media = new MediaPlayer();
            player = media;
            media.MediaOpened += (sender, args) => OnMediaOpened(media);
            media.MediaFailed += (sender, args) => { if (ReferenceEquals(media, player)) FailPlayback("audioUnreadable"); };
            media.MediaEnded += (sender, args) => OnMediaEnded(media);
            try
            {
                media.Volume = IsMuted ? 0 : Settings.LocalVolume;
                media.Open(new Uri(track.FullPath));
            }
            catch (InvalidOperationException) { FailPlayback("audioLoadFailed"); return false; }
            catch (IOException) { FailPlayback("audioLoadFailed"); return false; }
            catch (ArgumentException) { FailPlayback("audioLoadFailed"); return false; }
            SetPlaying(autoPlay);
            return true;
        }

        private void OnMediaOpened(MediaPlayer opened)
        {
            if (disposed || !ReferenceEquals(opened, player)) return;
            MediaReady = true;
            PlaybackError = null;
            Duration = opened.NaturalDuration.HasTimeSpan ? opened.NaturalDuration.TimeSpan.TotalSeconds : 0;
            opened.SpeedRatio = Settings.PlaybackSpeed;
            if (Duration > 0) opened.Position = TimeSpan.FromSeconds(Math.Min(Math.Max(0, Duration - .05), pendingPosition));
            pendingPosition = 0;
            if (sessionVisualizer != null) sessionVisualizer.Rebind();
            SetPlaying(IsPlaying);
        }

        private void OnMediaEnded(MediaPlayer ended)
        {
            if (disposed || !IsPlaying || !ReferenceEquals(ended, player)) return;
            if (Settings.RepeatMode == 1)
            {
                Seek(0);
                SetPlaying(true);
            }
            else MoveNext(true);
        }

        private void SetPlaying(bool playing)
        {
            IsPlaying = playing && player != null;
            try
            {
                if (player != null)
                {
                    if (IsPlaying) player.Play();
                    else player.Pause();
                }
            }
            catch (InvalidOperationException) { FailPlayback("audioPlayFailed"); return; }
            UpdateClock();
            UpdateVisualizer();
            Notify(PlaybackUpdated);
            Changed();
        }

        private void FailPlayback(string key)
        {
            IsPlaying = false;
            MediaReady = false;
            Duration = 0;
            PlaybackError = key;
            CancelPreviewResume();
            UpdateClock();
            UpdateVisualizer();
            Notify(PlaybackUpdated);
            Changed();
        }

        private void CloseTrack()
        {
            var old = player;
            player = null;
            CurrentTrack = null;
            IsPlaying = false;
            MediaReady = false;
            Duration = pendingPosition = 0;
            PlaybackError = null;
            if (old != null) old.Close();
            UpdateVisualizer();
            UpdateClock();
        }

        private void OnClock(object sender, EventArgs args)
        {
            int remaining = SleepMinutes;
            if (sleepDeadline.HasValue && remaining == 0)
            {
                sleepDeadline = null;
                Pause();
                UpdateClock();
            }
            if (remaining != sleepMinutesReported)
            {
                sleepMinutesReported = remaining;
                Changed(false);
            }
            if (!IsPlaying) return;
            Notify(PlaybackUpdated);
            if ((DateTime.UtcNow - lastSave).TotalSeconds >= 15) SaveNow();
        }

        private void UpdateClock()
        {
            if (IsPlaying || sleepDeadline.HasValue)
            {
                clock.Interval = TimeSpan.FromMilliseconds(IsPlaying ? 150 : 1000);
                clock.Start();
            }
            else clock.Stop();
        }

        public void SetVisualizerActive(bool active)
        {
            if (wantsVisualizer == active) return;
            wantsVisualizer = active;
            UpdateVisualizer();
        }

        private void UpdateVisualizer()
        {
            bool active = wantsVisualizer && IsPlaying && MediaReady;
            if (active && sessionVisualizer == null)
            {
                using (var process = Process.GetCurrentProcess()) sessionVisualizer = new SessionAudioVisualizer(process.Id);
            }
            if (sessionVisualizer != null) sessionVisualizer.SetActive(active && !fileSpectrumActive);
            if (!active && fileSpectrum != null) fileSpectrum.Suspend();
            if (!active) fileSpectrumActive = false;
        }

        public double[] ReadVisualizer()
        {
            if (!wantsVisualizer || !IsPlaying || !MediaReady || CurrentTrack == null) return SilentFrame;
            if (fileSpectrum == null && DateTime.UtcNow >= nextSpectrumCheck)
            {
                nextSpectrumCheck = DateTime.UtcNow.AddSeconds(2);
                string ffmpeg = Path.Combine(ToolsDirectory, "ffmpeg.exe");
                if (File.Exists(ffmpeg)) fileSpectrum = new TrackSpectrumReader(ffmpeg);
            }
            double[] levels = fileSpectrum == null ? null : fileSpectrum.Read(CurrentTrack.FullPath, Position);
            bool hasSpectrum = levels != null;
            if (hasSpectrum != fileSpectrumActive)
            {
                fileSpectrumActive = hasSpectrum;
                if (sessionVisualizer != null) sessionVisualizer.SetActive(!hasSpectrum);
            }
            return levels ?? (sessionVisualizer == null ? SilentFrame : sessionVisualizer.ReadFrame());
        }

        public void SelectPlaylist(string playlist)
        {
            string selection = ExistingPlaylistName(playlist);
            if (Names.Equals(ActivePlaylist, selection)) return;
            ActivePlaylist = selection;
            CatalogueChanged();
        }

        public string CreatePlaylist(string name)
        {
            name = (name ?? String.Empty).Trim();
            var failure = PlaylistNameError(name);
            if (failure != null) return failure;
            var existing = FindPlaylist(name);
            if (existing != null) { SelectPlaylist(existing.Name); return null; }
            var next = new List<MusicPlaylist>(Playlists) { new MusicPlaylist(name, new string[0]) };
            if (!CommitPlaylists(next)) return "playlistSaveFailed";
            ActivePlaylist = name;
            CatalogueChanged();
            return null;
        }

        internal string RenamePlaylist(string previousName, string name)
        {
            name = (name ?? String.Empty).Trim();
            var failure = PlaylistNameError(name);
            if (failure != null) return failure;
            var original = FindPlaylist(previousName);
            if (original == null) return "playlistUnavailable";
            if (String.Equals(original.Name, name, StringComparison.Ordinal)) return null;
            var existing = FindPlaylist(name);
            if (existing != null && existing != original) return "playlistNameTaken";

            var replacement = new MusicPlaylist(name, original.Tracks);
            var next = Playlists.Select(playlist => playlist == original ? replacement : playlist).ToList();
            if (!CommitPlaylists(next)) return "playlistSaveFailed";
            if (Names.Equals(ActivePlaylist, original.Name)) ActivePlaylist = name;
            if (Names.Equals(PlaybackPlaylist, original.Name)) PlaybackPlaylist = name;
            var renamed = PlaylistRenamed;
            if (renamed != null) renamed(original.Name, name);
            CatalogueChanged();
            return null;
        }

        private static string PlaylistNameError(string name)
        {
            if (name.Length == 0 || name.Length > 120 || name.Any(Char.IsControl)) return "namePlaylist";
            return Names.Equals(All, name) ? "reservedPlaylist" : null;
        }

        public bool DeletePlaylist(string name)
        {
            var selection = FindPlaylist(name);
            if (selection == null) return false;
            var next = Playlists.Where(playlist => !ReferenceEquals(playlist, selection)).ToList();
            if (!CommitPlaylists(next)) return ReportFailure("playlistSaveFailed");
            if (Names.Equals(ActivePlaylist, selection.Name)) ActivePlaylist = All;
            if (Names.Equals(PlaybackPlaylist, selection.Name))
            {
                PlaybackPlaylist = All;
                ClearShuffle();
            }
            CatalogueChanged();
            return true;
        }

        public bool SetPlaylistMembership(string playlist, IEnumerable<string> names, bool included)
        {
            var target = FindPlaylist(playlist);
            if (target == null || names == null) return false;
            var members = new List<string>(target.Tracks);
            if (included)
            {
                var added = new HashSet<string>(members, Names);
                foreach (var name in names.Where(IsFileName))
                {
                    LocalTrack track;
                    if (tracksByName.TryGetValue(name, out track) && File.Exists(track.FullPath) && added.Add(track.Name))
                        members.Add(track.Name);
                }
            }
            else
            {
                var requested = new HashSet<string>(names.Where(IsFileName), Names);
                members.RemoveAll(requested.Contains);
            }
            return ReplacePlaylistTracks(target, members);
        }

        public bool MoveTrack(string playlist, string from, string to, bool after)
        {
            var target = FindPlaylist(playlist);
            if (target == null || Names.Equals(from, to)) return false;
            // Reorder visible items while preserving slots for temporarily missing files.
            var visible = TracksFor(playlist).Select(track => track.Name).ToList();
            int fromIndex = visible.FindIndex(name => Names.Equals(name, from));
            int toIndex = visible.FindIndex(name => Names.Equals(name, to));
            if (fromIndex < 0 || toIndex < 0) return false;
            var original = visible[fromIndex];
            visible.RemoveAt(fromIndex);
            int insertAt = toIndex - (fromIndex < toIndex ? 1 : 0) + (after ? 1 : 0);
            visible.Insert(insertAt, original);
            int cursor = 0;
            var members = target.Tracks.Select(name => tracksByName.ContainsKey(name) ? visible[cursor++] : name).ToList();
            return ReplacePlaylistTracks(target, members);
        }

        private bool ReplacePlaylistTracks(MusicPlaylist target, List<string> members)
        {
            if (target.Tracks.SequenceEqual(members, Names)) return false;
            var replacement = new MusicPlaylist(target.Name, members);
            var next = Playlists.Select(playlist => ReferenceEquals(playlist, target) ? replacement : playlist).ToList();
            if (!CommitPlaylists(next)) return ReportFailure("playlistSaveFailed");
            if (Names.Equals(PlaybackPlaylist, target.Name)) ClearShuffle();
            CatalogueChanged();
            return true;
        }

        public int ImportFiles(IEnumerable<string> paths)
        {
            if (paths == null) return 0;
            int imported = 0;
            bool error = false;
            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                string temporary = null;
                try
                {
                    string name = Path.GetFileName(path);
                    if (!IsFileName(name) || !AudioExtensions.Contains(Path.GetExtension(name))) continue;
                    var destination = Path.Combine(MusicDirectory, name);
                    if (File.Exists(destination)) continue;
                    temporary = Path.Combine(MusicDirectory, ".importing-" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.Copy(path, temporary);
                    if (File.Exists(destination)) continue;
                    File.Move(temporary, destination);
                    imported++;
                }
                catch (IOException) { error = true; }
                catch (UnauthorizedAccessException) { error = true; }
                catch (ArgumentException) { error = true; }
                finally { DeleteTemporary(temporary); }
            }
            if (imported > 0) RefreshLibrary();
            if (error) ReportFailure("addFailed");
            return imported;
        }

        public bool DeleteTrack(string filename)
        {
            LocalTrack track;
            if (String.IsNullOrEmpty(filename) || !tracksByName.TryGetValue(filename, out track)) return false;
            List<MusicPlaylist> previousMembership = null;
            if (Playlists.Any(playlist => playlist.Tracks.Contains(track.Name, Names)))
            {
                previousMembership = Playlists;
                var next = Playlists.Select(playlist => new MusicPlaylist(playlist.Name,
                    playlist.Tracks.Where(name => !Names.Equals(name, track.Name)))).ToList();
                if (!CommitPlaylists(next)) return ReportFailure("playlistSaveFailed");
            }
            bool isCurrent = CurrentTrack != null && Names.Equals(CurrentTrack.Name, track.Name);
            bool wasPlaying = IsPlaying;
            double previousPosition = Position;
            if (isCurrent) { CancelPreviewResume(); CloseTrack(); }
            try { File.Delete(track.FullPath); }
            catch (IOException) { return RestoreAfterDeleteFailure(track, isCurrent, wasPlaying, previousPosition, previousMembership); }
            catch (UnauthorizedAccessException) { return RestoreAfterDeleteFailure(track, isCurrent, wasPlaying, previousPosition, previousMembership); }
            RefreshLibrary();
            return true;
        }

        private bool RestoreAfterDeleteFailure(LocalTrack track, bool isCurrent, bool wasPlaying, double position,
            List<MusicPlaylist> previousMembership)
        {
            if (previousMembership != null && !CommitPlaylists(previousMembership))
            {
                CatalogueChanged();
                ReportFailure("playlistSaveFailed");
            }
            if (isCurrent && File.Exists(track.FullPath)) OpenTrack(track, wasPlaying, position);
            return ReportFailure("fileDeleteFailed");
        }

        private static void DeleteTemporary(string path)
        {
            if (path == null) return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void RefreshLibrary()
        {
            if (!ReadLibrary()) return;
            if (CurrentTrack != null && !tracksByName.ContainsKey(CurrentTrack.Name))
            {
                CancelPreviewResume();
                CloseTrack();
                Notify(PlaybackUpdated);
            }
            ClearShuffle();
            CatalogueChanged();
        }

        private bool ReadLibrary()
        {
            try
            {
                Directory.CreateDirectory(MusicDirectory);
                Library = Directory.EnumerateFiles(MusicDirectory).Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
                    .Select(path => new LocalTrack(path)).OrderBy(track => track.Name, Names).ToList();
            }
            catch (IOException) { return ReportFailure("actionLibraryChanged"); }
            catch (UnauthorizedAccessException) { return ReportFailure("actionLibraryChanged"); }
            tracksByName = new Dictionary<string, LocalTrack>(Names);
            foreach (var track in Library) tracksByName[track.Name] = track;
            BuildPlaylistViews();
            return true;
        }

        private void BuildPlaylistViews()
        {
            var views = new Dictionary<string, List<LocalTrack>>(Names);
            foreach (var playlist in Playlists)
            {
                var visible = new List<LocalTrack>();
                foreach (var member in playlist.Tracks)
                {
                    LocalTrack track;
                    if (tracksByName.TryGetValue(member, out track)) visible.Add(track);
                }
                views[playlist.Name] = visible;
            }
            playlistViews = views;
        }

        private bool CommitPlaylists(List<MusicPlaylist> next)
        {
            try { persistence.WritePlaylists(next); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            Playlists = next;
            BuildPlaylistViews();
            return true;
        }

        private List<LocalTrack> Queue() { return TracksFor(PlaybackPlaylist); }

        private static int TrackIndex(List<LocalTrack> tracks, LocalTrack track)
        {
            return track == null ? -1 : tracks.FindIndex(entry => Names.Equals(entry.Name, track.Name));
        }

        private MusicPlaylist FindPlaylist(string name)
        {
            return Playlists.FirstOrDefault(playlist => Names.Equals(playlist.Name, name));
        }

        private string ExistingPlaylistName(string name)
        {
            var playlist = FindPlaylist(name);
            return playlist == null ? All : playlist.Name;
        }

        private void ClearShuffle()
        {
            shuffleBag.Clear();
            shuffleInitialized = false;
        }

        private bool ReportFailure(string key)
        {
            var handler = Problem;
            if (handler != null) handler(key);
            return false;
        }

        private static void Notify(Action handler) { if (handler != null) handler(); }

        private void CatalogueChanged()
        {
            Notify(LibraryChanged);
            Changed();
        }

        private void Changed(bool save = true)
        {
            if (save) ScheduleSave();
            Notify(StateChanged);
        }

        private void ScheduleSave()
        {
            settingsDirty = true;
            if (!saveTimer.IsEnabled) saveTimer.Start();
        }

        public void SaveNow()
        {
            if (disposed) return;
            saveTimer.Stop();
            Settings.ActivePlaylist = ActivePlaylist;
            Settings.PlaybackPlaylist = PlaybackPlaylist;
            Settings.CurrentTrackName = CurrentTrack == null ? null : CurrentTrack.Name;
            Settings.CurrentTrackPositionSeconds = Math.Round(Position, 1);
            lastSave = DateTime.UtcNow;
            try { persistence.WriteSettings(Settings); settingsDirty = false; }
            catch (IOException) { settingsDirty = true; }
            catch (UnauthorizedAccessException) { settingsDirty = true; }
        }

        public void Dispose()
        {
            if (disposed) return;
            if (settingsDirty || CurrentTrack != null) SaveNow();
            disposed = true;
            saveTimer.Stop();
            wantsVisualizer = false;
            sleepDeadline = null;
            CloseTrack();
            if (fileSpectrum != null) fileSpectrum.Dispose();
            if (sessionVisualizer != null) sessionVisualizer.Dispose();
        }
    }
}
