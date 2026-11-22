using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public sealed class TrackSearchItem
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Artist { get; set; }
    public string DurationLabel { get; set; }
    public bool IsLive { get; set; }
    public string Url { get; set; }
}

public sealed class TrackSearchResult
{
    public string Query { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasNext { get; set; }
    public bool Cancelled { get; set; }
    public string Error { get; set; }
    public List<TrackSearchItem> Items { get; set; }

    internal TrackSearchResult(string query, int page, int pageSize)
    {
        Query = query; Page = page; PageSize = pageSize;
        Error = String.Empty; Items = new List<TrackSearchItem>();
    }
}

public static class TrackSearch
{
    internal const int TimeoutMilliseconds = 28000;
    internal const int MaximumJsonCharacters = 4 * 1024 * 1024;
    private static readonly Regex VideoId = new Regex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant);

    public static TrackSearchRequest TaskStart(string query, int page, int pageSize, string ytdlpPath)
    {
        query = NormalizeQuery(query);
        ValidatePage(page, pageSize);
        if (String.IsNullOrWhiteSpace(ytdlpPath)) throw new ArgumentException("Chemin de recherche manquant.");
        return new TrackSearchRequest(query, page, pageSize, Path.GetFullPath(ytdlpPath));
    }

    public static string NormalizeQuery(string query)
    {
        query = (query ?? String.Empty).Trim();
        if (query.Length == 0 || query.Length > 200)
            throw new ArgumentException("invalidSearch");
        foreach (char character in query)
            if (Char.IsControl(character)) throw new ArgumentException("invalidSearch");
        return query;
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || page > 10 || pageSize < 1 || pageSize > 20)
            throw new ArgumentOutOfRangeException("page", "invalidSearch");
    }

    // Windows CommandLineToArgvW / C runtime quoting. Shell syntax never runs:
    // ProcessStartInfo uses the executable directly, with UseShellExecute=false.
    public static string QuoteWindowsArgument(string argument)
    {
        if (argument == null) argument = String.Empty;
        StringBuilder escaped = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"')
            {
                escaped.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            escaped.Append('\\', slashes).Append(character);
            slashes = 0;
        }
        escaped.Append('\\', slashes * 2).Append('"');
        return escaped.ToString();
    }

    internal static string BundledRuntimeArguments(string executable)
    {
        string deno = Path.Combine(Path.GetDirectoryName(executable), "deno.exe");
        return File.Exists(deno) ? "--js-runtimes " + QuoteWindowsArgument("deno:" + deno) + " " : "";
    }

    public static string BuildArguments(string query, int page, int pageSize)
    {
        query = NormalizeQuery(query); ValidatePage(page, pageSize);
        int start = (page - 1) * pageSize + 1;
        int end = page * pageSize + 1; // One look-ahead row, never downloaded.
        string[] arguments = {
            "--ignore-config", "--no-plugin-dirs", "--no-cookies", "--no-cookies-from-browser",
            "--no-cache-dir", "--flat-playlist", "--dump-single-json", "--skip-download", "--simulate",
            "--no-progress", "--no-warnings", "--socket-timeout", "8", "--retries", "0",
            "--extractor-retries", "0", "--encoding", "utf-8", "--xff", "never",
            "--playlist-start", start.ToString(CultureInfo.InvariantCulture),
            "--playlist-end", end.ToString(CultureInfo.InvariantCulture), "--",
            "ytsearch" + end.ToString(CultureInfo.InvariantCulture) + ":" + query
        };
        List<string> quoted = new List<string>();
        foreach (string argument in arguments) quoted.Add(QuoteWindowsArgument(argument));
        return String.Join(" ", quoted.ToArray());
    }

    private static string Text(IDictionary<string, object> item, string key, int maxLength)
    {
        object value;
        if (!item.TryGetValue(key, out value) || !(value is string)) return String.Empty;
        string result = Regex.Replace((string)value, @"[\p{Cc}\p{Cf}]+", " ").Trim();
        return result.Length > maxLength ? result.Substring(0, maxLength) : result;
    }

    public static string CanonicalYoutubeUrl(string id, string rawUrl)
    {
        if (id != null && VideoId.IsMatch(id)) return "https://www.youtube.com/watch?v=" + id;
        Uri uri;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out uri) || uri.Scheme != "https" ||
            !String.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return String.Empty;
        string host = uri.DnsSafeHost.ToLowerInvariant();
        string candidate = String.Empty;
        if (host == "youtu.be" || host == "www.youtu.be") candidate = uri.AbsolutePath.Trim('/');
        else if (host == "youtube.com" || host == "www.youtube.com" || host == "m.youtube.com" || host == "music.youtube.com")
        {
            if (uri.AbsolutePath == "/watch")
            {
                foreach (string pair in uri.Query.TrimStart('?').Split('&'))
                {
                    string[] parts = pair.Split(new char[] { '=' }, 2);
                    if (parts.Length == 2 && parts[0] == "v") candidate = Uri.UnescapeDataString(parts[1]);
                }
            }
            else
            {
                Match match = Regex.Match(uri.AbsolutePath, @"^/(?:shorts|embed)/([A-Za-z0-9_-]{11})/?$");
                if (match.Success) candidate = match.Groups[1].Value;
            }
        }
        return VideoId.IsMatch(candidate) ? "https://www.youtube.com/watch?v=" + candidate : String.Empty;
    }

    private static bool IsLive(IDictionary<string, object> item)
    {
        object raw;
        return (item.TryGetValue("is_live", out raw) && raw is bool && (bool)raw) ||
            Text(item, "live_status", 40) == "is_live";
    }

    private static string Duration(IDictionary<string, object> item)
    {
        object raw;
        double seconds;
        if (!item.TryGetValue("duration", out raw) || raw == null ||
            !Double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out seconds) || Double.IsNaN(seconds) ||
            Double.IsInfinity(seconds) || seconds < 0 || seconds > 604800) return "—";
        long total = (long)Math.Floor(seconds);
        return total >= 3600 ? String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", total / 3600, (total / 60) % 60, total % 60) :
            String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
    }

    // Public pure parser makes fixture validation independent of network access.
    public static TrackSearchResult ParseResult(string json, string query, int page, int pageSize)
    {
        query = NormalizeQuery(query); ValidatePage(page, pageSize);
        TrackSearchResult result = new TrackSearchResult(query, page, pageSize);
        if (String.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonCharacters)
        { result.Error = "searchEmptyResponse"; return result; }
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaximumJsonCharacters; serializer.RecursionLimit = 80;
            IDictionary<string, object> root = serializer.DeserializeObject(json) as IDictionary<string, object>;
            object rawEntries;
            if (root == null || !root.TryGetValue("entries", out rawEntries))
            { result.Error = "searchUnexpectedResponse"; return result; }
            if (rawEntries == null) return result;
            IEnumerable entries = rawEntries as IEnumerable;
            if (entries == null || rawEntries is string || rawEntries is IDictionary)
            { result.Error = "searchUnexpectedResponse"; return result; }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int position = 0;
            foreach (object entry in entries)
            {
                if (++position > pageSize) { result.HasNext = page < 10; break; }
                IDictionary<string, object> item = entry as IDictionary<string, object>;
                if (item == null) continue;
                string url = CanonicalYoutubeUrl(Text(item, "id", 200), Text(item, "url", 4096));
                if (url.Length == 0) url = CanonicalYoutubeUrl(null, Text(item, "webpage_url", 4096));
                if (url.Length == 0) continue;
                string title = Text(item, "title", 300);
                if (title.Length == 0 || !seen.Add(url)) continue;
                string artist = Text(item, "artist", 160);
                if (artist.Length == 0) artist = Text(item, "uploader", 160);
                if (artist.Length == 0) artist = Text(item, "channel", 160);
                if (artist.Length == 0) artist = Text(item, "creator", 160);
                result.Items.Add(new TrackSearchItem {
                    Id = url.Substring(url.Length - 11), Title = title, Artist = artist,
                    DurationLabel = Duration(item), IsLive = IsLive(item), Url = url
                });
            }
        }
        catch (Exception) { result.Error = "searchUnreadableResponse"; result.Items.Clear(); result.HasNext = false; }
        return result;
    }
}

public sealed class TrackPreviewProcess : IDisposable
{
    private readonly Task<string> work;
    private int disposed;

    public bool IsCompleted { get { return work.IsCompleted; } }
    public string StreamUrl { get { return work.Status == TaskStatus.RanToCompletion ? work.Result : null; } }

    public TrackPreviewProcess(string videoId, string executable)
    {
        string url = TrackSearch.CanonicalYoutubeUrl(videoId, null);
        if (url.Length == 0) throw new ArgumentException("searchResultExpired");
        if (!File.Exists(executable)) throw new FileNotFoundException("previewToolFailed");
        string[] arguments = {
            "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-cookies", "--no-cookies-from-browser",
            "--no-playlist", "--match-filters", "!is_live", "--socket-timeout", "8", "--retries", "0",
            "--extractor-retries", "0", "--no-warnings", "--get-url",
            "-f", "bestaudio[ext=m4a][protocol=https]/bestaudio[protocol=https]", "--", url
        };
        work = Task.Run(() => Run(executable, arguments));
    }

    private string Run(string executable, string[] arguments)
    {
        try
        {
            if (Volatile.Read(ref disposed) != 0) return null;
            var start = new ProcessStartInfo(executable, TrackSearch.BundledRuntimeArguments(executable) +
                String.Join(" ", Array.ConvertAll(arguments, TrackSearch.QuoteWindowsArgument))) {
                WorkingDirectory = Path.GetDirectoryName(executable), CreateNoWindow = true,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = start })
            using (var job = SearchProcessJob.TryCreate())
            {
                if (!process.Start()) return null;
                bool assigned = job != null && job.Assign(process);
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                process.BeginErrorReadLine();
                var clock = Stopwatch.StartNew();
                while (!process.WaitForExit(75))
                {
                    if (Volatile.Read(ref disposed) == 0 && clock.ElapsedMilliseconds < 25000) continue;
                    SearchProcessJob.StopTree(process, assigned ? job : null);
                    return null;
                }
                if (process.ExitCode != 0 || Volatile.Read(ref disposed) != 0 || !output.Wait(1000)) return null;
                string line = output.Result.Trim();
                Uri uri;
                if (line.Length > 8192 || line.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                    !Uri.TryCreate(line, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                    !uri.DnsSafeHost.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase))
                    return null;
                return uri.AbsoluteUri;
            }
        }
        catch (Exception) { return null; }
    }

    public void Dispose() { Interlocked.Exchange(ref disposed, 1); }
}

public sealed class TrackSearchRequest : IDisposable
{
    private readonly Task<TrackSearchResult> task;
    private int cancelled;
    public bool IsCompleted { get { return task.IsCompleted; } }
    public TrackSearchResult Result { get { return task.Status == TaskStatus.RanToCompletion ? task.Result : null; } }

    internal TrackSearchRequest(string query, int page, int pageSize, string executable)
    {
        task = Task.Factory.StartNew(() => Run(query, page, pageSize, executable),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Cancel() { Interlocked.Exchange(ref cancelled, 1); }
    public void Dispose() { Cancel(); } // Never wait for a process on the UI thread.
    private bool IsCancelled { get { return Interlocked.CompareExchange(ref cancelled, 0, 0) != 0; } }

    private TrackSearchResult Run(string query, int page, int pageSize, string executable)
    {
        TrackSearchResult result = new TrackSearchResult(query, page, pageSize);
        Process process = null; SearchProcessJob job = null;
        StringBuilder stdout = new StringBuilder();
        StringBuilder stderr = new StringBuilder();
        object outputLock = new object();
        int overflow = 0;
        TaskCompletionSource<bool> outputEnded = new TaskCompletionSource<bool>();
        TaskCompletionSource<bool> errorEnded = new TaskCompletionSource<bool>();
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            if (IsCancelled) { result.Cancelled = true; return result; }
            if (!File.Exists(executable)) { result.Error = "searchToolMissing"; return result; }
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = executable;
            start.Arguments = TrackSearch.BundledRuntimeArguments(executable) + TrackSearch.BuildArguments(query, page, pageSize);
            start.WorkingDirectory = Path.GetDirectoryName(executable);
            start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            start.StandardOutputEncoding = new UTF8Encoding(false); start.StandardErrorEncoding = new UTF8Encoding(false);
            process = new Process(); process.StartInfo = start;
            process.OutputDataReceived += (sender, args) => {
                if (args.Data == null) { outputEnded.TrySetResult(true); return; }
                lock (outputLock)
                {
                    if (stdout.Length + args.Data.Length > TrackSearch.MaximumJsonCharacters)
                        Interlocked.Exchange(ref overflow, 1);
                    else stdout.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (sender, args) => {
                if (args.Data == null) { errorEnded.TrySetResult(true); return; }
                lock (outputLock)
                {
                    int remaining = 32768 - stderr.Length;
                    if (remaining > 0) stderr.Append(args.Data, 0, Math.Min(remaining, args.Data.Length));
                }
            };
            job = SearchProcessJob.TryCreate();
            if (!process.Start()) { result.Error = "searchStartFailed"; return result; }
            bool assigned = job != null && job.Assign(process);
            if (!assigned && job != null) { job.Dispose(); job = null; }
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            while (!process.WaitForExit(60))
            {
                if (IsCancelled || clock.ElapsedMilliseconds >= TrackSearch.TimeoutMilliseconds ||
                    Interlocked.CompareExchange(ref overflow, 0, 0) != 0)
                {
                    SearchProcessJob.StopTree(process, job);
                    if (IsCancelled) result.Cancelled = true;
                    else result.Error = overflow != 0 ? "searchResponseTooLarge" :
                        "searchTimeout";
                    return result;
                }
            }
            int exitCode = process.ExitCode;
            // Close a successful job too: no inherited pipe/child survives a search.
            if (job != null) { job.Dispose(); job = null; }
            outputEnded.Task.Wait(500); errorEnded.Task.Wait(500);
            if (IsCancelled) { result.Cancelled = true; return result; }
            if (overflow != 0) { result.Error = "searchResponseTooLarge"; return result; }
            if (exitCode != 0)
            {
                string detail;
                lock (outputLock) detail = stderr.ToString();
                result.Error = Regex.IsMatch(detail, @"sign in|login|bot|429|403|forbidden", RegexOptions.IgnoreCase) ?
                    "searchRateLimit" :
                    "searchConnectionFailed";
                return result;
            }
            string json;
            lock (outputLock) json = stdout.ToString();
            return TrackSearch.ParseResult(json, query, page, pageSize);
        }
        catch (Exception)
        {
            if (IsCancelled) result.Cancelled = true;
            else result.Error = "searchFinishFailed";
            return result;
        }
        finally
        {
            if (process != null)
            {
                try { if (!process.HasExited) SearchProcessJob.StopTree(process, job); } catch { }
                process.Dispose();
            }
            if (job != null) job.Dispose();
        }
    }
}

// Windows Job Objects provide Kill(entireProcessTree) semantics on .NET 4.8.
// If a host job policy prevents assignment, the OS taskkill fallback is bounded
// and uses only the already-owned numeric PID, never a command shell or query.
internal sealed class SearchProcessJob : IDisposable
{
    private IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime, JobTime; public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr information, uint length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    internal static SearchProcessJob TryCreate()
    {
        IntPtr job = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return null;
            ExtendedLimits limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf(typeof(ExtendedLimits)); data = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(limits, data, false);
            if (!SetInformationJobObject(job, 9, data, (uint)size)) { CloseHandle(job); return null; }
            return new SearchProcessJob { handle = job };
        }
        catch { if (job != IntPtr.Zero) CloseHandle(job); return null; }
        finally { if (data != IntPtr.Zero) Marshal.FreeHGlobal(data); }
    }
    internal bool Assign(Process process) { try { return AssignProcessToJobObject(handle, process.Handle); } catch { return false; } }
    public void Dispose() { IntPtr value = Interlocked.Exchange(ref handle, IntPtr.Zero); if (value != IntPtr.Zero) CloseHandle(value); }
    internal static void StopTree(Process process, SearchProcessJob job)
    {
        if (job != null && job.handle != IntPtr.Zero)
        { try { TerminateJobObject(job.handle, 1); } catch { } }
        else
        {
            try
            {
                if (!process.HasExited)
                {
                    ProcessStartInfo stop = new ProcessStartInfo();
                    stop.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");
                    stop.Arguments = "/PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F";
                    stop.UseShellExecute = false; stop.CreateNoWindow = true; stop.WindowStyle = ProcessWindowStyle.Hidden;
                    using (Process killer = Process.Start(stop))
                    { if (killer != null && !killer.WaitForExit(800)) try { killer.Kill(); } catch { } }
                }
            }
            catch { }
        }
        try { if (!process.HasExited) process.Kill(); } catch { }
    }
}
