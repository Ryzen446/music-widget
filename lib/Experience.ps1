# Native OLED/fallback player and minimal shell; Glass presentation uses GlassWeb.ps1.
$script:IsInitializing=$true
$script:DesignMode='Minimalist'
$script:CurrentThemeIdx=1
$script:NativeGlassSupported=$false
$script:NativeBackdropResult=0
$script:IsUpdatingMiniVolume=$false
$script:MinimalPosition='TopCenter'
$script:MinimalPlacementLabels=[ordered]@{
    TopLeft='positionTopLeft'
    TopCenter='positionTopCenter'
    TopRight='positionTopRight'
    BottomLeft='positionBottomLeft'
    BottomCenter='positionBottomCenter'
    BottomRight='positionBottomRight'
}
$script:MinimalPlacements=[string[]]@($script:MinimalPlacementLabels.Keys)
$script:MinimalScreen=''
$script:MinimalistWidth=350.0
$script:MinimalistHeight=52.0
$script:CompactBaseWidth=400.0
$script:CompactBaseHeight=260.0
$script:CompactWidth=$script:CompactBaseWidth
$script:SizeScale=1.0
$script:AuraColor='#DCE4EF'
$script:AuraMode='Static'
$script:CompactLeft=$null
$script:CompactTop=$null
$script:CompactScreen=''
$script:ReducedMotion=$false
$script:AlwaysOnTop=$true
$script:PlaybackSpeed=1.0
$script:SleepDeadline=$null
$script:IsAppearanceOpen=$false
$script:OverlayBaseHeight=0.0
$script:ThemeAccent='#FFFFFF'
$script:AnchorTick=0
$script:IsPositioning=$false
$script:GlassFrameOwnedByWeb=$false

foreach ($name in @('PlayerLayout','HeaderRow','TrackRow','TrackMetadata','TimelineRow','PlaybackRow','VolumeRow','TxtStatus','TxtDesignLabel','WavePanel','RepeatOneBadge','EmptyLibraryHint','AppearanceOverlay','BtnAppearanceClose','DesignOptions','PaletteOptions','PinOption','ReducedMotionOption','PlaybackSpeedOptions','SleepOptions','PositionSettings','PositionOptions','MinimalCard','MinimalTitle','MinimalProgressBar','BtnMinimalPlay','BtnMinimalNext','BtnMinimalMenu','MinimalIconPlay','MinimalIconPause','MainReflection','MinimalReflection','MinimalSpecular','BtnMinimalMute','MinimalVolumePopup','BtnMinimalPopupMute','MinimalPopupSlider','MinimalPopupLevel','MinimalSpeaker','MinimalMuted')) {
    Set-Variable -Scope Script -Name $name -Value $window.FindName($name)
}
function New-PlayerPalette($name,$surface,$muted,$tile,$border) {
    [PSCustomObject]@{Name=$name;CardBg=$surface;CardBorder=$border;WaveBoxBg=$tile;WaveBoxBorder=$border;WaveColor='#FFFFFF';TxtTitle='#FFFFFF';TxtSubtitle=$muted;ControlsBg=$tile;ControlsBorder=$border;ControlsFg='#FFFFFF';PlayBg='#FFFFFF';PlayFg='#000000';IconsHeader=$muted;ItemHover=$tile;ItemActiveBg=$tile;ItemActiveFg='#FFFFFF';ShadowOpacity=0.22;Accent='#FFFFFF'}
}
$glassPalette=New-PlayerPalette 'Glass' '#B0202024' '#DDDDE0' '#26FFFFFF' '#66FFFFFF'
# Both compatibility slots retain the established internal Glass index.
$script:Themes=@($glassPalette,$glassPalette)
function Set-ResourceBrush($key,$value) {
    $brush=$bc.ConvertFromString($value); $brush.Freeze(); $window.Resources[$key]=$brush
}
function Update-ModeIndicators {
    $t=$script:Themes[$script:CurrentThemeIdx]
    $accent=$window.Resources['AccentBrush']; $muted=$window.Resources['MutedBrush']
    $IconShuffle.Fill=$null; $IconShuffle.Stroke=if ($script:IsShuffle) {$accent} else {$muted}
    $BtnShuffle.Opacity=if ($script:IsShuffle) {1.0} else {0.65}
    $BtnShuffle.ToolTip=T $(if ($script:IsShuffle) {'shuffleOn'} else {'inOrder'})
    $IconRepeat.Fill=if ($script:RepeatMode -eq 2) {$muted} else {$accent}
    $BtnRepeat.Opacity=if ($script:RepeatMode -eq 2) {0.65} else {1.0}
    $RepeatOneBadge.Visibility=if ($script:RepeatMode -eq 1) {'Visible'} else {'Collapsed'}
    $BtnRepeat.ToolTip=T (@('repeatList','repeatTrack','repeatOff')[$script:RepeatMode])
    $IconPlaylist.Stroke=if ($script:IsDrawerOpen) {$accent} else {$muted}; $IconPlaylist.Fill=$IconPlaylist.Stroke
    $IconSpeaker.Fill=$muted; $IconSpeaker.Stroke=$muted
    $EmptyLibraryHint.Visibility=if ($script:Playlist.Count -eq 0) {'Visible'} else {'Collapsed'}
}
function Apply-Theme {
    param([int]$Index)
    $script:CurrentThemeIdx=1
    $t=$script:Themes[$script:CurrentThemeIdx]
    foreach ($pair in @(@('SurfaceBrush',$t.CardBg),@('ModalSurfaceBrush','#FF17171B'),@('CardBorderBrush',$t.CardBorder),@('TextBrush','#FFFFFF'),@('MutedBrush','#E0E0E5'),@('TileBrush',$t.WaveBoxBg),@('TrackBrush',$(if ($script:CurrentThemeIdx -eq 1) {'#60FFFFFF'} else {'#FF363636'})),@('AccentBrush','#FFFFFF'),@('AccentTextBrush',$(if ($script:CurrentThemeIdx -eq 1) {'#FFFFFF'} else {'#000000'})),@('PlayBrush',$(if ($script:CurrentThemeIdx -eq 1) {'#28FFFFFF'} else {'#FFFFFF'})),@('PlayBorderBrush',$(if ($script:CurrentThemeIdx -eq 1) {'#A0FFFFFF'} else {'#FFFFFF'})),@('PlayReflectionBrush',$(if ($script:CurrentThemeIdx -eq 1) {'#16FFFFFF'} else {'#00FFFFFF'})))) { Set-ResourceBrush $pair[0] $pair[1] }
    if ($script:CurrentThemeIdx -eq 1) {
        $glass=[System.Windows.Media.LinearGradientBrush]::new()
        $glass.StartPoint='0,0'; $glass.EndPoint='1,1'
        [void]$glass.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#18FFFFFF'),0))
        [void]$glass.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#03101016'),0.5))
        [void]$glass.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#10FFFFFF'),1))
        $glass.Freeze(); $window.Resources['SurfaceBrush']=$glass
        $edge=[System.Windows.Media.LinearGradientBrush]::new()
        $edge.StartPoint='0,0'; $edge.EndPoint='0.8,1'
        [void]$edge.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#BFFFFFFF'),0))
        [void]$edge.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#20FFFFFF'),0.55))
        [void]$edge.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#60FFFFFF'),1))
        $edge.Freeze(); $window.Resources['CardBorderBrush']=$edge
    }
    foreach ($card in @($MainCard,$MinimalCard)) { $card.Background=$window.Resources['SurfaceBrush']; $card.BorderBrush=$window.Resources['CardBorderBrush'] }
    $MainReflection.Visibility=if ($script:CurrentThemeIdx -eq 1) {'Visible'} else {'Collapsed'}; $MinimalReflection.Visibility=$MainReflection.Visibility
    $PnlWaveBox.Background=$window.Resources['TileBrush']; $PnlWaveBox.BorderBrush=$window.Resources['CardBorderBrush']
    foreach ($wave in @($WaveBar1,$WaveBar2,$WaveBar3,$WaveBar4,$WaveBar5)) { $wave.Background=$window.Resources['AccentBrush'] }
    $TxtTitle.Foreground=$window.Resources['TextBrush']
    foreach ($text in @($TxtSubtitle,$TxtCurrentTime,$TxtTotalTime,$TxtVolumePercent,$TxtPlaylistCount)) { $text.Foreground=$window.Resources['MutedBrush'] }
    foreach ($icon in @($IconTheme,$IconHide,$IconNewPlaylist,$IconAddMusic)) { $icon.Fill=$window.Resources['MutedBrush']; $icon.Stroke=$window.Resources['MutedBrush'] }
    foreach ($button in @($BtnPrev,$BtnNext)) { $button.Background=$window.Resources['TileBrush']; $button.BorderBrush=$window.Resources['CardBorderBrush'] }
    $IconPrev.Fill=$window.Resources['TextBrush']; $IconNext.Fill=$window.Resources['TextBrush']
    $BtnPlay.Background=$window.Resources['PlayBrush']; $IconPlay.Fill=$window.Resources['AccentTextBrush']; $IconPause.Fill=$window.Resources['AccentTextBrush']
    $DrawerDivider.BorderBrush=$window.Resources['CardBorderBrush']; $BtnTheme.ToolTip=(T 'appearance')+' · '+$t.Name
    Update-NativeMaterial
    Update-PlaylistSelectionUI; Render-PlaylistShelf; Update-ModeIndicators; Update-AppearanceSelection; Save-AppSettings
    if (Get-Command Set-GlassWebVisibility -ErrorAction SilentlyContinue) {
        if ($script:CurrentThemeIdx -eq 1 -and -not $NoRun -and $null -eq $script:GlassWebControl) {[void](Initialize-GlassWeb)}
        Set-GlassWebVisibility
        if (-not $script:GlassWebActive) {Update-PlayerSize}
    }
}
function Test-MotionEnabled { (-not $script:ReducedMotion) -and [System.Windows.SystemParameters]::ClientAreaAnimation }
function Get-BasePlayerHeight { if ($script:DesignMode -eq 'Minimalist' -and -not $script:IsDrawerOpen) {$script:MinimalistHeight} else {218.0} }
function Test-ContentOverlay {
    $script:IsAppearanceOpen -or $script:IsModalOpen -or $script:IsUploadModalOpen -or $script:IsCreatePlaylistModalOpen -or $script:IsContextMenuOpen
}
function Get-AnchoredBounds($Area,[double]$Width,[double]$Height,[string]$Position) {
    $gap=16.0
    $left=switch -Regex ($Position) {
        'Left$' {$Area.Left+$gap}
        'Right$' {$Area.Right-$Width-$gap}
        default {$Area.Left+($Area.Width-$Width)/2}
    }
    $top=if ($Position -like 'Bottom*') {$Area.Bottom-$Height-$gap} else {$Area.Top+$gap}
    [pscustomobject]@{Left=[math]::Max($Area.Left,[math]::Min($Area.Right-$Width,$left));Top=[math]::Max($Area.Top,[math]::Min($Area.Bottom-$Height,$top))}
}
function Get-MinimalWorkArea {
    $screen=@([System.Windows.Forms.Screen]::AllScreens | Where-Object {$_.DeviceName -eq $script:MinimalScreen}) | Select-Object -First 1
    if ($null -eq $screen) { $screen=[System.Windows.Forms.Screen]::PrimaryScreen }
    $scaleX=1.0; $scaleY=1.0
    try { $dpi=[System.Windows.Media.VisualTreeHelper]::GetDpi($window); $scaleX=$dpi.DpiScaleX; $scaleY=$dpi.DpiScaleY } catch {}
    $a=$screen.WorkingArea
    [System.Windows.Rect]::new($a.Left/$scaleX,$a.Top/$scaleY,$a.Width/$scaleX,$a.Height/$scaleY)
}
function Set-MinimalPlacement {
    param([string]$Position=$script:MinimalPosition)
    if ($Position -cnotin $script:MinimalPlacements) {$Position='TopCenter'}
    $script:MinimalPosition=$Position
    if ($script:DesignMode -ne 'Minimalist' -or $script:IsPositioning) { return }
    $script:IsPositioning=$true
    try {
        $bounds=Get-AnchoredBounds (Get-MinimalWorkArea) $window.Width $window.Height $Position
        if ([math]::Abs($window.Left-$bounds.Left) -gt 0.5) {$window.Left=$bounds.Left}
        if ([math]::Abs($window.Top-$bounds.Top) -gt 0.5) {$window.Top=$bounds.Top}
    } finally {$script:IsPositioning=$false}
}
function Select-MinimalPlacement([string]$Position) {
    if ($Position -cnotin $script:MinimalPlacements) {return}
    $script:MinimalPosition=$Position
    $PositionOptions.SelectedIndex=[array]::IndexOf($script:MinimalPlacements,$Position)
    if ($script:GlassWebActive) {Update-GlassWebLayout} else {Set-MinimalPlacement}
    Save-AppSettings
}
function Get-CompactWorkArea {
    $screen=@([System.Windows.Forms.Screen]::AllScreens | Where-Object {$_.DeviceName -eq $script:CompactScreen}) | Select-Object -First 1
    if ($null -eq $screen) { $screen=[System.Windows.Forms.Screen]::PrimaryScreen }
    $scaleX=1.0; $scaleY=1.0
    try {$dpi=[System.Windows.Media.VisualTreeHelper]::GetDpi($window);$scaleX=$dpi.DpiScaleX;$scaleY=$dpi.DpiScaleY} catch {}
    $area=$screen.WorkingArea
    [System.Windows.Rect]::new($area.Left/$scaleX,$area.Top/$scaleY,$area.Width/$scaleX,$area.Height/$scaleY)
}

function Set-CompactPlacement {
    if ($script:DesignMode -ne 'Compact' -or $script:IsPositioning -or $script:IsDraggingWindow) {return}
    $area=Get-CompactWorkArea
    $left=if ($null -ne $script:CompactLeft) {[double]$script:CompactLeft} else {$area.Right-$window.Width-16}
    $top=if ($null -ne $script:CompactTop) {[double]$script:CompactTop} else {$area.Top+16}
    $left=[math]::Max($area.Left,[math]::Min([math]::Max($area.Left,$area.Right-$window.Width),$left))
    $top=[math]::Max($area.Top,[math]::Min([math]::Max($area.Top,$area.Bottom-$window.Height),$top))
    $script:IsPositioning=$true
    $wasDragging=$script:IsDraggingWindow; $script:IsDraggingWindow=$true
    try {$window.Left=$left;$window.Top=$top} finally {$script:IsDraggingWindow=$wasDragging;$script:IsPositioning=$false}
}

function Remember-CompactPosition {
    if ($script:DesignMode -ne 'Compact' -or $script:IsInitializing) {return}
    if ($script:GlassWebActive -and $script:GlassWebPane -ne 'player') {return}
    $inset=if ($script:GlassWebActive) {$script:GlassWebPlayerInset} else {0}
    $script:CompactLeft=[math]::Round($window.Left,1);$script:CompactTop=[math]::Round($window.Top+$inset,1)
    try {
        $screenPoint=$window.PointToScreen([System.Windows.Point]::new(20,$inset+20))
        $point=[System.Drawing.Point]::new([int]$screenPoint.X,[int]$screenPoint.Y)
        $script:CompactScreen=[System.Windows.Forms.Screen]::FromPoint($point).DeviceName
    } catch {}
    Save-AppSettings
}

function Set-PlayerScale([double]$Scale) {
    if ([double]::IsNaN($Scale) -or [double]::IsInfinity($Scale)) {return}
    $script:SizeScale=[math]::Round([math]::Max(0.8,[math]::Min(1.5,$Scale)),3)
    $script:CompactWidth=$script:CompactBaseWidth*$script:SizeScale
    $script:UserCompactHeight=$script:CompactBaseHeight*$script:SizeScale
    if ($script:GlassWebActive) {Update-GlassWebLayout} else {Update-PlayerSize}
    Save-AppSettings
}

function Set-DesignMode {
    param([string]$Mode,[switch]$KeepWidth)
    if ($Mode -notin @('Compact','Minimalist')) {$Mode='Compact'}
    if ($Mode -eq 'Minimalist' -and $script:DesignMode -ne 'Minimalist') {
        $point=[System.Drawing.Point]::new([int]$window.Left+20,[int]$window.Top+20)
        $script:MinimalScreen=[System.Windows.Forms.Screen]::FromPoint($point).DeviceName
    }
    $script:DesignMode=$Mode
    if ($Mode -eq 'Minimalist') {$script:IsDrawerOpen=$false}
    $window.BeginAnimation([System.Windows.Window]::HeightProperty,$null); $PlaylistDrawer.BeginAnimation([System.Windows.FrameworkElement]::HeightProperty,$null)
    $PlayerLayout.ColumnDefinitions[1].Width=[System.Windows.GridLength]::new(0)
    $TrackRow.RowDefinitions.Clear()
    [System.Windows.Controls.Grid]::SetColumnSpan($PnlWaveBox,1)
    [System.Windows.Controls.Grid]::SetColumn($TrackMetadata,1); [System.Windows.Controls.Grid]::SetColumnSpan($TrackMetadata,1); [System.Windows.Controls.Grid]::SetRow($TrackMetadata,0)
    $TrackRow.Margin='0,0,0,8'; $TrackMetadata.Margin='12,0,0,0'; $PnlWaveBox.Width=38; $PnlWaveBox.Height=38; $PnlWaveBox.CornerRadius=12; $PnlWaveBox.HorizontalAlignment='Left'
    $HeaderRow.Margin='0,0,0,5'; $PlaybackRow.Margin='0'; $VolumeRow.Visibility='Visible'; $TxtTitle.FontSize=13; $TxtDesignLabel.Text=''
    $script:CompactWidth=$script:CompactBaseWidth*$script:SizeScale
    $script:UserCompactHeight=$script:CompactBaseHeight*$script:SizeScale
    $script:UserExpandedHeight=[math]::Max(438,[math]::Min(750,$script:UserExpandedHeight))
    Update-PlayerSize; Update-AppearanceSelection; Update-MinimalView; Update-VisualActivity; Start-TitleMarqueeAnimation; Save-AppSettings
}
function Update-PlayerSize {
    if ($script:GlassWebActive) {Update-GlassWebLayout;return}
    $overlay=Test-ContentOverlay
    $minimal=$script:DesignMode -eq 'Minimalist' -and -not $script:IsDrawerOpen -and -not $overlay
    $window.MinHeight=$script:MinimalistHeight; $window.MinWidth=280
    $MinimalCard.Visibility=if ($minimal) {'Visible'} else {'Collapsed'}
    $MainCard.Visibility=if ($script:DesignMode -eq 'Compact' -or $script:IsDrawerOpen) {'Visible'} else {'Collapsed'}
    foreach ($thumb in @($ResizeGripThumb,$RightResizeThumb,$BottomResizeThumb)) {$thumb.Visibility=if ($script:DesignMode -eq 'Minimalist' -or $overlay) {'Collapsed'} else {'Visible'}}
    $drawerHeight=if ($script:IsDrawerOpen) {[math]::Max(180,$script:UserExpandedHeight-218)} else {0}
    $PlaylistDrawer.Height=$drawerHeight
    if ($minimal) {$window.Width=$script:MinimalistWidth; $window.Height=$script:MinimalistHeight}
    elseif ($overlay) {$window.Width=[math]::Max(352,$script:CompactWidth); $window.Height=if ($script:IsAppearanceOpen) {550} else {480}}
    else {$window.Width=[math]::Max(320,$script:CompactWidth); $window.Height=if ($script:IsDrawerOpen) {218+$drawerHeight} else {$script:UserCompactHeight}}
    if ($script:DesignMode -eq 'Minimalist') {Set-MinimalPlacement} else {Set-CompactPlacement}
    if ($script:CurrentThemeIdx -eq 1 -and $script:GlassWebReady -and -not $script:GlassWebFailed) {Set-GlassWebVisibility;Update-GlassWebLayout}
}
function Toggle-PlaylistDrawer {
    $script:IsDrawerOpen=-not $script:IsDrawerOpen
    Update-PlayerSize
    if ($script:IsDrawerOpen -and (Test-MotionEnabled)) {
        $fade=[System.Windows.Media.Animation.DoubleAnimation]::new(0,1,[TimeSpan]::FromMilliseconds(180)); $PlaylistDrawer.BeginAnimation([System.Windows.UIElement]::OpacityProperty,$fade)
    }
    Update-ModeIndicators; Update-VisualActivity; Start-TitleMarqueeAnimation; Save-AppSettings
}
function Resize-Player([double]$WidthDelta,[double]$HeightDelta) {
    if ($script:DesignMode -eq 'Minimalist' -or (Test-ContentOverlay)) {return}
    if ($script:GlassWebActive -and $script:GlassWebPane -ne 'player') {return}
    if (-not $script:IsDrawerOpen) {
        $dx=$WidthDelta/$script:CompactBaseWidth
        $dy=$HeightDelta/$script:CompactBaseHeight
        $delta=if ([math]::Abs($dx) -ge [math]::Abs($dy)) {$dx} else {$dy}
        Set-PlayerScale ($script:SizeScale+$delta)
        return
    }
    $window.Width=[math]::Max(320,[math]::Min(550,$window.Width+$WidthDelta)); $script:CompactWidth=$window.Width
    $min=if ($script:IsDrawerOpen) {398} else {218}
    $window.Height=[math]::Max($min,[math]::Min(750,$window.Height+$HeightDelta))
    if ($script:IsDrawerOpen) {$script:UserExpandedHeight=$window.Height; $PlaylistDrawer.Height=$window.Height-218} else {$script:UserCompactHeight=$window.Height}
    Clamp-WindowToScreen; Save-AppSettings
}
function Update-MinimalView {
    $title=$TxtTitle.Text
    if ($MinimalTitle.Text -ne $title) {$MinimalTitle.Text=$title}
    $MinimalIconPlay.Visibility=if ($script:IsPlaying) {'Collapsed'} else {'Visible'}
    $MinimalIconPause.Visibility=if ($script:IsPlaying) {'Visible'} else {'Collapsed'}
    $progress=if ($script:MediaIsReady) {$ProgressSlider.Value} else {0}
    if ($MinimalProgressBar.Value -ne $progress) {$MinimalProgressBar.Value=$progress}
}
function Update-VisualActivity {
    $active=$script:IsPlaying -and $window.IsVisible -and $MainCard.IsVisible -and (Test-MotionEnabled)
    if ($active -and -not $liveTimer.IsEnabled) {$liveTimer.Start()} elseif (-not $active -and $liveTimer.IsEnabled) {$liveTimer.Stop()}
    if (-not $active) {foreach ($bar in @($WaveBar1,$WaveBar2,$WaveBar3,$WaveBar4,$WaveBar5)) {$bar.RenderTransform.ScaleY=0.15}}
    if (-not $MainCard.IsVisible -or -not (Test-MotionEnabled)) {Stop-TitleMarqueeAnimation}
}
function Update-AppearanceSelection {
    foreach ($button in $DesignOptions.Children) {$button.BorderBrush=if ($button.Tag -eq $script:DesignMode) {$window.Resources['AccentBrush']} else {$window.Resources['CardBorderBrush']}; $button.Background=if ($button.Tag -eq $script:DesignMode) {$window.Resources['TileBrush']} else {[System.Windows.Media.Brushes]::Transparent}}
    foreach ($button in $PaletteOptions.Children) {$button.BorderBrush=if ([int]$button.Tag -eq $script:CurrentThemeIdx) {$window.Resources['AccentBrush']} else {$window.Resources['CardBorderBrush']}}
    $PositionSettings.Visibility=if ($script:DesignMode -eq 'Minimalist') {'Visible'} else {'Collapsed'}
}
function Show-Appearance {
    if ($script:GlassWebActive) {Set-GlassWebPane 'settings';return}
    if ($script:IsModalOpen -or $script:IsUploadModalOpen -or $script:IsCreatePlaylistModalOpen) {return}
    $script:IsAppearanceOpen=$true; $AppearanceOverlay.Visibility='Visible'; Update-PlayerSize; Update-AppearanceSelection; Update-VisualActivity
    if (Test-MotionEnabled) {$fade=[System.Windows.Media.Animation.DoubleAnimation]::new(0,1,[TimeSpan]::FromMilliseconds(160)); $AppearanceOverlay.BeginAnimation([System.Windows.UIElement]::OpacityProperty,$fade)}
    [void]$BtnAppearanceClose.Focus()
}
function Hide-Appearance {
    $script:IsAppearanceOpen=$false; $AppearanceOverlay.Visibility='Collapsed'; Update-PlayerSize; Update-VisualActivity; Start-TitleMarqueeAnimation; Save-AppSettings
}
foreach ($mode in @(@('Compact','compact','compactDescription'),@('Minimalist','minimal','minimalDescription'))) {
    $button=[System.Windows.Controls.Button]::new(); $button.Tag=$mode[0]; $button.Margin='3'; $button.Padding='10,10'; $button.HorizontalContentAlignment='Left'; $button.Style=$window.FindResource('UploadOptionBtn'); $button.BorderThickness='1'
    $stack=[System.Windows.Controls.StackPanel]::new()
    $title=[System.Windows.Controls.TextBlock]::new(); $title.SetResourceReference([System.Windows.Controls.TextBlock]::TextProperty,('i.'+$mode[1])); $title.FontSize=12; $title.FontWeight='SemiBold'; $title.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty,'TextBrush')
    $detail=[System.Windows.Controls.TextBlock]::new(); $detail.SetResourceReference([System.Windows.Controls.TextBlock]::TextProperty,('i.'+$mode[2])); $detail.FontSize=10; $detail.Margin='0,4,0,0'; $detail.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty,'MutedBrush')
    [void]$stack.Children.Add($title); [void]$stack.Children.Add($detail); $button.Content=$stack
    $button.Add_Click({param($s,$e); Set-DesignMode ([string]$s.Tag)})
    [void]$DesignOptions.Children.Add($button)
}
foreach ($i in @(1)) {
    $button=[System.Windows.Controls.Button]::new(); $button.Tag=$i; $button.Margin='3'; $button.Padding='10,10'; $button.Style=$window.FindResource('UploadOptionBtn'); $button.BorderThickness='1'; $button.Background=[System.Windows.Media.Brushes]::Transparent
    $stack=[System.Windows.Controls.StackPanel]::new()
    $sample=[System.Windows.Controls.Border]::new(); $sample.Height=23; $sample.CornerRadius=7; $sample.BorderThickness=1; $sample.BorderBrush=$bc.ConvertFromString('#99FFFFFF')
    if ($i -eq 0) {$sample.Background=[System.Windows.Media.Brushes]::Black} else {$sample.Background=[System.Windows.Media.LinearGradientBrush]::new([System.Windows.Media.Colors]::LightGray,[System.Windows.Media.Colors]::Transparent,35)}
    $label=[System.Windows.Controls.TextBlock]::new(); $label.Text=$script:Themes[$i].Name; $label.FontSize=11; $label.Margin='0,7,0,0'; $label.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty,'TextBrush')
    [void]$stack.Children.Add($sample); [void]$stack.Children.Add($label); $button.Content=$stack; $button.ToolTip=$label.Text
    [System.Windows.Automation.AutomationProperties]::SetName($button,$label.Text)
    $button.Add_Click({param($s,$e); Apply-Theme ([int]$s.Tag)})
    [void]$PaletteOptions.Children.Add($button)
}
function Add-NativeSettingsOption($Options, [string]$Key, $Value=$null) {
    $item=[System.Windows.Controls.ComboBoxItem]::new()
    $item.Tag=$Value
    if ($null -eq $Value) {$item.SetResourceReference([System.Windows.Controls.ContentControl]::ContentProperty,('i.'+$Key))}
    [void]$Options.Items.Add($item)
}
foreach ($key in $script:MinimalPlacementLabels.Values) {Add-NativeSettingsOption $PositionOptions $key}
$PositionOptions.SelectedIndex=[array]::IndexOf($script:MinimalPlacements,$script:MinimalPosition)
$PositionOptions.Add_SelectionChanged({
    if ($PositionOptions.SelectedIndex -lt 0) {return}
    $position=$script:MinimalPlacements[$PositionOptions.SelectedIndex]
    if ($position -ne $script:MinimalPosition) {Select-MinimalPlacement $position}
})

$script:MinimalMenu=[System.Windows.Controls.Menu]::new()
$script:MinimalMenu.Resources=$window.Resources
$script:MinimalMenu.Background=[System.Windows.Media.Brushes]::Transparent
$script:MinimalMenu.IsMainMenu=$false
$script:MinimalMenu.ItemsPanel=[System.Windows.Markup.XamlReader]::Parse('<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Orientation="Vertical"/></ItemsPanelTemplate>')
$script:MinimalMenu.Template=[System.Windows.Markup.XamlReader]::Parse('<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Menu"><Border x:Name="ContextSurface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent"><ItemsPresenter/></Border></ControlTemplate>')
function Add-MinimalMenuItem($Key,$Action,$Parent=$script:MinimalMenu) {
    $item=[System.Windows.Controls.MenuItem]::new(); $item.SetResourceReference([System.Windows.Controls.HeaderedItemsControl]::HeaderProperty,('i.'+$Key)); $item.Add_Click($Action); [void]$Parent.Items.Add($item); $item
}
$null=Add-MinimalMenuItem 'appearance' {Show-Appearance}
$null=Add-MinimalMenuItem 'library' {Toggle-PlaylistDrawer}
$layoutMenu=[System.Windows.Controls.MenuItem]::new(); $layoutMenu.SetResourceReference([System.Windows.Controls.HeaderedItemsControl]::HeaderProperty,'i.layout'); [void]$script:MinimalMenu.Items.Add($layoutMenu)
$null=Add-MinimalMenuItem 'compact' {Set-DesignMode Compact} $layoutMenu
$null=Add-MinimalMenuItem 'minimal' {Set-DesignMode Minimalist} $layoutMenu
$positionMenu=[System.Windows.Controls.MenuItem]::new(); $positionMenu.SetResourceReference([System.Windows.Controls.HeaderedItemsControl]::HeaderProperty,'i.playerPosition'); [void]$script:MinimalMenu.Items.Add($positionMenu)
foreach ($position in $script:MinimalPlacements) {
    $item=[System.Windows.Controls.MenuItem]::new(); $item.SetResourceReference([System.Windows.Controls.HeaderedItemsControl]::HeaderProperty,('i.'+$script:MinimalPlacementLabels[$position])); $item.Tag=$position
    $item.Add_Click({param($s,$e); Select-MinimalPlacement ([string]$s.Tag)})
    [void]$positionMenu.Items.Add($item)
}
[void]$script:MinimalMenu.Items.Add([System.Windows.Controls.Separator]::new())
$null=Add-MinimalMenuItem 'next' {Next-Track; Update-MinimalView}
$null=Add-MinimalMenuItem 'hideWidget' {Hide-Widget}
$MinimalCard.Add_MouseRightButtonUp({param($s,$e);Show-LiquidMenu;$e.Handled=$true})
$BtnMinimalMenu.Add_Click({Show-LiquidMenu})
$BtnMinimalPlay.Add_Click({Toggle-PlayPause; Update-MinimalView; Update-VisualActivity})
$BtnMinimalMute.Add_Click({$MinimalVolumePopup.IsOpen=-not $MinimalVolumePopup.IsOpen; Sync-MinimalMute})
$BtnMinimalPopupMute.Add_Click({Toggle-LocalMute})
$MinimalPopupSlider.Add_ValueChanged({
    if ($script:SyncingMinimalVolume) {return}
    $script:LocalVolume=$MinimalPopupSlider.Value/100
    if ($script:LocalVolume -gt 0) {$script:IsMuted=$false}
    Update-LocalVolumeUI
})
$BtnMinimalNext.Add_Click({Next-Track; Update-MinimalView})
$BtnAppearanceClose.Add_Click({Hide-Appearance})
$window.FindName('BtnRefreshLibrary').Add_Click({Reload-Playlist; Update-ModeIndicators; Update-MinimalView})
$AppearanceOverlay.Add_MouseLeftButtonDown({param($s,$e);$e.Handled=$true})
foreach ($rate in @(0.75,1.0,1.25,1.5,2.0)) {Add-NativeSettingsOption $PlaybackSpeedOptions 'nativeSpeed' $rate}
$PlaybackSpeedOptions.SelectedIndex=1
$PlaybackSpeedOptions.Add_SelectionChanged({$script:PlaybackSpeed=@(0.75,1.0,1.25,1.5,2.0)[$PlaybackSpeedOptions.SelectedIndex];$script:Player.SpeedRatio=$script:PlaybackSpeed;Save-AppSettings})
Add-NativeSettingsOption $SleepOptions 'nativeSleepOff'
foreach ($minutes in @(15,30,60)) {Add-NativeSettingsOption $SleepOptions 'nativeSleep' $minutes}
$SleepOptions.SelectedIndex=0
$SleepOptions.Add_SelectionChanged({$minutes=@(0,15,30,60)[$SleepOptions.SelectedIndex];$script:SleepDeadline=if ($minutes -gt 0) {[DateTime]::Now.AddMinutes($minutes)} else {$null}})
$PinOption.IsChecked=$true
$PinOption.Add_Click({$script:AlwaysOnTop=[bool]$PinOption.IsChecked;$window.Topmost=$script:AlwaysOnTop;Save-AppSettings})
$ReducedMotionOption.Add_Click({$script:ReducedMotion=[bool]$ReducedMotionOption.IsChecked;Update-VisualActivity;if (Test-MotionEnabled) {Start-TitleMarqueeAnimation};Save-AppSettings})
$window.Add_IsVisibleChanged({if(-not $window.IsVisible){$MinimalVolumePopup.IsOpen=$false;if($null -ne $script:LiquidMenuWindow){$script:LiquidMenuWindow.Hide()}};Update-VisualActivity;if ($window.IsVisible) {Set-MinimalPlacement;if (Test-MotionEnabled) {Start-TitleMarqueeAnimation}}})
foreach ($overlay in @($ModalOverlay,$UploadOverlay,$CreatePlaylistOverlay,$ContextMenuOverlay)) {$overlay.Add_IsVisibleChanged({Update-PlayerSize;Update-VisualActivity})}
$window.Add_PreviewKeyDown({
    param($s,$e)
    if ($script:GlassWebActive) {return}
    if ($e.Key -eq [System.Windows.Input.Key]::Escape -and $script:IsAppearanceOpen) {Hide-Appearance;$e.Handled=$true;return}
    if (Test-ContentOverlay) {return}
    if ([System.Windows.Input.Keyboard]::FocusedElement -is [System.Windows.Controls.TextBox]) {return}
    $ctrl=([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control) -ne 0
    switch ($e.Key.ToString()) {
        'Space' {Toggle-PlayPause;Update-MinimalView;$e.Handled=$true}
        'M' {Toggle-LocalMute;$e.Handled=$true}
        'L' {if ($ctrl) {Toggle-PlaylistDrawer;$e.Handled=$true}}
        'Left' {if ($ctrl) {Prev-Track} elseif ($script:Player.NaturalDuration.HasTimeSpan) {$script:Player.Position=[TimeSpan]::FromSeconds([math]::Max(0,$script:Player.Position.TotalSeconds-5))};$e.Handled=$true}
        'Right' {if ($ctrl) {Next-Track} elseif ($script:Player.NaturalDuration.HasTimeSpan) {$script:Player.Position=[TimeSpan]::FromSeconds([math]::Min($script:Player.NaturalDuration.TimeSpan.TotalSeconds,$script:Player.Position.TotalSeconds+5))};$e.Handled=$true}
    }
})
$progressTimer=[System.Windows.Threading.DispatcherTimer]::new();$progressTimer.Interval=[TimeSpan]::FromMilliseconds(250)
$progressTimer.Add_Tick({
    try {
        if ($null -ne $script:ShowRequest -and $script:ShowRequest.WaitOne(0)) {Show-Widget}
        if ($script:SleepDeadline -and [DateTime]::Now -ge $script:SleepDeadline) {
            Stop-TrackPreview -NoResume
            if ($script:IsPlaying) {Toggle-PlayPause}
            $script:SleepDeadline=$null
            $SleepOptions.SelectedIndex=0
        }
        if ($script:GlassWebActive -or $script:GlassWebStartupSuppressed -or -not $window.IsVisible) {return}
        Update-VisualActivity
        $EmptyLibraryHint.Visibility=if ($script:Playlist.Count -eq 0) {'Visible'} else {'Collapsed'}
        $status=T $(if ($script:IsPlaying) {'playingStatus'} elseif (-not $script:LoadedTrackPath -and $script:Playlist.Count -eq 0) {'addMusicStatus'} else {'pausedStatus'})
        if ($script:SleepDeadline) {$status+=' · '+(T 'sleepStatus' @{minutes=[math]::Ceiling(($script:SleepDeadline-[DateTime]::Now).TotalMinutes)})}
        if ($TxtStatus.Text -ne $status) {$TxtStatus.Text=$status}
        if (-not $script:IsSeeking -and $script:Player.NaturalDuration.HasTimeSpan) {
            $dur=$script:Player.NaturalDuration.TimeSpan;$pos=$script:Player.Position
            $current=Format-PlaybackTime $pos;$total=Format-PlaybackTime $dur
            if ($TxtCurrentTime.Text -ne $current) {$TxtCurrentTime.Text=$current};if ($TxtTotalTime.Text -ne $total) {$TxtTotalTime.Text=$total}
            if ($dur.TotalSeconds -gt 0) {$ProgressSlider.Value=[math]::Min(100,100*$pos.TotalSeconds/$dur.TotalSeconds)}
        }
        if ($script:DesignMode -eq 'Minimalist') {Update-MinimalView;$script:AnchorTick++;if ($script:AnchorTick -ge 8) {$script:AnchorTick=0;Set-MinimalPlacement}}
    } catch {}
})
$progressTimer.Start()
$saveTimer=[System.Windows.Threading.DispatcherTimer]::new();$saveTimer.Interval=[TimeSpan]::FromSeconds(1)
$saveTimer.Add_Tick({if ($script:SettingsDirty -or ($script:IsPlaying -and ([DateTime]::Now-$script:LastSettingsWrite).TotalSeconds -ge 15)) {Save-AppSettings -Flush}});$saveTimer.Start()
function Format-PlaybackTime([TimeSpan]$time) {
    if ($time.TotalHours -ge 1) {'{0}:{1:00}:{2:00}' -f [int][math]::Floor($time.TotalHours),$time.Minutes,$time.Seconds} else {'{0}:{1:00}' -f [int][math]::Floor($time.TotalMinutes),$time.Seconds}
}
function Restore-ExperienceSettings($cfg) {
    $script:DesignMode='Minimalist';$script:CurrentThemeIdx=1
    $script:MinimalPosition='TopCenter'
    $script:SizeScale=1.0;$script:AuraColor='#DCE4EF';$script:AuraMode='Static'
    $script:CompactLeft=$null;$script:CompactTop=$null;$script:CompactScreen=''
    if ($cfg) {
        if ($cfg.DesignMode -in @('Compact','Minimalist')) {$script:DesignMode=[string]$cfg.DesignMode}
        if ($cfg.MinimalPosition -cin $script:MinimalPlacements) {$script:MinimalPosition=[string]$cfg.MinimalPosition}
        if ($cfg.MinimalScreen) {$script:MinimalScreen=[string]$cfg.MinimalScreen}
        try {
            if ($null -ne $cfg.SizeScale) {
                $savedScale=[double]$cfg.SizeScale
                if (-not [double]::IsNaN($savedScale) -and -not [double]::IsInfinity($savedScale)) {$script:SizeScale=[math]::Round([math]::Max(0.8,[math]::Min(1.5,$savedScale)),3)}
            }
            if ($null -ne $cfg.CompactLeft -and $null -ne $cfg.CompactTop) {
                $savedLeft=[double]$cfg.CompactLeft;$savedTop=[double]$cfg.CompactTop
                if (-not [double]::IsNaN($savedLeft) -and -not [double]::IsInfinity($savedLeft) -and -not [double]::IsNaN($savedTop) -and -not [double]::IsInfinity($savedTop)) {
                    $script:CompactLeft=$savedLeft;$script:CompactTop=$savedTop
                }
            }
        } catch {}
        if ($cfg.CompactScreen) {$script:CompactScreen=[string]$cfg.CompactScreen}
        if ($cfg.AuraColor -is [string] -and $cfg.AuraColor -match '^#[0-9a-fA-F]{6}$') {$script:AuraColor=$cfg.AuraColor.ToUpperInvariant()}
        if ($cfg.AuraMode -in @('Static','RGB')) {$script:AuraMode=[string]$cfg.AuraMode}
        if ($null -ne $cfg.ReducedMotion) {$script:ReducedMotion=[bool]$cfg.ReducedMotion}
        if ($null -ne $cfg.AlwaysOnTop) {$script:AlwaysOnTop=[bool]$cfg.AlwaysOnTop}
        if ($cfg.PlaybackSpeed -in @(0.75,1.0,1.25,1.5,2.0)) {$script:PlaybackSpeed=[double]$cfg.PlaybackSpeed}
    }
    $script:IsDrawerOpen=$false
    $PinOption.IsChecked=$script:AlwaysOnTop;$ReducedMotionOption.IsChecked=$script:ReducedMotion;$window.Topmost=$script:AlwaysOnTop
    $PlaybackSpeedOptions.SelectedIndex=[array]::IndexOf(@(0.75,1.0,1.25,1.5,2.0),$script:PlaybackSpeed);$script:Player.SpeedRatio=$script:PlaybackSpeed
    $PositionOptions.SelectedIndex=[array]::IndexOf($script:MinimalPlacements,$script:MinimalPosition)
    Set-DesignMode $script:DesignMode -KeepWidth
}

function Sync-MinimalMute {
    if ($null -eq $MinimalSpeaker) {return}
    $MinimalSpeaker.Visibility=if ($script:IsMuted -or $script:LocalVolume -le 0) {'Collapsed'} else {'Visible'}
    $MinimalMuted.Visibility=if ($script:IsMuted -or $script:LocalVolume -le 0) {'Visible'} else {'Collapsed'}
    if (-not $MinimalVolumePopup.IsOpen) {return}
    $script:SyncingMinimalVolume=$true
    try {$MinimalPopupSlider.Value=[math]::Round($script:LocalVolume*100)} finally {$script:SyncingMinimalVolume=$false}
    $BtnMinimalPopupMute.Content=T $(if ($script:IsMuted -or $script:LocalVolume -le 0) {'unmute'} else {'muted'})
    $MinimalPopupLevel.Text=('{0}%' -f $MinimalPopupSlider.Value)
}
function Update-NativeMaterial {
    $glass=$script:CurrentThemeIdx -eq 1
    if (-not $script:GlassFrameOwnedByWeb) {
        $script:WindowChrome.GlassFrameThickness=if ($glass) {[System.Windows.Thickness]::new(-1)} else {[System.Windows.Thickness]::new(0)}
    }
    $source=[System.Windows.PresentationSource]::FromVisual($window)
    if ($null -eq $source) {return}
    $source.CompositionTarget.BackgroundColor=[System.Windows.Media.Colors]::Transparent
    $hwnd=([System.Windows.Interop.WindowInteropHelper]::new($window)).Handle
    $transparency=[Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize','EnableTransparency',1)
    $usePlayerAcrylic=$glass -and $script:GlassFrameOwnedByWeb -and -not [Windows.SystemParameters]::HighContrast -and $transparency -ne 0
    if ($script:GlassPlayerAcrylic -and -not $usePlayerAcrylic) {
        [void][NativeBackdrop]::TrySetPlayerAcrylic($hwnd,$false)
        $script:GlassPlayerAcrylic=$false
    }
    if (-not $script:GlassPlayerAcrylic) {
        $script:NativeBackdropResult=[NativeBackdrop]::Apply($hwnd,$glass)
        $script:NativeGlassSupported=$script:NativeBackdropResult -ge 0
        if ($usePlayerAcrylic -and $script:NativeGlassSupported) {
            $script:GlassPlayerAcrylic=[NativeBackdrop]::TrySetPlayerAcrylic($hwnd,$true)
        }
    }
    if ($glass -and -not $script:NativeGlassSupported) {
        $fallback=$bc.ConvertFromString('#ED202025');$MainCard.Background=$fallback;$MinimalCard.Background=$fallback
    }
    if ($script:GlassFrameOwnedByWeb -and (Get-Command Request-GlassWindowShapeReapply -ErrorAction SilentlyContinue)) {
        Request-GlassWindowShapeReapply
    }
}

function Set-GlassNativeFrameMode([bool]$WebActive) {
    $chrome=[System.Windows.Shell.WindowChrome]::GetWindowChrome($window)
    if ($script:GlassFrameOwnedByWeb -eq $WebActive -and (($WebActive -and $null -eq $chrome) -or (-not $WebActive -and $null -ne $chrome))) {return}
    $script:GlassFrameOwnedByWeb=$WebActive
    # WindowChrome repaints can clear SetWindowRgn even when its cached web
    # bounds did not change. The browser host already supplies its own resize
    # and drag controls; NativeBackdrop.Apply extends the DWM frame directly.
    if ($WebActive) {[System.Windows.Shell.WindowChrome]::SetWindowChrome($window,$null)}
    else {[System.Windows.Shell.WindowChrome]::SetWindowChrome($window,$script:WindowChrome)}
    $script:GlassShapeLastKey=''
    Update-NativeMaterial
    if ($WebActive) {
        if (Get-Command Request-GlassWindowShapeReapply -ErrorAction SilentlyContinue) {Request-GlassWindowShapeReapply}
    } elseif (Get-Command Clear-GlassWindowShape -ErrorAction SilentlyContinue) {
        [void](Clear-GlassWindowShape -Window $window)
    }
}
$script:WindowChrome=[System.Windows.Shell.WindowChrome]::new()
$script:WindowChrome.CaptionHeight=0
$script:WindowChrome.ResizeBorderThickness=[System.Windows.Thickness]::new(0)
$script:WindowChrome.GlassFrameThickness=[System.Windows.Thickness]::new(-1)
$script:WindowChrome.UseAeroCaptionButtons=$false
[System.Windows.Shell.WindowChrome]::SetWindowChrome($window,$script:WindowChrome)
$window.Add_SourceInitialized({Update-NativeMaterial})
$window.Add_ContentRendered({Update-NativeMaterial})
$MinimalCard.Add_MouseMove({
    param($s,$e)
    if ($script:CurrentThemeIdx -ne 1 -or -not (Test-MotionEnabled)) {return}
    $point=$e.GetPosition($MinimalCard)
    $center=[System.Windows.Point]::new([math]::Max(0,[math]::Min(1,$point.X/[math]::Max(1,$MinimalCard.ActualWidth))),0)
    $MinimalSpecular.Background.Center=$center;$MinimalSpecular.Background.GradientOrigin=$center;$MinimalSpecular.Opacity=0.65
})
$MinimalCard.Add_MouseLeave({$MinimalSpecular.Opacity=0})

# A non-layered owned window lets Windows blur the menu background as well.
$script:LiquidMenuWindow=[System.Windows.Window]::new()
$script:LiquidMenuWindow.Title=T 'contextTitle'
$script:LiquidMenuWindow.WindowStyle='None';$script:LiquidMenuWindow.ResizeMode='NoResize'
$script:LiquidMenuWindow.AllowsTransparency=$false;$script:LiquidMenuWindow.Background=[System.Windows.Media.Brushes]::Transparent
$script:LiquidMenuWindow.ShowInTaskbar=$false;$script:LiquidMenuWindow.SizeToContent='WidthAndHeight'
$script:LiquidMenuWindow.Resources=$window.Resources
$script:LiquidMenuWindow.WindowStartupLocation='Manual'
$menuSurface=[System.Windows.Controls.Border]::new();$menuSurface.CornerRadius=8;$menuSurface.BorderThickness=1;$menuSurface.Padding=5;$menuSurface.MinWidth=235
$menuSurface.SetResourceReference([System.Windows.Controls.Border]::BackgroundProperty,'SurfaceBrush')
$menuSurface.SetResourceReference([System.Windows.Controls.Border]::BorderBrushProperty,'CardBorderBrush')
$menuSurface.Child=$script:MinimalMenu;$script:LiquidMenuWindow.Content=$menuSurface
$script:MenuChrome=[System.Windows.Shell.WindowChrome]::new();$script:MenuChrome.CaptionHeight=0;$script:MenuChrome.ResizeBorderThickness=[System.Windows.Thickness]::new(0);$script:MenuChrome.GlassFrameThickness=[System.Windows.Thickness]::new(-1);$script:MenuChrome.UseAeroCaptionButtons=$false
[System.Windows.Shell.WindowChrome]::SetWindowChrome($script:LiquidMenuWindow,$script:MenuChrome)
function Update-LiquidMenuMaterial {
    $source=[System.Windows.PresentationSource]::FromVisual($script:LiquidMenuWindow)
    if ($null -eq $source) {return}
    $source.CompositionTarget.BackgroundColor=[System.Windows.Media.Colors]::Transparent
    $glass=$script:CurrentThemeIdx -eq 1
    $script:MenuChrome.GlassFrameThickness=if($glass){[System.Windows.Thickness]::new(-1)}else{[System.Windows.Thickness]::new(0)}
    $handle=([System.Windows.Interop.WindowInteropHelper]::new($script:LiquidMenuWindow)).Handle
    $script:MenuBackdropResult=[NativeBackdrop]::Apply($handle,$glass)
}
function Show-LiquidMenu {
    if ($script:GlassWebActive) {Set-GlassWebPane 'menu';return}
    if ($null -eq $script:LiquidMenuWindow.Owner) {$script:LiquidMenuWindow.Owner=$window}
    $script:LiquidMenuWindow.Topmost=$script:AlwaysOnTop
    $point=$window.PointToScreen([System.Windows.Point]::new(0,$window.ActualHeight+5))
    $dpi=[System.Windows.Media.VisualTreeHelper]::GetDpi($window)
    $script:LiquidMenuWindow.Left=$point.X/$dpi.DpiScaleX;$script:LiquidMenuWindow.Top=$point.Y/$dpi.DpiScaleY
    $script:LiquidMenuWindow.Show();$script:LiquidMenuWindow.UpdateLayout();Update-LiquidMenuMaterial
    $screen=Get-MinimalWorkArea
    $script:LiquidMenuWindow.Left=[math]::Max($screen.Left,[math]::Min($screen.Right-$script:LiquidMenuWindow.ActualWidth,$script:LiquidMenuWindow.Left))
    $script:LiquidMenuWindow.Top=[math]::Max($screen.Top,[math]::Min($screen.Bottom-$script:LiquidMenuWindow.ActualHeight,$script:LiquidMenuWindow.Top))
    [void]$script:LiquidMenuWindow.Activate()
}
$script:LiquidMenuWindow.Add_SourceInitialized({Update-LiquidMenuMaterial})
$script:LiquidMenuWindow.Add_Deactivated({$script:LiquidMenuWindow.Hide()})
$script:LiquidMenuWindow.Add_PreviewKeyDown({param($s,$e);if($e.Key -eq [System.Windows.Input.Key]::Escape){$script:LiquidMenuWindow.Hide();$e.Handled=$true}})
$script:MinimalMenu.AddHandler([System.Windows.Controls.MenuItem]::ClickEvent,[System.Windows.RoutedEventHandler]{param($s,$e);if($e.OriginalSource -is [System.Windows.Controls.MenuItem] -and -not $e.OriginalSource.HasItems){$script:LiquidMenuWindow.Hide()}})

function Update-NativeLocale {
    foreach ($item in $PlaybackSpeedOptions.Items) {$item.Content=T 'nativeSpeed' @{rate=$item.Tag}}
    foreach ($item in $SleepOptions.Items) {if ($null -ne $item.Tag) {$item.Content=T 'nativeSleep' @{minutes=$item.Tag}}}
    $script:LiquidMenuWindow.Title=T 'contextTitle'
    Sync-MinimalMute
}
