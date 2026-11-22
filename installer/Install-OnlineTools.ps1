#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ToolsDirectory)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'MusicWidget-OnlineTools'; Accept = 'application/vnd.github+json' }

function Get-ReleaseAsset([string]$Repository, [string]$FilePattern) {
    $releaseUri = 'https://api.github.com/repos/{0}/releases/latest' -f $Repository
    $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers -TimeoutSec 30
    $assets = @($release.assets | Where-Object { $_.name -cmatch $FilePattern -and $_.state -eq 'uploaded' })
    if ($assets.Count -ne 1) { throw "Cannot find the Windows release for $Repository." }
    $asset = $assets[0]
    $expectedPrefix = 'https://github.com/{0}/releases/download/' -f $Repository
    if (-not ([string]$asset.browser_download_url).StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $asset.digest -notmatch '^sha256:[a-f0-9]{64}$' -or $asset.size -le 0 -or $asset.size -gt 500MB) {
        throw "The release metadata for $Repository could not be verified."
    }
    return $asset
}

function Save-VerifiedAsset($Asset, [string]$Destination) {
    Write-Output "Downloading $($Asset.name)..."
    Invoke-WebRequest -UseBasicParsing -Uri $Asset.browser_download_url -Headers @{ 'User-Agent' = 'MusicWidget-OnlineTools' } -TimeoutSec 600 -OutFile $Destination
    $expectedHash = $Asset.digest.Substring(7)
    $actualHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    if ([IO.FileInfo]::new($Destination).Length -ne $Asset.size -or $actualHash -ine $expectedHash) {
        throw "The downloaded file did not match the published checksum: $($Asset.name)."
    }
}

function Expand-ToolExecutable([string]$Archive, [string]$EntryPattern, [string]$Destination) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName -cmatch $EntryPattern })
        if ($entries.Count -ne 1 -or $entries[0].Length -le 0 -or $entries[0].Length -gt 300MB) {
            throw 'The verified archive does not contain the expected Windows executable.'
        }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $Destination)
    } finally { $zip.Dispose() }
}

$guard = $null
$staging = $null
try {
    $bin = [IO.Path]::GetFullPath($ToolsDirectory)
    [void][IO.Directory]::CreateDirectory($bin)
    $lockPath = Join-Path $bin '.online-tools.lock'
    try {
        $guard = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch [IO.IOException] {
        throw 'Online tools are already being installed. Please try again after that installation completes.'
    }

    $required = @('yt-dlp.exe', 'deno.exe', 'ffmpeg.exe', 'ffprobe.exe')
    $missing = @($required | Where-Object { -not [IO.File]::Exists((Join-Path $bin $_)) })
    if ($missing.Count -eq 0) {
        Write-Output 'Online tools are ready.'
        return
    }

    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'The automatic online tools installation requires 64-bit Windows.'
    }
    Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
    $staging = Join-Path $bin ('.install-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($staging)

    if ('yt-dlp.exe' -in $missing) {
        Save-VerifiedAsset (Get-ReleaseAsset 'yt-dlp/yt-dlp' '^yt-dlp\.exe$') (Join-Path $staging 'yt-dlp.exe')
    }
    if ('deno.exe' -in $missing) {
        $denoArchive = Join-Path $staging 'deno.zip'
        Save-VerifiedAsset (Get-ReleaseAsset 'denoland/deno' '^deno-x86_64-pc-windows-msvc\.zip$') $denoArchive
        Expand-ToolExecutable $denoArchive '^deno\.exe$' (Join-Path $staging 'deno.exe')
    }
    if ('ffmpeg.exe' -in $missing -or 'ffprobe.exe' -in $missing) {
        $ffmpegArchive = Join-Path $staging 'ffmpeg.zip'
        Save-VerifiedAsset (Get-ReleaseAsset 'GyanD/codexffmpeg' '^ffmpeg-\d+(\.\d+)+-essentials_build\.zip$') $ffmpegArchive
        foreach ($file in @('ffmpeg.exe','ffprobe.exe') | Where-Object { $_ -in $missing }) {
            $pattern = '^ffmpeg-\d+(\.\d+)+-essentials_build/bin/' + [regex]::Escape($file) + '$'
            Expand-ToolExecutable $ffmpegArchive $pattern (Join-Path $staging $file)
        }
    }

    foreach ($file in $missing) {
        $destination = Join-Path $bin $file
        if (-not [IO.File]::Exists($destination)) {
            [IO.File]::Move((Join-Path $staging $file), $destination)
        }
    }
    Write-Output 'Online tools are ready.'
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    if ($staging -and [IO.Directory]::Exists($staging)) {
        foreach ($file in [IO.Directory]::EnumerateFiles($staging)) { [IO.File]::Delete($file) }
        [IO.Directory]::Delete($staging, $false)
    }
    if ($null -ne $guard) { $guard.Dispose() }
}
