# Comparing the native and WebView2 mini players

Build the C# app with `native/Build-Native.ps1`. Extract the [portable 5.5.0 release](https://github.com/Ryzen446/music-widget/releases/tag/v5.5.0) to a separate folder. From the repository root, run the benchmark in Windows PowerShell.

```powershell
./experimental/benchmark/Measure-PlayerStartup.ps1 `
  -WebRoot 'C:\Bench\MusicWidget-5.5.0' `
  -NativeApp -NativeExe './artifacts/native/MusicWidget.exe' `
  -OutputPath 'C:\Bench\MusicWidget-results.json'
```

The two players open the same 350 × 52 DIP surface with an empty library and playback paused. The harness alternates the launch order and uses a temporary WebView2 profile. It measures five pairs with a new profile and five with an existing profile. This measures profile initialization on a running machine.

Startup ends when the 5.5.0 release reveals its verified Glass frame or the native app reports WPF's `ContentRendered` and completes `DwmFlush`. Two seconds later, Windows performance counters provide the private resident working set for the host and all of its WebView2 processes. The report includes private committed memory and individual trials.

Both apps expose their main player at startup. The harness suppresses its test instances' tray icons and global shortcuts. Library, settings and online panes are built on demand in the native app. To reproduce the earlier [mini-player prototype](../native-mini/README.md) comparison, omit `-NativeApp` and pass the prototype executable.
