[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '64-bit .NET Framework 4.8 is required.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$output = Join-Path $OutputDirectory 'MusicWidget.NativeMini.exe'
$references = @(
    (Join-Path $framework 'WPF\PresentationCore.dll')
    (Join-Path $framework 'WPF\PresentationFramework.dll')
    (Join-Path $framework 'WPF\WindowsBase.dll')
    (Join-Path $framework 'System.Xaml.dll')
    (Join-Path $framework 'System.Windows.Forms.dll')
    (Join-Path $framework 'System.Drawing.dll')
)
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/langversion:5', '/optimize+', '/utf8output',
    "/out:$output", "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')",
    "/win32icon:$(Join-Path $repository 'assets\music_widget_glass.ico')"
)
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += @(
    (Join-Path $PSScriptRoot 'NativeMiniPlayer.cs')
    (Join-Path $PSScriptRoot 'GlassControls.cs')
    (Join-Path $repository 'lib\NativeBackdrop.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native mini player compilation failed.' }
Get-Item -LiteralPath $output
