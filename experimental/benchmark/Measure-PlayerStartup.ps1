#Requires -Version 5.1
param(
    [Parameter(Mandatory)][string]$WebRoot,
    [Parameter(Mandatory)][string]$NativeExe,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$NativeApp,
    [ValidateRange(2,12)][int]$Runs = 5
)

$ErrorActionPreference = 'Stop'
$WebRoot = (Resolve-Path -LiteralPath $WebRoot).Path
$NativeExe = (Resolve-Path -LiteralPath $NativeExe).Path
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$worker = Join-Path $PSScriptRoot 'Start-WebPlayerBenchmark.ps1'
$runDirectory = Join-Path ([IO.Path]::GetTempPath()) ('MusicWidget-benchmark-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($runDirectory)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BenchmarkWindow {
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr h, uint message, IntPtr wParam, IntPtr lParam);
    public static void Close(long window, uint expectedPid) {
        uint owner;
        IntPtr handle = new IntPtr(window);
        if (GetWindowThreadProcessId(handle, out owner) != 0 && owner == expectedPid)
            PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }
}
'@

function Wait-ForReadyFile([Diagnostics.Process]$Process,[string]$ReadyPath,[string]$LogPath) {
    $timeout = [Diagnostics.Stopwatch]::StartNew()
    while ($timeout.Elapsed.TotalSeconds -lt 30) {
        if ([IO.File]::Exists($ReadyPath)) {
            try {
                $snapshot = Get-Content -Raw -LiteralPath $ReadyPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                if ($null -ne $snapshot) { return $snapshot }
            } catch {
                # A short write to the ready file can race this read.
            }
        }
        if ($Process.HasExited) { break }
        Start-Sleep -Milliseconds 12
    }
    $details = if ([IO.File]::Exists($LogPath)) { Get-Content -Raw -LiteralPath $LogPath }
    throw "Player failed to report a frame. $details"
}

function Get-PrivateWorkingSet([int[]]$Ids) {
    $filter = ($Ids | ForEach-Object { "IDProcess = $_" }) -join ' OR '
    $processes = @(Get-CimInstance -ClassName Win32_PerfRawData_PerfProc_Process -Filter $filter)
    if ($processes.Count -eq 0) { throw 'No process metrics were returned' }
    [pscustomobject]@{
        processCount = $processes.Count
        privateWorkingSetMiB = [Math]::Round(($processes | Measure-Object WorkingSetPrivate -Sum).Sum / 1MB, 1)
        privateCommitMiB = [Math]::Round(($processes | Measure-Object PrivateBytes -Sum).Sum / 1MB, 1)
    }
}

function Invoke-PlayerTrial([string]$Player,[string]$Profile,[int]$Trial,[string]$Scenario) {
    $prefix = Join-Path $runDirectory "$Player-$Scenario-$Trial"
    $readyPath = "$prefix-ready.json"
    $stopPath = "$prefix-stop"
    $logPath = "$prefix-errors.txt"
    $start = [DateTime]::UtcNow
    if ($Player -eq 'web') {
        $arguments = '-NoProfile -STA -ExecutionPolicy Bypass -File "{0}" -BenchmarkWebRoot "{1}" -BenchmarkProfile "{2}" -BenchmarkReady "{3}" -BenchmarkStop "{4}"' -f $worker,$WebRoot,$Profile,$readyPath,$stopPath
        $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardError $logPath
    } else {
        $arguments = '--no-tray --ready-file "{0}"' -f $readyPath
        if ($NativeApp) { $arguments += ' --data-dir "{0}"' -f ($Profile + '-native') }
        $process = Start-Process -FilePath $NativeExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }

    $ready = $null
    try {
        $ready = Wait-ForReadyFile $process $readyPath $logPath
        $rendered = if ($Player -eq 'web') { ([DateTime]$ready.paintUtc).ToUniversalTime() } else { ([DateTime]$ready.utc).ToUniversalTime() }
        if ($ready.width -ne 350 -or $ready.height -ne 52) {
            throw "Unexpected $Player benchmark window size ($($ready.width) × $($ready.height))"
        }
        $processIds = if ($Player -eq 'web') { [int[]]$ready.processIds } else { @($process.Id) }
        Start-Sleep -Seconds 2
        $metrics = Get-PrivateWorkingSet $processIds
        [pscustomobject]@{
            player = $Player
            scenario = $Scenario
            trial = $Trial
            openMs = [Math]::Round(($rendered - $start).TotalMilliseconds, 1)
            privateWorkingSetMiB = $metrics.privateWorkingSetMiB
            privateCommitMiB = $metrics.privateCommitMiB
            processCount = $metrics.processCount
        }
    } finally {
        if ($Player -eq 'web') {
            [IO.File]::WriteAllText($stopPath, '')
        } elseif (-not $process.HasExited -and $null -ne $ready) {
            [BenchmarkWindow]::Close($ready.hwnd, $process.Id)
        }
        if (-not $process.WaitForExit(4000)) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$results = foreach ($scenario in 'fresh-profile','existing-profile') {
    for ($trial = 1; $trial -le $Runs; $trial++) {
        $profile = if ($scenario -eq 'fresh-profile') {
            Join-Path $runDirectory "profile-$trial"
        } else {
            Join-Path $runDirectory 'profile-1'
        }
        $order = if ($trial % 2) { @('web','native') } else { @('native','web') }
        foreach ($player in $order) { Invoke-PlayerTrial $player $profile $trial $scenario }
    }
}

$summary = foreach ($group in ($results | Group-Object player,scenario)) {
    [pscustomobject]@{
        player = $group.Group[0].player
        scenario = $group.Group[0].scenario
        medianOpenMs = [Math]::Round((Get-Median $group.Group.openMs), 1)
        medianPrivateWorkingSetMiB = [Math]::Round((Get-Median $group.Group.privateWorkingSetMiB), 1)
        medianPrivateCommitMiB = [Math]::Round((Get-Median $group.Group.privateCommitMiB), 1)
        samples = $group.Count
    }
}
$os = Get-CimInstance Win32_OperatingSystem
$cpuName = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name).Trim()
$report = [pscustomobject]@{
    machine = @{ windows = "$($os.Caption), build $($os.BuildNumber)"; cpu = $cpuName }
    nativeMode = if ($NativeApp) { 'full-app' } else { 'prototype' }
    methodology = 'Paused empty library. Process launch to first verified web reveal or native ContentRendered and DwmFlush. Private resident working set, including the host and its WebView2 processes, sampled two seconds after presentation.'
    summary = $summary
    trials = $results
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$summary | Format-Table -AutoSize
