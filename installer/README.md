# Music Widget Windows setup

Music Widget Setup installs the native C# application for the current user on x64 Windows. It creates a Start menu entry, an optional desktop shortcut, and an uninstall entry. Setup checks for .NET Framework 4.8 or a later installed version.

The optional online-tools task installs the same tools offered by **Enable Music Widget online tools** in the Start menu. The user can run that shortcut later or use the activation button from the online search panel.

Executable files are installed in `%LOCALAPPDATA%\Programs\Music Widget` by default. Settings, local audio, playlists, and optional tools are kept in `%LOCALAPPDATA%\MusicWidget\Data` across updates and uninstallation.

## Build

Use Windows x64 with Windows PowerShell 5.1, .NET Framework 4.8 or later, and Inno Setup. The build was tested with Inno Setup 7.1.1.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Setup.ps1
```

The build writes `MusicWidget-Setup.exe`, its SHA-256 and `MusicWidget-Native-Portable.zip` to `artifacts`. Pass `-CompilerPath` or `-OutputDirectory` to use explicit paths.

[Inno Setup downloads](https://jrsoftware.org/inno-setup/downloads) · [Microsoft .NET Framework detection](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed)
