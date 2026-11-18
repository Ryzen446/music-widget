$script:TrackPreviewRequest = $null
$script:TrackPreviewResumePath = ''
$script:TrackPreviewGeneration = 0
$script:TrackPreview = @{ id=''; phase='idle'; error='' }

function Stop-TrackPreview {
    param([switch]$NoResume)
    if ($null -ne $script:TrackPreviewRequest) {
        $script:TrackPreviewRequest.Dispose()
        $script:TrackPreviewRequest = $null
    }
    $script:TrackPreview = @{ id=''; phase='idle'; error='' }
    $resumePath = $script:TrackPreviewResumePath
    $script:TrackPreviewResumePath = ''
    if (-not $NoResume -and $resumePath -and $script:LoadedTrackPath -eq $resumePath -and -not $script:IsPlaying) {
        Toggle-PlayPause
    }
}

function Set-TrackPreviewError([string]$Message) {
    $id = $script:TrackPreview.id
    Stop-TrackPreview
    $script:TrackPreview = @{ id=$id; phase='error'; error=$Message }
}

function Start-TrackPreview([string]$Id) {
    $isSame = $script:TrackPreview.id -ceq $Id -and $script:TrackPreview.phase -in @('loading','playing')
    Stop-TrackPreview
    if ($isSame) { return }
    $script:TrackPreviewGeneration++
    $script:TrackPreview = @{ id=$Id; phase='loading'; error=''; generation=$script:TrackPreviewGeneration }
    try {
        if (-not (Initialize-TrackSearch)) { throw 'Playback tool unavailable.' }
        $script:TrackPreviewRequest = [TrackPreviewProcess]::new($Id, $script:TrackSearchExecutable)
    } catch {
        Set-TrackPreviewError 'previewToolFailed'
    }
}

function Update-TrackPreview {
    if ($script:TrackPreview.phase -ne 'loading') { return }
    if ($script:TrackPreview.url) {
        if ([DateTime]::UtcNow -gt $script:TrackPreviewDeadline) {Set-TrackPreviewError 'previewOpenFailed'}
        return
    }
    $request = $script:TrackPreviewRequest
    if ($null -eq $request -or -not $request.IsCompleted) { return }
    $script:TrackPreviewRequest = $null
    if (-not $request.StreamUrl) {
        Set-TrackPreviewError 'previewNotFound'
        return
    }
    $script:TrackPreview.url = $request.StreamUrl
    $script:TrackPreviewDeadline = [DateTime]::UtcNow.AddSeconds(15)
}

function Update-TrackPreviewMedia([long]$Generation,[string]$Event) {
    if ($Generation -ne $script:TrackPreview.generation -or -not $script:TrackPreview.url -or $script:GlassWebPane -ne 'search') {return}
    switch ($Event) {
        'started' {
            if ($script:TrackPreview.phase -ne 'loading') {return}
            if ($script:IsPlaying -and $script:LoadedTrackPath) {
                $script:TrackPreviewResumePath = $script:LoadedTrackPath
                Toggle-PlayPause
            }
            $script:TrackPreview.phase = 'playing'
        }
        'ended' { Stop-TrackPreview }
        'error' { Set-TrackPreviewError 'previewPlayFailed' }
    }
}

function Move-GlassPlaylistTrack([string]$Name, $Move) {
    if ($script:GlassWebPane -ne 'library' -or $Name -eq 'ALL' -or $Name -ne $script:ActivePlaylist) {
        throw (T 'playlistReorderSelect')
    }
    $from = -1
    $to = -1
    for ($i=0; $i -lt $script:Playlist.Count; $i++) {
        if ($script:Playlist[$i].Name -ceq $Move.from) {$from=$i}
        if ($script:Playlist[$i].Name -ceq $Move.to) {$to=$i}
    }
    if ($from -lt 0 -or $to -lt 0) { throw (T 'playlistReorderChanged') }
    if ($from -eq $to) { return }
    Reorder-Playlist -FromIndex $from -ToIndex ($to + [int]$Move.after)
    $script:GlassWebLastLibraryKey = ''
}
