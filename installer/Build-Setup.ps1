#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '6.1.5',
    [string]$OutputDirectory,
    [string]$CompilerPath,
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'artifacts' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $CompilerPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $CompilerPath = $command.Source }
    else {
        $CompilerPath = @(
            "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup is required. Download it from https://jrsoftware.org/isdl.php or pass -CompilerPath.'
}
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
$buildRoot = Join-Path $project 'artifacts'
$payload = Join-Path $buildRoot ('native-payload-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($output)
[void][IO.Directory]::CreateDirectory($payload)
try {
    & (Join-Path $project 'native\Build-Native.ps1') -OutputDirectory $payload | Out-Null
    foreach ($file in 'LICENSE','LISEZ-MOI.txt') {
        Copy-Item -LiteralPath (Join-Path $project $file) -Destination $payload
    }
    $name = if ($SmokeTest) { 'MusicWidget-Setup-SmokeTest' } else { 'MusicWidget-Setup' }
    $argsLine = @("/O$output", "/F$name", "/DAppVersion=$Version", "/DPayloadDir=$payload")
    if ($SmokeTest) { $argsLine += '/DSmokeTest' }
    & $CompilerPath @argsLine (Join-Path $PSScriptRoot 'MusicWidget.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

    $setup = Join-Path $output ($name + '.exe')
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $output ($name + '.sha256.txt')) -Value "$hash  $name.exe" -Encoding ascii
    if (-not $SmokeTest) {
        Set-Content -LiteralPath (Join-Path $payload 'portable.txt') -Value 'Music Widget portable mode' -Encoding ascii
        $zip = Join-Path $output 'MusicWidget-Native-Portable.zip'
        Compress-Archive -LiteralPath @(
            (Join-Path $payload 'MusicWidget.exe'),
            (Join-Path $payload 'portable.txt'),
            (Join-Path $payload 'LICENSE'),
            (Join-Path $payload 'OFL-Inter.txt'),
            (Join-Path $payload 'LISEZ-MOI.txt')
        ) -DestinationPath $zip -Force
        Get-Item -LiteralPath $zip
    }
    Get-Item -LiteralPath $setup
} finally {
    $rootPath = (Resolve-Path -LiteralPath $buildRoot).Path.TrimEnd('\') + '\'
    $resolvedPayload = (Resolve-Path -LiteralPath $payload).Path
    if ($resolvedPayload.StartsWith($rootPath,[StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedPayload).StartsWith('native-payload-',[StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedPayload -Recurse -Force
    }
}
