using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace MusicWidget.NativeApp
{
    internal sealed class OnlinePreview : IDisposable
    {
        private readonly PlayerModel model;
        private readonly DispatcherTimer timer;
        private MediaPlayer player;
        private TrackPreviewProcess request;
        private CancellationTokenSource download;
        private string downloadedFile;
        private string toolPath;
        private Uri stream;
        private DateTime openingDeadline;
        private bool directOpening;
        private bool resumePlayback;

        public string TrackId { get; private set; }
        public string Error { get; private set; }
        public bool IsLoading { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsActive { get { return TrackId != null; } }
        public event Action Changed;

        public OnlinePreview(PlayerModel model)
        {
            this.model = model;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += Poll;
            model.StateChanged += ModelChanged;
        }

        public void Toggle(TrackSearchItem item, string executable)
        {
            if (String.Equals(TrackId, item.Id, StringComparison.Ordinal))
            {
                if (IsLoading) Stop(true);
                else if (IsPlaying) Pause();
                else Play();
                return;
            }
            Reset(true);
            TrackId = item.Id;
            toolPath = executable;
            IsLoading = true;
            try
            {
                request = new TrackPreviewProcess(item.Id, executable);
                timer.Start();
                RaiseChanged();
            }
            catch (Exception) { Fail("previewToolFailed"); }
        }

        private void Poll(object sender, EventArgs args)
        {
            if (request != null && request.IsCompleted)
            {
                string url = request.StreamUrl;
                request.Dispose();
                request = null;
                if (!Uri.TryCreate(url, UriKind.Absolute, out stream))
                {
                    StartLocalFallback();
                    return;
                }
                OpenMedia(stream, true);
                return;
            }
            if (IsLoading && player != null && DateTime.UtcNow > openingDeadline)
            {
                if (directOpening) StartLocalFallback();
                else Fail("previewOpenFailed");
            }
        }

        private void OpenMedia(Uri source, bool direct)
        {
            CloseMedia();
            directOpening = direct;
            openingDeadline = DateTime.UtcNow.AddSeconds(direct ? 4 : 15);
            player = new MediaPlayer();
            player.MediaOpened += MediaOpened;
            player.MediaEnded += MediaEnded;
            player.MediaFailed += MediaFailed;
            try
            {
                // Open starts buffering. Keep the current track running until the preview is ready.
                player.Open(source);
                timer.Start();
            }
            catch (Exception)
            {
                if (direct) StartLocalFallback();
                else Fail("previewOpenFailed");
            }
        }

        private void MediaOpened(object sender, EventArgs args)
        {
            if (!ReferenceEquals(player, sender) || !IsLoading) return;
            IsLoading = false;
            timer.Stop();
            Play();
        }

        private void Play()
        {
            resumePlayback = model.PreparePreview();
            ModelChanged();
            try { player.Play(); }
            catch (Exception) { Fail("previewPlayFailed"); return; }
            IsPlaying = true;
            RaiseChanged();
        }

        private void Pause()
        {
            try { player.Pause(); }
            catch (Exception) { Fail("previewPlayFailed"); return; }
            IsPlaying = false;
            if (resumePlayback) model.ResumeFromPreview(true);
            resumePlayback = false;
            RaiseChanged();
        }

        private void MediaEnded(object sender, EventArgs args)
        {
            if (ReferenceEquals(player, sender)) Stop(true);
        }

        private void MediaFailed(object sender, ExceptionEventArgs args)
        {
            if (!ReferenceEquals(player, sender)) return;
            if (IsLoading && directOpening) StartLocalFallback();
            else Fail("previewPlayFailed");
        }

        private async void StartLocalFallback()
        {
            CloseMedia();
            timer.Stop();
            if (TrackId == null || download != null) { Fail("previewOpenFailed"); return; }
            string id = TrackId, executable = toolPath;
            var pending = new CancellationTokenSource(TimeSpan.FromSeconds(80));
            download = pending;
            try
            {
                string path = await Task.Run(() => DownloadAudio(id, executable, pending.Token), pending.Token);
                if (!ReferenceEquals(download, pending))
                {
                    DeleteTemporaryFile(path);
                    return;
                }
                download = null;
                downloadedFile = path;
                OpenMedia(new Uri(path), false);
            }
            catch (Exception)
            {
                if (ReferenceEquals(download, pending))
                {
                    download = null;
                    Fail("previewOpenFailed");
                }
            }
            finally { pending.Dispose(); }
        }

        private static string DownloadAudio(string id, string executable, CancellationToken cancellation)
        {
            string url = TrackSearch.CanonicalYoutubeUrl(id, null);
            if (url.Length == 0 || !File.Exists(executable)) throw new InvalidDataException();
            const long fileLimit = 192L * 1024 * 1024;
            string work = Path.Combine(Path.GetTempPath(), "MusicWidget-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string bin = Path.GetDirectoryName(executable);
                string arguments = "--ignore-config --no-plugin-dirs --no-cache-dir --no-cookies --no-cookies-from-browser " +
                    "--no-playlist --no-warnings --no-progress --no-simulate --encoding utf-8 --match-filters \"!is_live\" " +
                    "--socket-timeout 12 --retries 0 --extractor-retries 0 --max-filesize 192M " +
                    "-f \"bestaudio[ext=m4a][protocol=https]/bestaudio[protocol=https]\" ";
                if (File.Exists(Path.Combine(bin, "ffmpeg.exe")))
                    arguments += "--ffmpeg-location " + TrackSearch.QuoteWindowsArgument(bin) + " -x --audio-format mp3 --audio-quality 4 ";
                arguments += TrackSearch.BundledRuntimeArguments(executable) + "--print after_move:filepath -P " +
                    TrackSearch.QuoteWindowsArgument(work) + " -o preview.%(ext)s -- " + TrackSearch.QuoteWindowsArgument(url);
                var start = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work,
                    RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
                };
                using (var process = new Process { StartInfo = start })
                using (var job = SearchProcessJob.TryCreate())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!process.Start()) throw new IOException();
                    bool ownsTree = job != null && job.Assign(process);
                    var output = process.StandardOutput.ReadToEndAsync();
                    process.BeginErrorReadLine();
                    var sizeCheck = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        if (!cancellation.IsCancellationRequested)
                        {
                            if (sizeCheck.ElapsedMilliseconds < 1000) continue;
                            sizeCheck.Restart();
                            bool tooLarge = false;
                            foreach (var item in new DirectoryInfo(work).EnumerateFiles())
                            {
                                try { if (item.Exists && item.Length > fileLimit) tooLarge = true; }
                                catch (FileNotFoundException) { }
                            }
                            if (!tooLarge) continue;
                        }
                        SearchProcessJob.StopTree(process, ownsTree ? job : null);
                        cancellation.ThrowIfCancellationRequested();
                        throw new IOException();
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0 || !output.Wait(1000) || String.IsNullOrWhiteSpace(output.Result))
                        throw new IOException();
                    string path = Path.GetFullPath(output.Result.Trim());
                    if (!String.Equals(Path.GetDirectoryName(path), work, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(path) || new FileInfo(path).Length > fileLimit) throw new InvalidDataException();
                    string temporary = work + Path.GetExtension(path);
                    File.Move(path, temporary);
                    return temporary;
                }
            }
            finally
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(work)) File.Delete(file);
                    Directory.Delete(work, false);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void ModelChanged()
        {
            if (player != null)
            {
                player.Volume = model.Settings.LocalVolume;
                player.IsMuted = model.Settings.IsMuted;
            }
        }

        public void Stop(bool resume)
        {
            if (!IsActive && String.IsNullOrEmpty(Error)) return;
            Reset(resume);
            RaiseChanged();
        }

        private void Fail(string key)
        {
            Reset(true);
            Error = key;
            RaiseChanged();
        }

        private void Reset(bool resume)
        {
            timer.Stop();
            if (request != null) { request.Dispose(); request = null; }
            var downloading = download;
            download = null;
            if (downloading != null) downloading.Cancel();
            CloseMedia();
            DeleteTemporaryFile(downloadedFile);
            downloadedFile = null;
            toolPath = null;
            stream = null;
            TrackId = null;
            Error = null;
            IsLoading = false;
            IsPlaying = false;
            bool hadSuspendedTrack = resumePlayback;
            resumePlayback = false;
            if (hadSuspendedTrack) model.ResumeFromPreview(resume);
        }

        private void CloseMedia()
        {
            var previous = player;
            player = null;
            if (previous == null) return;
            previous.MediaOpened -= MediaOpened;
            previous.MediaEnded -= MediaEnded;
            previous.MediaFailed -= MediaFailed;
            previous.Close();
        }

        private static void DeleteTemporaryFile(string path)
        {
            if (path == null) return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void RaiseChanged()
        {
            if (Changed != null) Changed();
        }

        public void Dispose()
        {
            model.StateChanged -= ModelChanged;
            Reset(false);
        }
    }
}
