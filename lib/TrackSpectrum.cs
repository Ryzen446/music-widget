using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MusicWidget.Visualization
{
    // Decode only a short interval around the native player's position. The UI
    // reads five cached amplitudes and never waits for the decoder or FFT.
    public sealed class TrackSpectrumReader : IDisposable
    {
        private const int SampleRate = 12000;
        private const int FrameSize = 512;
        private const int IntervalSeconds = 24;
        private const int BandCount = 5;
        private static readonly int[] BandEdges = { 40, 140, 360, 900, 2200, 5600 };
        private static readonly double[] Window = MakeHannWindow();
        private readonly string ffmpeg;
        private Chunk current;
        private Task<Chunk> pending;
        private CancellationTokenSource cancellation;
        private string requestedPath;
        private int requestedInterval = -1;

        private sealed class Chunk
        {
            public string Path;
            public int Interval;
            public double Start;
            public double[] Levels;
        }

        public TrackSpectrumReader(string executable) { ffmpeg = executable; }

        public double[] Read(string path, double position)
        {
            if (String.IsNullOrEmpty(path) || Double.IsNaN(position) || Double.IsInfinity(position) || position < 0 || position > Int32.MaxValue - IntervalSeconds)
                return null;
            int interval = (int)(position / IntervalSeconds) * IntervalSeconds;
            if (pending != null)
            {
                if (pending.IsCompleted)
                {
                    if (pending.Status == TaskStatus.RanToCompletion && pending.Result != null) current = pending.Result;
                    pending = null;
                    cancellation.Dispose();
                    cancellation = null;
                }
                else if (!String.Equals(requestedPath, path, StringComparison.OrdinalIgnoreCase) || requestedInterval != interval)
                {
                    cancellation.Cancel();
                }
            }
            bool intervalCached = current != null && current.Interval == interval && String.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase);
            if (pending == null && !intervalCached && (!String.Equals(requestedPath, path, StringComparison.OrdinalIgnoreCase) || requestedInterval != interval))
            {
                requestedPath = path;
                requestedInterval = interval;
                double start = Math.Max(0, interval - 1);
                cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                CancellationToken token = cancellation.Token;
                pending = Task.Run(() => Decode(path, interval, start, token));
            }
            if (current == null || !String.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase)) return null;
            double index = (position - current.Start) * SampleRate / FrameSize;
            int frame = (int)Math.Floor(index);
            int frames = current.Levels.Length / BandCount;
            if (frame < 0 || frame >= frames) return null;
            int next = Math.Min(frame + 1, frames - 1);
            double blend = index - frame;
            var levels = new double[BandCount];
            for (int band = 0; band < BandCount; band++)
                levels[band] = current.Levels[frame * BandCount + band] * (1 - blend) + current.Levels[next * BandCount + band] * blend;
            return levels;
        }

        public void Suspend()
        {
            if (cancellation != null) cancellation.Cancel();
            requestedPath = null;
            requestedInterval = -1;
        }

        public void Dispose()
        {
            Suspend();
            if (pending != null && cancellation != null)
            {
                CancellationTokenSource finished = cancellation;
                pending.ContinueWith(ignored => finished.Dispose(), TaskScheduler.Default);
                pending = null;
                cancellation = null;
            }
            current = null;
        }

        private Chunk Decode(string path, int interval, double start, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested || !File.Exists(path) || !File.Exists(ffmpeg) || path.Contains("\"")) return null;
                string arguments = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "-nostdin -hide_banner -loglevel error -protocol_whitelist file,pipe -threads 1 -ss {0} -i \"{1}\" -t 26 -map 0:a:0 -vn -sn -dn -ac 1 -ar {2} -f s16le pipe:1",
                    start, Path.GetFullPath(path), SampleRate);
                var info = new ProcessStartInfo(ffmpeg, arguments) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = Process.Start(info))
                using (token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }))
                {
                    try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    process.BeginErrorReadLine();
                    var data = new List<double>();
                    var real = new double[FrameSize];
                    var imaginary = new double[FrameSize];
                    var pcm = new byte[FrameSize * 2];
                    Stream source = process.StandardOutput.BaseStream;
                    while (!token.IsCancellationRequested && ReadBlock(source, pcm))
                    {
                        for (int i = 0; i < FrameSize; i++)
                        {
                            short sample = unchecked((short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)));
                            real[i] = sample * (Window[i] / 32768.0);
                            imaginary[i] = 0;
                        }
                        Transform(real, imaginary);
                        for (int band = 0; band < BandCount; band++)
                        {
                            double sum = 0;
                            int low = (int)Math.Ceiling((double)BandEdges[band] * FrameSize / SampleRate);
                            int high = (int)Math.Ceiling((double)BandEdges[band + 1] * FrameSize / SampleRate);
                            for (int bin = low; bin < high; bin++) sum += real[bin] * real[bin] + imaginary[bin] * imaginary[bin];
                            data.Add(10 * Math.Log10(Math.Max(1e-9, sum / (FrameSize * FrameSize))));
                        }
                    }
                    if (token.IsCancellationRequested || !process.WaitForExit(1000) || process.ExitCode != 0 || data.Count == 0) return null;
                    return new Chunk { Path = path, Interval = interval, Start = start, Levels = Scale(data) };
                }
            }
            catch (IOException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static bool ReadBlock(Stream input, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = input.Read(buffer, offset, buffer.Length - offset);
                if (count == 0) return false;
                offset += count;
            }
            return true;
        }

        private static double[] MakeHannWindow()
        {
            var window = new double[FrameSize];
            for (int i = 0; i < window.Length; i++) window[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / (FrameSize - 1));
            return window;
        }

        private static void Transform(double[] real, double[] imaginary)
        {
            for (int i = 1, j = 0; i < FrameSize; i++)
            {
                int bit = FrameSize >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double entry = real[i]; real[i] = real[j]; real[j] = entry; }
            }
            for (int size = 2; size <= FrameSize; size <<= 1)
            {
                int half = size >> 1;
                double angle = -2 * Math.PI / size;
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                for (int start = 0; start < FrameSize; start += size)
                {
                    double wr = 1, wi = 0;
                    for (int j = start; j < start + half; j++)
                    {
                        int other = j + half;
                        double r = wr * real[other] - wi * imaginary[other];
                        double im = wr * imaginary[other] + wi * real[other];
                        real[other] = real[j] - r; real[j] += r;
                        imaginary[other] = imaginary[j] - im; imaginary[j] += im;
                        double next = wr * cos - wi * sin;
                        wi = wr * sin + wi * cos; wr = next;
                    }
                }
            }
        }

        private static double[] Scale(List<double> data)
        {
            var levels = new double[data.Count];
            int frames = data.Count / BandCount;
            var sorted = new double[frames];
            for (int band = 0; band < BandCount; band++)
            {
                for (int i = 0; i < frames; i++) sorted[i] = data[i * BandCount + band];
                Array.Sort(sorted);
                double quiet = sorted[(int)((frames - 1) * .15)];
                double range = Math.Max(9, sorted[(int)((frames - 1) * .90)] - quiet + 4);
                for (int i = 0; i < frames; i++)
                {
                    int index = i * BandCount + band;
                    double value = data[index];
                    double strength = Math.Max(0, Math.Min(1, (value - quiet + 2) / range));
                    double enoughSignal = Math.Max(0, Math.Min(1, (value + 72) / 18));
                    levels[index] = enoughSignal * (.10 + .87 * strength);
                }
            }
            return levels;
        }
    }
}
