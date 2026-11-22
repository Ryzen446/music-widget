# Native Mini Player experiment

This x64 WPF prototype runs the Minimalist surface as a compiled C# executable. It uses the same Windows Acrylic helper as Music Widget, native WPF drawing for the card and controls, and WPF MediaPlayer for local audio. It runs without starting PowerShell or WebView2.

## Build and run

```powershell
./experimental/native-mini/Build-NativeMiniPlayer.ps1
./experimental/native-mini/bin/MusicWidget.NativeMini.exe
```

The build uses the compiler and WPF libraries supplied with 64-bit .NET Framework on Windows. The app has a local queue, a file picker, play and pause, track skipping, seek, volume, mute, an always-on-top setting, and a tray icon that can restore the window. The prototype keeps an independent in-memory library.

```powershell
./experimental/native-mini/bin/MusicWidget.NativeMini.exe --track "C:\Music\Example.wav" --play
```

## Compare first paint

```powershell
$ready = Join-Path $env:TEMP ("MusicWidget-" + [guid]::NewGuid() + ".json")
./experimental/native-mini/bin/MusicWidget.NativeMini.exe --ready-file $ready
```

The UTF-8 JSON is written atomically after WPF reports `ContentRendered` and `DwmFlush` completes. It includes the process ID, UTC timestamp, configured window bounds and backdrop status. The timer in the JSON starts at managed `Main`. Compare both executables from an external launch timestamp to account for process and runtime initialization. The optional `--state-file` reports playback changes for a synthetic media smoke test.

For repeatable visual captures, use `--title`, `--aura '#8AA9FF'`, `--progress 0.42`, `--left`, `--top` and `--language en`. `--background '#181D29'` selects a known flat background for material-only captures. Leave it unset to test Windows Acrylic. `--no-tray` is available for isolated smoke runs.

## Liquid Glass rendering

The shipped React app sets Open Glass UI's renderer to CSS. The main card uses the clear material, an 8 DIP corner radius, a translucent aura fill, an inner edge highlight and a four-pixel progress line. With the current theme, the browser computes the card filter as `blur(1.8px) saturate(1.9) brightness(0.46)`. The secondary buttons add their own backdrop filter and a conic edge highlight. The WPF prototype uses identical card and button dimensions and ports the original icon paths and color mixes. Linear gradients and pens approximate the optical layers. The menu and volume popup use WPF compositing and are simpler than the full browser controls.

The same DWM Acrylic helper can supply the desktop backdrop for a native client. Windows applies its own rules for backdrop and transparency, with a solid fallback for high contrast or unavailable composition. [Desktop backdrop types](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)

For a broader port, Windows Composition offers a host backdrop brush and animatable effect brushes. A native visual layer can reproduce the blur, saturation, tint, highlights and rounded masks. WPF pixel shaders can process the inputs supplied by a visual. They need a separate input to sample a backdrop outside that visual. A compatibility target should compare short and long titles, popups, animation, and several wallpaper auras across display scales. [Win32 host backdrop flag](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) · [Composition effects](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects) · [WPF pixel shaders](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.pixelshader)
