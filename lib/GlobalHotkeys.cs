using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class GlobalHotKeyManager : NativeWindow, IDisposable
{
    private const int HotKeyMessage = 0x0312;
    private const uint NoRepeat = 0x4000;
    private readonly Dictionary<int, Binding> bindings = new Dictionary<int, Binding>();
    private sealed class Binding
    {
        public int Id;
        public uint Modifiers;
        public uint Key;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    public event Action<int> Pressed;

    public GlobalHotKeyManager()
    {
        CreateHandle(new CreateParams());
    }

    public bool Apply(int action, uint modifiers, uint key)
    {
        Binding current;
        bindings.TryGetValue(action, out current);
        if (current != null && current.Modifiers == modifiers && current.Key == key) return true;

        if (key == 0)
        {
            if (current == null) return true;
            if (!UnregisterHotKey(Handle, current.Id)) return false;
            bindings.Remove(action);
            return true;
        }

        // Register under a second ID first so a conflicting choice leaves the old hotkey intact.
        int firstId = 9100 + action * 2;
        int id = current != null && current.Id == firstId ? firstId + 1 : firstId;
        if (!RegisterHotKey(Handle, id, modifiers | NoRepeat, key)) return false;
        if (current != null && !UnregisterHotKey(Handle, current.Id))
        {
            UnregisterHotKey(Handle, id);
            return false;
        }
        bindings[action] = new Binding { Id = id, Modifiers = modifiers, Key = key };
        return true;
    }

    public void UnregisterAll()
    {
        foreach (Binding binding in bindings.Values) UnregisterHotKey(Handle, binding.Id);
        bindings.Clear();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotKeyMessage)
        {
            int id = message.WParam.ToInt32();
            int action = -1;
            foreach (KeyValuePair<int, Binding> entry in bindings)
            {
                if (entry.Value.Id == id) { action = entry.Key; break; }
            }
            Action<int> callback = Pressed;
            if (action >= 0 && callback != null) callback(action);
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        UnregisterAll();
        DestroyHandle();
    }
}
