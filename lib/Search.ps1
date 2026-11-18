# Local asynchronous YouTube metadata search. No network activity on dot-source.
# Setup: . (Join-Path $LibDir 'Search.ps1'); [void](Initialize-TrackSearch)
# UI: Start-TrackSearch -Query 'artist title' -Page 1 -PageSize 8
# Timer: [void](Update-TrackSearch); publish (Get-TrackSearchState)
# On close / leaving search: Cancel-TrackSearch. Downloads remain in the existing
# Start-MusicDownload flow, using the canonical URL of a selected result.
if ($null -eq $script:Translations) {. (Join-Path $PSScriptRoot 'I18n.ps1')}
$script:TrackSearchRoot = $PSScriptRoot
$script:TrackSearchExecutable = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\yt-dlp.exe'
$script:TrackSearchRequest = $null
$script:TrackSearchGeneration = 0
$script:TrackSearchActiveGeneration = 0
$script:TrackSearchState = @{ query=''; page=1; pageSize=8; hasNext=$false; busy=$false; error=''; items=@() }

function Initialize-TrackSearch {
    param([string]$ExecutablePath = '')
    try {
        if (-not ('TrackSearch' -as [type])) {
            Add-Type -Path (Join-Path $script:TrackSearchRoot 'TrackSearch.cs') -ReferencedAssemblies 'System.Web.Extensions.dll'
        }
        if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) {
            $script:TrackSearchExecutable = [IO.Path]::GetFullPath($ExecutablePath)
        }
        return $true
    } catch {
        $script:TrackSearchState.error = 'searchModuleFailed'
        return $false
    }
}

function Cancel-TrackSearch {
    param([switch]$Clear)
    $script:TrackSearchGeneration++
    $request = $script:TrackSearchRequest
    $script:TrackSearchRequest = $null
    if ($null -ne $request) { $request.Cancel(); $request.Dispose() }
    $script:TrackSearchState.busy = $false
    $script:TrackSearchState.error = ''
    if ($Clear) {
        $script:TrackSearchState = @{ query=''; page=1; pageSize=8; hasNext=$false; busy=$false; error=''; items=@() }
    }
}

function Stop-TrackSearch { Cancel-TrackSearch }

function Start-TrackSearch {
    param([string]$Query, [int]$Page = 1, [int]$PageSize = 8)
    Cancel-TrackSearch
    $queryText = if ($null -eq $Query) { '' } else { $Query.Trim() }
    $script:TrackSearchState = @{ query=$queryText; page=([math]::Max(1,[math]::Min(10,$Page))); pageSize=([math]::Max(1,[math]::Min(20,$PageSize))); hasNext=$false; busy=$false; error=''; items=@() }
    if ([string]::IsNullOrWhiteSpace($queryText)) {
        $script:TrackSearchState.page = 1
        return
    }
    if ($Page -lt 1 -or $Page -gt 10 -or $PageSize -lt 1 -or $PageSize -gt 20 -or $queryText.Length -gt 200 -or $queryText -match '[\x00-\x1f\x7f]') {
        $script:TrackSearchState.error = 'invalidSearch'
        return
    }
    if (-not (Initialize-TrackSearch)) { return }
    try {
        $script:TrackSearchRequest = [TrackSearch]::TaskStart($queryText, $Page, $PageSize, $script:TrackSearchExecutable)
        $script:TrackSearchActiveGeneration = $script:TrackSearchGeneration
        $script:TrackSearchState.busy = $true
    } catch {
        $script:TrackSearchState.error = 'searchStartFailed'
    }
}

function Update-TrackSearch {
    $request = $script:TrackSearchRequest
    if ($null -eq $request -or -not $request.IsCompleted) { return $false }
    if ($script:TrackSearchActiveGeneration -ne $script:TrackSearchGeneration) { return $false }
    $result = $request.Result
    $script:TrackSearchRequest = $null
    $request.Dispose()
    $script:TrackSearchState.busy = $false
    if ($null -eq $result) {
        $script:TrackSearchState.error = 'searchDisplayFailed'
        return $true
    }
    if ($result.Cancelled) { return $true }
    $script:TrackSearchState.query = $result.Query
    $script:TrackSearchState.page = $result.Page
    $script:TrackSearchState.pageSize = $result.PageSize
    $script:TrackSearchState.hasNext = $result.HasNext
    $script:TrackSearchState.error = $result.Error
    $items = @()
    foreach ($item in $result.Items) {
        $items += @{ id=$item.Id; title=$item.Title; artist=$item.Artist; durationLabel=$item.DurationLabel; isLive=$item.IsLive; url=$item.Url }
    }
    $script:TrackSearchState.items = $items
    return $true
}

function Get-TrackSearchState {
    # Return fresh dictionaries/arrays so a consumer cannot mutate search state.
    $items = @()
    foreach ($item in $script:TrackSearchState.items) {
        $items += @{ id=[string]$item.id; title=[string]$item.title; artist=if ($item.artist) {[string]$item.artist} else {T 'unknownArtist'}; durationLabel=if ($item.isLive) {T 'live'} else {[string]$item.durationLabel}; url=[string]$item.url }
    }
    return @{
        query=[string]$script:TrackSearchState.query
        page=[int]$script:TrackSearchState.page
        pageSize=[int]$script:TrackSearchState.pageSize
        hasNext=[bool]$script:TrackSearchState.hasNext
        busy=[bool]$script:TrackSearchState.busy
        error=if ($script:TrackSearchState.error) {T $script:TrackSearchState.error} else {''}
        items=$items
    }
}
