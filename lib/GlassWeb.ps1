# WebView2 presentation adapter. Audio remains in the existing WPF MediaPlayer.
# Microsoft.Web.WebView2 1.0.4314.35, net462; third-party notices: lib/webview2/.
$script:GlassWebOrigin = 'https://music-widget.local/index.html'
$script:GlassWebPane = 'player'
$script:GlassWebPlayerInset = 0.0
$script:GlassWebPlayerHeight = 0.0
$script:GlassWebReady = $false
$script:GlassWebInitialized = $false
$script:GlassWebFailed = $false
$script:GlassWebActive = $false
$script:GlassWebRequestedVisible = $true
$script:GlassWebError = ''
$script:GlassWebLastWireJson = ''
$script:GlassWebLastStateKey = ''
$script:GlassWebLastLibraryKey = ''
$script:GlassWebLastPlaylistsKey = ''
$script:GlassWebSearchDirty = $true
$script:GlassWebDownloadBusy = $false
$script:GlassWebQueue = [System.Collections.Generic.Queue[object]]::new()
$script:GlassWebDrainScheduled = $false
$script:GlassWebNativeVisibility = @{}
$script:GlassWebSuspendTask = $null
$script:GlassWebSleeping = $false
$script:GlassWebInteractive = $false
$script:GlassWebHandle = [IntPtr]::Zero
$script:GlassWebInactiveSince = $null
$script:GlassWebLastSuspendAttempt = [DateTime]::MinValue
$script:GlassWebLastPowerRunning = $null
$script:GlassWebPresentationGeneration=0
$script:GlassWebPresentationReady=$false
$script:GlassWebPresentationRevealed=$false
$script:GlassWebNativeCloaked=$false
$script:GlassWebStateDelivered=$false
$script:GlassWebPaintSignal=0
$script:GlassWebDesiredOpacity=$null
$script:GlassWebStartupSuppressed=$false
$script:GlassWebWantsVisible=$false
$script:GlassPresentationJobs=@{}
$script:GlassPresentationViewportFixes=@{}
$script:GlassPresentationViewportRepairGeneration=@{}
if ($null -eq $script:SizeScale) {$script:SizeScale=1.0}
if ([string]::IsNullOrEmpty($script:AuraColor)) {$script:AuraColor='#DCE4EF'}
if ($script:AuraMode -notin @('Static','RGB')) {$script:AuraMode='Static'}
$shapeHelper=Join-Path $LibDir 'GlassWindowShape.ps1'
if (Test-Path -LiteralPath $shapeHelper) {. $shapeHelper}
. (Join-Path $LibDir 'Search.ps1')
. (Join-Path $LibDir 'WidgetFeatures.ps1')
. (Join-Path $LibDir 'AudioVisualizationBridge.ps1')

function Test-GlassWebNumber($Value, [double]$Minimum, [double]$Maximum) {
    if ($null -eq $Value -or $Value -is [bool] -or $Value -is [string]) { return $false }
    if ($Value -isnot [ValueType]) { return $false }
    try {
        $number = [double]$Value
        return (-not [double]::IsNaN($number) -and -not [double]::IsInfinity($number) -and $number -ge $Minimum -and $number -le $Maximum)
    } catch { return $false }
}

function Hide-GlassNativeControls {
    if ($null -eq $script:GlassWebRoot) {$script:GlassWebRoot=$window.FindName('RootGrid')}
    if ($null -eq $script:GlassWebRoot) {return}
    foreach ($child in $script:GlassWebRoot.Children) {
        if ([object]::ReferenceEquals($child,$script:GlassWebControl)) {continue}
        if (-not $script:GlassWebNativeVisibility.ContainsKey($child.Name)) {$script:GlassWebNativeVisibility[$child.Name]=$child.Visibility}
        $child.Visibility=[System.Windows.Visibility]::Collapsed
    }
}

function Restore-GlassNativeControls {
    if ($null -eq $script:GlassWebRoot) {return}
    foreach ($child in $script:GlassWebRoot.Children) {
        if ([object]::ReferenceEquals($child,$script:GlassWebControl)) {continue}
        if ($script:GlassWebNativeVisibility.ContainsKey($child.Name)) {$child.Visibility=$script:GlassWebNativeVisibility[$child.Name]}
    }
}

function Set-GlassMainCloaked([bool]$Hidden) {
    try {
        $interop=[System.Windows.Interop.WindowInteropHelper]::new($window)
        $handle=if ($Hidden) {$interop.EnsureHandle()} else {$interop.Handle}
        if ($handle -eq [IntPtr]::Zero) {return $false}
        $script:GlassWebHandle=$handle
        $result=[NativeBackdrop]::SetCloaked($handle,$Hidden)
        if ($result -ge 0) {$script:GlassWebNativeCloaked=$Hidden}
        [GlassWebWindowMove]::EnableInteraction($handle,(-not $Hidden))
        return ($result -ge 0)
    } catch {return $false}
}

function Prepare-GlassMainPresentation {
    if ($null -eq $script:GlassWebDesiredOpacity) {$script:GlassWebDesiredOpacity=$window.Opacity}
    $script:GlassWebPresentationGeneration++
    $script:GlassWebPresentationReady=$false;$script:GlassWebPresentationRevealed=$false;$script:GlassWebStateDelivered=$false;$script:GlassWebPaintSignal=0
    $script:GlassWebStartupSuppressed=$true
    $script:GlassWebReady=$false
    $window.Opacity=0;$window.ShowActivated=$false
    Hide-GlassNativeControls
    # Keep the actual render opacity while native composition is cloaked. A
    # transparent WPF tree can otherwise yield a valid but blank first frame.
    if (Set-GlassMainCloaked $true) {$window.Opacity=$script:GlassWebDesiredOpacity}
}

function Get-GlassPresentationEvidence([IO.MemoryStream]$Stream) {
    $Stream.Position=0
    $decoder=[System.Windows.Media.Imaging.PngBitmapDecoder]::new($Stream,[System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $bitmap=[System.Windows.Media.Imaging.FormatConvertedBitmap]::new($decoder.Frames[0],[System.Windows.Media.PixelFormats]::Bgra32,$null,0)
    $stride=$bitmap.PixelWidth*4
    $pixels=[byte[]]::new($stride*$bitmap.PixelHeight)
    $bitmap.CopyPixels($pixels,$stride,0)
    $counts=[NativeBackdrop]::InspectPresentationFrame($pixels,$bitmap.PixelWidth,$bitmap.PixelHeight,$stride)
    [pscustomobject]@{Width=$bitmap.PixelWidth;Height=$bitmap.PixelHeight;VisiblePixels=$counts[0];ColorRange=$counts[1];Painted=($counts[0] -ge 16 -and $counts[1] -ge 12)}
}

function Start-GlassPresentationCapture([string]$Target,[long]$Generation) {
    if ($script:GlassPresentationJobs.ContainsKey($Target) -or $script:GlassPresentationViewportFixes.ContainsKey($Target)) {return}
    $core=$script:GlassWebControl.CoreWebView2
    if ($null -eq $core) {return}
    if (-not $script:GlassPresentationTimer.IsEnabled) {$script:GlassPresentationTimer.Start()}
    $stream=[IO.MemoryStream]::new()
    try {
        $task=$core.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png,$stream)
        $renderWindow=$window
        $renderControl=$script:GlassWebControl
        $renderHandle=([System.Windows.Interop.WindowInteropHelper]::new($renderWindow)).Handle
        $dpi=[NativeBackdrop]::GetWindowDpiScale($renderHandle)
        $script:GlassPresentationJobs[$Target]=[pscustomobject]@{Generation=$Generation;Task=$task;Stream=$stream;Started=[DateTime]::Now;RenderOpacity=$renderWindow.Opacity;CloakState=[NativeBackdrop]::GetCloakState($renderHandle);ExpectedWidth=[math]::Ceiling($renderControl.ActualWidth*$dpi);ExpectedHeight=[math]::Ceiling($renderControl.ActualHeight*$dpi)}
    } catch {$stream.Dispose()}
}

function Repair-GlassPresentationViewport([string]$Target,[long]$Generation) {
    if ($script:GlassPresentationViewportRepairGeneration[$Target] -eq $Generation) {return}
    $control=$script:GlassWebControl
    $renderWindow=$window
    if ($null -eq $control -or $control.ActualWidth -le 0) {return}
    $script:GlassPresentationViewportRepairGeneration[$Target]=$Generation
    # WebView2 can retain its initial 961x1032 compositor surface after hidden
    # initialization, even with a correctly sized DOM. One concealed resize
    # forces a fresh controller surface; restore the layout on the next turn.
    $script:GlassPresentationViewportFixes[$Target]=[pscustomobject]@{Control=$control;Window=$renderWindow;Width=$control.Width}
    $control.Width=$control.ActualWidth+1
    $renderWindow.UpdateLayout()
    [void]$window.Dispatcher.BeginInvoke([Action]{
        foreach ($targetKey in @($script:GlassPresentationViewportFixes.Keys)) {
            $fix=$script:GlassPresentationViewportFixes[$targetKey]
            try {$fix.Control.Width=$fix.Width;$fix.Window.UpdateLayout()} catch {}
            $script:GlassPresentationViewportFixes.Remove($targetKey)
        }
    },[System.Windows.Threading.DispatcherPriority]::Render)
}

function Update-GlassWebPresentation {
    # CapturePreviewAsync confirms a rendered browser frame after React's
    # state commit and two RAFs. Only completed tasks are observed on STA.
    foreach ($target in @($script:GlassPresentationJobs.Keys)) {
        $job=$script:GlassPresentationJobs[$target]
        if (-not $job.Task.IsCompleted) {continue}
        $ok=$false
        try {
            $job.Task.GetAwaiter().GetResult()
            $evidence=Get-GlassPresentationEvidence $job.Stream
            $evidence | Add-Member -NotePropertyName RenderOpacity -NotePropertyValue $job.RenderOpacity
            $evidence | Add-Member -NotePropertyName CloakState -NotePropertyValue $job.CloakState
            $sizeMatches=$job.ExpectedWidth -gt 0 -and $job.ExpectedHeight -gt 0 -and [math]::Abs($evidence.Width-$job.ExpectedWidth) -le 1 -and [math]::Abs($evidence.Height-$job.ExpectedHeight) -le 1
            $evidence | Add-Member -NotePropertyName SizeMatches -NotePropertyValue $sizeMatches
            $ok=$evidence.Painted -and $sizeMatches
            if (-not $sizeMatches -and $job.Generation -eq $script:GlassWebPresentationGeneration) {Repair-GlassPresentationViewport $target $job.Generation}
            $script:GlassWebLastPaintEvidence=$evidence
        } catch {} finally {$job.Stream.Dispose()}
        $script:GlassPresentationJobs.Remove($target)
        if ($ok -and $job.Generation -eq $script:GlassWebPresentationGeneration -and $script:GlassWebPaintSignal -eq $job.Generation -and $script:GlassWebStateDelivered -and -not $script:GlassWebFailed) {
            $script:GlassWebPresentationReady=$true;$script:GlassWebStartupSuppressed=$false
        }
    }
    if ($script:GlassWebPresentationReady -and $script:GlassWebWantsVisible -and $window.IsVisible -and -not $script:GlassWebFailed) {
        $window.Opacity=$script:GlassWebDesiredOpacity
        if (-not $script:GlassWebPresentationRevealed -and $script:GlassWebDesiredOpacity -gt 0) {
            [void][NativeBackdrop]::FlushComposition()
            [void](Set-GlassMainCloaked $false)
            $script:GlassWebPresentationRevealed=$true
        }
    }
    if (-not $script:GlassWebPresentationReady -and $script:GlassWebPaintSignal -eq $script:GlassWebPresentationGeneration -and $script:GlassWebStateDelivered -and -not $script:GlassWebFailed) {
        Start-GlassPresentationCapture 'main' $script:GlassWebPresentationGeneration
    } elseif ($script:GlassPresentationJobs.Count -eq 0) {
        $script:GlassPresentationTimer.Stop()
    }
}

function Test-GlassWebName($Value) {
    $Value -is [string] -and $Value.Trim().Length -ge 1 -and $Value.Length -le 120 -and $Value -notmatch '[\x00-\x1f]'
}

function Test-GlassWebRect($Value) {
    if ($Value -isnot [PSCustomObject]) {return $false}
    foreach ($key in $Value.PSObject.Properties.Name) {if ($key -cnotin @('x','y','width','height','radius')) {return $false}}
    (Test-GlassWebNumber $Value.x 0 4096) -and (Test-GlassWebNumber $Value.y 0 4096) -and
    (Test-GlassWebNumber $Value.width 1 4096) -and (Test-GlassWebNumber $Value.height 1 4096) -and
    (Test-GlassWebNumber $Value.radius 0 512)
}

function Test-GlassWebShape($Value) {
    if ($Value -isnot [PSCustomObject]) {return $false}
    foreach ($key in $Value.PSObject.Properties.Name) {if ($key -cnotin @('main','menu')) {return $false}}
    (Test-GlassWebRect $Value.main) -and ($null -eq $Value.menu -or (Test-GlassWebRect $Value.menu))
}

function ConvertFrom-GlassWebCommand([string]$Json) {
    if ([string]::IsNullOrEmpty($Json) -or $Json.Length -gt 1048576) { return $null }
    try { $message = ConvertFrom-Json -InputObject $Json -ErrorAction Stop } catch { return $null }
    if ($null -eq $message -or $message -isnot [PSCustomObject]) { return $null }
    if ($Json.Length -gt 16384 -and $message.command -cne 'addPlaylistTracks') { return $null }
    foreach ($property in $message.PSObject.Properties.Name) {
        if ($property -notin @('v','id','command','value','index','name','url')) { return $null }
    }
    if ($message.PSObject.Properties.Name -contains 'id') {
        if (-not (Test-GlassWebNumber $message.id 0 9007199254740991) -or [math]::Floor([double]$message.id) -ne [double]$message.id) { return $null }
    }
    if (-not (Test-GlassWebNumber $message.v 1 1) -or $message.command -isnot [string]) { return $null }
    if ($message.command -cnotin @('togglePlay','next','prev','mute','importLocal','hide','ready','presentationReady','drag','seek','seekRelative','volume','shuffle','topmost','motion','repeat','design','theme','placement','speed','sleep','pane','selectTrack','deleteTrack','selectPlaylist','deletePlaylist','createPlaylist','addToPlaylist','removeFromPlaylist','addPlaylistTracks','download','search','previewTrack','previewMedia','moveTrack','resize','auraColor','auraMode','shape','language','shortcutCapture','shortcutSet')) { return $null }
    $valid = switch -CaseSensitive ($message.command) {
        { $_ -in @('togglePlay','next','prev','mute','importLocal','hide','ready','drag') } { $true; break }
        'presentationReady' {
            $message.value -is [PSCustomObject] -and @($message.value.PSObject.Properties.Name | Where-Object {$_ -cnotin @('target','generation')}).Count -eq 0 -and
            $message.value.target -ceq 'main' -and (Test-GlassWebNumber $message.value.generation 1 9007199254740991) -and
            [math]::Floor([double]$message.value.generation) -eq [double]$message.value.generation
            break
        }
        'seek' { Test-GlassWebNumber $message.value 0 604800; break }
        'seekRelative' { (Test-GlassWebNumber $message.value -5 5) -and $message.value -in @(-5,5); break }
        'volume' { Test-GlassWebNumber $message.value 0 100; break }
        { $_ -in @('shuffle','topmost','motion','shortcutCapture') } { $message.value -is [bool]; break }
        'repeat' { (Test-GlassWebNumber $message.value 0 2) -and [math]::Floor([double]$message.value) -eq [double]$message.value; break }
        'design' { $message.value -is [string] -and $message.value -cin @('Minimalist','Compact'); break }
        'theme' { $message.value -is [string] -and $message.value -ceq 'Glass'; break }
        'resize' { Test-GlassWebNumber $message.value 0.8 1.5; break }
        'auraColor' { $message.value -is [string] -and $message.value -match '^#[0-9a-fA-F]{6}$'; break }
        'auraMode' { $message.value -is [string] -and $message.value -cin @('Static','RGB'); break }
        'shape' { Test-GlassWebShape $message.value; break }
        'language' { $message.value -is [string] -and $message.value -cin @('auto','en','fr'); break }
        'shortcutSet' {
            $message.name -is [string] -and $message.name -cin $script:ShortcutActions -and
                $message.PSObject.Properties.Name -contains 'value' -and (Test-WidgetShortcut $message.value)
            break
        }
        'placement' { $message.value -is [string] -and $message.value -cin $script:MinimalPlacements; break }
        'speed' { (Test-GlassWebNumber $message.value 0.75 2) -and [double]$message.value -in @(0.75,1.0,1.25,1.5,2.0); break }
        'sleep' { (Test-GlassWebNumber $message.value 0 60) -and [double]$message.value -in @(0,15,30,60); break }
        'pane' { $message.value -is [string] -and $message.value -cin @('player','settings','library','import','createPlaylist','playlistTracks','menu','volume','delete','search'); break }
        'search' { $message.value -is [string] -and $message.value.Trim().Length -ge 2 -and $message.value.Length -le 200 -and $message.value -notmatch '[\x00-\x1f\x7f]' -and (Test-GlassWebNumber $message.index 1 10) -and [math]::Floor([double]$message.index) -eq [double]$message.index; break }
        'previewTrack' { $message.value -is [string] -and $message.value -cmatch '^[A-Za-z0-9_-]{11}$'; break }
        'previewMedia' {
            $message.value -is [string] -and $message.value -cin @('started','ended','error') -and
                (Test-GlassWebNumber $message.index 1 9007199254740991) -and [math]::Floor([double]$message.index) -eq [double]$message.index
            break
        }
        'moveTrack' {
            (Test-GlassWebName $message.name) -and $message.name -ne 'ALL' -and
            $message.value -is [PSCustomObject] -and
            @($message.value.PSObject.Properties.Name | Where-Object {$_ -cnotin @('from','to','after')}).Count -eq 0 -and
            $message.value.from -is [string] -and $message.value.from.Length -ge 1 -and $message.value.from.Length -le 255 -and
            $message.value.to -is [string] -and $message.value.to.Length -ge 1 -and $message.value.to.Length -le 255 -and $message.value.after -is [bool]
            break
        }
        { $_ -in @('selectTrack','deleteTrack') } { (Test-GlassWebNumber $message.index 0 ([math]::Max(-1,$script:Playlist.Count-1))) -and [math]::Floor([double]$message.index) -eq [double]$message.index; break }
        { $_ -in @('selectPlaylist','deletePlaylist') } { (Test-GlassWebName $message.name) -and ($message.name -eq 'ALL' -or @($script:CustomPlaylists | Where-Object { $_.name -eq $message.name }).Count -gt 0) -and ($message.command -ne 'deletePlaylist' -or $message.name -ne 'ALL'); break }
        'createPlaylist' { (Test-GlassWebName $message.name) -and $message.name.Trim() -ne 'ALL'; break }
        'addPlaylistTracks' {
            if ($script:GlassWebPane -ne 'playlistTracks' -or $message.name -ne $script:ActivePlaylist -or
                -not (Test-GlassWebName $message.name) -or $message.name -eq 'ALL' -or
                $message.value -isnot [array] -or $message.value.Count -eq 0 -or
                $message.value.Count -gt $script:LibraryFiles.Count) { $false; break }
            $validNames=$true
            foreach ($name in $message.value) {
                if ($name -isnot [string] -or $name.Length -lt 1 -or $name.Length -gt 255) {$validNames=$false;break}
            }
            $validNames
            break
        }
        { $_ -cin @('addToPlaylist','removeFromPlaylist') } {
            $hasName = $message.value -is [string] -and $message.value.Length -ge 1 -and $message.value.Length -le 255
            $hasLegacyIndex = $message.command -ceq 'addToPlaylist' -and $null -eq $message.value -and
                (Test-GlassWebNumber $message.index 0 ($script:Playlist.Count-1)) -and [math]::Floor([double]$message.index) -eq [double]$message.index
            ($hasName -or $hasLegacyIndex) -and (Test-GlassWebName $message.name) -and $message.name -ne 'ALL' -and
                @($script:CustomPlaylists | Where-Object { $_.name -eq $message.name }).Count -gt 0
            break
        }
        'download' {
            $uri = $null
            $playlistAllowed = $null -eq $message.name -or ((Test-GlassWebName $message.name) -and
                @($script:CustomPlaylists | Where-Object { $_.name -eq $message.name }).Count -gt 0)
            $playlistAllowed -and $message.url -is [string] -and $message.url.Length -le 4096 -and [Uri]::TryCreate($message.url,[UriKind]::Absolute,[ref]$uri) -and $uri.Scheme -in @('https','http') -and $uri.Host -match '(^|\.)(youtube\.com|youtu\.be|spotify\.com)$'
            break
        }
        default { $false }
    }
    if (-not $valid) { return $null }
    return $message
}

function Send-GlassWebAck([string]$Command, [bool]$Ok, [string]$ErrorText = '', $Id=$null) {
    if (-not $script:GlassWebInitialized -or $null -eq $script:GlassWebControl.CoreWebView2) { return }
    if ($script:GlassWebControl.CoreWebView2.Source -cne $script:GlassWebOrigin) { return }
    try {
        $payload = [ordered]@{ type='ack'; command=$Command; ok=$Ok; error=$ErrorText }
        if ($null -ne $Id) { $payload.id=$Id }
        $script:GlassWebControl.CoreWebView2.PostWebMessageAsJson((ConvertTo-Json -InputObject $payload -Compress))
    } catch { }
}

function Invoke-GlassWebCommand {
    param($Message)
    # Revalidate against the current playlist: it may have changed since receipt.
    $validated = ConvertFrom-GlassWebCommand (ConvertTo-Json -InputObject $Message -Depth 6 -Compress)
    if ($null -eq $validated) { throw (T 'actionLibraryChanged') }
    switch -CaseSensitive ($validated.command) {
        'ready' { $script:GlassWebReady=$true; $script:GlassWebLastWireJson=''; $script:GlassWebLastLibraryKey=''; $script:GlassWebLastPlaylistsKey=''; Set-GlassWebVisibility }
        'presentationReady' {
            $generation=[long]$validated.value.generation
            if ($generation -eq $script:GlassWebPresentationGeneration -and $script:GlassWebReady -and $script:GlassWebStateDelivered) {
                $script:GlassWebPaintSignal=$generation;Start-GlassPresentationCapture 'main' $generation
            }
        }
        'togglePlay' { Stop-TrackPreview -NoResume; Toggle-PlayPause }
        'next' { Stop-TrackPreview -NoResume; Next-Track }
        'prev' { Stop-TrackPreview -NoResume; Prev-Track }
        { $_ -cin @('seek','seekRelative') } {
            if ($script:MediaIsReady -and $script:Player.NaturalDuration.HasTimeSpan) {
                $target=[double]$validated.value
                if ($validated.command -ceq 'seekRelative') { $target += $script:Player.Position.TotalSeconds }
                $seconds=[math]::Max(0.0,[math]::Min($target,$script:Player.NaturalDuration.TimeSpan.TotalSeconds))
                $script:Player.Position=[TimeSpan]::FromSeconds($seconds); Save-AppSettings
            }
        }
        'volume' { Set-WidgetVolume ([double]$validated.value/100.0) }
        'mute' { Toggle-LocalMute }
        'shuffle' { $script:IsShuffle=[bool]$validated.value; $script:ShuffleBag=@(); $script:ShuffleBagInitialized=$false; Update-ModeIndicators; Save-AppSettings }
        'repeat' { $script:RepeatMode=[int]$validated.value; Update-ModeIndicators; Save-AppSettings }
        'design' { Set-DesignMode ([string]$validated.value); Update-GlassWebLayout }
        'theme' { Apply-Theme 1; Set-GlassWebVisibility; Update-GlassWebLayout }
        'resize' { Set-PlayerScale ([double]$validated.value); Update-GlassWebLayout }
        'auraColor' { $script:AuraColor=$validated.value.ToUpperInvariant(); Save-AppSettings }
        'auraMode' { $script:AuraMode=[string]$validated.value; Save-AppSettings }
        'shape' {
            if ($script:GlassWebActive -and (Get-Command Set-GlassWindowShape -ErrorAction SilentlyContinue)) {
                [void](Set-GlassWindowShape -Shape $validated.value -Window $window)
            }
        }
        'placement' { Select-MinimalPlacement ([string]$validated.value) }
        'topmost' { $script:AlwaysOnTop=[bool]$validated.value; $PinOption.IsChecked=$script:AlwaysOnTop; $window.Topmost=$script:AlwaysOnTop; Save-AppSettings }
        'motion' { $script:ReducedMotion=[bool]$validated.value; $ReducedMotionOption.IsChecked=$script:ReducedMotion; Update-VisualActivity; Save-AppSettings }
        'speed' { $script:PlaybackSpeed=[double]$validated.value; $PlaybackSpeedOptions.SelectedIndex=[array]::IndexOf(@(0.75,1.0,1.25,1.5,2.0),$script:PlaybackSpeed); $script:Player.SpeedRatio=$script:PlaybackSpeed; Save-AppSettings }
        'sleep' { $minutes=[int]$validated.value; $SleepOptions.SelectedIndex=[array]::IndexOf(@(0,15,30,60),$minutes); $script:SleepDeadline=if ($minutes -gt 0) {[DateTime]::Now.AddMinutes($minutes)} else {$null} }
        'pane' { Set-GlassWebPane ([string]$validated.value) }
        'language' { Set-WidgetLanguage ([string]$validated.value) }
        'shortcutCapture' {
            if ($script:GlassWebPane -eq 'settings') {Set-ShortcutCapture ([bool]$validated.value)}
        }
        'shortcutSet' {
            if ($script:GlassWebPane -eq 'settings') {Set-WidgetShortcut ([string]$validated.name) $validated.value}
        }
        'search' {
            if ($script:GlassWebPane -ne 'search') {throw (T 'openSearchFirst')}
            Stop-TrackPreview
            Start-TrackSearch -Query ([string]$validated.value) -Page ([int]$validated.index) -PageSize 8
            $script:GlassWebSearchDirty=$true
        }
        'previewTrack' {
            $id = [string]$validated.value
            if ($script:GlassWebPane -ne 'search' -or $id -cnotin @($script:TrackSearchState.items | ForEach-Object {$_.id})) {
                throw (T 'searchResultExpired')
            }
            Start-TrackPreview $id
        }
        'previewMedia' { Update-TrackPreviewMedia ([long]$validated.index) ([string]$validated.value) }
        'moveTrack' { Move-GlassPlaylistTrack -Name $validated.name -Move $validated.value }
        'selectTrack' { Play-Track ([int]$validated.index) }
        'selectPlaylist' { Select-Playlist ([string]$validated.name) }
        'createPlaylist' { $TxtPlaylistNameInput.Text=$validated.name.Trim(); Create-NewPlaylist; Set-GlassWebPane 'library' }
        'addPlaylistTracks' {
            if (-not ($script:CustomPlaylists | Where-Object { $_.name -eq $validated.name })) {throw (T 'actionLibraryChanged')}
            $filesByName=@{}
            foreach ($file in $script:LibraryFiles) { $filesByName[$file.Name]=$file }
            $tracks=@(foreach ($name in $validated.value) {
                $file=$filesByName[$name]
                if ($null -eq $file -or -not [IO.File]::Exists($file.FullName)) {
                    Reload-Playlist
                    throw (T 'trackNotInList')
                }
                $file
            })
            Set-PlaylistTrackMembership -Track $tracks -PlaylistName $validated.name -Included $true
            Set-GlassWebPane 'library'
        }
        { $_ -cin @('addToPlaylist','removeFromPlaylist') } {
            $track = if ($validated.value -is [string]) {
                $script:Playlist | Where-Object { $_.Name -ceq $validated.value } | Select-Object -First 1
            } else { $script:Playlist[[int]$validated.index] }
            if ($null -eq $track) { throw (T 'trackNotInList') }
            Set-PlaylistTrackMembership -Track $track -PlaylistName $validated.name -Included ($validated.command -ceq 'addToPlaylist')
        }
        'deleteTrack' { $script:PendingDeleteIndex=[int]$validated.index; Confirm-DeleteTrack; Set-GlassWebPane 'library' }
        'deletePlaylist' { Delete-CustomPlaylist -Name ([string]$validated.name) }
        'importLocal' { $BtnChoiceLocal.RaiseEvent([System.Windows.RoutedEventArgs]::new([System.Windows.Controls.Button]::ClickEvent)) }
        'download' {
            if ($script:GlassWebDownloadBusy) { throw (T 'downloadAlreadyRunning') }
            $TxtUrlInput.Text=[string]$validated.url
            Start-MusicDownload -PlaylistName ([string]$validated.name)
        }
        'hide' {Set-GlassWebPane 'player';Hide-Widget}
        'drag' {
            if ($script:GlassWebPane -eq 'player' -and [GlassWebWindowMove]::IsLeftPressed()) {
                $script:IsDraggingWindow=$true
                try {
                    if ([System.Windows.Input.Mouse]::LeftButton -eq [System.Windows.Input.MouseButtonState]::Pressed) {
                        try {$window.DragMove()} catch {[GlassWebWindowMove]::BeginMove(([System.Windows.Interop.WindowInteropHelper]::new($window)).Handle)}
                    } else {
                        # A child WebView HWND may not forward WPF mouse state.
                        [GlassWebWindowMove]::BeginMove(([System.Windows.Interop.WindowInteropHelper]::new($window)).Handle)
                    }
                } finally { $script:IsDraggingWindow=$false }
                Clamp-WindowToScreen; Remember-CompactPosition; Update-GlassWebLayout; Save-AppSettings
            }
        }
    }
}

function Invoke-GlassWebQueue {
    # This runs after WebMessageReceived returns, allowing native modal dialogs.
    $script:GlassWebDrainScheduled=$false
    $processed=0
    while ($script:GlassWebQueue.Count -gt 0 -and $processed -lt 32) {
        $message=$script:GlassWebQueue.Dequeue(); $processed++
        try {
            Invoke-GlassWebCommand $message
            $script:GlassWebError=''
            Send-GlassWebAck $message.command $true '' $message.id
        } catch {
            $script:GlassWebError=$_.Exception.Message
            Send-GlassWebAck $message.command $false $script:GlassWebError $message.id
        }
    }
    Set-GlassWebVisibility
    Publish-GlassWebState
    if ($script:GlassWebQueue.Count -gt 0 -and -not $script:GlassWebDrainScheduled) {
        $script:GlassWebDrainScheduled=$true
        [void]$window.Dispatcher.BeginInvoke([Action]{ Invoke-GlassWebQueue },[System.Windows.Threading.DispatcherPriority]::Background)
    }
}

function Set-GlassWebPane {
    param([string]$Pane='player')
    if ($Pane -notin @('player','settings','library','import','createPlaylist','playlistTracks','menu','volume','delete','search')) { return }
    if ($Pane -eq 'playlistTracks' -and $script:ActivePlaylist -eq 'ALL') { throw (T 'actionLibraryChanged') }
    if ($Pane -eq 'volume' -and $script:DesignMode -ne 'Minimalist') { return }
    if ($Pane -ne 'settings') {Set-ShortcutCapture $false}
    if ($script:GlassWebPane -eq 'search' -and $Pane -ne 'search') { Stop-TrackPreview; Cancel-TrackSearch; $script:GlassWebSearchDirty=$true }
    $sameAddMusicPane=$script:GlassWebPane -in @('search','import') -and $Pane -in @('search','import')
    $keepPlayerLayout=$Pane -eq $script:GlassWebPane -or
        ($script:DesignMode -eq 'Compact' -and $script:GlassWebPane -in @('player','menu') -and $Pane -in @('player','menu'))
    $script:GlassWebPane=$Pane
    $script:GlassWebLastWireJson=''; $script:GlassWebLastLibraryKey=''; $script:GlassWebLastPlaylistsKey=''
    if (-not ($keepPlayerLayout -or $sameAddMusicPane)) {Update-GlassWebLayout}
    if ($Pane -eq 'player' -and $script:GlassShapeLastPayload.menu) {
        [void](Set-GlassWindowShape -Shape ([pscustomobject]@{main=$script:GlassShapeLastPayload.main;menu=$null}) -Window $window)
    }
    Publish-GlassWebState -Force
}

function Set-GlassWebPlayerLayout([double]$Scale) {
    if ($script:IsDraggingWindow -or $script:IsPositioning) { return }
    $compact=$script:DesignMode -eq 'Compact'
    $width=if ($compact) {$script:CompactBaseWidth*$Scale} else {$script:MinimalistWidth}
    $height=if ($compact) {$script:CompactBaseHeight*$Scale} else {$script:MinimalistHeight}
    $area=if ($compact) {Get-CompactWorkArea} else {Get-MinimalWorkArea}
    if ($compact) {
        $left=if ($null -ne $script:CompactLeft) {$script:CompactLeft} else {$area.Right-$width-16}
        $top=if ($null -ne $script:CompactTop) {$script:CompactTop} else {$area.Top+16}
        $left=[math]::Max($area.Left,[math]::Min($area.Right-$width,$left))
        $top=[math]::Max($area.Top,[math]::Min($area.Bottom-$height,$top))
    } else {
        $bounds=Get-AnchoredBounds $area $width $height $script:MinimalPosition
        $left=$bounds.Left; $top=$bounds.Top
    }
    # The region stays clipped to the card and its visible floating surface.
    $menuInset=0.0
    $windowHeight=$height
    if (-not $compact) {
        $heightBelow,$heightAbove=switch ($script:GlassWebPane) {
            'menu' {288,224}
            'volume' {160,104}
            default {$height,0}
        }
        $roomBelow=$area.Bottom-$top
        if ($heightAbove -gt 0 -and $roomBelow -lt $heightBelow -and $top-$area.Top -ge $heightAbove) {
            $menuInset=$heightAbove
            $windowHeight=$height+$menuInset
        } else {
            $windowHeight=[math]::Max($height,[math]::Min($heightBelow,$roomBelow))
        }
    }
    $script:GlassWebPlayerHeight=$height
    $script:GlassWebPlayerInset=$menuInset
    $script:IsPositioning=$true
    try {
        $window.Width=$width; $window.Height=$windowHeight
        $window.Left=$left; $window.Top=$top-$menuInset
        $main=[pscustomobject]@{x=0;y=$menuInset;width=$width;height=$height;radius=8*$Scale}
        [void](Set-GlassWindowShape -Shape ([pscustomobject]@{main=$main;menu=$null}) -Window $window)
    } finally {$script:IsPositioning=$false}
}

function Update-GlassWebLayout {
    if (-not $script:GlassWebReady -or $script:GlassWebFailed -or $script:CurrentThemeIdx -ne 1 -or -not $script:GlassWebRequestedVisible) { return }
    $scale=if ($script:DesignMode -eq 'Compact') {[math]::Max(0.8,[math]::Min(1.5,$script:SizeScale))} else {1.0}
    $zoom=if ($script:GlassWebPane -in @('player','menu')) {$scale} else {1.0}
    if ([math]::Abs($script:GlassWebControl.ZoomFactor-$zoom) -gt 0.0001) {$script:GlassWebControl.ZoomFactor=$zoom}
    $window.BeginAnimation([System.Windows.Window]::HeightProperty,$null)
    $window.MinWidth=280; $window.MinHeight=$script:MinimalistHeight
    if ($script:GlassWebPane -in @('player','menu','volume')) {Set-GlassWebPlayerLayout $scale; return}
    $script:GlassWebPlayerInset=0
    $size=switch ($script:GlassWebPane) {
        'settings' { @(420,600) }
        'library' { @(440,620) }
        'playlistTracks' { @(440,620) }
        'search' { @(440,620) }
        'import' { @(440,620) }
        'createPlaylist' { @(420,320) }
        'delete' { @(420,320) }
        default { if ($script:DesignMode -eq 'Minimalist') {@($script:MinimalistWidth,$script:MinimalistHeight)} else {@(($script:CompactBaseWidth*$scale),($script:CompactBaseHeight*$scale))} }
    }
    $window.Width=[double]$size[0]; $window.Height=[double]$size[1]
    if ($script:DesignMode -eq 'Minimalist') { Set-MinimalPlacement } else { Set-CompactPlacement }
}

function Set-GlassWebVisibility {
    param([bool]$Visible=$true)
    $script:GlassWebRequestedVisible=$Visible
    $useWeb=$Visible -and $script:CurrentThemeIdx -eq 1 -and $script:GlassWebReady -and -not $script:GlassWebFailed
    if ($null -eq $script:GlassWebControl) {
        if ($script:GlassWebFailed -and $script:GlassWebStartupSuppressed) {Restore-GlassNativeControls}
        Update-GlassAudioVisualization
        return
    }
    if (-not $script:GlassWebReady -and -not $script:GlassWebFailed) {
        Hide-GlassNativeControls
        Update-GlassAudioVisualization
        return
    }
    if ($useWeb) {
        if (-not $script:GlassWebActive) {
            Set-GlassNativeFrameMode $true
            Hide-GlassNativeControls
            $script:GlassWebControl.Visibility=[System.Windows.Visibility]::Visible
            $script:GlassWebActive=$true
            Update-GlassWebLayout
            $liveTimer.Stop(); Stop-TitleMarqueeAnimation
        }
    } else {
        $wasActive=$script:GlassWebActive
        $script:GlassWebControl.Visibility=[System.Windows.Visibility]::Hidden
        if ($script:GlassWebActive -or ($script:GlassWebFailed -and $script:GlassWebStartupSuppressed)) {
            Restore-GlassNativeControls
            $overlay=$script:IsAppearanceOpen -or $script:IsModalOpen -or $script:IsUploadModalOpen -or $script:IsCreatePlaylistModalOpen -or $script:IsContextMenuOpen
            $MinimalCard.Visibility=if ($script:DesignMode -eq 'Minimalist' -and -not $script:IsDrawerOpen -and -not $overlay) {'Visible'} else {'Collapsed'}
            $MainCard.Visibility=if ($script:DesignMode -eq 'Compact' -or $script:IsDrawerOpen) {'Visible'} else {'Collapsed'}
        }
        $script:GlassWebActive=$false
        if ($wasActive) {
            if (Get-Command Set-GlassNativeFrameMode -ErrorAction SilentlyContinue) {Set-GlassNativeFrameMode $false}
            elseif (Get-Command Clear-GlassWindowShape -ErrorAction SilentlyContinue) {[void](Clear-GlassWindowShape -Window $window)}
        }
    }
    $script:GlassWebInteractive=$useWeb -and $window.IsVisible -and
        ($window.IsActive -or [NativeBackdrop]::IsPointerOver($script:GlassWebHandle))
    Update-GlassWebPowerState
    Update-GlassAudioVisualization
}

function Update-GlassWebPowerState {
    # Only the browser renderer sleeps. WPF audio, the hotkey and native timers
    # keep running while the widget or browser presentation is hidden.
    if (-not $script:GlassWebInitialized -or -not $script:GlassWebReady -or $script:GlassWebFailed) { return }
    try {
        $core=$script:GlassWebControl.CoreWebView2
        if ($null -eq $core) { return }
        if ($null -ne $script:GlassWebSuspendTask -and $script:GlassWebSuspendTask.IsCompleted) {
            # The task is already complete: observe faults without blocking STA.
            try { [void]$script:GlassWebSuspendTask.GetAwaiter().GetResult() } catch { }
            $script:GlassWebSuspendTask=$null
        }
        $shouldRun=$script:GlassWebActive -and $window.IsVisible -and $script:GlassWebControl.IsVisible -and $script:CurrentThemeIdx -eq 1
        if ($shouldRun) {
            if ($core.IsSuspended -or $null -ne $script:GlassWebSuspendTask) { $core.Resume() }
            if ($script:GlassWebLastPowerRunning -eq $false) {
                # Force a fresh snapshot after sleeping: native playback may
                # have advanced or changed tracks while JavaScript was paused.
                $script:GlassWebLastWireJson=''; $script:GlassWebLastLibraryKey=''; $script:GlassWebLastPlaylistsKey=''
            }
            $script:GlassWebSleeping=$false
            $script:GlassWebLastPowerRunning=$true
            $script:GlassWebInactiveSince=$null
            $script:GlassWebLastSuspendAttempt=[DateTime]::MinValue
            return
        }
        if ($null -eq $script:GlassWebInactiveSince) { $script:GlassWebInactiveSince=[DateTime]::Now }
        $script:GlassWebLastPowerRunning=$false
        # Give WPF time to update the underlying controller's IsVisible=false.
        # Suspension is best effort; retry a refusal sparingly, not every tick.
        $script:GlassWebSleeping=$core.IsSuspended
        if ($script:GlassWebSleeping -or $null -ne $script:GlassWebSuspendTask -or
            ([DateTime]::Now-$script:GlassWebInactiveSince).TotalSeconds -lt 0.5 -or
            ([DateTime]::Now-$script:GlassWebLastSuspendAttempt).TotalSeconds -lt 15) { return }
        $script:GlassWebLastSuspendAttempt=[DateTime]::Now
        $script:GlassWebSuspendTask=$core.TrySuspendAsync()
    } catch {
        # A browser that refuses suspension remains usable; never interrupt
        # native music playback or turn a power-saving failure into a UI error.
    }
}

function Publish-GlassWebState {
    param([switch]$Force)
    if ($script:IsInitializing) { return }
    if (-not $script:GlassWebReady -or -not $script:GlassWebInitialized -or $script:GlassWebFailed) { return }
    if (-not $Force -and (-not $window.IsVisible -or $script:CurrentThemeIdx -ne 1)) { return }
    try {
        $core=$script:GlassWebControl.CoreWebView2
        if ($null -eq $core -or $core.Source -cne $script:GlassWebOrigin) { return }
        if ($core.IsSuspended -or ($null -ne $script:GlassWebSuspendTask -and -not $script:GlassWebSuspendTask.IsCompleted)) { return }
        $position=0.0; $duration=0.0
        if ($script:MediaIsReady -and $script:Player.NaturalDuration.HasTimeSpan) {
            $position=[math]::Max(0.0,$script:Player.Position.TotalSeconds)
            $duration=[math]::Max(0.0,$script:Player.NaturalDuration.TimeSpan.TotalSeconds)
        }
        $downloadStatus=[string]$TxtUploadStatus.Text
        $downloadError=if ($TxtUploadError.Visibility -eq [System.Windows.Visibility]::Visible) {[string]$TxtUploadError.Text} else {''}
        $showDownload=$script:GlassWebPane -in @('search','import') -and $script:GlassWebDownloadBusy
        $downloadReceived=if ($showDownload) {[long]$script:DownloadBytesReceived} else {0}
        $downloadTotal=if ($showDownload) {[long]$script:DownloadBytesTotal} else {0}
        $sleepMinutes=if ($script:SleepDeadline) {[math]::Max(0,[int][math]::Ceiling(($script:SleepDeadline-[DateTime]::Now).TotalMinutes))} else {0}
        $presentationError=if ($script:SubtitleKey -eq 'audioUnreadable') {[string]$TxtSubtitle.Text} else {[string]$script:GlassWebError}
        $showPlayerProgress=$script:GlassWebPane -in @('player','menu','volume')
        $state=[ordered]@{
            type='state'; title=[string]$TxtTitle.Text; subtitle=[string]$TxtSubtitle.Text
            language=$script:WidgetLanguage; languagePreference=$script:LanguagePreference
            shortcutCapturing=$script:ShortcutCapturing; shortcutError=$script:ShortcutError
            presentationGeneration=[long]$script:GlassWebPresentationGeneration
            interactive=[bool]$script:GlassWebInteractive
            isPlaying=[bool]$script:IsPlaying; volume=[math]::Round($script:LocalVolume*100,1); isMuted=[bool]$script:IsMuted
            position=if ($showPlayerProgress -and ($script:GlassWebInteractive -or -not $script:IsPlaying)) {[math]::Round($position,2)} else {[math]::Floor($position)}
            duration=[math]::Round($duration,2)
            positionLabel=(Format-PlaybackTime ([TimeSpan]::FromSeconds($position))); durationLabel=(Format-PlaybackTime ([TimeSpan]::FromSeconds($duration)))
            shuffle=[bool]$script:IsShuffle; repeatMode=[int]$script:RepeatMode
            design=[string]$script:DesignMode; theme=[string]$script:Themes[$script:CurrentThemeIdx].Name
            placement=[string]$script:MinimalPosition; topmost=[bool]$script:AlwaysOnTop; reducedMotion=[bool]$script:ReducedMotion
            speed=[double]$script:PlaybackSpeed; sleepMinutes=$sleepMinutes; pane=$script:GlassWebPane
            sizeScale=[double]$script:SizeScale; auraColor=[string]$script:AuraColor; auraMode=[string]$script:AuraMode
            activePlaylist=[string]$script:ActivePlaylist; currentIndex=[int]$script:CurrentIndex
            playerInset=$script:GlassWebPlayerInset/$script:GlassWebControl.ZoomFactor
            download=[ordered]@{busy=[bool]$script:GlassWebDownloadBusy;succeeded=$script:DownloadSucceeded;status=$downloadStatus;error=$downloadError;received=$downloadReceived;total=$downloadTotal}
            error=$presentationError
        }
        # Only serialize when visible playback data or one of the lists changes.
        $previewKey=if ($script:GlassWebPane -eq 'search') {@($script:TrackPreview.id,$script:TrackPreview.phase,$script:TrackPreview.error,$script:TrackPreview.generation,[bool]$script:TrackPreview.url) -join [char]0} else {''}
        $shortcutKey=''
        if ($script:GlassWebPane -eq 'settings' -or -not $script:GlassWebStateDelivered) {
            $state.shortcuts=$script:Shortcuts
            $state.shortcutInactive=@($script:ShortcutInactive.Keys)
            $shortcutKey=(@(foreach ($action in $script:ShortcutActions) {
                $binding=$script:Shortcuts[$action]
                "$($binding.modifiers):$($binding.key):$($script:ShortcutInactive.Contains($action))"
            }) -join '|')
        }
        $stateKey=(@($state.Values) + @($downloadStatus,$downloadError,$script:GlassWebDownloadBusy,$script:DownloadSucceeded,$downloadReceived,$downloadTotal,$previewKey,$shortcutKey)) -join [char]0
        $includeLibrary=$script:GlassWebPane -in @('library','createPlaylist','playlistTracks','import','delete')
        $libraryFiles=if ($script:GlassWebPane -eq 'playlistTracks') {,$script:LibraryFiles} else {,$script:Playlist}
        $libraryKey=([System.Runtime.CompilerServices.RuntimeHelpers]::GetHashCode([object]$libraryFiles)).ToString()+'/'+$libraryFiles.Count+'/'+$script:ActivePlaylist+'/'+$script:GlassWebPane
        if ($includeLibrary -and ($Force -or $libraryKey -ne $script:GlassWebLastLibraryKey)) {
            $library=@(for ($i=0;$i -lt $libraryFiles.Count;$i++) {
                $file=$libraryFiles[$i]
                [ordered]@{id=$i;title=[IO.Path]::GetFileNameWithoutExtension($file.Name);name=$file.Name}
            })
            $state.library=$library; $script:GlassWebLastLibraryKey=$libraryKey
        }
        $playlistsKey=([System.Runtime.CompilerServices.RuntimeHelpers]::GetHashCode([object]$script:LibraryFiles)).ToString()+'/' + $includeLibrary + '/' + (@($script:CustomPlaylists | ForEach-Object {$_.name+':'+$_.tracks.Count}) -join '|')
        if ($Force -or $playlistsKey -ne $script:GlassWebLastPlaylistsKey) {
            $known=@{}; foreach ($file in $script:LibraryFiles) {$known[$file.Name]=$file.Name}
            $playlists=@(foreach ($playlist in $script:CustomPlaylists) {
                $members = @(foreach ($name in $playlist.tracks) {
                    if ($known.ContainsKey([string]$name)) { $known[[string]$name] }
                })
                $entry = [ordered]@{name=$playlist.name;count=$members.Count}
                if ($includeLibrary) { $entry.tracks=$members }
                $entry
            })
            $state.playlists=$playlists; $script:GlassWebLastPlaylistsKey=$playlistsKey
        }
        $includeSearch=$script:GlassWebPane -eq 'search' -and ($Force -or $script:GlassWebSearchDirty)
        if ($includeSearch) {$state.search=Get-TrackSearchState}
        if ($script:GlassWebPane -eq 'search') {
            $state.trackPreview=$script:TrackPreview.Clone()
            if ($state.trackPreview.error) {$state.trackPreview.error=T $state.trackPreview.error}
        }
        if (-not $Force -and $script:GlassWebLastWireJson -and $stateKey -ceq $script:GlassWebLastStateKey -and
            -not ($state.Contains('library') -or $state.Contains('playlists') -or $includeSearch)) {return}
        # Passive playback still publishes by whole seconds. Send the exact
        # position of each sample so the progress ratio stays accurate.
        if ($showPlayerProgress) {$state.position=[math]::Round($position,2)}
        $json=ConvertTo-Json -InputObject $state -Depth 8 -Compress
        if ($Force -or $json -cne $script:GlassWebLastWireJson) {
            $core.PostWebMessageAsJson($json); $script:GlassWebLastWireJson=$json;$script:GlassWebStateDelivered=$true
            if ($includeSearch) {$script:GlassWebSearchDirty=$false}
        }
        $script:GlassWebLastStateKey=$stateKey
    } catch { $script:GlassWebError=(T 'syncUnavailable') }
}

function Set-GlassWebFailure([string]$Message) {
    if ($script:GlassWebFailed) {return}
    $script:GlassWebError=$Message; $script:GlassWebFailed=$true; $script:GlassWebReady=$false
    if ($null -ne $script:GlassPresentationTimer) {$script:GlassPresentationTimer.Stop()}
    Set-GlassWebVisibility -Visible $false
    Render-PlaylistUI; Render-PlaylistShelf
    Update-PlayerSize
    $script:GlassWebStartupSuppressed=$false
    if ($window.IsVisible -and $script:GlassWebWantsVisible -and $null -ne $script:GlassWebDesiredOpacity) {$window.Opacity=$script:GlassWebDesiredOpacity}
    if ($script:GlassWebDesiredOpacity -gt 0) {[void](Set-GlassMainCloaked $false)}
}

function Initialize-GlassWeb {
    if ($null -ne $script:GlassWebControl) { return (-not $script:GlassWebFailed) }
    try {
        Prepare-GlassMainPresentation
        $script:GlassPresentationTimer=[System.Windows.Threading.DispatcherTimer]::new()
        $script:GlassPresentationTimer.Interval=[TimeSpan]::FromMilliseconds(27)
        $script:GlassPresentationTimer.Add_Tick({Update-GlassWebPresentation})
        $sdk=Join-Path $LibDir 'webview2'
        Add-Type -Path (Join-Path $sdk 'Microsoft.Web.WebView2.Core.dll')
        Add-Type -Path (Join-Path $sdk 'Microsoft.Web.WebView2.Wpf.dll')
        $architecture=if ([IntPtr]::Size -eq 8) {'x64'} else {'x86'}
        [Microsoft.Web.WebView2.Core.CoreWebView2Environment]::SetLoaderDllFolderPath((Join-Path $sdk $architecture))
        $dist=Join-Path $ScriptDir 'frontend\dist'
        if (-not (Test-Path -LiteralPath (Join-Path $dist 'index.html'))) { throw (T 'glassBuildMissing') }
        $script:GlassWebDist=$dist
        $script:GlassWebRoot=$window.FindName('RootGrid')
        if ($null -eq $script:GlassWebRoot) { throw (T 'glassRootMissing') }
        $script:GlassWebControl=[Microsoft.Web.WebView2.Wpf.WebView2]::new()
        $script:GlassWebControl.Name='GlassWebView'
        $script:GlassWebControl.DefaultBackgroundColor=[System.Drawing.Color]::Transparent
        $script:GlassWebControl.Visibility=[System.Windows.Visibility]::Hidden
        $properties=[Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties]::new()
        $localData=[Environment]::GetFolderPath('LocalApplicationData')
        $properties.UserDataFolder=if ($NoRun -or $DataDirectory -or -not $localData) {
            Join-Path $dataRoot 'webview-profile'
        } else {
            Join-Path $localData 'MusicWidget\WebView2'
        }
        $script:GlassWebControl.CreationProperties=$properties
        [System.Windows.Controls.Panel]::SetZIndex($script:GlassWebControl,100)
        [void]$script:GlassWebRoot.Children.Add($script:GlassWebControl)
        $script:GlassWebNavigated=$false
        $script:GlassWebControl.Add_CoreWebView2InitializationCompleted({
            param($sender,$eventArgs)
            if (-not $eventArgs.IsSuccess) { Set-GlassWebFailure (T 'glassInitFailed'); return }
            try {
                $core=$sender.CoreWebView2
                $settings=$core.Settings
                $settings.AreDefaultContextMenusEnabled=$false; $settings.AreDevToolsEnabled=$false
                $settings.AreBrowserAcceleratorKeysEnabled=$false; $settings.IsStatusBarEnabled=$false
                $settings.IsZoomControlEnabled=$false; $settings.IsPinchZoomEnabled=$false
                $settings.AreHostObjectsAllowed=$false; $settings.AreDefaultScriptDialogsEnabled=$false; $settings.IsWebMessageEnabled=$true
                $core.SetVirtualHostNameToFolderMapping('music-widget.local',$script:GlassWebDist,[Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind]::DenyCors)
                $core.Add_NavigationStarting({
                    param($s,$e)
                    if ($e.Uri -cne $script:GlassWebOrigin) {$e.Cancel=$true;return}
                    if ($script:GlassWebNavigated) {Prepare-GlassMainPresentation}
                    $script:GlassWebNavigated=$true
                })
                $core.Add_NewWindowRequested({param($s,$e);$e.Handled=$true})
                $core.Add_PermissionRequested({param($s,$e);$e.State=[Microsoft.Web.WebView2.Core.CoreWebView2PermissionState]::Deny})
                $core.Add_DownloadStarting({param($s,$e);$e.Cancel=$true})
                $core.Add_ProcessFailed({param($s,$e);Set-GlassWebFailure (T 'glassInterrupted')})
                $core.Add_NavigationCompleted({param($s,$e);if (-not $e.IsSuccess -and $e.WebErrorStatus -ne [Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus]::OperationCanceled) {Set-GlassWebFailure (T 'glassLoadFailed')}})
                $core.Add_WebMessageReceived({
                    param($s,$e)
                    if ($e.Source -cne $script:GlassWebOrigin -or $script:GlassWebFailed) { return }
                    $message=ConvertFrom-GlassWebCommand $e.WebMessageAsJson
                    if ($null -eq $message) { Send-GlassWebAck '' $false (T 'invalidCommand'); return }
                    if ($script:GlassWebQueue.Count -ge 128) { Send-GlassWebAck ([string]$message.command) $false (T 'commandQueueFull') $message.id; return }
                    $script:GlassWebQueue.Enqueue($message)
                    if (-not $script:GlassWebDrainScheduled) {
                        $script:GlassWebDrainScheduled=$true
                        [void]$window.Dispatcher.BeginInvoke([Action]{Invoke-GlassWebQueue},[System.Windows.Threading.DispatcherPriority]::Background)
                    }
                })
                $script:GlassWebInitialized=$true
                $core.Navigate($script:GlassWebOrigin)
            } catch { Set-GlassWebFailure (T 'glassUnavailable') }
        })
        # Never wait on this task: WPF's message loop completes initialization.
        $script:GlassWebInitializationStarted=[DateTime]::Now
        [void]$window.Dispatcher.BeginInvoke([Action]{
            Start-GlassWebRenderer
        },[System.Windows.Threading.DispatcherPriority]::Loaded)
        if (-not $script:GlassWebTickRegistered) {
            $progressTimer.Add_Tick({
                if (-not $window.IsVisible) {
                    if (-not $script:GlassWebSleeping) {Update-GlassWebPowerState}
                    return
                }
                if ($script:GlassWebWantsVisible -and -not $script:GlassWebPresentationReady -and -not $script:GlassWebFailed -and ([DateTime]::Now-$script:GlassWebInitializationStarted).TotalSeconds -gt 20) { Set-GlassWebFailure (T 'glassTimeout') }
                if (Update-TrackSearch) {$script:GlassWebSearchDirty=$true}
                Update-TrackPreview
                Set-GlassWebVisibility; Publish-GlassWebState
            })
            $script:GlassWebTickRegistered=$true
        }
        $window.Add_IsVisibleChanged({
            $script:GlassWebWantsVisible=$window.IsVisible
            if ($window.IsVisible -and -not $script:GlassWebPresentationReady) {$script:GlassWebInitializationStarted=[DateTime]::Now}
            if (-not $window.IsVisible) {
                $hasStream=[bool]$script:TrackPreview.url
                Stop-TrackPreview
                Cancel-TrackSearch
                $script:GlassWebSearchDirty=$true
                $script:GlassWebPane='player'
                $script:GlassWebLastWireJson=''
                Update-GlassWebLayout
                if ($hasStream) {Publish-GlassWebState -Force}
            }
            Set-GlassWebVisibility
            if ($window.IsVisible) {
                Publish-GlassWebState -Force
                if ($script:GlassWebPresentationReady -and -not $script:GlassWebPresentationRevealed) {Update-GlassWebPresentation}
            }
        })
        $window.Add_Deactivated({Set-ShortcutCapture $false;if ($script:GlassWebPane -in @('menu','volume')) {Set-GlassWebPane 'player'}})
        $window.Add_Closed({ $script:GlassPresentationTimer.Stop();Stop-TrackPreview -NoResume;Stop-TrackSearch;Stop-GlassAudioVisualization; if ($null -ne $script:GlassWebControl) {$script:GlassWebControl.Dispose()} })
        return $true
    } catch {
        Set-GlassWebFailure $_.Exception.Message
        return $false
    }
}

function Start-GlassWebRenderer {
    if ($null -eq $script:GlassWebControl -or $null -ne $script:GlassWebInitializationTask -or $script:GlassWebFailed) {return}
    try {
        $script:GlassWebInitializationStarted=[DateTime]::Now
        $script:GlassWebInitializationTask=$script:GlassWebControl.EnsureCoreWebView2Async($null)
    } catch { Set-GlassWebFailure (T 'glassWebviewFailed') }
}
