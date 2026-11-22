#Requires -Version 5.1
param(
    [Parameter(Mandatory)][string]$BenchmarkWebRoot,
    [Parameter(Mandatory)][string]$BenchmarkProfile,
    [Parameter(Mandatory)][string]$BenchmarkReady,
    [Parameter(Mandatory)][string]$BenchmarkStop,
    [switch]$BenchmarkVisual
)

$ErrorActionPreference = 'Stop'
. (Join-Path $BenchmarkWebRoot 'MusicWidget.ps1') -NoRun -DataDirectory $BenchmarkProfile
$window.ShowInTaskbar = $false
$window.ShowActivated = $false
if ($BenchmarkVisual) {
    $script:AuraColor = '#A284D0'
    $TxtTitle.Text = 'Night Drive'
}

function Invoke-BenchmarkDispatcherFrame {
    $frame = [Windows.Threading.DispatcherFrame]::new()
    $timer = [Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds(15)
    $timer.Add_Tick({ $timer.Stop(); $frame.Continue = $false }.GetNewClosure())
    $timer.Start()
    [Windows.Threading.Dispatcher]::PushFrame($frame)
}

try {
    [void](Initialize-GlassWeb)
    Show-Widget
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    while (-not $script:GlassWebPresentationRevealed) {
        if ($script:GlassWebFailed -or [DateTime]::UtcNow -ge $deadline) {
            throw "WebView2 did not reveal the verified Glass frame. $script:GlassWebError"
        }
        Invoke-BenchmarkDispatcherFrame
    }
    $paintUtc = [DateTime]::UtcNow
    $browser = $script:GlassWebControl.CoreWebView2
    $processIds = @($PID) + @($browser.Environment.GetProcessInfos() | ForEach-Object ProcessId)
    $handle = ([System.Windows.Interop.WindowInteropHelper]::new($window)).Handle
    $result = @{
        paintUtc = $paintUtc.ToString('o')
        processIds = @($processIds | Sort-Object -Unique)
        width = $window.Width
        height = $window.Height
        left = $window.Left
        top = $window.Top
        handle = $handle.ToInt64()
    }
    $result | ConvertTo-Json -Compress | Set-Content -LiteralPath $BenchmarkReady -Encoding UTF8
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while (-not (Test-Path -LiteralPath $BenchmarkStop) -and [DateTime]::UtcNow -lt $deadline) {
        Invoke-BenchmarkDispatcherFrame
    }
} finally {
    $window.Close()
    $progressTimer.Stop()
    $saveTimer.Stop()
    $script:TrayIcon.Dispose()
}
