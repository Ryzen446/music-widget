using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MusicWidget.NativeApp
{
    internal sealed class OnlineInstallProgress
    {
        public string Tool { get; private set; }
        public long BytesReceived { get; private set; }
        public long TotalBytes { get; private set; }
        public bool Verifying { get; private set; }

        public OnlineInstallProgress(string tool, long received, long total, bool verifying)
        {
            Tool = tool;
            BytesReceived = received;
            TotalBytes = total;
            Verifying = verifying;
        }
    }

    internal static class OnlineToolInstaller
    {
        private const long MaxReleaseSize = 500L * 1024 * 1024;
        private const long MaxExecutableSize = 300L * 1024 * 1024;
        private static readonly string[] ToolFiles = { "yt-dlp.exe", "deno.exe", "ffmpeg.exe", "ffprobe.exe" };

        public static bool IsAvailable(string directory)
        {
            return IsPresent(Path.Combine(directory, "yt-dlp.exe"));
        }

        public static bool IsComplete(string directory)
        {
            return ToolFiles.All(file => IsPresent(Path.Combine(directory, file)));
        }

        private static bool IsPresent(string path)
        {
            try { return File.Exists(path) && new FileInfo(path).Length > 0; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static Task InstallAsync(string toolsDirectory, IProgress<OnlineInstallProgress> progress, CancellationToken cancellation)
        {
            string directory = Path.GetFullPath(toolsDirectory);
            return Task.Run(() => Install(directory, progress, cancellation), cancellation);
        }

        private static void Install(string directory, IProgress<OnlineInstallProgress> progress, CancellationToken cancellation)
        {
            Directory.CreateDirectory(directory);
            string lockPath = Path.Combine(directory, ".online-tools.lock");
            using (var guard = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                string[] missing = ToolFiles.Where(file => !IsPresent(Path.Combine(directory, file))).ToArray();
                if (missing.Length == 0) return;
                if (!Environment.Is64BitOperatingSystem)
                    throw new PlatformNotSupportedException("64-bit Windows is required to install the online tools.");

                string staging = Path.Combine(directory, ".install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    if (missing.Contains("yt-dlp.exe"))
                    {
                        var asset = FindAsset("yt-dlp/yt-dlp", @"^yt-dlp\.exe$", cancellation);
                        Download("yt-dlp", asset, Path.Combine(staging, "yt-dlp.exe"), progress, cancellation);
                    }
                    if (missing.Contains("deno.exe"))
                    {
                        var asset = FindAsset("denoland/deno", @"^deno-x86_64-pc-windows-msvc\.zip$", cancellation);
                        string archive = Path.Combine(staging, "deno.zip");
                        Download("Deno", asset, archive, progress, cancellation);
                        Extract(archive, @"^deno\.exe$", Path.Combine(staging, "deno.exe"), cancellation);
                    }
                    var ffmpegFiles = missing.Where(file => file == "ffmpeg.exe" || file == "ffprobe.exe").ToArray();
                    if (ffmpegFiles.Length != 0)
                    {
                        var asset = FindAsset("GyanD/codexffmpeg", @"^ffmpeg-\d+(\.\d+)+-essentials_build\.zip$", cancellation);
                        string archive = Path.Combine(staging, "ffmpeg.zip");
                        Download("FFmpeg", asset, archive, progress, cancellation);
                        foreach (var file in ffmpegFiles)
                            Extract(archive, @"^ffmpeg-\d+(\.\d+)+-essentials_build/bin/" +
                                Regex.Escape(file) + "$", Path.Combine(staging, file), cancellation);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    foreach (var file in missing)
                    {
                        string destination = Path.Combine(directory, file);
                        if (IsPresent(destination)) continue;
                        string source = Path.Combine(staging, file);
                        if (File.Exists(destination)) File.Replace(source, destination, null);
                        else File.Move(source, destination);
                    }
                    if (!IsComplete(directory)) throw new IOException("The online tools were not fully installed.");
                }
                finally { DeleteStaging(staging); }
            }
        }

        private sealed class ReleaseAsset
        {
            public Uri Uri;
            public string Digest;
            public long Size;
        }

        private static ReleaseAsset FindAsset(string repository, string filePattern, CancellationToken cancellation)
        {
            var uri = new Uri("https://api.github.com/repos/" + repository + "/releases/latest");
            using (var text = new MemoryStream())
            {
                OnlineHttp.Read(uri, host => host == "api.github.com", 30000,
                    "application/vnd.github+json", cancellation, (response, source) =>
                        OnlineHttp.CopyBounded(source, text, 2 * 1024 * 1024, cancellation, null));
                var serializer = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 32 };
                var data = serializer.DeserializeObject(Encoding.UTF8.GetString(text.ToArray())) as IDictionary<string, object>;
                object assets;
                if (data == null || !data.TryGetValue("assets", out assets) || !(assets is object[]))
                    throw new InvalidDataException("Missing GitHub release assets.");

                var matches = ((object[])assets).OfType<IDictionary<string, object>>().Where(item =>
                    GetText(item, "state") == "uploaded" &&
                    Regex.IsMatch(GetText(item, "name"), filePattern, RegexOptions.CultureInvariant)).ToList();
                if (matches.Count != 1) throw new InvalidDataException("Cannot identify the Windows release for " + repository);
                var match = matches[0];
                Uri download;
                long size;
                object rawSize;
                string digest = GetText(match, "digest");
                if (!Uri.TryCreate(GetText(match, "browser_download_url"), UriKind.Absolute, out download) ||
                    !OnlineHttp.IsApproved(download, host => host == "github.com") ||
                    !download.AbsolutePath.StartsWith("/" + repository + "/releases/download/", StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(digest, @"^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) ||
                    !match.TryGetValue("size", out rawSize) || !Int64.TryParse(Convert.ToString(rawSize), out size) ||
                    size <= 0 || size > MaxReleaseSize)
                    throw new InvalidDataException("The release metadata could not be verified for " + repository);

                return new ReleaseAsset { Uri = download, Digest = digest.Substring(7), Size = size };
            }
        }

        private static string GetText(IDictionary<string, object> item, string key)
        {
            object value;
            return item.TryGetValue(key, out value) && value is string ? (string)value : String.Empty;
        }

        private static void Download(string tool, ReleaseAsset asset, string destination,
            IProgress<OnlineInstallProgress> progress, CancellationToken cancellation)
        {
            var watch = Stopwatch.StartNew();
            Report(progress, tool, 0, asset.Size, false);
            long size = 0;
            using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                OnlineHttp.Read(asset.Uri, host => host == "github.com" || host == "release-assets.githubusercontent.com" ||
                    host == "objects.githubusercontent.com" || host == "github-releases.githubusercontent.com",
                    45000, "application/octet-stream", cancellation, (response, source) =>
                    {
                        size = OnlineHttp.CopyBounded(source, file, asset.Size, cancellation, bytes =>
                        {
                            if (watch.ElapsedMilliseconds < 180) return;
                            Report(progress, tool, bytes, asset.Size, false);
                            watch.Restart();
                        });
                    });
            }
            if (size != asset.Size) throw new InvalidDataException(tool + " download size did not match.");
            Report(progress, tool, size, asset.Size, true);
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(destination))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", String.Empty);
                if (!String.Equals(actual, asset.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(tool + " download checksum did not match.");
            }
        }

        private static void Extract(string archive, string entryPattern, string destination, CancellationToken cancellation)
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                var entries = zip.Entries.Where(entry =>
                    Regex.IsMatch(entry.FullName, entryPattern, RegexOptions.CultureInvariant)).ToList();
                if (entries.Count != 1 || entries[0].Length <= 0 || entries[0].Length > MaxExecutableSize)
                    throw new InvalidDataException("The verified archive does not contain the expected executable.");

                using (var input = entries[0].Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    if (OnlineHttp.CopyBounded(input, output, entries[0].Length, cancellation, null) != entries[0].Length)
                        throw new InvalidDataException("The archive executable was truncated.");
            }
        }

        private static void Report(IProgress<OnlineInstallProgress> progress, string tool, long bytes, long total, bool verifying)
        {
            if (progress != null) progress.Report(new OnlineInstallProgress(tool, bytes, total, verifying));
        }

        private static void DeleteStaging(string staging)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(staging)) File.Delete(file);
                Directory.Delete(staging, false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class OnlineHttp
    {
        public static bool IsApproved(Uri uri, Func<string, bool> approveHost)
        {
            return uri != null && uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
                String.IsNullOrEmpty(uri.UserInfo) && approveHost(uri.DnsSafeHost.ToLowerInvariant());
        }

        public static void Read(Uri uri, Func<string, bool> approveHost, int timeout, string accept,
            CancellationToken cancellation, Action<HttpWebResponse, Stream> consume)
        {
            // Keep Windows TLS defaults where supported, with TLS 1.2 available for older .NET runtime defaults.
            if (ServicePointManager.SecurityProtocol != (SecurityProtocolType)0)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!IsApproved(uri, approveHost)) throw new InvalidDataException("Unexpected download address.");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false;
                request.UserAgent = "MusicWidget-OnlineTools";
                request.Timeout = timeout;
                request.ReadWriteTimeout = timeout;
                request.Accept = accept;
                using (cancellation.Register(request.Abort))
                {
                    try
                    {
                        using (var response = (HttpWebResponse)request.GetResponse())
                        {
                            int code = (int)response.StatusCode;
                            if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
                            {
                                string location = response.Headers[HttpResponseHeader.Location];
                                Uri redirected;
                                if (!Uri.TryCreate(uri, location, out redirected))
                                    throw new InvalidDataException("The download redirect is invalid.");
                                uri = redirected;
                                continue;
                            }
                            if (code != 200) throw new InvalidDataException("The server did not return the requested file.");
                            using (Stream stream = response.GetResponseStream()) consume(response, stream);
                            return;
                        }
                    }
                    catch (WebException) { cancellation.ThrowIfCancellationRequested(); throw; }
                    catch (IOException) { cancellation.ThrowIfCancellationRequested(); throw; }
                }
            }
            throw new InvalidDataException("Too many download redirects.");
        }

        public static long CopyBounded(Stream source, Stream target, long limit,
            CancellationToken cancellation, Action<long> progress)
        {
            var buffer = new byte[64 * 1024];
            long total = 0;
            for (;;)
            {
                cancellation.ThrowIfCancellationRequested();
                int received = source.Read(buffer, 0, buffer.Length);
                if (received == 0) return total;
                total += received;
                if (total > limit) throw new InvalidDataException("The download exceeds its expected size.");
                target.Write(buffer, 0, received);
                if (progress != null) progress(total);
            }
        }
    }
}
