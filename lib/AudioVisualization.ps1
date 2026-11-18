# The visualizer uses the current file's spectrum, with session peaks as a fallback.
$script:AudioVisualizationRoot = $PSScriptRoot
$script:AudioVisualizationSampler = $null
$script:AudioTrackSpectrum = $null
$script:AudioVisualizationSource = 'idle'
$script:AudioVisualizationInitializationFailed = $false

function Initialize-AudioVisualization {
    if ($null -ne $script:AudioVisualizationSampler) { return $true }
    if ($script:AudioVisualizationInitializationFailed) { return $false }
    try {
        if (-not ('MusicWidget.Visualization.SessionAudioVisualizer' -as [type])) {
            $sources = @('AudioVisualization.cs','TrackSpectrum.cs') | ForEach-Object {Join-Path $script:AudioVisualizationRoot $_}
            Add-Type -Path $sources
        }
        $script:AudioVisualizationSampler = [MusicWidget.Visualization.SessionAudioVisualizer]::new([int]$PID)
        $executable = Join-Path (Split-Path -Parent $script:AudioVisualizationRoot) 'bin\ffmpeg.exe'
        $script:AudioTrackSpectrum = [MusicWidget.Visualization.TrackSpectrumReader]::new($executable)
        return $true
    } catch {
        $script:AudioVisualizationInitializationFailed = $true
        return $false
    }
}

function Set-AudioVisualizationActive {
    param([bool]$Active)
    if ($Active -and $null -eq $script:AudioVisualizationSampler) { [void](Initialize-AudioVisualization) }
    if ($null -ne $script:AudioVisualizationSampler) { $script:AudioVisualizationSampler.SetActive($Active) }
    if (-not $Active) {
        if ($null -ne $script:AudioTrackSpectrum) {$script:AudioTrackSpectrum.Suspend()}
        $script:AudioVisualizationSource = 'idle'
    }
}

function Get-AudioVisualizationFrame {
    if ($null -eq $script:AudioVisualizationSampler) { return ,([double[]]@(0,0,0,0,0)) }
    return ,($script:AudioVisualizationSampler.ReadFrame())
}

function Get-GlassLiveAudioFrame {
    if ($null -ne $script:AudioTrackSpectrum -and $script:MediaIsReady -and $script:LoadedTrackPath -and $script:Player.Source.IsFile) {
        $levels = $script:AudioTrackSpectrum.Read($script:LoadedTrackPath,$script:Player.Position.TotalSeconds)
        if ($null -ne $levels) {
            if ($script:AudioVisualizationSource -ne 'file-spectrum') {$script:AudioVisualizationSampler.SetActive($false)}
            $script:AudioVisualizationSource = 'file-spectrum'
            return ,$levels
        }
    }
    if ($script:AudioVisualizationSource -eq 'file-spectrum' -and $null -ne $script:AudioVisualizationSampler) {
        $script:AudioVisualizationSampler.SetActive($true)
    }
    $script:AudioVisualizationSource = 'session-peak'
    return ,(Get-AudioVisualizationFrame)
}

function Reset-AudioVisualization {
    param([switch]$Rebind)
    if ($null -ne $script:AudioVisualizationSampler) { $script:AudioVisualizationSampler.Reset([bool]$Rebind) }
}

function Rebind-AudioVisualization {
    if ($null -ne $script:AudioVisualizationSampler) { $script:AudioVisualizationSampler.Rebind() }
}

function Get-AudioVisualizationStatus {
    if ($null -eq $script:AudioVisualizationSampler) {
        return @{ ProcessId=[int]$PID; Active=$false; WorkerRunning=$false; BoundSessionCount=0; LastHResult=0; Peak=0.0; Samples=0; Discoveries=0; Source='process-session-vu'; InitializationFailed=$script:AudioVisualizationInitializationFailed }
    }
    return $script:AudioVisualizationSampler.GetStatus()
}

function Stop-AudioVisualization {
    if ($null -ne $script:AudioTrackSpectrum) {
        $script:AudioTrackSpectrum.Dispose()
        $script:AudioTrackSpectrum = $null
    }
    if ($null -ne $script:AudioVisualizationSampler) {
        $script:AudioVisualizationSampler.Dispose()
        $script:AudioVisualizationSampler = $null
    }
}
