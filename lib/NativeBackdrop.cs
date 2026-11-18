using System;
using System.Runtime.InteropServices;

/// <summary>
/// Windows system backdrop interop. No SDK package is required.
/// Call on the owning UI thread after a non-layered HWND has been created.
/// </summary>
public static class NativeBackdrop
{
    private const int DWMWA_CLOAK = 13;
    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_FAIL = unchecked((int)0x80004005);
    private const int RGN_OR = 2;
    private const int RGN_AND = 1;

    public static int LastResult { get; private set; }
    public static int LastFrameResult { get; private set; }
    public static int LastBackdropResult { get; private set; }
    public static bool LastSupported { get; private set; }
    public static bool LastAppliedGlass { get; private set; }
    public static int LastRegionResult { get; private set; }

    /// <summary>
    /// Hides the complete native presentation, including child HWNDs and the
    /// DWM backdrop. Unlike WPF Opacity, a cloak applies to the compositor.
    /// DWM continues composing the window while it is cloaked.
    /// </summary>
    public static int SetCloaked(IntPtr hwnd, bool hidden)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return E_INVALIDARG;
        try { return SetAttribute(hwnd, DWMWA_CLOAK, hidden ? 1 : 0); }
        catch (DllNotFoundException) { return E_NOTIMPL; }
        catch (EntryPointNotFoundException) { return E_NOTIMPL; }
    }

    public static int GetCloakState(IntPtr hwnd)
    {
        int value;
        try { return hwnd != IntPtr.Zero && DwmGetWindowAttribute(hwnd,
            DWMWA_CLOAKED, out value, sizeof(int)) >= 0 ? value : -1; }
        catch (DllNotFoundException) { return -1; }
        catch (EntryPointNotFoundException) { return -1; }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int DwmFlush();

    public static int FlushComposition()
    {
        try { return DwmFlush(); }
        catch (DllNotFoundException) { return E_NOTIMPL; }
        catch (EntryPointNotFoundException) { return E_NOTIMPL; }
    }

    // The PNG completion alone does not prove that the browser painted its UI.
    // Return visible pixel count and largest per-channel range from BGRA32.
    public static int[] InspectPresentationFrame(byte[] pixels, int width, int height, int stride)
    {
        int visible = 0, minR = 255, minG = 255, minB = 255;
        int maxR = 0, maxG = 0, maxB = 0;
        if (pixels == null || width < 1 || height < 1 || stride < width * 4 ||
            (long)stride * height > pixels.Length) return new int[] { 0, 0 };
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * stride + x * 4;
            if (pixels[index + 3] < 16) continue;
            visible++;
            int b = pixels[index], g = pixels[index + 1], r = pixels[index + 2];
            minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
            minG = Math.Min(minG, g); maxG = Math.Max(maxG, g);
            minB = Math.Min(minB, b); maxB = Math.Max(maxB, b);
        }
        int range = visible == 0 ? 0 : Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
        return new int[] { visible, range };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    public static bool IsPointerOver(IntPtr hwnd)
    {
        POINT point;
        if (hwnd == IntPtr.Zero || !GetCursorPos(out point)) return false;
        IntPtr hit = WindowFromPoint(point);
        return hit == hwnd || (hit != IntPtr.Zero && GetAncestor(hit, 2) == hwnd);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top,
        int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    /// <summary>Scale from CSS pixels at WebView zoom 1 to physical HWND pixels.</summary>
    public static double GetWindowDpiScale(IntPtr hwnd)
    {
        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
        catch (EntryPointNotFoundException) { return 1.0; }
        catch (DllNotFoundException) { return 1.0; }
    }

    private static bool ValidSurface(double[] surface)
    {
        if (surface == null || surface.Length != 5)
            return false;
        for (int i = 0; i < surface.Length; i++)
        {
            if (double.IsNaN(surface[i]) || double.IsInfinity(surface[i]) ||
                Math.Abs(surface[i]) > 65536)
                return false;
        }
        return surface[2] > 0 && surface[3] > 0 && surface[4] >= 0;
    }

    private static IntPtr CreateSurfaceRegion(double[] surface, double scale, int offsetX, int offsetY)
    {
        int left = offsetX + (int)Math.Floor(surface[0] * scale);
        int top = offsetY + (int)Math.Floor(surface[1] * scale);
        int right = offsetX + (int)Math.Ceiling((surface[0] + surface[2]) * scale);
        int bottom = offsetY + (int)Math.Ceiling((surface[1] + surface[3]) * scale);
        double radius = Math.Min(surface[4], Math.Min(surface[2], surface[3]) / 2.0);
        int diameter = (int)Math.Round(radius * scale * 2.0);
        return diameter == 0 ? CreateRectRgn(left, top, right, bottom) :
            CreateRoundRectRgn(left, top, right, bottom, diameter, diameter);
    }

    private static bool AddSurfaceRegion(IntPtr target, double[] surface, double scale, int x, int y)
    {
        if (surface == null) return true;
        IntPtr region = CreateSurfaceRegion(surface, scale, x, y);
        if (region == IntPtr.Zero) return false;
        try { return CombineRgn(target, target, region, RGN_OR) != 0; }
        finally { DeleteObject(region); }
    }

    /// <summary>
    /// Clips the HWND and its Acrylic backdrop to the union of the player
    /// and its optional menu. Each surface is [x,y,width,height,
    /// radius] in host DIPs. The caller accounts for WebView ZoomFactor when
    /// converting getBoundingClientRect; cssToDeviceScale is then Windows DPI
    /// only. Do not multiply the configured player size scale twice.
    /// Setting a region disables automatic DWM rounding; these regions supply
    /// the rounded corners instead. This does not change the backdrop type.
    /// </summary>
    public static int SetGlassRegion(IntPtr hwnd, double cssToDeviceScale,
        double[] main, double[] menu)
    {
        LastRegionResult = E_INVALIDARG;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !ValidSurface(main) ||
            (menu != null && !ValidSurface(menu)) ||
            double.IsNaN(cssToDeviceScale) ||
            double.IsInfinity(cssToDeviceScale) || cssToDeviceScale < 0.25 || cssToDeviceScale > 8)
            return LastRegionResult;

        IntPtr combined = IntPtr.Zero;
        IntPtr bounds = IntPtr.Zero;
        try
        {
            RECT windowRect;
            POINT clientOrigin = new POINT();
            if (!GetWindowRect(hwnd, out windowRect) || !ClientToScreen(hwnd, ref clientOrigin))
                return LastRegionResult = E_FAIL;
            int offsetX = clientOrigin.X - windowRect.Left;
            int offsetY = clientOrigin.Y - windowRect.Top;
            combined = CreateSurfaceRegion(main, cssToDeviceScale, offsetX, offsetY);
            if (combined == IntPtr.Zero)
                return LastRegionResult = E_FAIL;
            if (!AddSurfaceRegion(combined, menu, cssToDeviceScale, offsetX, offsetY))
                return LastRegionResult = E_FAIL;
            // Never grant a web surface drawing outside the host window.
            bounds = CreateRectRgn(0, 0, windowRect.Right - windowRect.Left,
                windowRect.Bottom - windowRect.Top);
            if (bounds == IntPtr.Zero || CombineRgn(combined, combined, bounds, RGN_AND) == 0)
                return LastRegionResult = E_FAIL;
            if (SetWindowRgn(hwnd, combined, true) == 0)
                return LastRegionResult = E_FAIL;
            // Successful SetWindowRgn transfers ownership to Windows. Never
            // delete, reuse, or cache that HRGN after this point.
            combined = IntPtr.Zero;
            return LastRegionResult = 0;
        }
        finally
        {
            if (combined != IntPtr.Zero) DeleteObject(combined);
            if (bounds != IntPtr.Zero) DeleteObject(bounds);
        }
    }

    /// <summary>Restores the normal rectangular HWND and DWM rounding policy.</summary>
    public static int ClearGlassRegion(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return LastRegionResult = E_INVALIDARG;
        return LastRegionResult = SetWindowRgn(hwnd, IntPtr.Zero, true) != 0 ? 0 : E_FAIL;
    }

    /// <summary>Returns the configured system backdrop, or -1 when unsupported.</summary>
    public static int GetBackdropType(IntPtr hwnd)
    {
        int value;
        if (hwnd == IntPtr.Zero)
            return -1;
        try
        {
            return DwmGetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                out value, sizeof(int)) >= 0 ? value : -1;
        }
        catch (DllNotFoundException) { return -1; }
        catch (EntryPointNotFoundException) { return -1; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;

        public MARGINS(int value)
        {
            Left = Right = Top = Bottom = value;
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd, int attribute, out int value, int valueSize);

    [DllImport("dwmapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State, Flags, GradientColor, AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public UIntPtr Size;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    // The documented transient backdrop switches to a solid brush for an
    // inactive window. The player deliberately opens without activation.
    public static bool TrySetPlayerAcrylic(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        const int WcaAccentPolicy = 19;
        const int AccentDisabled = 0;
        const int AccentAcrylicBlurBehind = 4;
        var policy = new AccentPolicy {
            State = enabled ? AccentAcrylicBlurBehind : AccentDisabled,
            Flags = enabled ? 2 : 0,
            GradientColor = enabled ? 0x301B1715 : 0 // Alpha, blue, green, red
        };
        int size = Marshal.SizeOf(typeof(AccentPolicy));
        var attribute = new WindowCompositionAttributeData {
            Attribute = WcaAccentPolicy, Data = Marshal.AllocHGlobal(size), Size = (UIntPtr)size
        };
        try
        {
            Marshal.StructureToPtr(policy, attribute.Data, false);
            if (enabled && SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE) < 0) return false;
            if (SetWindowCompositionAttribute(hwnd, ref attribute)) return true;
        }
        catch (EntryPointNotFoundException) { }
        finally { Marshal.FreeHGlobal(attribute.Data); }
        if (enabled) SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);
        return false;
    }

    private static int SetAttribute(IntPtr hwnd, int attribute, int value)
    {
        return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }

    /// <summary>
    /// Tests support for the documented system backdrop attribute (Windows 11
    /// build 22621+). This uses the HWND capability, not manifest-sensitive
    /// Environment.OSVersion. It does not change window appearance.
    /// </summary>
    public static bool Probe(IntPtr hwnd)
    {
        LastSupported = false;
        try
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                LastResult = E_INVALIDARG;
                return false;
            }

            int value;
            LastResult = DwmGetWindowAttribute(
                hwnd, DWMWA_SYSTEMBACKDROP_TYPE, out value, sizeof(int));
            LastSupported = LastResult >= 0;
            return LastSupported;
        }
        catch (DllNotFoundException) { LastResult = E_NOTIMPL; }
        catch (EntryPointNotFoundException) { LastResult = E_NOTIMPL; }
        catch (BadImageFormatException) { LastResult = E_NOTIMPL; }
        return false;
    }

    /// <summary>
    /// Enables Desktop Acrylic for Glass, or disables the backdrop for OLED.
    /// A negative HRESULT means the caller must supply an opaque fallback.
    /// A successful request is not a guarantee that Windows will show blur:
    /// system transparency/accessibility and performance policy still apply.
    /// </summary>
    public static int Apply(IntPtr hwnd, bool glass)
    {
        LastAppliedGlass = false;
        LastFrameResult = E_NOTIMPL;
        LastBackdropResult = E_NOTIMPL;
        if (!Probe(hwnd))
            return LastResult;

        try
        {
            // Decorations are best effort; do not reject Acrylic if a frame
            // customization prevents one of these optional preferences.
            SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
            SetAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            SetAttribute(hwnd, DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE);

            MARGINS margins = new MARGINS(glass ? -1 : 0);
            LastFrameResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (LastFrameResult < 0)
            {
                SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE);
                LastResult = LastFrameResult;
                return LastResult;
            }

            LastBackdropResult = SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                glass ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE);
            LastResult = LastBackdropResult;
            if (LastResult < 0)
            {
                // Clear a possibly previous successful material before the
                // caller paints its fallback; preserve the original HRESULT.
                SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE);
                margins = new MARGINS(0);
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                return LastResult;
            }

            LastAppliedGlass = glass;
            return LastResult;
        }
        catch (DllNotFoundException) { LastResult = E_NOTIMPL; }
        catch (EntryPointNotFoundException) { LastResult = E_NOTIMPL; }
        catch (BadImageFormatException) { LastResult = E_NOTIMPL; }
        return LastResult;
    }
}

public static class GlassWebWindowMove {
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr window, bool enabled);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static bool IsLeftPressed() { return (GetAsyncKeyState(1) & 0x8000) != 0; }
    public static void EnableInteraction(IntPtr window, bool enabled) { if (window != IntPtr.Zero) EnableWindow(window, enabled); }
    public static void BeginMove(IntPtr window) {
        if (window == IntPtr.Zero || !IsLeftPressed()) return;
        ReleaseCapture();
        SendMessage(window, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }
}
