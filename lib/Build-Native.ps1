#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$compiler = Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not [IO.File]::Exists($compiler)) { throw 'Run this script with Windows PowerShell 5.1.' }
$sources = @('NativeBackdrop.cs','TrayMenu.cs','GlobalHotkeys.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$assembly = Join-Path $PSScriptRoot 'MusicWidget.Native.dll'
& $compiler /nologo /target:library /optimize+ "/out:$assembly" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'MusicWidget.Native.dll build failed.' }
