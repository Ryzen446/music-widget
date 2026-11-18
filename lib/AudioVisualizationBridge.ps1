# The UI receives only a small cached frame. Session metering runs on its own
# MTA worker; no audio samples or global endpoint peaks cross this adapter.
$script:GlassAudioTimer=$null
$script:GlassAudioInitialized=$false
$script:GlassAudioActive=$false
$script:GlassAudioFailed=$false
$script:GlassAudioLastJson=''
$script:GlassAudioBackendPath=Join-Path $LibDir 'AudioVisualization.ps1'
if (Test-Path -LiteralPath $script:GlassAudioBackendPath) {. $script:GlassAudioBackendPath}

function Test-GlassAudioVisualizationActive {
    $script:GlassWebReady -and $script:GlassWebActive -and $script:GlassWebPresentationReady -and
    -not $script:GlassWebFailed -and $script:GlassWebRequestedVisible -and $window.IsVisible -and
    $script:DesignMode -eq 'Compact' -and $script:GlassWebPane -in @('player','menu') -and
    $script:IsPlaying -and -not $script:IsMuted -and $script:LocalVolume -gt 0 -and (Test-MotionEnabled)
}

function Send-GlassAudioVisualizationFrame($Frame) {
    if (-not $script:GlassWebInitialized -or $script:GlassWebFailed -or $null -eq $script:GlassWebControl) {return}
    try {
        $core=$script:GlassWebControl.CoreWebView2
        if ($null -eq $core -or $core.Source -cne $script:GlassWebOrigin -or $core.IsSuspended) {return}
        $raw=@($Frame);$levels=[double[]]@(0,0,0,0,0)
        for($i=0;$i -lt 5;$i++) {
            if ($i -ge $raw.Count) {continue}
            try {$number=[double]$raw[$i]} catch {continue}
            if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) {continue}
            $levels[$i]=[math]::Round([math]::Max(0.0,[math]::Min(1.0,$number)),3)
        }
        $json=ConvertTo-Json -InputObject ([ordered]@{type='visualizer';levels=$levels}) -Compress
        if ($json -cne $script:GlassAudioLastJson) {$core.PostWebMessageAsJson($json);$script:GlassAudioLastJson=$json}
    } catch {
        # An unavailable visualizer must never interrupt native playback.
    }
}

function Initialize-GlassAudioVisualization {
    if ($script:GlassAudioInitialized) {return $true}
    if ($script:GlassAudioFailed) {return $false}
    try {
        if (-not (Get-Command Initialize-AudioVisualization -ErrorAction SilentlyContinue)) {return $false}
        if (-not (Initialize-AudioVisualization)) {$script:GlassAudioFailed=$true;return $false}
        $script:GlassAudioTimer=[System.Windows.Threading.DispatcherTimer]::new([System.Windows.Threading.DispatcherPriority]::Background)
        $script:GlassAudioTimer.Interval=[TimeSpan]::FromMilliseconds(50)
        $script:GlassAudioTimer.Add_Tick({
            if (-not (Test-GlassAudioVisualizationActive)) {Update-GlassAudioVisualization;return}
            try {Send-GlassAudioVisualizationFrame (Get-GlassLiveAudioFrame)} catch {}
        })
        $script:GlassAudioInitialized=$true
        return $true
    } catch {$script:GlassAudioFailed=$true;return $false}
}

function Update-GlassAudioVisualization {
    $active=Test-GlassAudioVisualizationActive
    if ($active) {
        if (-not (Initialize-GlassAudioVisualization)) {return}
        $interval=if ($script:GlassWebInteractive) {50} else {125}
        if ($script:GlassAudioTimer.Interval.TotalMilliseconds -ne $interval) {
            $script:GlassAudioTimer.Interval=[TimeSpan]::FromMilliseconds($interval)
        }
        if (-not $script:GlassAudioActive) {
            Set-AudioVisualizationActive -Active $true
            $script:GlassAudioActive=$true;$script:GlassAudioLastJson=''
            $script:GlassAudioTimer.Start()
        }
    } else {
        if ($null -ne $script:GlassAudioTimer -and $script:GlassAudioTimer.IsEnabled) {$script:GlassAudioTimer.Stop()}
        if ($script:GlassAudioActive) {
            Set-AudioVisualizationActive -Active $false
            $script:GlassAudioActive=$false
            Send-GlassAudioVisualizationFrame ([double[]]@(0,0,0,0,0))
        }
    }
}

function Stop-GlassAudioVisualization {
    if ($null -ne $script:GlassAudioTimer) {$script:GlassAudioTimer.Stop();$script:GlassAudioTimer=$null}
    $script:GlassAudioActive=$false
    if ($script:GlassAudioInitialized) {Stop-AudioVisualization}
    $script:GlassAudioInitialized=$false
}
