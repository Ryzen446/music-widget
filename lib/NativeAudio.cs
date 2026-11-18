using System;
using System.Runtime.InteropServices;

public static class NativeAudio
{
    private const int VK_VOLUME_MUTE = 0xAD;
    private const int VK_VOLUME_DOWN = 0xAE;
    private const int VK_VOLUME_UP = 0xAF;
    private const int VK_MEDIA_NEXT_TRACK = 0xB0;
    private const int VK_MEDIA_PREV_TRACK = 0xB1;
    private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const int VK_MEDIA_STOP = 0xB2;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    public static void SendMediaKey(byte vk)
    {
        keybd_event(vk, 0, 0, 0);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
    }

    public static void MediaPlayPause() { SendMediaKey(VK_MEDIA_PLAY_PAUSE); }
    public static void MediaNext() { SendMediaKey(VK_MEDIA_NEXT_TRACK); }
    public static void MediaPrevious() { SendMediaKey(VK_MEDIA_PREV_TRACK); }
    public static void MediaStop() { SendMediaKey(VK_MEDIA_STOP); }
    public static void VolumeUp() { SendMediaKey(VK_VOLUME_UP); }
    public static void VolumeDown() { SendMediaKey(VK_VOLUME_DOWN); }
    public static void VolumeMute() { SendMediaKey(VK_VOLUME_MUTE); }

    public static void AdjustVolumeSteps(int steps)
    {
        byte key = (byte)(steps > 0 ? VK_VOLUME_UP : VK_VOLUME_DOWN);
        int count = Math.Abs(steps);
        for (int i = 0; i < count; i++)
        {
            SendMediaKey(key);
        }
    }
}

public static class HotKeyHelper
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_ALT = 0x0001;
    public const int HOTKEY_TOGGLE = 9001;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public class VolumeController : IDisposable
{
    private object _volume;
    private object _enumerator;
    private float _fallbackLevel = 0.5f;
    private bool _fallbackMuted;
    public bool UsesSystemVolume { get; private set; }

    public VolumeController()
    {
        try
        {
            Type enumType = Type.GetTypeFromProgID("MMDeviceEnumerator");
            if (enumType == null) return;

            _enumerator = Activator.CreateInstance(enumType);
            object device = enumType.InvokeMember(
                "GetDefaultAudioEndpoint",
                System.Reflection.BindingFlags.InvokeMethod,
                null, _enumerator, new object[] { 0, 1 });

            _volume = device.GetType().InvokeMember(
                "AudioEndpointVolume",
                System.Reflection.BindingFlags.GetProperty,
                null, device, null);

            UsesSystemVolume = _volume != null;
            if (UsesSystemVolume)
            {
                _fallbackLevel = GetMasterVolume();
                _fallbackMuted = GetMute();
            }
        }
        catch
        {
            UsesSystemVolume = false;
        }
    }

    public float GetMasterVolume()
    {
        if (UsesSystemVolume && _volume != null)
        {
            try
            {
                return (float)_volume.GetType().InvokeMember(
                    "MasterVolumeLevelScalar",
                    System.Reflection.BindingFlags.GetProperty,
                    null, _volume, null);
            }
            catch { }
        }
        return _fallbackLevel;
    }

    public bool GetMute()
    {
        if (UsesSystemVolume && _volume != null)
        {
            try
            {
                return (bool)_volume.GetType().InvokeMember(
                    "Mute",
                    System.Reflection.BindingFlags.GetProperty,
                    null, _volume, null);
            }
            catch { }
        }
        return _fallbackMuted;
    }

    public void SetMasterVolume(float level)
    {
        level = Math.Max(0f, Math.Min(1f, level));

        if (UsesSystemVolume && _volume != null)
        {
            try
            {
                _volume.GetType().InvokeMember(
                    "MasterVolumeLevelScalar",
                    System.Reflection.BindingFlags.SetProperty,
                    null, _volume, new object[] { level });
                _fallbackLevel = level;
                return;
            }
            catch { }
        }

        float delta = level - _fallbackLevel;
        int steps = (int)Math.Round(delta * 50);
        if (steps != 0)
        {
            NativeAudio.AdjustVolumeSteps(steps);
            _fallbackLevel = level;
        }
    }

    public void SetMute(bool mute)
    {
        if (UsesSystemVolume && _volume != null)
        {
            try
            {
                _volume.GetType().InvokeMember(
                    "Mute",
                    System.Reflection.BindingFlags.SetProperty,
                    null, _volume, new object[] { mute });
                _fallbackMuted = mute;
                return;
            }
            catch { }
        }

        if (_fallbackMuted != mute)
        {
            NativeAudio.VolumeMute();
            _fallbackMuted = mute;
        }
    }

    public void StepVolume(int direction)
    {
        if (direction > 0) NativeAudio.VolumeUp();
        else if (direction < 0) NativeAudio.VolumeDown();

        if (UsesSystemVolume)
        {
            _fallbackLevel = GetMasterVolume();
        }
        else
        {
            _fallbackLevel = Math.Max(0f, Math.Min(1f, _fallbackLevel + (direction * 0.02f)));
        }
    }

    public void Dispose()
    {
        if (_volume != null)
        {
            try { Marshal.ReleaseComObject(_volume); } catch { }
        }
        if (_enumerator != null)
        {
            try { Marshal.ReleaseComObject(_enumerator); } catch { }
        }
    }
}
