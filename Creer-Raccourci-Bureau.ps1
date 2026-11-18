#Requires -Version 5.1
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$LauncherVbs = Join-Path $ScriptDir 'Lancer-Widget.vbs'
$IconPath = Join-Path $ScriptDir 'assets\music_widget_glass.ico'
if (-not (Test-Path $IconPath)) { $IconPath = Join-Path $ScriptDir 'assets\music_player.ico' }
if (-not (Test-Path $IconPath)) { $IconPath = Join-Path $ScriptDir 'assets\icon.ico' }

$desktop = [Environment]::GetFolderPath('DesktopDirectory')
if (-not $desktop -or -not (Test-Path -LiteralPath $desktop -PathType Container)) {
    throw 'Windows could not locate the desktop folder.'
}

$WshShell = New-Object -ComObject WScript.Shell

$ShortcutPath = Join-Path $desktop 'Music Widget.lnk'
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = "$env:SystemRoot\System32\wscript.exe"
$Shortcut.Arguments = "`"$LauncherVbs`""
$Shortcut.WorkingDirectory = $ScriptDir
$Shortcut.Description = 'Music Widget - Liquid Glass'
if (Test-Path -LiteralPath $IconPath) {
    $Shortcut.IconLocation = "$IconPath,0"
}
$Shortcut.WindowStyle = 7
$Shortcut.Save()
Write-Host "Shortcut saved to $ShortcutPath" -ForegroundColor Green

# Force Windows Shell Icon Cache to refresh
try {
    $code = @'
    using System;
    using System.Runtime.InteropServices;
    public class ShellIconRefresh {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
'@
    Add-Type -TypeDefinition $code -ErrorAction SilentlyContinue
    [ShellIconRefresh]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
} catch {}
