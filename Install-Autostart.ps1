#Requires -Version 5.1
<#
.SYNOPSIS
    Installe ou desinstalle le demarrage automatique du widget musique au demarrage du PC.
.PARAMETER Remove
    Supprime le raccourci de demarrage automatique.
#>
param([switch]$Remove)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$LauncherVbs = Join-Path $ScriptDir 'Lancer-Widget.vbs'
$IconPath = Join-Path $ScriptDir 'assets\music_widget_glass.ico'
if (-not (Test-Path $IconPath)) { $IconPath = Join-Path $ScriptDir 'assets\icon.ico' }
$StartupFolder = [Environment]::GetFolderPath('Startup')
$ShortcutPath = Join-Path $StartupFolder 'Music Widget.lnk'

if ($Remove) {
    if (Test-Path $ShortcutPath) {
        Remove-Item $ShortcutPath -Force
        Write-Host "Demarrage automatique desactive avec succes." -ForegroundColor Yellow
    } else {
        Write-Host "Aucun raccourci de demarrage automatique trouve." -ForegroundColor Gray
    }
    exit 0
}

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = "$env:SystemRoot\System32\wscript.exe"
$Shortcut.Arguments = "`"$LauncherVbs`""
$Shortcut.WorkingDirectory = $ScriptDir
$Shortcut.Description = 'Music Widget - Liquid Glass'
if (Test-Path $IconPath) {
    $Shortcut.IconLocation = "$IconPath,0"
}
$Shortcut.WindowStyle = 7
$Shortcut.Save()

Write-Host "Demarrage automatique active !" -ForegroundColor Green
Write-Host "Le widget demarrera automatiquement avec Windows de maniere 100% silencieuse (sans fenetre CMD)." -ForegroundColor Green
