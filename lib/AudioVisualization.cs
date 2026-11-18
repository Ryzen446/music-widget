using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MusicWidget.Visualization
{
    // These are five VU envelopes, not frequency bins or a BPM estimator.
    // Every input comes from the selected process's audio-session peak meter.
    public sealed class VuEnvelope
    {
        private readonly double[] mono = new double[16];
        private readonly double[] left = new double[16];
        private readonly double[] right = new double[16];
        private readonly double[] levels = new double[5];
        private readonly int[] delays = { 2, 0, 0, 1, 3 };
        private readonly double[] weights = { .72, .94, 1.0, .88, .68 };
        private readonly double[] attack = { .018, .016, .012, .026, .022 };
        private readonly double[] release = { .095, .075, .065, .105, .120 };
        private readonly double[] reference = new double[3];
        private readonly bool[] referenceReady = new bool[3];
        private int cursor;

        public static double Clamp(double value)
        { return Double.IsNaN(value) || Double.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
        private double DisplayAmplitude(double value, int channel, double seconds)
        {
            value = Clamp(value);
            if (!referenceReady[channel])
            { reference[channel] = value; referenceReady[channel] = value > .001; }
            else reference[channel] += (value - reference[channel]) * (1 - Math.Exp(-seconds / .6));
            if (value <= .001) return 0;

            // Preserve headroom and expand measured changes around the recent
            // signal level. Dense/limited music has small absolute peak changes;
            // a compressed absolute-level curve alone made those nearly static
            // in 25px bars. A steady input converges to a steady height: there is
            // no oscillator, beat guess, frequency synthesis or local-volume gain.
            double body = .60 * Math.Sqrt((value - .001) / .999);
            double relative = (value - reference[channel]) / Math.Max(.025, reference[channel]);
            double contrast = .36 * Math.Tanh(relative * 6) * Math.Min(1, value / .02);
            return Clamp(body + contrast);
        }

        public double[] Step(double peak, float[] channels, double seconds)
        {
            double l = peak, r = peak;
            if (channels != null && channels.Length > 0)
            {
                l = channels[0]; r = channels[channels.Length - 1];
                foreach (float channel in channels) peak = Math.Max(Clamp(peak), Clamp(channel));
            }
            double dt = Double.IsNaN(seconds) || Double.IsInfinity(seconds) ? .05 : Math.Max(.005, Math.Min(.2, seconds));
            mono[cursor] = DisplayAmplitude(peak, 0, dt);
            left[cursor] = DisplayAmplitude(l, 1, dt); right[cursor] = DisplayAmplitude(r, 2, dt);
            for (int i = 0; i < levels.Length; i++)
            {
                int index = (cursor - delays[i] + mono.Length) % mono.Length;
                double sample = i == 0 ? left[index] : i == 4 ? right[index] : mono[index];
                double target = sample * weights[i];
                double tau = target > levels[i] ? attack[i] : release[i];
                levels[i] = Clamp(levels[i] + (target - levels[i]) * (1 - Math.Exp(-dt / tau)));
                if (target == 0 && levels[i] < .001) levels[i] = 0;
            }
            cursor = (cursor + 1) % mono.Length;
            return (double[])levels.Clone();
        }
        public void Reset()
        {
            Array.Clear(mono, 0, mono.Length); Array.Clear(left, 0, left.Length); Array.Clear(right, 0, right.Length);
            Array.Clear(levels, 0, levels.Length); Array.Clear(reference, 0, reference.Length);
            Array.Clear(referenceReady, 0, referenceReady.Length); cursor = 0;
        }
    }

    public static class SessionSelection
    {
        public static bool Matches(int expectedProcessId, uint actualProcessId,
            int processIdHResult, int state, bool hasMeter, bool systemSounds)
        {
            // WPF's active renderer commonly reports the documented
            // AUDCLNT_S_NO_SINGLE_PROCESS success code. Its returned PID is
            // the creator of that session; accept only our own creator PID.
            // Reject arbitrary success codes, every other PID and system mix.
            return expectedProcessId > 0 && actualProcessId == (uint)expectedProcessId &&
                (processIdHResult == 0 || processIdHResult == 0x0889000D) &&
                (state == 0 || state == 1) && hasMeter && !systemSounds;
        }
    }

    public sealed class AudioVisualizationStatus
    {
        public int ProcessId { get; set; }
        public bool Active { get; set; }
        public bool WorkerRunning { get; set; }
        public int BoundSessionCount { get; set; }
        public int LastHResult { get; set; }
        public double Peak { get; set; }
        public long Samples { get; set; }
        public long Discoveries { get; set; }
        public int ActiveSessionCount { get; set; }
        public long MeterReads { get; set; }
        public double ChannelPeak { get; set; }
        public string Source { get { return "process-session-vu"; } }
    }

    public sealed class SessionAudioVisualizer : IDisposable
    {
        private readonly int processId;
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly VuEnvelope envelope = new VuEnvelope();
        private readonly List<SessionBinding> bindings = new List<SessionBinding>();
        private Thread worker;
        private bool active, disposed, workerRunning;
        private int resetVersion, rebindVersion, boundCount, lastHResult;
        private double peak;
        private long frameStamp, samples, discoveries;
        private long meterReads;
        private int activeSessionCount;
        private double channelPeak;
        private double[] frame = new double[5];

        public SessionAudioVisualizer(int targetProcessId)
        {
            if (targetProcessId <= 0) throw new ArgumentOutOfRangeException("targetProcessId");
            processId = targetProcessId;
        }

        // Initialization does not create COM objects, enumerate devices, open
        // streams, or start a worker. Those actions start only when active.
        public void SetActive(bool value)
        {
            lock (gate)
            {
                if (disposed || active == value) return;
                active = value; resetVersion++;
                frame = new double[5]; peak = 0; channelPeak = 0; activeSessionCount = 0;
                frameStamp = Stopwatch.GetTimestamp();
                if (value)
                {
                    rebindVersion++;
                    if (worker == null)
                    {
                        worker = new Thread(WorkerLoop); worker.IsBackground = true;
                        worker.Name = "Music Widget session VU";
                        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
                    }
                }
                if (worker != null) wake.Set();
            }
        }

        public double[] ReadFrame()
        {
            lock (gate)
            {
                double age = (Stopwatch.GetTimestamp() - frameStamp) / (double)Stopwatch.Frequency;
                return !active || disposed || age > .3 ? new double[5] : (double[])frame.Clone();
            }
        }

        public void Reset(bool rebind)
        {
            lock (gate)
            {
                if (disposed) return;
                frame = new double[5]; peak = 0; channelPeak = 0; activeSessionCount = 0; resetVersion++;
                if (rebind) rebindVersion++;
                if (worker != null) wake.Set();
            }
        }

        public void Rebind() { Reset(true); }

        public AudioVisualizationStatus GetStatus()
        {
            lock (gate) return new AudioVisualizationStatus {
                ProcessId = processId, Active = active, WorkerRunning = workerRunning,
                BoundSessionCount = boundCount, LastHResult = lastHResult, Peak = peak,
                Samples = samples, Discoveries = discoveries, ActiveSessionCount = activeSessionCount,
                MeterReads = meterReads, ChannelPeak = channelPeak
            };
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true; active = false; frame = new double[5]; peak = 0;
                channelPeak = 0; activeSessionCount = 0;
                if (worker == null) wake.Dispose(); else wake.Set();
            }
            // Never Join the worker on the dispatcher. It releases every COM
            // reference on its own MTA thread, then disposes the wake event.
        }

        private bool GetActivity(out int reset, out int rebind)
        {
            lock (gate) { reset = resetVersion; rebind = rebindVersion; return active && !disposed; }
        }
        private bool IsDisposed { get { lock (gate) return disposed; } }
        private void ClearBindings()
        {
            foreach (SessionBinding binding in bindings) binding.Dispose();
            bindings.Clear();
            lock (gate) { boundCount = 0; activeSessionCount = 0; channelPeak = 0; }
        }

        private void WorkerLoop()
        {
            int initialized = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
            int lastReset = -1, lastRebind = -1;
            long previous = Stopwatch.GetTimestamp();
            double nextDiscovery = 0;
            Stopwatch clock = Stopwatch.StartNew();
            lock (gate) workerRunning = true;
            try
            {
                if (initialized < 0) { lock (gate) lastHResult = initialized; return; }
                while (!IsDisposed)
                {
                    int reset, rebind;
                    if (!GetActivity(out reset, out rebind))
                    {
                        ClearBindings(); envelope.Reset(); nextDiscovery = 0;
                        wake.WaitOne(); previous = Stopwatch.GetTimestamp(); continue;
                    }
                    if (reset != lastReset) { envelope.Reset(); lastReset = reset; }
                    if (rebind != lastRebind || clock.Elapsed.TotalMilliseconds >= nextDiscovery)
                    {
                        ClearBindings(); DiscoverSessions(); lastRebind = rebind;
                        nextDiscovery = clock.Elapsed.TotalMilliseconds + (bindings.Count == 0 ? 500 : 2000);
                    }
                    float rawPeak = 0; float[] channels = null; bool invalidated = false;
                    int activeCount = 0, reads = 0; double measuredChannelPeak = 0;
                    foreach (SessionBinding binding in bindings)
                    {
                        float reading; int error;
                        if (!binding.TryRead(processId, out reading, out error))
                        { invalidated = true; lock (gate) lastHResult = error; continue; }
                        if (binding.LastState == 1) { activeCount++; reads++; }
                        measuredChannelPeak = Math.Max(measuredChannelPeak, binding.LastChannelPeak);
                        if (reading >= rawPeak) { rawPeak = reading; channels = binding.ChannelValues; }
                    }
                    if (invalidated) nextDiscovery = 0;
                    long now = Stopwatch.GetTimestamp();
                    double elapsed = (now - previous) / (double)Stopwatch.Frequency; previous = now;
                    double[] updated = envelope.Step(rawPeak, channels, elapsed);
                    lock (gate)
                    {
                        if (active && !disposed && reset == resetVersion)
                        { frame = updated; peak = rawPeak; frameStamp = now; samples++;
                          activeSessionCount = activeCount; meterReads += reads; channelPeak = measuredChannelPeak; }
                    }
                    wake.WaitOne(50);
                }
            }
            catch (Exception error) { lock (gate) lastHResult = Marshal.GetHRForException(error); }
            finally
            {
                ClearBindings(); envelope.Reset();
                if (initialized >= 0) CoUninitialize();
                lock (gate)
                {
                    workerRunning = false; worker = null; frame = new double[5]; peak = 0;
                    // A failed COM initialization may recover after a later
                    // activation. Keep the event usable unless disposed.
                    if (disposed) wake.Dispose();
                }
            }
        }

        private void DiscoverSessions()
        {
            IAvDeviceEnumerator enumerator = null; IAvDeviceCollection devices = null;
            int lastError = 0;
            try
            {
                enumerator = (IAvDeviceEnumerator)new AvDeviceEnumerator();
                int hr = enumerator.EnumAudioEndpoints(0, 1, out devices); // eRender, ACTIVE; never capture/microphone.
                if (hr < 0) { lastError = hr; return; }
                uint count;
                hr = devices.GetCount(out count);
                if (hr < 0) { lastError = hr; return; }
                for (uint deviceIndex = 0; deviceIndex < Math.Min(count, 64u); deviceIndex++)
                {
                    IAvDevice device = null; object managerObject = null; IAvSessionEnumerator sessions = null;
                    try
                    {
                        if (devices.Item(deviceIndex, out device) < 0) continue;
                        Guid iid = typeof(IAvSessionManager2).GUID;
                        hr = device.Activate(ref iid, 23, IntPtr.Zero, out managerObject);
                        IAvSessionManager2 manager = managerObject as IAvSessionManager2;
                        if (hr < 0 || manager == null) { lastError = hr; continue; }
                        if ((hr = manager.GetSessionEnumerator(out sessions)) < 0) { lastError = hr; continue; }
                        int sessionCount;
                        if ((hr = sessions.GetCount(out sessionCount)) < 0) { lastError = hr; continue; }
                        for (int sessionIndex = 0; sessionIndex < Math.Min(sessionCount, 256); sessionIndex++)
                        {
                            object sessionObject = null;
                            try
                            {
                                if (sessions.GetSession(sessionIndex, out sessionObject) < 0 || sessionObject == null) continue;
                                IAvSessionControl2 control = sessionObject as IAvSessionControl2;
                                if (control == null) continue;
                                uint owner; int state;
                                int ownerResult = control.GetProcessId(out owner);
                                int stateResult = control.GetState(out state);
                                if (stateResult < 0 ||
                                    !SessionSelection.Matches(processId, owner, ownerResult, state, true, control.IsSystemSoundsSession() == 0)) continue;
                                // Crucially, query the meter on the owned SESSION,
                                // never IMMDevice.Activate(IAudioMeterInformation).
                                IAvMeter meter = sessionObject as IAvMeter;
                                if (meter == null) continue;
                                if (bindings.Count < 16)
                                {
                                    bindings.Add(new SessionBinding(sessionObject, control, meter));
                                    sessionObject = null; // Ownership transferred to binding.
                                }
                            }
                            catch (COMException) { }
                            finally { Release(sessionObject); }
                        }
                    }
                    catch (COMException error) { lastError = error.ErrorCode; }
                    finally { Release(sessions); Release(managerObject); Release(device); }
                }
            }
            catch (Exception error) { lastError = Marshal.GetHRForException(error); }
            finally
            {
                Release(devices); Release(enumerator);
                lock (gate) { boundCount = bindings.Count; lastHResult = lastError; discoveries++; }
            }
        }

        private sealed class SessionBinding : IDisposable
        {
            private object ownedObject;
            private readonly IAvSessionControl2 control;
            private readonly IAvMeter meter;
            public readonly float[] ChannelValues;
            public int LastState;
            public double LastChannelPeak;
            public SessionBinding(object owner, IAvSessionControl2 session, IAvMeter information)
            {
                ownedObject = owner; control = session; meter = information;
                uint channelCount;
                if (meter.GetMeteringChannelCount(out channelCount) == 0 && channelCount > 0 && channelCount <= 32)
                    ChannelValues = new float[channelCount];
            }
            public bool TryRead(int targetPid, out float value, out int error)
            {
                value = 0; error = 0;
                try
                {
                    uint owner; int state;
                    int ownerResult = control.GetProcessId(out owner);
                    error = control.GetState(out state);
                    LastState = state; LastChannelPeak = 0;
                    if (error < 0 || !SessionSelection.Matches(targetPid, owner, ownerResult, state, true, false)) return false;
                    if (state != 1) { if (ChannelValues != null) Array.Clear(ChannelValues, 0, ChannelValues.Length); return true; }
                    error = meter.GetPeakValue(out value);
                    if (error < 0) { value = 0; return false; }
                    value = (float)VuEnvelope.Clamp(value);
                    if (ChannelValues != null && meter.GetChannelsPeakValues((uint)ChannelValues.Length, ChannelValues) != 0)
                    {
                        // Optional channel meters can be unavailable. Use the
                        // actual session peak for both sides, not stale data.
                        for (int i = 0; i < ChannelValues.Length; i++) ChannelValues[i] = value;
                    }
                    if (ChannelValues != null) foreach (float channel in ChannelValues)
                        LastChannelPeak = Math.Max(LastChannelPeak, VuEnvelope.Clamp(channel));
                    return true;
                }
                catch (Exception failure) { error = Marshal.GetHRForException(failure); return false; }
            }
            public void Dispose() { Release(ownedObject); ownedObject = null; }
        }

        private static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) try { Marshal.ReleaseComObject(value); } catch { } }
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
    }

    // Unique namespace/names avoid conflicts with the legacy global meter.
    // Vtable order and GUIDs match Windows SDK 10.0.26100 audiopolicy.h,
    // mmdeviceapi.h and endpointvolume.h. HRESULTs must retain PreserveSig.
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class AvDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAvDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IAvDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IAvDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IAvDevice endpoint);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAvDeviceCollection
    { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IAvDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAvDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAvSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAvSessionEnumerator sessions);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string session, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAvSessionEnumerator
    { [PreserveSig] int GetCount(out int count); [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session); }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAvSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool value);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAvMeter
    {
        [PreserveSig] int GetPeakValue(out float value);
        [PreserveSig] int GetMeteringChannelCount(out uint count);
        [PreserveSig] int GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] values);
        [PreserveSig] int QueryHardwareSupport(out uint support);
    }
}
