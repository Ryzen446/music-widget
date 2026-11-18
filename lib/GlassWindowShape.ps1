# Window regions clip the native Acrylic backdrop as well as the WebView.
# Shape coordinates are host DIPs: frontend multiplies getBoundingClientRect
# by its WebView ZoomFactor before sending them. Only Windows DPI is applied
# here. Keep this separate from playlist/audio presentation state.
$script:GlassShapeLastKey = ''
$script:GlassShapeLastError = 0
$script:GlassShapeLastPayload = $null
$script:GlassShapeLastWindow = $null
$script:GlassShapeReapplyQueued = $false
$script:GlassShapeReapplying = $false
$script:GlassShapeRefreshMaterial = $false

function Request-GlassWindowShapeReapply {
    param([switch]$RefreshMaterial)
    if (-not $script:GlassFrameOwnedByWeb -or $script:GlassShapeReapplying) { return }
    if ($RefreshMaterial) {$script:GlassShapeRefreshMaterial=$true}
    $script:GlassShapeLastKey=''
    if ($script:GlassShapeReapplyQueued -or $null -eq $script:GlassShapeLastWindow) { return }
    $script:GlassShapeReapplyQueued=$true
    [void]$script:GlassShapeLastWindow.Dispatcher.BeginInvoke([Action]{
        $script:GlassShapeReapplyQueued=$false
        if (-not $script:GlassFrameOwnedByWeb -or $null -eq $script:GlassShapeLastPayload) {return}
        $script:GlassShapeReapplying=$true
        try {
            if ($script:GlassShapeRefreshMaterial) {
                $script:GlassShapeRefreshMaterial=$false
                Update-NativeMaterial
            }
            $script:GlassShapeLastKey=''
            [void](Set-GlassWindowShape -Shape $script:GlassShapeLastPayload -Window $script:GlassShapeLastWindow)
        } finally {$script:GlassShapeReapplying=$false}
    },[System.Windows.Threading.DispatcherPriority]::Render)
}

function Register-GlassWindowShapeEvents($Window) {
    if ([object]::ReferenceEquals($script:GlassShapeEventsWindow,$Window)) {return}
    $handle=[System.Windows.Interop.WindowInteropHelper]::new($Window).Handle
    $source=[System.Windows.Interop.HwndSource]::FromHwnd($handle)
    if ($null -eq $source) {return}
    $script:GlassShapeEventsWindow=$Window
    $script:GlassShapeEventSource=$source
    $script:GlassShapeNativeHook=[System.Windows.Interop.HwndSourceHook]{
        param([IntPtr]$h,[int]$message,[IntPtr]$w,[IntPtr]$l,[ref]$handled)
        # Reapply after WPF/DWM have completed a DPI, composition or theme
        # transition. SetWindowRgn's own WINDOWPOSCHANGED is deliberately absent.
        if ($message -in @(0x02E0,0x031E,0x031A,0x001A)) {
            Request-GlassWindowShapeReapply -RefreshMaterial
        }
        $handled.Value=$false
        return [IntPtr]::Zero
    }
    $source.AddHook($script:GlassShapeNativeHook)
    $Window.Add_SizeChanged({Request-GlassWindowShapeReapply})
    $Window.Add_Closed({
        try {if ($null -ne $script:GlassShapeEventSource) {$script:GlassShapeEventSource.RemoveHook($script:GlassShapeNativeHook)}} catch {}
    })
}

function Set-GlassWindowShape {
    param($Shape, $Window = $window)
    if ($null -eq $Window -or $null -eq $Shape -or $null -eq $Shape.main) { return $false }
    try {
        $handle = [System.Windows.Interop.WindowInteropHelper]::new($Window).Handle
        if ($handle -eq [IntPtr]::Zero) { return $false }
        Register-GlassWindowShapeEvents $Window
        $script:GlassShapeLastPayload=$Shape
        $script:GlassShapeLastWindow=$Window
        $dpiScale = [NativeBackdrop]::GetWindowDpiScale($handle)
        $mainBounds = [double[]]@($Shape.main.x, $Shape.main.y, $Shape.main.width, $Shape.main.height, $Shape.main.radius)
        $menuBounds = $null
        if ($null -ne $Shape.menu) {
            $menuBounds = [double[]]@($Shape.menu.x, $Shape.menu.y, $Shape.menu.width, $Shape.menu.height, $Shape.menu.radius)
        }
        # SetWindowRgn generates WindowPosChanged. Avoid repeating the same
        # region when the resulting observer/layout notification returns.
        $key = ConvertTo-Json -InputObject @($handle.ToInt64(), $dpiScale, $Window.Width, $Window.Height, $mainBounds, $menuBounds) -Compress -Depth 4
        if ($key -ceq $script:GlassShapeLastKey) { return $true }
        $script:GlassShapeLastError = [NativeBackdrop]::SetGlassRegion($handle, $dpiScale, $mainBounds, $menuBounds)
        if ($script:GlassShapeLastError -ge 0) {
            $script:GlassShapeLastKey = $key
            return $true
        }
    } catch {
        $script:GlassShapeLastError = -2147467259
    }
    return $false
}

function Clear-GlassWindowShape {
    param($Window = $window)
    $script:GlassShapeLastKey = ''
    if ($null -eq $Window) { return $false }
    try {
        $handle = [System.Windows.Interop.WindowInteropHelper]::new($Window).Handle
        if ($handle -eq [IntPtr]::Zero) { return $false }
        $script:GlassShapeLastError = [NativeBackdrop]::ClearGlassRegion($handle)
        return $script:GlassShapeLastError -ge 0
    } catch {
        $script:GlassShapeLastError = -2147467259
        return $false
    }
}
