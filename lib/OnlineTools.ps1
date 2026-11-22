# Dependencies are provisioned only in response to an explicit action.
$script:OnlineToolsSetup = Join-Path (Split-Path -Parent $PSScriptRoot) 'installer\Install-OnlineTools.ps1'
$script:OnlineToolsProcess = $null
$script:OnlineToolsTimer = $null
$script:OnlineToolsFailed = $false
$script:OnlineToolsState = $null
$script:OnlineToolsNextCheck = [DateTime]::MinValue

function Get-OnlineToolsState {
    $now = [DateTime]::UtcNow
    if ($script:OnlineToolsState -and $now -lt $script:OnlineToolsNextCheck) { return $script:OnlineToolsState }
    $available = [IO.File]::Exists((Join-Path $script:OnlineToolsDirectory 'yt-dlp.exe'))
    $complete = $available -and [IO.File]::Exists((Join-Path $script:OnlineToolsDirectory 'deno.exe')) -and
        [IO.File]::Exists((Join-Path $script:OnlineToolsDirectory 'ffmpeg.exe')) -and
        [IO.File]::Exists((Join-Path $script:OnlineToolsDirectory 'ffprobe.exe'))
    $script:OnlineToolsState = [ordered]@{
        available = $available
        complete = $complete
        installing = $null -ne $script:OnlineToolsProcess
        failed = $script:OnlineToolsFailed -and -not $complete
        canInstall = [Environment]::Is64BitOperatingSystem -and [IO.File]::Exists($script:OnlineToolsSetup)
    }
    $script:OnlineToolsNextCheck = $now.AddSeconds(1)
    return $script:OnlineToolsState
}

function Stop-OnlineToolsMonitor {
    if ($null -ne $script:OnlineToolsTimer) {
        $script:OnlineToolsTimer.Stop()
        $script:OnlineToolsTimer = $null
    }
    if ($null -ne $script:OnlineToolsProcess) {
        $script:OnlineToolsProcess.Dispose()
        $script:OnlineToolsProcess = $null
    }
    $script:OnlineToolsNextCheck = [DateTime]::MinValue
}

function Start-OnlineToolsInstall {
    $state = Get-OnlineToolsState
    if (-not $state.canInstall -or $state.complete -or $state.installing) { return }
    $script:OnlineToolsFailed = $false
    $script:OnlineToolsNextCheck = [DateTime]::MinValue
    try {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
        $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -ToolsDirectory "{1}"' -f $script:OnlineToolsSetup,$script:OnlineToolsDirectory
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $script:OnlineToolsProcess = [Diagnostics.Process]::Start($start)
        $script:OnlineToolsTimer = [Windows.Threading.DispatcherTimer]::new()
        $script:OnlineToolsTimer.Interval = [TimeSpan]::FromMilliseconds(400)
        $script:OnlineToolsTimer.Add_Tick({
            if ($null -eq $script:OnlineToolsProcess -or -not $script:OnlineToolsProcess.HasExited) { return }
            $script:OnlineToolsFailed = $script:OnlineToolsProcess.ExitCode -ne 0
            Stop-OnlineToolsMonitor
            Publish-GlassWebState -Force
        })
        $script:OnlineToolsTimer.Start()
    } catch {
        $script:OnlineToolsFailed = $true
        Stop-OnlineToolsMonitor
    }
}
