#Requires -Version 5.1
[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts\native' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not [IO.File]::Exists($compiler)) { throw '.NET Framework 4.8 on 64-bit Windows is required.' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($destination)
$exe = Join-Path $destination 'MusicWidget.exe'
$icon = Join-Path $repo 'assets\music_widget_glass.ico'
$cscArgs = @(
    '/nologo','/target:winexe','/langversion:5','/platform:x64','/optimize+','/utf8output',
    "/out:$exe", "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')", "/win32icon:$icon",
    "/resource:$icon,app.ico", "/resource:$(Join-Path $repo 'assets\music_widget_glass.png'),app.png",
    "/resource:$(Join-Path $repo 'lib\translations\en.json'),localization.en.json",
    "/resource:$(Join-Path $repo 'lib\translations\fr.json'),localization.fr.json",
    "/resource:$(Join-Path $PSScriptRoot 'GlassScrollBars.xaml'),styles.scrollbars.xaml"
)
foreach ($assembly in 'WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll',
    'System.Windows.Forms.dll','System.Drawing.dll','Microsoft.CSharp.dll','System.Web.Extensions.dll',
    'System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Net.Http.dll') {
    $cscArgs += "/reference:$(Join-Path $framework $assembly)"
}
$cscArgs += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
$cscArgs += Join-Path $repo 'experimental\native-mini\GlassControls.cs'
foreach ($source in 'NativeBackdrop.cs','TrayMenu.cs','GlobalHotkeys.cs','AudioVisualization.cs','TrackSpectrum.cs','TrackSearch.cs','TrackDownloader.cs') {
    $cscArgs += Join-Path $repo "lib\$source"
}
$fonts = & (Join-Path $PSScriptRoot 'Build-FontResources.ps1')
try {
    & $compiler @cscArgs "/resource:$fonts,MusicWidget.g.resources"
    if ($LASTEXITCODE -ne 0) { throw 'Music Widget native build failed.' }
} finally {
    Remove-Item -LiteralPath $fonts -ErrorAction SilentlyContinue
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fonts\OFL-Inter.txt') -Destination $destination
Get-Item -LiteralPath $exe
