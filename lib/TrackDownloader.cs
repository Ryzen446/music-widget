using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;

public static class TrackDownloader
{
    private const string PublicOptions = "--ignore-config --no-plugin-dirs --no-cache-dir --no-cookies --no-cookies-from-browser ";
    private const string FileMarker = "MusicWidgetFile:";
    private const string ProgressMarker = "MusicWidgetProgress:";
    private const string ConversionMarker = "MusicWidgetConversion:ExtractAudio|";

    private static string Quote(string value)
    {
        return "\"" + Regex.Replace(value, @"(\\*)""|(\\+)$", match =>
            new string('\\', match.Value.Length * 2 - (match.Groups[1].Success ? 1 : 0)) +
            (match.Groups[1].Success ? "\"" : "")) + "\"";
    }

    private static string MoveIntoLibrary(string file, string library)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        string extension = Path.GetExtension(file);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] downloadedHash = null;
            for (int number = 1; ; number++)
            {
                string candidate = Path.Combine(library, number == 1 ? name + extension : name + " (" + number + ")" + extension);
                try { File.Move(file, candidate); return candidate; }
                catch (IOException)
                {
                    if (!File.Exists(candidate)) throw;
                    if (new FileInfo(file).Length != new FileInfo(candidate).Length) continue;
                    if (downloadedHash == null)
                    {
                        using (var downloaded = File.OpenRead(file)) downloadedHash = sha.ComputeHash(downloaded);
                    }
                    using (var existing = File.OpenRead(candidate))
                    {
                        if (System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(downloadedHash, sha.ComputeHash(existing)))
                            return candidate;
                    }
                }
            }
        }
    }

    private static async Task<string> ReadDownloadOutput(StreamReader output, Action<string, long, long> report)
    {
        string artifact = null;
        long received = 0;
        long total = 0;
        string line;
        while ((line = await output.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            if (line.StartsWith(FileMarker, StringComparison.Ordinal))
                artifact = line.Substring(FileMarker.Length);
            else if (line.StartsWith(ProgressMarker, StringComparison.Ordinal))
            {
                string[] fields = line.Substring(ProgressMarker.Length).Split('|');
                if (fields.Length != 4) continue;
                long receivedNow, exact, estimated;
                if (Int64.TryParse(fields[1], out receivedNow) && receivedNow >= 0) received = receivedNow;
                Int64.TryParse(fields[2], out exact);
                Int64.TryParse(fields[3], out estimated);
                if (exact > 0 || estimated > 0) total = exact > 0 ? exact : estimated;
                if (fields[0] == "downloading")
                    report("downloadTransferring", received, total);
                else if (fields[0] == "finished")
                {
                    if (received > 0) total = received;
                    report("downloadFinalizing", received, total);
                }
            }
            else if (line.Equals(ConversionMarker + "started", StringComparison.Ordinal))
                report("downloadAudioConversion", received, total);
            else if (line.Equals(ConversionMarker + "finished", StringComparison.Ordinal))
                report("downloadFinalizing", received, total);
        }
        return artifact;
    }

    public static void StartDownload(
        string rawUrl,
        string ytdlpPath,
        string outputDir,
        System.Windows.Threading.Dispatcher dispatcher,
        Action<string, long, long> onProgress,
        Action<bool, string> onCompleted)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            string staging = null;
            Action<string, long, long> report = (status, received, total) =>
                dispatcher.BeginInvoke(new Action(() => onProgress(status, received, total)));
            try
            {
                string input = rawUrl.Trim();
                Uri link = new Uri(input, UriKind.Absolute);
                bool isSpotify = link.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase);
                string binDir = Path.GetDirectoryName(ytdlpPath);
                string bundledDeno = Path.Combine(binDir, "deno.exe");
                string publicOptions = PublicOptions;
                if (File.Exists(bundledDeno)) publicOptions += "--js-runtimes " + Quote("deno:" + bundledDeno) + " ";
                string downloadUrl = input;
                string finalTitle = "";

                if (isSpotify)
                {
                    report("downloadTrackCheck", 0, 0);

                    string title = "";
                    string artist = "";
                    int targetDuration = 0;

                    try
                    {
                        var trackId = Regex.Match(link.AbsolutePath, @"^/(?:intl-[A-Za-z-]+/)?track/([A-Za-z0-9]{22})/?$");
                        if (!trackId.Success) throw new ArgumentException("invalidLink");
                        var request = (HttpWebRequest)WebRequest.Create("https://open.spotify.com/embed/track/" + trackId.Groups[1].Value);
                        request.AllowAutoRedirect = false;
                        request.Timeout = 8000;
                        request.ReadWriteTimeout = 8000;
                        request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                        using (var response = request.GetResponse())
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            string html = reader.ReadToEnd();

                            System.Text.RegularExpressions.Match mNext = System.Text.RegularExpressions.Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">([^<]+)</script>");
                            if (mNext.Success)
                            {
                                string json = mNext.Groups[1].Value;
                                System.Text.RegularExpressions.Match mName = System.Text.RegularExpressions.Regex.Match(json, @"""name"":\s*""([^""]+)""");
                                if (mName.Success) { title = mName.Groups[1].Value; }

                                System.Text.RegularExpressions.Match mArt = System.Text.RegularExpressions.Regex.Match(json, @"""artists"":\s*\[\{\s*""name"":\s*""([^""]+)""");
                                if (mArt.Success) { artist = mArt.Groups[1].Value; }

                                System.Text.RegularExpressions.Match mDur = System.Text.RegularExpressions.Regex.Match(json, @"""duration"":\s*([0-9]+)");
                                if (mDur.Success)
                                {
                                    int durMs = int.Parse(mDur.Groups[1].Value);
                                    targetDuration = durMs / 1000;
                                }
                            }
                            else
                            {
                                System.Text.RegularExpressions.Match mTitle = System.Text.RegularExpressions.Regex.Match(html, @"<meta\s+property=""og:title""\s+content=""([^""]+)""");
                                if (mTitle.Success) { title = mTitle.Groups[1].Value.Trim(); }

                                System.Text.RegularExpressions.Match mDesc = System.Text.RegularExpressions.Regex.Match(html, @"<meta\s+name=""description""\s+content=""[^·]+·\s*([^·]+)\s*·");
                                if (mDesc.Success) { artist = mDesc.Groups[1].Value.Trim(); }
                            }
                        }
                    }
                    catch { }

                    title = System.Net.WebUtility.HtmlDecode(title).Trim();
                    artist = System.Net.WebUtility.HtmlDecode(artist).Trim();

                    string searchQuery = !string.IsNullOrEmpty(artist) ? (artist + " " + title) : title;
                    if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("downloadFailed");
                    downloadUrl = "ytsearch1:" + searchQuery;
                    finalTitle = !string.IsNullOrEmpty(artist) ? (artist + " - " + title) : title;

                    // Ultra-fast search query matching exact Spotify studio duration
                    try
                    {
                        System.Diagnostics.ProcessStartInfo psiSearch = new System.Diagnostics.ProcessStartInfo();
                        psiSearch.FileName = ytdlpPath;
                        psiSearch.Arguments = publicOptions + "--flat-playlist --encoding utf-8 --print " +
                            Quote("%(id)s|%(duration)s") + " --no-warnings -- " + Quote("ytsearch8:" + searchQuery);
                        psiSearch.StandardOutputEncoding = Encoding.UTF8;
                        psiSearch.UseShellExecute = false;
                        psiSearch.CreateNoWindow = true;
                        psiSearch.RedirectStandardOutput = true;
                        psiSearch.RedirectStandardError = true;

                        using (System.Diagnostics.Process pSearch = System.Diagnostics.Process.Start(psiSearch))
                        {
                            var outputTask = pSearch.StandardOutput.ReadToEndAsync();
                            var errorTask = pSearch.StandardError.ReadToEndAsync();
                            if (!pSearch.WaitForExit(60000)) { pSearch.Kill(); throw new TimeoutException("searchTimeout"); }
                            string stdout = outputTask.Result;
                            errorTask.Wait();

                            string[] lines = stdout.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            string bestUrl = "";
                            int minDiff = int.MaxValue;

                            foreach (string line in lines)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                string[] parts = line.Split('|');
                                if (parts.Length >= 2)
                                {
                                    string candidateId = parts[0].Trim();
                                    if (!Regex.IsMatch(candidateId, @"^[A-Za-z0-9_-]{11}$")) continue;
                                    string candUrl = "https://www.youtube.com/watch?v=" + candidateId;
                                    int candDur = 0;
                                    int.TryParse(parts[1].Trim(), out candDur);

                                    if (string.IsNullOrEmpty(bestUrl)) { bestUrl = candUrl; }

                                    if (targetDuration > 0 && candDur > 0)
                                    {
                                        int diff = Math.Abs(candDur - targetDuration);
                                        if (diff < minDiff)
                                        {
                                            minDiff = diff;
                                            bestUrl = candUrl;
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(bestUrl))
                            {
                                downloadUrl = bestUrl;
                            }
                        }
                    }
                    catch
                    {
                        // Use the single-result search if ranking is unavailable.
                    }
                }

                string outTemplate = (String.IsNullOrEmpty(finalTitle) ? "%(title)s" :
                    Regex.Replace(finalTitle, @"[\\/:*?""<>|]", "_").Replace("%", "%%")) + ".%(ext)s";
                bool hasFfmpeg = File.Exists(Path.Combine(binDir, "ffmpeg.exe"));

                string audioArgs = hasFfmpeg
                    ? "--ffmpeg-location " + Quote(binDir) + " -x --audio-format mp3 --audio-quality 0"
                    : "-f \"140/139/ba[ext=m4a]\"";

                string libraryPath = Path.GetFullPath(outputDir);
                staging = Path.Combine(libraryPath, ".download-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                var start = new ProcessStartInfo(ytdlpPath,
                    publicOptions + audioArgs + " --no-playlist --playlist-items 1 --no-warnings --no-simulate --no-overwrites" +
                    " --windows-filenames --trim-filenames 170 --encoding utf-8 --progress --newline --progress-delta 0.25 " +
                    "--progress-template " + Quote("download:" + ProgressMarker + "%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s") +
                    " --progress-template " + Quote("postprocess:MusicWidgetConversion:%(progress.postprocessor)s|%(progress.status)s") + " --print " +
                    Quote("after_move:" + FileMarker + "%(filepath)s") + " -P " + Quote(staging) +
                    " -o " + Quote(outTemplate) + " -- " + Quote(downloadUrl)) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(start))
                {
                    var output = ReadDownloadOutput(process.StandardOutput, report);
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(300000)) { process.Kill(); throw new TimeoutException("downloadTooLong"); }
                    if (output.Wait(1000) && process.ExitCode == 0 && !String.IsNullOrEmpty(output.Result))
                    {
                        string path = Path.GetFullPath(output.Result);
                        if (String.Equals(Path.GetDirectoryName(path), staging, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                        {
                            string saved = MoveIntoLibrary(path, libraryPath);
                            dispatcher.BeginInvoke(new Action(() => onCompleted(true, saved)));
                            return;
                        }
                    }
                }

                dispatcher.BeginInvoke(new Action(() => onCompleted(false, "downloadFailed")));
            }
            catch (Exception ex)
            {
                string reason = ex is TimeoutException ? "downloadTooLong" : "downloadFailed";
                dispatcher.BeginInvoke(new Action(() => onCompleted(false, reason)));
            }
            finally
            {
                if (staging != null && Directory.Exists(staging))
                {
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(staging)) File.Delete(file);
                        Directory.Delete(staging);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        });
    }
}
