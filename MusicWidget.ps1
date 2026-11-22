#Requires -Version 5.1
<#
.SYNOPSIS
    Lecteur musical Windows : Compact ou Minimaliste, interface Liquid Glass.
.DESCRIPTION
    Lecture locale, bibliothèque, playlists et import de morceaux.
    Ctrl+Shift+M : afficher ou masquer. Apparence : disposition, palette,
    vitesse de lecture, veille, épinglage et mouvement réduit.
.PARAMETER NoRun
    Initialise les contrôles sans lancer la boucle de fenêtre (tests isolés).
.PARAMETER DataDirectory
    Dossier alternatif pour les réglages, playlists, fichiers audio et outils.
#>

param([switch]$NoRun, [string]$DataDirectory = '')

$ErrorActionPreference = 'Stop'
$script:IsInitializing = $true
$script:SettingsDirty = $false
$script:LastSettingsWrite = [DateTime]::MinValue
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$LibDir = Join-Path $ScriptDir 'lib'
$AssetsDir = Join-Path $ScriptDir 'assets'
$dataRoot = if ([string]::IsNullOrWhiteSpace($DataDirectory)) { $ScriptDir } else { [IO.Path]::GetFullPath($DataDirectory) }
if (-not (Test-Path -LiteralPath $dataRoot)) { [void][IO.Directory]::CreateDirectory($dataRoot) }
$legacyMusicDir = Join-Path (Split-Path -Parent $ScriptDir) 'musique'
$script:MusicDir = if ([string]::IsNullOrWhiteSpace($DataDirectory) -and (Test-Path -LiteralPath $legacyMusicDir)) { $legacyMusicDir } else { Join-Path $dataRoot 'musique' }
$script:OnlineToolsDirectory = Join-Path $dataRoot 'bin'
# One instance; never terminate another player while it is saving settings.
if (-not $NoRun) {
    $createdNew = $false
    $script:Mutex = [System.Threading.Mutex]::new($true, 'Local\MusicWidget.Player', [ref]$createdNew)
    if (-not $createdNew) {
        try { $showSignal=[System.Threading.EventWaitHandle]::OpenExisting('Local\MusicWidget.Show'); [void]$showSignal.Set(); $showSignal.Dispose() } catch { }
        $script:Mutex.Dispose(); return
    }
    $script:ShowRequest=[System.Threading.EventWaitHandle]::new($false,[System.Threading.EventResetMode]::AutoReset,'Local\MusicWidget.Show')
}
# --- Load assemblies ---
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

. (Join-Path $LibDir 'I18n.ps1')
. (Join-Path $LibDir 'Shortcuts.ps1')
. (Join-Path $LibDir 'OnlineTools.ps1')

# --- Compile Windows CoreAudio WASAPI Peak Meter for Real-Time Sound Waves ---
$audioMeterCs = @"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
}

[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    int GetPeakValue(out float pfPeak);
    int GetMeteringChannelCount(out int pnChannelCount);
    int GetChannelsPeakValues(int u32ChannelCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] afPeakValues);
    int QueryHardwareSupport(out int pdwHardwareSupportMask);
}

public class CoreAudioMeter
{
    private IAudioMeterInformation _meter;

    public CoreAudioMeter()
    {
        try
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice defaultDevice;
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out defaultDevice);
            if (hr == 0 && defaultDevice != null)
            {
                Guid meterGuid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
                object obj;
                defaultDevice.Activate(ref meterGuid, 1, IntPtr.Zero, out obj);
                _meter = obj as IAudioMeterInformation;
            }
        }
        catch { }
    }

    public float GetPeak()
    {
        if (_meter == null) return 0f;
        try
        {
            float peak;
            _meter.GetPeakValue(out peak);
            return peak;
        }
        catch { return 0f; }
    }
}


"@
function Initialize-LegacyAudioTools {
    if (-not ('CoreAudioMeter' -as [type])) {
        Add-Type -TypeDefinition $script:audioMeterCs -ReferencedAssemblies "System.Net", "WindowsBase" -Language CSharp
    }
}

# The release includes these Win32 helpers precompiled for .NET Framework.
# Source checkouts can still run with Windows PowerShell alone.
$nativeAssembly=Join-Path $LibDir 'MusicWidget.Native.dll'
if ([IO.File]::Exists($nativeAssembly)) {
    try { Add-Type -Path $nativeAssembly } catch { }
}
if (-not ('NativeBackdrop' -as [type])) {
    $nativeSources=@('NativeBackdrop.cs','TrayMenu.cs','GlobalHotkeys.cs') | ForEach-Object {Join-Path $LibDir $_}
    Add-Type -Path $nativeSources -ReferencedAssemblies 'System.Windows.Forms','System.Drawing'
}
# --- Load XAML ---
$xamlContent = [System.IO.File]::ReadAllText((Join-Path $LibDir 'MainWindow.xaml'), [System.Text.Encoding]::UTF8)
$stringReader = New-Object System.IO.StringReader($xamlContent)
$xmlReader = [System.Xml.XmlReader]::Create($stringReader)
$window = [Windows.Markup.XamlReader]::Load($xmlReader)

# --- Set App Icon ---
$iconIcoPath = Join-Path $AssetsDir 'music_widget_glass.ico'
$iconPngPath = Join-Path $AssetsDir 'music_widget_glass.png'
if (-not (Test-Path $iconIcoPath)) { $iconIcoPath = Join-Path $AssetsDir 'music_player.ico' }
if (-not (Test-Path $iconPngPath)) { $iconPngPath = Join-Path $AssetsDir 'music_player.png' }
if (-not (Test-Path $iconIcoPath)) { $iconIcoPath = Join-Path $AssetsDir 'icon.ico' }
if (-not (Test-Path $iconPngPath)) { $iconPngPath = Join-Path $AssetsDir 'icon.png' }
if (Test-Path $iconPngPath) {
    try {
        $window.Icon = [System.Windows.Media.Imaging.BitmapFrame]::Create([Uri]::new($iconPngPath))
    } catch { }
}

# --- Find UI elements ---
$MainCard           = $window.FindName('MainCard')
$CardShadow         = $window.FindName('CardShadow')
$PnlWaveBox         = $window.FindName('PnlWaveBox')
$WaveBar1           = $window.FindName('WaveBar1')
$WaveBar2           = $window.FindName('WaveBar2')
$WaveBar3           = $window.FindName('WaveBar3')
$WaveBar4           = $window.FindName('WaveBar4')
$WaveBar5           = $window.FindName('WaveBar5')
$TxtTitle           = $window.FindName('TxtTitle')
$TitleContainer     = $window.FindName('TitleContainer')
$TitleCanvas        = $window.FindName('TitleCanvas')
$TitleTransform     = $window.FindName('TitleTransform')
$TxtSubtitle        = $window.FindName('TxtSubtitle')
$BtnTheme           = $window.FindName('BtnTheme')
$IconTheme          = $window.FindName('IconTheme')
$BtnPlaylist        = $window.FindName('BtnPlaylist')
$IconPlaylist       = $window.FindName('IconPlaylist')
$BtnHide            = $window.FindName('BtnHide')
$IconHide           = $window.FindName('IconHide')
$TxtCurrentTime     = $window.FindName('TxtCurrentTime')
$ProgressSlider     = $window.FindName('ProgressSlider')
$TxtTotalTime       = $window.FindName('TxtTotalTime')
$BtnShuffle         = $window.FindName('BtnShuffle')
$IconShuffle        = $window.FindName('IconShuffle')
$BtnPrev            = $window.FindName('BtnPrev')
$IconPrev           = $window.FindName('IconPrev')
$BtnPlay            = $window.FindName('BtnPlay')
$IconPlay           = $window.FindName('IconPlay')
$IconPause          = $window.FindName('IconPause')
$BtnNext            = $window.FindName('BtnNext')
$IconNext           = $window.FindName('IconNext')
$BtnRepeat          = $window.FindName('BtnRepeat')
$IconRepeat         = $window.FindName('IconRepeat')
$BtnMute            = $window.FindName('BtnMute')
$IconSpeaker        = $window.FindName('IconSpeaker')
$IconMuted          = $window.FindName('IconMuted')
$VolumeSlider       = $window.FindName('VolumeSlider')
$TxtVolumePercent   = $window.FindName('TxtVolumePercent')
$PlaylistDrawer     = $window.FindName('PlaylistDrawer')
$DrawerDivider      = $window.FindName('DrawerDivider')
$TxtPlaylistCount   = $window.FindName('TxtPlaylistCount')
$BtnAddMusic        = $window.FindName('BtnAddMusic')
$IconAddMusic       = $window.FindName('IconAddMusic')
$TrackListContainer = $window.FindName('TrackListContainer')
$ResizeGripThumb    = $window.FindName('ResizeGripThumb')
$RightResizeThumb   = $window.FindName('RightResizeThumb')
$BottomResizeThumb  = $window.FindName('BottomResizeThumb')

# Delete Modal elements
$ModalOverlay       = $window.FindName('ModalOverlay')
$ModalBackdrop      = $window.FindName('ModalBackdrop')
$ModalCard          = $window.FindName('ModalCard')
$ModalScale         = $window.FindName('ModalScale')
$ModalTrackName     = $window.FindName('ModalTrackName')
$BtnModalCancel     = $window.FindName('BtnModalCancel')
$BtnModalDelete     = $window.FindName('BtnModalDelete')

# Upload Modal elements (Local vs Smart URL)
$UploadOverlay       = $window.FindName('UploadOverlay')
$UploadModalBackdrop = $window.FindName('UploadModalBackdrop')
$UploadModalCard     = $window.FindName('UploadModalCard')
$BtnUploadBack       = $window.FindName('BtnUploadBack')
$TxtUploadHeader     = $window.FindName('TxtUploadHeader')
$BtnUploadClose      = $window.FindName('BtnUploadClose')
$PnlUploadStep1      = $window.FindName('PnlUploadStep1')
$BtnChoiceLocal      = $window.FindName('BtnChoiceLocal')
$BtnChoiceUrl        = $window.FindName('BtnChoiceUrl')
$PnlUploadStepUrl    = $window.FindName('PnlUploadStepUrl')
$PnlPlatformBadge    = $window.FindName('PnlPlatformBadge')
$IconBadgeNeutral    = $window.FindName('IconBadgeNeutral')
$IconBadgeYoutube    = $window.FindName('IconBadgeYoutube')
$IconBadgeSpotify    = $window.FindName('IconBadgeSpotify')
$TxtPlatformBadge    = $window.FindName('TxtPlatformBadge')
$TxtUrlInput         = $window.FindName('TxtUrlInput')
$BtnPasteUrl         = $window.FindName('BtnPasteUrl')
$BtnStartDownload    = $window.FindName('BtnStartDownload')
$PnlUploadProgress   = $window.FindName('PnlUploadProgress')
$UploadProgressBar   = $window.FindName('UploadProgressBar')
$TxtUploadStatus     = $window.FindName('TxtUploadStatus')
$TxtUploadError      = $window.FindName('TxtUploadError')

# Playlist & Context Menu elements
$BtnNewPlaylist           = $window.FindName('BtnNewPlaylist')
$IconNewPlaylist          = $window.FindName('IconNewPlaylist')
$PlaylistShelfScrollViewer = $window.FindName('PlaylistShelfScrollViewer')
$PlaylistShelfContainer   = $window.FindName('PlaylistShelfContainer')
$CreatePlaylistOverlay    = $window.FindName('CreatePlaylistOverlay')
$CreatePlaylistBackdrop   = $window.FindName('CreatePlaylistBackdrop')
$CreatePlaylistCard       = $window.FindName('CreatePlaylistCard')
$BtnCreatePlaylistClose   = $window.FindName('BtnCreatePlaylistClose')
$TxtPlaylistNameInput     = $window.FindName('TxtPlaylistNameInput')
$BtnCreatePlaylistConfirm = $window.FindName('BtnCreatePlaylistConfirm')
$ContextMenuOverlay       = $window.FindName('ContextMenuOverlay')
$ContextMenuBackdrop      = $window.FindName('ContextMenuBackdrop')
$ContextMenuCard          = $window.FindName('ContextMenuCard')
$TxtContextMenuTrackTitle = $window.FindName('TxtContextMenuTrackTitle')
$BtnMenuAddToPlaylist     = $window.FindName('BtnMenuAddToPlaylist')
$IconMenuChevron          = $window.FindName('IconMenuChevron')
$PnlMenuPlaylistsSublist  = $window.FindName('PnlMenuPlaylistsSublist')
$BtnMenuDeleteTrack       = $window.FindName('BtnMenuDeleteTrack')

# --- Audio Engine State ---
$script:Player = New-Object System.Windows.Media.MediaPlayer
$script:Playlist = @()
$script:CurrentIndex = -1
$script:IsPlaying = $false
$script:IsShuffle = $false
$script:RepeatMode = 0 # 0 = Repeat All, 1 = Repeat One, 2 = Off
$script:IsDrawerOpen = $false
$script:IsSeeking = $false
$script:LocalVolume = 0.20 # 20% par défaut
$script:LastAudibleVolume = $script:LocalVolume
$script:IsMuted = $false
$script:WavePhase = 0.0
$script:IsUpdatingVolSlider = $false

# Modal & Playlist state
$script:PendingDeleteIndex = -1
$script:IsModalOpen = $false
$script:IsUploadModalOpen = $false
$script:IsCreatePlaylistModalOpen = $false
$script:IsContextMenuOpen = $false
$script:ContextMenuTrackIndex = -1
$script:IsClosingTrack = $false  # Guards MediaEnded during track deletion

# Playlist & Settings Storage State
$script:PlaylistsFile = Join-Path $dataRoot 'playlists.json'
$script:SettingsFile  = Join-Path $dataRoot 'settings.json'
$script:CustomPlaylists = @()
$script:ActivePlaylist = "ALL"
$script:PlaybackPlaylist = ''
$script:SaveSettingsCounter = 0

# Liquid wave smoothing heights & real-time audio meter
$script:AudioMeter = $null
$script:WaveH1 = 3.5
$script:WaveH2 = 3.5
$script:WaveH3 = 3.5
$script:WaveH4 = 3.5
$script:WaveH5 = 3.5

# Drag-and-drop playlist reorder state
$script:DragState = [PSCustomObject]@{
    Active           = $false
    SourceIndex      = -1
    StartPoint       = $null
    CurrentTargetIdx = -1
}
$script:BeatTimer = 0
$script:BeatBoost = 0.0

# User-controlled responsive dimensions
$script:UserWidth = $window.Width
$script:UserCompactHeight = 196.0
$script:UserExpandedHeight = 386.0

# --- Title Marquee Text Scrolling Animation ---
function Stop-TitleMarqueeAnimation {
    if ($null -ne $TitleTransform) {
        $TitleTransform.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $null)
        $TitleTransform.X = 0
    }
}

function Start-TitleMarqueeAnimation {
    Stop-TitleMarqueeAnimation
    if ($MainCard.Visibility -ne [System.Windows.Visibility]::Visible) { return }
    if ([string]::IsNullOrEmpty($TxtTitle.Text)) { return }
    if ($script:ReducedMotion -or -not $window.IsVisible -or -not [System.Windows.SystemParameters]::ClientAreaAnimation) { return }

    $window.Dispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Loaded, [Action]{
        try {
            $typeFace = New-Object System.Windows.Media.Typeface(
                $TxtTitle.FontFamily,
                $TxtTitle.FontStyle,
                $TxtTitle.FontWeight,
                $TxtTitle.FontStretch
            )
            $pixelsPerDip = 1.0
            try { $pixelsPerDip = [System.Windows.Media.VisualTreeHelper]::GetDpi($window).PixelsPerDip } catch { }

            $formattedText = New-Object System.Windows.Media.FormattedText(
                $TxtTitle.Text,
                [System.Globalization.CultureInfo]::CurrentCulture,
                [System.Windows.FlowDirection]::LeftToRight,
                $typeFace,
                $TxtTitle.FontSize,
                [System.Windows.Media.Brushes]::White,
                $pixelsPerDip
            )
            $textW = $formattedText.Width
            $containerW = $TitleContainer.ActualWidth
            if ($containerW -le 0) {
                $containerW = [math]::Max(120.0, $window.Width - 180.0)
            }

            if ($textW -gt $containerW) {
                $overflow = $textW - $containerW + 22.0
                $speed = 28.0 # px per second
                $durSec = [math]::Max(3.0, $overflow / $speed)

                $anim = New-Object System.Windows.Media.Animation.DoubleAnimation
                $anim.From = 0.0
                $anim.To = -$overflow
                $anim.Duration = [System.Windows.Duration]::new([TimeSpan]::FromSeconds($durSec))
                $anim.BeginTime = [TimeSpan]::FromSeconds(1.5)
                $anim.AutoReverse = $true
                $anim.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever

                $TitleTransform.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $anim)
            } else {
                $TitleTransform.X = 0
            }
        } catch { }
    }) | Out-Null
}

# --- Settings Persistence (settings.json) across PC restarts ---
function Save-AppSettings {
    param([switch]$Flush)
    if ($script:IsInitializing) { return }
    $script:SettingsDirty = $true
    if (-not $Flush) { return }
    try {
        $currentTrackFile = [IO.Path]::GetFileName($script:LoadedTrackPath)

        $currentSec = if ($script:PendingRestorePositionSeconds -gt 0) { $script:PendingRestorePositionSeconds } else { 0.0 }
        try {
            if ($script:MediaIsReady -and $script:Player.NaturalDuration.HasTimeSpan) {
                $currentSec = $script:Player.Position.TotalSeconds
            }
        } catch { }

        $settings = [PSCustomObject]@{
            Left                        = [math]::Round($window.Left, 1)
            Top                         = [math]::Round($window.Top, 1)
            Width                       = [math]::Round($window.Width, 1)
            Height                      = [math]::Round($window.Height, 1)
            UserCompactHeight           = [math]::Round($script:UserCompactHeight, 1)
            UserExpandedHeight          = [math]::Round($script:UserExpandedHeight, 1)
            IsDrawerOpen                = $script:IsDrawerOpen
            Language                    = $script:LanguagePreference
            Shortcuts                   = $script:Shortcuts
            LocalVolume                 = [math]::Round($script:LocalVolume, 3)
            IsMuted                     = $script:IsMuted
            ThemeIndex                  = $script:CurrentThemeIdx
            DesignMode                  = $script:DesignMode
            AppearanceVersion           = 4
            ThemeName                   = $script:Themes[$script:CurrentThemeIdx].Name
            CompactWidth                = $script:CompactWidth
            SizeScale                   = $script:SizeScale
            AuraColor                   = $script:AuraColor
            AuraMode                    = $script:AuraMode
            CompactLeft                 = $script:CompactLeft
            CompactTop                  = $script:CompactTop
            CompactScreen               = $script:CompactScreen
            MinimalPosition             = $script:MinimalPosition
            MinimalScreen               = $script:MinimalScreen
            ReducedMotion               = $script:ReducedMotion
            AlwaysOnTop                 = $script:AlwaysOnTop
            PlaybackSpeed               = $script:PlaybackSpeed
            IsShuffle                   = $script:IsShuffle
            RepeatMode                  = $script:RepeatMode
            ActivePlaylist              = $script:ActivePlaylist
            PlaybackPlaylist            = $script:PlaybackPlaylist
            CurrentTrackName            = $currentTrackFile
            CurrentTrackPositionSeconds = [math]::Round($currentSec, 1)
        }

        $json = $settings | ConvertTo-Json -Depth 5
        $temporaryFile = $script:SettingsFile + '.tmp'
        [System.IO.File]::WriteAllText($temporaryFile, $json, [System.Text.Encoding]::UTF8)
        if (Test-Path -LiteralPath $script:SettingsFile) { [IO.File]::Replace($temporaryFile,$script:SettingsFile,[System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporaryFile,$script:SettingsFile) }
        $script:SettingsDirty = $false
        $script:LastSettingsWrite = [DateTime]::Now
    } catch { }
}

function Load-AppSettings {
    if (-not (Test-Path $script:SettingsFile)) { return $null }
    try {
        $raw = Get-Content -Path $script:SettingsFile -Raw -Encoding UTF8
        $cfg = $raw | ConvertFrom-Json
        if ($null -eq $cfg) { return $null }

        if ($null -ne $cfg.Left -and $null -ne $cfg.Top) {
            $window.Left = [double]$cfg.Left
            $window.Top  = [double]$cfg.Top
        }
        if ($null -ne $cfg.Width -and [double]$cfg.Width -ge 270.0) {
            $window.Width = [math]::Max(320,[math]::Min(650,[double]$cfg.Width))
            $script:UserWidth = [double]$cfg.Width
        }
        if ($null -ne $cfg.UserCompactHeight) {
            $script:UserCompactHeight = [double]$cfg.UserCompactHeight
        }
        if ($null -ne $cfg.UserExpandedHeight) {
            $script:UserExpandedHeight = [double]$cfg.UserExpandedHeight
        }
        if ($null -ne $cfg.LocalVolume) {
            $script:LocalVolume = [math]::Max(0.0,[math]::Min(1.0,[double]$cfg.LocalVolume))
        }
        if ($null -ne $cfg.IsMuted) {
            $script:IsMuted = [bool]$cfg.IsMuted
        }
        if ($null -ne $cfg.ThemeIndex) {
            $script:CurrentThemeIdx = [int]$cfg.ThemeIndex
        }
        if ($null -ne $cfg.IsShuffle) {
            $script:IsShuffle = [bool]$cfg.IsShuffle
        }
        if ($null -ne $cfg.RepeatMode) {
            $script:RepeatMode = [math]::Max(0,[math]::Min(2,[int]$cfg.RepeatMode))
        }
        if ($null -ne $cfg.ActivePlaylist -and -not [string]::IsNullOrEmpty($cfg.ActivePlaylist)) {
            $script:ActivePlaylist = [string]$cfg.ActivePlaylist
        }
        $script:PlaybackPlaylist = if ($cfg.PlaybackPlaylist) {[string]$cfg.PlaybackPlaylist} else {$script:ActivePlaylist}
        if ($null -ne $cfg.IsDrawerOpen) {
            $script:IsDrawerOpen = [bool]$cfg.IsDrawerOpen
        }
        return $cfg
    } catch { return $null }
}

$script:CurrentThemeIdx = 0
$bc = [System.Windows.Media.BrushConverter]::new()

# --- Custom Playlists Storage (playlists.json) ---
function Load-CustomPlaylists {
    if (Test-Path $script:PlaylistsFile) {
        try {
            $raw = Get-Content -Path $script:PlaylistsFile -Raw -Encoding UTF8
            $data = $raw | ConvertFrom-Json
            if ($null -ne $data) {
                # Ensure each playlist has a valid array for tracks
                $list = @()
                foreach ($p in $data) {
                    $tArr = @()
                    if ($null -ne $p.tracks) { $tArr = @($p.tracks) }
                    $list += [PSCustomObject]@{ name = [string]$p.name; tracks = [System.Collections.ArrayList]::new($tArr) }
                }
                $script:CustomPlaylists = $list
            } else {
                $script:CustomPlaylists = @()
            }
        } catch {
            $script:CustomPlaylists = @()
        }
    } else {
        $script:CustomPlaylists = @()
    }
}

function Save-CustomPlaylists {
    param([switch]$ThrowOnError)
    try {
        $saveData = @()
        foreach ($p in $script:CustomPlaylists) {
            $saveData += [PSCustomObject]@{
                name   = $p.name
                tracks = @($p.tracks)
            }
        }
        $json = ConvertTo-Json -InputObject @($saveData) -Depth 5
        $temporaryFile = $script:PlaylistsFile + '.tmp'
        [System.IO.File]::WriteAllText($temporaryFile, $json, [System.Text.Encoding]::UTF8)
        if (Test-Path -LiteralPath $script:PlaylistsFile) { [IO.File]::Replace($temporaryFile,$script:PlaylistsFile,[System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporaryFile,$script:PlaylistsFile) }
    } catch { if ($ThrowOnError) { throw } }
}

# --- Horizontal Playlist Shelf Rendering ---
function Render-PlaylistShelf {
    if ($script:GlassWebActive -or ($script:IsInitializing -and -not $NoRun -and $script:CurrentThemeIdx -eq 1)) {return}
    $PlaylistShelfContainer.Children.Clear()
    $supportedExts = @('.mp3', '.wav', '.m4a', '.flac', '.aac', '.wma', '.ogg')
    $allFiles = @($script:LibraryFiles)
    $fileNames = @{}
    foreach ($file in $allFiles) { $fileNames[$file.Name] = $true }

    # Pill 1: "🎵 Tous" (Global folder)
    $pillAll = New-Object System.Windows.Controls.Border
    $pillAll.CornerRadius = [System.Windows.CornerRadius]::new(11)
    $pillAll.Height = 24
    $pillAll.Padding = [System.Windows.Thickness]::new(9, 0, 9, 0)
    $pillAll.Margin = [System.Windows.Thickness]::new(0, 0, 6, 0)
    $pillAll.Cursor = [System.Windows.Input.Cursors]::Hand

    $isAllActive = ($script:ActivePlaylist -eq "ALL")
    $pillAll.Background = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].WaveBoxBg)
    $pillAll.BorderBrush = if ($isAllActive) { $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].Accent) } else { $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].CardBorder) }
    $pillAll.BorderThickness = [System.Windows.Thickness]::new(1)

    $tbAll = New-Object System.Windows.Controls.TextBlock
    $tbAll.Text = (T 'all') + " ($($allFiles.Count))"
    $tbAll.FontFamily = [System.Windows.Media.FontFamily]::new("Segoe UI Variable Text, Segoe UI")
    $tbAll.FontSize = 10.5
    $tbAll.FontWeight = if ($isAllActive) { [System.Windows.FontWeights]::SemiBold } else { [System.Windows.FontWeights]::Normal }
    $tbAll.Foreground = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].TxtTitle)
    $tbAll.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
    $pillAll.Child = $tbAll

    $pillAll.Add_MouseLeftButtonDown({
        param($s, $e)
        $e.Handled = $true
        Select-Playlist -Id "ALL"
    })
    [void]$PlaylistShelfContainer.Children.Add($pillAll)

    # Custom Playlists Pills
    foreach ($p in $script:CustomPlaylists) {
        $pName = $p.name
        $matchCount = 0
        foreach ($tName in $p.tracks) {
            if ($fileNames.ContainsKey($tName)) { $matchCount++ }
        }

        $pill = New-Object System.Windows.Controls.Border
        $pill.CornerRadius = [System.Windows.CornerRadius]::new(11)
        $pill.Height = 24
        $pill.Padding = [System.Windows.Thickness]::new(9, 0, 7, 0)
        $pill.Margin = [System.Windows.Thickness]::new(0, 0, 6, 0)
        $pill.Cursor = [System.Windows.Input.Cursors]::Hand

        $isActive = ($script:ActivePlaylist -eq $pName)
        $pill.Background = if ($isActive) { $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].WaveBoxBg) } else { $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].ControlsBg) }
        $pill.BorderBrush = if ($isActive) { $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].Accent) } else { $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].CardBorder) }
        $pill.BorderThickness = [System.Windows.Thickness]::new(1)

        $gridPill = New-Object System.Windows.Controls.Grid
        $colT = New-Object System.Windows.Controls.ColumnDefinition; $colT.Width = [System.Windows.GridLength]::Auto
        $colD = New-Object System.Windows.Controls.ColumnDefinition; $colD.Width = [System.Windows.GridLength]::Auto
        [void]$gridPill.ColumnDefinitions.Add($colT)
        [void]$gridPill.ColumnDefinitions.Add($colD)

        $tb = New-Object System.Windows.Controls.TextBlock
        $tb.Text = "$pName ($matchCount)"
        $tb.FontFamily = [System.Windows.Media.FontFamily]::new("Segoe UI Variable Text, Segoe UI")
        $tb.FontSize = 10.5
        $tb.FontWeight = if ($isActive) { [System.Windows.FontWeights]::SemiBold } else { [System.Windows.FontWeights]::Normal }
        $tb.Foreground = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].TxtTitle)
        $tb.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
        [System.Windows.Controls.Grid]::SetColumn($tb, 0)
        [void]$gridPill.Children.Add($tb)

        # Small delete (x) button for the custom playlist
        $btnDelPl = New-Object System.Windows.Controls.Button
        $btnDelPl.Style = $window.FindResource('MiniIconBtn')
        $btnDelPl.Width = 14
        $btnDelPl.Height = 14
        $btnDelPl.Margin = [System.Windows.Thickness]::new(6, 0, 0, 0)
        $btnDelPl.ToolTip = T 'deletePlaylist'
        $btnDelPl.Tag = $pName

        $pathX = New-Object System.Windows.Shapes.Path
        $pathX.Data = [System.Windows.Media.Geometry]::Parse("M 4,4 L 16,16 M 16,4 L 4,16")
        $pathX.Stroke = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].TxtSubtitle)
        $pathX.StrokeThickness = 1.6
        $pathX.Width = 7
        $pathX.Height = 7
        $pathX.Stretch = [System.Windows.Media.Stretch]::Uniform
        $btnDelPl.Content = $pathX

        $btnDelPl.Add_Click({
            param($s, $e)
            $e.Handled = $true
            Delete-CustomPlaylist -Name ([string]$s.Tag)
        })

        [System.Windows.Controls.Grid]::SetColumn($btnDelPl, 1)
        [void]$gridPill.Children.Add($btnDelPl)

        $pill.Child = $gridPill
        $pill.Tag = $pName

        $pill.Add_MouseLeftButtonDown({
            param($s, $e)
            $e.Handled = $true
            Select-Playlist -Id ([string]$s.Tag)
        })

        [void]$PlaylistShelfContainer.Children.Add($pill)
    }
}

function Select-Playlist {
    param([string]$Id)
    if ($Id -eq $script:ActivePlaylist) { return }
    $script:ActivePlaylist = $Id
    Reload-Playlist -Browsing
    Save-AppSettings
}

function Delete-CustomPlaylist {
    param([string]$Name)
    $script:CustomPlaylists = @($script:CustomPlaylists | Where-Object { $_.name -ne $Name })
    Save-CustomPlaylists
    if ($script:ActivePlaylist -eq $Name) {
        $script:ActivePlaylist = "ALL"
    }
    Reload-Playlist
    Save-AppSettings
}

function Get-PlaylistTracks([string]$Id) {
    if ($Id -eq 'ALL') { return ,$script:LibraryFiles }
    $playlist = $script:CustomPlaylists | Where-Object { $_.name -eq $Id } | Select-Object -First 1
    if ($null -eq $playlist) { return ,$script:LibraryFiles }
    $byName = @{}
    foreach ($file in $script:LibraryFiles) { $byName[$file.Name] = $file }
    return ,@(foreach ($name in $playlist.tracks) {
        if ($byName.ContainsKey([string]$name)) { $byName[[string]$name] }
    })
}

function Get-PlaybackQueue {
    if (-not $script:PlaybackPlaylist -or $script:PlaybackPlaylist -eq $script:ActivePlaylist) { return ,$script:Playlist }
    Get-PlaylistTracks $script:PlaybackPlaylist
}

function Find-TrackIndex($Tracks, [string]$Path) {
    if ($Path) {
        for ($i = 0; $i -lt $Tracks.Count; $i++) {
            if ($Tracks[$i].FullName -eq $Path) { return $i }
        }
    }
    return -1
}

function Update-PlayingTrackSubtitle {
    if (-not $script:LoadedTrackPath) { return }
    $queue = Get-PlaybackQueue
    $index = Find-TrackIndex $queue $script:LoadedTrackPath
    if ($index -ge 0) {
        Set-WidgetSubtitle 'trackLabel' @{index=$index+1;count=$queue.Count}
    } else {
        Set-WidgetSubtitle $(if ($script:IsPlaying) {'playingStatus'} else {'pausedStatus'})
    }
}

# --- Scan and reload playlist tracks ---
function Reload-Playlist {
    param([switch]$Browsing)
    if (-not (Test-Path $script:MusicDir)) {
        New-Item -ItemType Directory -Path $script:MusicDir -Force | Out-Null
    }
    $supportedExts = @('.mp3', '.wav', '.m4a', '.flac', '.aac', '.wma', '.ogg')
    $script:LibraryFiles = @(Get-ChildItem -LiteralPath $script:MusicDir -File -ErrorAction SilentlyContinue | Where-Object { $supportedExts -contains $_.Extension.ToLower() })
    if ($script:PlaybackPlaylist -and $script:PlaybackPlaylist -ne 'ALL' -and $script:PlaybackPlaylist -notin $script:CustomPlaylists.name) {
        $script:PlaybackPlaylist = 'ALL'
    }
    if ($script:ActivePlaylist -ne 'ALL' -and $script:ActivePlaylist -notin $script:CustomPlaylists.name) {
        $script:ActivePlaylist = 'ALL'
    }
    $script:Playlist = Get-PlaylistTracks $script:ActivePlaylist
    $script:CurrentIndex = Find-TrackIndex $script:Playlist $script:LoadedTrackPath
    if ($script:LoadedTrackPath) {
        if ((Find-TrackIndex $script:LibraryFiles $script:LoadedTrackPath) -lt 0) {
            Reset-LoadedTrack
        } elseif ($script:SubtitleKey -ne 'audioUnreadable') {
            Update-PlayingTrackSubtitle
        }
    }
    if (-not $Browsing) {
        $script:ShuffleBag = @()
        $script:ShuffleBagInitialized = $false
    }
    Render-PlaylistUI
    Render-PlaylistShelf
}

# --- Render playlist items from in-memory $script:Playlist (no disk scan) ---
function Render-PlaylistUI {
    if ($script:GlassWebActive -or ($script:IsInitializing -and -not $NoRun -and $script:CurrentThemeIdx -eq 1)) {return}
    $TxtPlaylistCount.Text = T $(if ($script:Playlist.Count -eq 1) {'trackOne'} else {'trackOther'}) @{count=$script:Playlist.Count}

    $TrackListContainer.Children.Clear()

    for ($i = 0; $i -lt $script:Playlist.Count; $i++) {
        $file = $script:Playlist[$i]
        $trackIdx = $i
        
        $itemBorder = New-Object System.Windows.Controls.Border
        $itemBorder.Height = 32
        $itemBorder.CornerRadius = [System.Windows.CornerRadius]::new(8)
        $itemBorder.Background = [System.Windows.Media.Brushes]::Transparent
        $itemBorder.Margin = [System.Windows.Thickness]::new(0, 1.5, 0, 1.5)
        $itemBorder.Cursor = [System.Windows.Input.Cursors]::Hand
        $itemBorder.Padding = [System.Windows.Thickness]::new(8, 0, 8, 0)
        $itemBorder.Tag = $trackIdx

        $grid = New-Object System.Windows.Controls.Grid
        $col0 = New-Object System.Windows.Controls.ColumnDefinition; $col0.Width = [System.Windows.GridLength]::new(22)
        $col1 = New-Object System.Windows.Controls.ColumnDefinition; $col1.Width = [System.Windows.GridLength]::new(1, [System.Windows.GridUnitType]::Star)
        [void]$grid.ColumnDefinitions.Add($col0)
        [void]$grid.ColumnDefinitions.Add($col1)

        # Container for Left Number / Vector Play Icon
        $iconGrid = New-Object System.Windows.Controls.Grid
        $iconGrid.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
        $iconGrid.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Left
        [System.Windows.Controls.Grid]::SetColumn($iconGrid, 0)

        # Track Number Text
        $tbNum = New-Object System.Windows.Controls.TextBlock
        $tbNum.Text = "$($trackIdx + 1)"
        $tbNum.FontFamily = [System.Windows.Media.FontFamily]::new("Segoe UI Variable Text, Segoe UI")
        $tbNum.FontSize = 10.5
        $tbNum.Foreground = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].TxtSubtitle)
        $tbNum.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
        $tbNum.Tag = "tbNum"
        [void]$iconGrid.Children.Add($tbNum)

        # Crisp Vector Icon for Active Playing Track
        $activeIcon = New-Object System.Windows.Shapes.Path
        $activeIcon.Data = [System.Windows.Media.Geometry]::Parse("M 3,2 L 11,7 L 3,12 Z")
        $activeIcon.Fill = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].Accent)
        $activeIcon.Width = 10
        $activeIcon.Height = 10
        $activeIcon.Stretch = [System.Windows.Media.Stretch]::Uniform
        $activeIcon.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
        $activeIcon.Visibility = [System.Windows.Visibility]::Collapsed
        $activeIcon.Tag = "activeIcon"
        [void]$iconGrid.Children.Add($activeIcon)

        [void]$grid.Children.Add($iconGrid)

        # Track Name
        $tbName = New-Object System.Windows.Controls.TextBlock
        $cleanName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $tbName.Text = $cleanName
        $tbName.FontFamily = [System.Windows.Media.FontFamily]::new("Segoe UI Variable Text, Segoe UI")
        $tbName.FontSize = 11.5
        $tbName.Foreground = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].TxtTitle)
        $tbName.TextTrimming = [System.Windows.TextTrimming]::CharacterEllipsis
        $tbName.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
        $tbName.Margin = [System.Windows.Thickness]::new(4, 0, 4, 0)
        $tbName.Tag = "tbName"
        [System.Windows.Controls.Grid]::SetColumn($tbName, 1)
        [void]$grid.Children.Add($tbName)

        $itemBorder.Child = $grid

        # Hover effects based on current theme
        $itemBorder.Add_MouseEnter({
            param($s, $e)
            if ($s.Tag -ne $script:CurrentIndex) {
                $t = $script:Themes[$script:CurrentThemeIdx]
                $s.Background = $bc.ConvertFromString($t.ItemHover)
            }
        })
        $itemBorder.Add_MouseLeave({
            param($s, $e)
            if ($s.Tag -ne $script:CurrentIndex) {
                $s.Background = [System.Windows.Media.Brushes]::Transparent
            }
        })

        # Left-click: record potential drag start
        $itemBorder.Add_MouseLeftButtonDown({
            param($s, $e)
            if ($script:IsModalOpen) { return }
            $script:DragState.SourceIndex = [int]$s.Tag
            $script:DragState.StartPoint  = $e.GetPosition($TrackListContainer)
            $script:DragState.Active      = $false
        })

        # Left-click UP on item: play if it was NOT a drag
        $itemBorder.Add_MouseLeftButtonUp({
            param($s, $e)
            if ($script:IsModalOpen) { return }
            if (-not $script:DragState.Active -and $script:DragState.SourceIndex -eq [int]$s.Tag) {
                Play-Track -Index ([int]$s.Tag)
            }
            # Reset drag source so window-level MouseLeftButtonUp doesn't re-fire
            if (-not $script:DragState.Active) {
                $script:DragState.SourceIndex = -1
            }
        })

        # Right-click: show context menu (Delete OR Add to Playlist)
        $itemBorder.Add_MouseRightButtonDown({
            param($s, $e)
            $e.Handled = $true
            Show-TrackContextMenu -TrackIndex ([int]$s.Tag)
        })

        [void]$TrackListContainer.Children.Add($itemBorder)
    }

    Update-PlaylistSelectionUI

    # The container survives re-renders: register these delegates exactly once.
    if ($script:PlaylistDragHandlersRegistered) { return }
    $script:PlaylistDragHandlersRegistered = $true
    # --- Drag-and-Drop handlers on the container (handles all children) ---
    $TrackListContainer.Add_PreviewMouseMove({
        param($s, $e)
        if ($script:IsModalOpen -or $script:DragState.SourceIndex -lt 0) { return }
        if ($e.LeftButton -ne [System.Windows.Input.MouseButtonState]::Pressed) {
            # Button released outside - reset
            if ($script:DragState.Active) { Reset-DragState }
            return
        }

        $pt = $e.GetPosition($TrackListContainer)
        if (-not $script:DragState.Active) {
            $dx = $pt.X - $script:DragState.StartPoint.X
            $dy = $pt.Y - $script:DragState.StartPoint.Y
            if ([math]::Sqrt($dx*$dx + $dy*$dy) -gt 7) {
                $script:DragState.Active = $true
                # Dim the dragged item
                $src = $script:DragState.SourceIndex
                if ($src -ge 0 -and $src -lt $s.Children.Count) {
                    $s.Children[$src].Opacity = 0.40
                }
            }
        }

        if ($script:DragState.Active) {
            $itemH = 35.0
            $newTarget = [math]::Max(0, [math]::Min($s.Children.Count, [int][math]::Floor(($pt.Y + $itemH / 2) / $itemH)))
            if ($newTarget -ne $script:DragState.CurrentTargetIdx) {
                # Clear previous highlight
                if ($script:DragState.CurrentTargetIdx -ge 0 -and $s.Children.Count -gt 0) {
                    $prev = $s.Children[[math]::Min($script:DragState.CurrentTargetIdx,$s.Children.Count-1)]
                    $prev.BorderThickness = [System.Windows.Thickness]::new(0)
                    $prev.BorderBrush = [System.Windows.Media.Brushes]::Transparent
                }
                $script:DragState.CurrentTargetIdx = $newTarget
                if ($newTarget -ne $script:DragState.SourceIndex) {
                    $tgt = $s.Children[[math]::Min($newTarget,$s.Children.Count-1)]
                    $tgt.BorderBrush = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].Accent)
                    $tgt.BorderThickness = if ($newTarget -eq $s.Children.Count) { [System.Windows.Thickness]::new(0,0,0,2) } else { [System.Windows.Thickness]::new(0,2,0,0) }
                }
            }
            $e.Handled = $true
        }
    })

    $TrackListContainer.Add_PreviewMouseLeftButtonUp({
        param($s, $e)
        if (-not $script:DragState.Active) { return }
        $src = $script:DragState.SourceIndex
        $tgt = $script:DragState.CurrentTargetIdx
        Reset-DragState
        if ($src -ge 0 -and $tgt -ge 0 -and $src -ne $tgt) {
            Reorder-Playlist -FromIndex $src -ToIndex $tgt
        }
        $e.Handled = $true
    })
}

function Reset-DragState {
    # Restore opacity and borders on all items
    for ($i = 0; $i -lt $TrackListContainer.Children.Count; $i++) {
        $c = $TrackListContainer.Children[$i]
        $c.Opacity = 1.0
        $c.BorderThickness = [System.Windows.Thickness]::new(0)
        $c.BorderBrush = [System.Windows.Media.Brushes]::Transparent
    }
    $script:DragState.Active           = $false
    $script:DragState.SourceIndex      = -1
    $script:DragState.CurrentTargetIdx = -1
    $script:DragState.StartPoint       = $null
}

function Reorder-Playlist {
    param([int]$FromIndex, [int]$ToIndex)
    if ($FromIndex -lt 0 -or $ToIndex -lt 0 -or $FromIndex -ge $script:Playlist.Count -or $ToIndex -gt $script:Playlist.Count) { return }
    if ($FromIndex -eq $ToIndex) { return }

    # Reorder in-memory array
    $asList = [System.Collections.ArrayList]::new(@($script:Playlist))
    $item = $asList[$FromIndex]
    $asList.RemoveAt($FromIndex)
    $insertAt = if ($ToIndex -gt $FromIndex) { $ToIndex - 1 } else { $ToIndex }
    if ($insertAt -eq $FromIndex) { return }
    $asList.Insert($insertAt, $item)
    $script:Playlist = @($asList)

    # Adjust current playing index
    if ($script:CurrentIndex -eq $FromIndex) {
        $script:CurrentIndex = $insertAt
    } elseif ($FromIndex -lt $script:CurrentIndex -and $insertAt -ge $script:CurrentIndex) {
        $script:CurrentIndex--
    } elseif ($FromIndex -gt $script:CurrentIndex -and $insertAt -le $script:CurrentIndex) {
        $script:CurrentIndex++
    }

    # If inside a custom playlist, persist the new track order in JSON!
    if ($script:ActivePlaylist -ne 'ALL') {
        $pl = $script:CustomPlaylists | Where-Object { $_.name -eq $script:ActivePlaylist } | Select-Object -First 1
        if ($null -ne $pl) {
            # Replace the visible slots in order and retain entries for temporarily missing files.
            $visibleNames = @{}
            foreach ($file in $script:Playlist) { $visibleNames[$file.Name] = $true }
            $orderedNames = @($script:Playlist | ForEach-Object { $_.Name })
            $next = 0
            for ($i=0; $i -lt $pl.tracks.Count; $i++) {
                if ($visibleNames.ContainsKey([string]$pl.tracks[$i])) {
                    $pl.tracks[$i] = $orderedNames[$next]
                    $next++
                }
            }
            Save-CustomPlaylists
        }
    }

    if ($script:ActivePlaylist -eq $script:PlaybackPlaylist) { Update-PlayingTrackSubtitle }
    Save-AppSettings
    # Re-render only the UI (no file scan needed)
    Render-PlaylistUI
}

# --- Delete Modal Functions ---
function Show-DeleteModal {
    param([int]$TrackIndex)
    if ($TrackIndex -lt 0 -or $TrackIndex -ge $script:Playlist.Count) { return }

    $script:PendingDeleteIndex = $TrackIndex
    $script:IsModalOpen = $true

    $track = $script:Playlist[$TrackIndex]
    $ModalTrackName.Text = [System.IO.Path]::GetFileNameWithoutExtension($track.Name)

    # Show overlay instantly (clean & crisp)
    $ModalOverlay.Opacity = 1.0
    $ModalOverlay.Visibility = [System.Windows.Visibility]::Visible
}

function Hide-DeleteModal {
    $script:IsModalOpen = $false
    $script:PendingDeleteIndex = -1
    # Hide instantly - 100% synchronous, no delegate crash
    $ModalOverlay.Visibility = [System.Windows.Visibility]::Collapsed
}

function Confirm-DeleteTrack {
    $idx = $script:PendingDeleteIndex
    if ($idx -lt 0 -or $idx -ge $script:Playlist.Count) {
        Hide-DeleteModal
        return
    }

    $track = $script:Playlist[$idx]
    $prevIdx = $script:CurrentIndex
    $wasPlaying = $script:IsPlaying
    $previousPosition = $script:Player.Position.TotalSeconds
    Hide-DeleteModal

    # If this track is currently loaded, stop it first (guard MediaEnded)
    if ($idx -eq $prevIdx) {
        Reset-LoadedTrack
    }

    # Delete the file
    try { Remove-Item -LiteralPath $track.FullName -Force -ErrorAction Stop } catch {
        if ($idx -eq $prevIdx) {
            Load-Track -Index $idx
            $script:PendingRestorePositionSeconds = $previousPosition
            if ($wasPlaying) { $script:PendingAutoPlay = $true; $script:Player.Play(); Set-PlaybackState $true }
        }
        Set-WidgetSubtitle 'fileDeleteFailed'
        return
    }

    # Also remove this track from any custom playlists in playlists.json
    foreach ($p in $script:CustomPlaylists) {
        if ($p.tracks.Contains($track.Name)) {
            $p.tracks.Remove($track.Name)
        }
    }
    Save-CustomPlaylists

    # Reload resolves the playing identity by path, including reordered libraries.
    Reload-Playlist
    Update-PlaylistSelectionUI
    Save-AppSettings
}

# --- Upload Modal Functions (Downloads vs Smart Auto-Detecting URL) ---
function Show-UploadModal {
    $script:IsUploadModalOpen = $true
    $TxtUrlInput.Text = ""
    Set-WidgetDownloadError ''
    $PnlUploadProgress.Visibility = [System.Windows.Visibility]::Collapsed
    $BtnStartDownload.IsEnabled = $true
    Set-WidgetDownloadStatus 'analyseLink'
    $TxtUploadStatus.Foreground = $bc.ConvertFromString("#A1A1AA")

    Set-UploadStep 1
    $UploadOverlay.Visibility = [System.Windows.Visibility]::Visible
}

function Hide-UploadModal {
    $script:IsUploadModalOpen = $false
    $UploadOverlay.Visibility = [System.Windows.Visibility]::Collapsed
}

function Set-UploadStep {
    param([int]$Step)
    if ($Step -eq 1) {
        $PnlUploadStep1.Visibility = [System.Windows.Visibility]::Visible
        $PnlUploadStepUrl.Visibility = [System.Windows.Visibility]::Collapsed
        $BtnUploadBack.Visibility = [System.Windows.Visibility]::Collapsed
        $TxtUploadHeader.Text = T 'addMusic'
    } elseif ($Step -eq 2) {
        $PnlUploadStep1.Visibility = [System.Windows.Visibility]::Collapsed
        $PnlUploadStepUrl.Visibility = [System.Windows.Visibility]::Visible
        $BtnUploadBack.Visibility = [System.Windows.Visibility]::Visible
        $TxtUploadHeader.Text = T 'downloadFromUrl'
        
        # Check if clipboard has a YouTube or Spotify URL and auto-paste it!
        try {
            if ([System.Windows.Clipboard]::ContainsText()) {
                $cb = [System.Windows.Clipboard]::GetText().Trim()
                if ($cb -match 'youtube\.com|youtu\.be|spotify\.com|music\.youtube\.com') {
                    $TxtUrlInput.Text = $cb
                }
            }
        } catch { }

        Update-UrlPlatformBadge
        $TxtUrlInput.Focus()
    }
}

function Update-UrlPlatformBadge {
    $url = $TxtUrlInput.Text.Trim()
    if ($url -match 'youtube\.com|youtu\.be|music\.youtube\.com') {
        $IconBadgeYoutube.Visibility = [System.Windows.Visibility]::Visible
        $IconBadgeSpotify.Visibility = [System.Windows.Visibility]::Collapsed
        $IconBadgeNeutral.Visibility = [System.Windows.Visibility]::Collapsed
        $TxtPlatformBadge.Text = T 'linkYouTube'
        $TxtPlatformBadge.Foreground = $bc.ConvertFromString("#FF453A")
    } elseif ($url -match 'spotify\.com|open\.spotify\.com') {
        $IconBadgeYoutube.Visibility = [System.Windows.Visibility]::Collapsed
        $IconBadgeSpotify.Visibility = [System.Windows.Visibility]::Visible
        $IconBadgeNeutral.Visibility = [System.Windows.Visibility]::Collapsed
        $TxtPlatformBadge.Text = T 'linkSpotify'
        $TxtPlatformBadge.Foreground = $bc.ConvertFromString("#30D158")
    } else {
        $IconBadgeYoutube.Visibility = [System.Windows.Visibility]::Collapsed
        $IconBadgeSpotify.Visibility = [System.Windows.Visibility]::Collapsed
        $IconBadgeNeutral.Visibility = [System.Windows.Visibility]::Visible
        $TxtPlatformBadge.Text = T 'pasteLink'
        $TxtPlatformBadge.Foreground = $bc.ConvertFromString("#A1A1AA")
    }
}

function Set-WidgetDownloadProgress([long]$Received, [long]$Total) {
    $script:DownloadBytesReceived=$Received
    $script:DownloadBytesTotal=$Total
    $UploadProgressBar.IsIndeterminate=$Total -le 0
    $UploadProgressBar.Value=if ($Total -gt 0) {[math]::Min(100, 100.0 * $Received / $Total)} else {0}
}

function Start-MusicDownload {
    param([string]$PlaylistName='')
    if ($script:GlassWebDownloadBusy) { return }
    $script:DownloadSucceeded=$false
    Set-WidgetDownloadProgress 0 0
    $url = $TxtUrlInput.Text.Trim()
    $link = $null
    if (-not [Uri]::TryCreate($url,[UriKind]::Absolute,[ref]$link) -or
        $link.Scheme -notin @('https','http') -or $link.UserInfo -or -not $link.IsDefaultPort -or
        ($link.Host -notmatch '(^|\.)(youtube\.com|youtu\.be)$' -and
            -not ($link.Host -eq 'open.spotify.com' -and $link.AbsolutePath -cmatch '^/(?:intl-[A-Za-z-]+/)?track/[A-Za-z0-9]{22}/?$'))) {
        Set-WidgetDownloadError 'invalidLink'
        return
    }
    $url = $link.AbsoluteUri

    Set-WidgetDownloadError ''
    $PnlUploadProgress.Visibility = [System.Windows.Visibility]::Visible
    try {
        if (-not ('TrackDownloader' -as [type])) {
            Add-Type -Path (Join-Path $LibDir 'TrackDownloader.cs') -ReferencedAssemblies 'System.Net','WindowsBase'
        }
    } catch {
        Set-WidgetDownloadError 'downloadInitFailed'
        return
    }
    $script:DownloadPlaylistName=$PlaylistName
    $script:GlassWebDownloadBusy=$true
    $BtnStartDownload.IsEnabled = $false
    Set-WidgetDownloadStatus 'downloadStarting'
    $TxtUploadStatus.Foreground = $bc.ConvertFromString("#A1A1AA")

    $ytdlpExe = Join-Path $script:OnlineToolsDirectory 'yt-dlp.exe'
    $outDir = $script:MusicDir

    # Crash-proof native C# asynchronous background task
    [TrackDownloader]::StartDownload(
        $url,
        $ytdlpExe,
        $outDir,
        $window.Dispatcher,
        [Action[string, long, long]]{
            param($statusText, $received, $total)
            if (-not $script:GlassWebDownloadBusy) {return}
            if ($script:DownloadStatusKey -ne $statusText) {Set-WidgetDownloadStatus $statusText}
            Set-WidgetDownloadProgress $received $total
        },
        [Action[bool, string]]{
            param($success, $result)
            $destination=$script:DownloadPlaylistName
            $script:DownloadPlaylistName=''
            $script:GlassWebDownloadBusy=$false
            $BtnStartDownload.IsEnabled=$true
            if ($success) {
                try {
                    Reload-Playlist
                    $track=$script:LibraryFiles | Where-Object { $_.FullName -eq $result } | Select-Object -First 1
                } catch { $track=$null }
                $status='downloadSuccess'
                if ($destination) {
                    $status='downloadPlaylistUnavailable'
                    if ($track -and ($script:CustomPlaylists | Where-Object { $_.name -eq $destination })) {
                        try {
                            Set-PlaylistTrackMembership -PlaylistName $destination -Track $track -Included $true
                            $status='downloadSavedToPlaylist'
                        } catch { }
                    }
                }
                $TxtUploadStatus.Foreground = $bc.ConvertFromString("#30D158")
                Hide-UploadModal
                $script:DownloadSucceeded=$true
                Set-WidgetDownloadStatus $status
            } else {
                $PnlUploadProgress.Visibility = [System.Windows.Visibility]::Collapsed
                Set-WidgetDownloadProgress 0 0
                Set-WidgetDownloadError $result
            }
            Publish-GlassWebState
        }
    )
}

function Update-PlaylistSelectionUI {
    $t = $script:Themes[$script:CurrentThemeIdx]
    for ($i = 0; $i -lt $TrackListContainer.Children.Count; $i++) {
        $child = $TrackListContainer.Children[$i]
        $grid = $child.Child
        $iconGrid = $grid.Children[0]
        $tbNum = $iconGrid.Children[0]
        $activeIcon = $iconGrid.Children[1]
        $tbName = $grid.Children[1]

        if ($i -eq $script:CurrentIndex) {
            $activeIcon.Fill = $bc.ConvertFromString($t.Accent)
            $child.Background = $bc.ConvertFromString($t.ItemActiveBg)
            $tbNum.Visibility = [System.Windows.Visibility]::Collapsed
            $activeIcon.Visibility = [System.Windows.Visibility]::Visible
            $tbName.Foreground = $bc.ConvertFromString($t.ItemActiveFg)
            $tbName.FontWeight = [System.Windows.FontWeights]::SemiBold
        } else {
            $child.Background = [System.Windows.Media.Brushes]::Transparent
            $tbNum.Visibility = [System.Windows.Visibility]::Visible
            $activeIcon.Visibility = [System.Windows.Visibility]::Collapsed
            $tbName.Foreground = $bc.ConvertFromString($t.TxtTitle)
            $tbName.FontWeight = [System.Windows.FontWeights]::Normal
        }
    }
}

# --- Audio Loading & Playback Logic ---
$script:LoadedTrackPath = $null
$script:MediaIsReady = $false
$script:PendingAutoPlay = $false
$script:PendingRestorePositionSeconds = 0.0
$script:PlaybackHistory = @()
$script:ShuffleBag = @()
$script:ShuffleBagInitialized = $false

function Set-PlaybackState {
    param([bool]$Playing)
    $script:IsPlaying = $Playing
    $IconPlay.Visibility = if ($Playing) { [System.Windows.Visibility]::Collapsed } else { [System.Windows.Visibility]::Visible }
    $IconPause.Visibility = if ($Playing) { [System.Windows.Visibility]::Visible } else { [System.Windows.Visibility]::Collapsed }
}

function Reset-LoadedTrack {
    $script:IsClosingTrack = $true
    try { $script:Player.Stop(); $script:Player.Close() } catch { }
    $script:IsClosingTrack = $false
    $script:LoadedTrackPath = $null
    $script:MediaIsReady = $false
    $script:PendingAutoPlay = $false
    $script:PendingRestorePositionSeconds = 0.0
    $script:CurrentIndex = -1
    Set-PlaybackState $false
    Stop-TitleMarqueeAnimation
    $TxtTitle.Text = T 'emptyTitle'
    Set-WidgetSubtitle $(if ($script:Playlist.Count -eq 0) {'firstTrack'} else {'noTrackChosen'})
    $TxtCurrentTime.Text = '0:00'
    $TxtTotalTime.Text = '0:00'
    $ProgressSlider.Value = 0
}

function Load-Track {
    param([int]$Index, [IO.FileInfo]$Track)
    if ($null -eq $Track) {
        if ($script:Playlist.Count -eq 0) { Reset-LoadedTrack; return }
        if ($Index -lt 0) { $Index = $script:Playlist.Count - 1 }
        if ($Index -ge $script:Playlist.Count) { $Index = 0 }
        $Track = $script:Playlist[$Index]
    }
    if (-not $script:PlaybackPlaylist) { $script:PlaybackPlaylist = $script:ActivePlaylist }
    if (-not (Test-Path -LiteralPath $track.FullName -PathType Leaf)) {
        Reset-LoadedTrack
        Set-WidgetSubtitle 'audioFileMissing'
        return
    }

    try {
        $script:IsClosingTrack = $true
        $script:Player.Stop()
        # Reopening the same URI does not raise MediaOpened unless it is closed first.
        if ($script:LoadedTrackPath -eq $track.FullName) { $script:Player.Close() }
        $script:LoadedTrackPath = $track.FullName
        $script:CurrentIndex = Find-TrackIndex $script:Playlist $script:LoadedTrackPath
        $script:MediaIsReady = $false
        $script:PendingAutoPlay = $false
        $script:PendingRestorePositionSeconds = 0.0
        Set-PlaybackState $false
        $ProgressSlider.Value = 0
        $TxtCurrentTime.Text = '0:00'
        $TxtTotalTime.Text = '0:00'
        $script:Player.Open([Uri]::new($track.FullName))
        $script:IsClosingTrack = $false
        $script:Player.Volume = if ($script:IsMuted) { 0.0 } else { $script:LocalVolume }

        $cleanTitle = [System.IO.Path]::GetFileNameWithoutExtension($track.Name)
        $TxtTitle.Text = $cleanTitle
        $TxtTitle.ToolTip = $cleanTitle
        Update-PlayingTrackSubtitle

        Update-PlaylistSelectionUI
        Start-TitleMarqueeAnimation
        Save-AppSettings
    } catch {
        $script:IsClosingTrack = $false
        Reset-LoadedTrack
        Set-WidgetSubtitle 'audioLoadFailed'
    }
}

function Play-Track {
    param([int]$Index, [switch]$FromHistory, [switch]$FromQueue)
    $tracks = if ($FromQueue) { Get-PlaybackQueue } else { ,$script:Playlist }
    if ($tracks.Count -eq 0) { return }
    if ($Index -lt 0) { $Index = $tracks.Count - 1 }
    if ($Index -ge $tracks.Count) { $Index = 0 }
    if (-not $FromQueue -and $script:PlaybackPlaylist -ne $script:ActivePlaylist) {
        $script:PlaybackPlaylist = $script:ActivePlaylist
        $script:ShuffleBag = @()
        $script:ShuffleBagInitialized = $false
    } elseif (-not $script:PlaybackPlaylist) {
        $script:PlaybackPlaylist = $script:ActivePlaylist
    }
    $previousPath = $script:LoadedTrackPath
    $track = $tracks[$Index]
    Load-Track -Track $track
    if ($script:LoadedTrackPath -ne $track.FullName) { return }
    if (-not $FromHistory -and -not [string]::IsNullOrEmpty($previousPath) -and $previousPath -ne $script:LoadedTrackPath) {
        $script:PlaybackHistory = @($script:PlaybackHistory) + @($previousPath)
        if ($script:PlaybackHistory.Count -gt 100) { $script:PlaybackHistory = @($script:PlaybackHistory | Select-Object -Last 100) }
    }
    if ($script:ShuffleBagInitialized) {
        $script:ShuffleBag = @($script:ShuffleBag | Where-Object { $_ -ne $script:LoadedTrackPath })
    }
    try {
        $script:PendingAutoPlay = $true
        $script:Player.Play()
        Set-PlaybackState $true
    } catch {
        $script:PendingAutoPlay = $false
        Set-PlaybackState $false
        Set-WidgetSubtitle 'audioPlayFailed'
    }
}

function Toggle-PlayPause {
    if ($script:IsPlaying) {
        $script:PendingAutoPlay = $false
        $script:Player.Pause()
        Set-PlaybackState $false
        Save-AppSettings
    } else {
        if (-not $script:LoadedTrackPath) {
            Play-Track 0
            return
        }
        if ($script:SubtitleKey -eq 'audioUnreadable') {
            Load-Track -Track ([IO.FileInfo]::new($script:LoadedTrackPath))
            if (-not $script:LoadedTrackPath) { return }
        }
        $script:PendingAutoPlay = $true
        $script:Player.Play()
        Set-PlaybackState $true
    }
}

function Next-Track {
    param([switch]$Automatic)
    $queue = Get-PlaybackQueue
    if ($queue.Count -eq 0) {
        if ($Automatic) { $script:PendingAutoPlay = $false; Set-PlaybackState $false; Save-AppSettings }
        return
    }
    $index = Find-TrackIndex $queue $script:LoadedTrackPath
    if ($script:IsShuffle) {
        if ($script:ShuffleBag.Count -gt 0) {
            $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($track in $queue) { [void]$paths.Add($track.FullName) }
            $script:ShuffleBag = @($script:ShuffleBag | Where-Object { $paths.Contains($_) })
        }
        if ($script:ShuffleBagInitialized -and $script:ShuffleBag.Count -eq 0 -and $Automatic -and $script:RepeatMode -eq 2) {
            $script:PendingAutoPlay = $false
            $script:Player.Position = [TimeSpan]::Zero
            Set-PlaybackState $false
            Save-AppSettings
            return
        }
        if (-not $script:ShuffleBagInitialized -or $script:ShuffleBag.Count -eq 0) {
            $choices = @($queue | Where-Object { $_.FullName -ne $script:LoadedTrackPath } | ForEach-Object { $_.FullName })
            $script:ShuffleBag = @(if ($choices.Count -gt 0) { Get-Random -InputObject $choices -Count $choices.Count })
            $script:ShuffleBagInitialized = $true
        }
        if ($script:ShuffleBag.Count -eq 0) {
            if ($Automatic -and $script:RepeatMode -eq 2) {
                $script:PendingAutoPlay = $false
                $script:Player.Position = [TimeSpan]::Zero
                Set-PlaybackState $false
                Save-AppSettings
                return
            }
            Play-Track $index -FromQueue
            return
        }
        $nextIndex = Find-TrackIndex $queue $script:ShuffleBag[0]
        Play-Track $nextIndex -FromQueue
    } else {
        if ($Automatic -and $script:RepeatMode -eq 2 -and $index -ge $queue.Count - 1) {
            $script:PendingAutoPlay = $false
            $script:Player.Position = [TimeSpan]::Zero
            Set-PlaybackState $false
            Save-AppSettings
            return
        }
        Play-Track ($index + 1) -FromQueue
    }
}

function Prev-Track {
    if ($script:Player.Position.TotalSeconds -gt 3) {
        $script:Player.Position = [TimeSpan]::Zero
        return
    }
    $queue = Get-PlaybackQueue
    if ($queue.Count -eq 0) { return }
    if ($script:IsShuffle -and $script:PlaybackHistory.Count -gt 0) {
        while ($script:PlaybackHistory.Count -gt 0) {
            $previousPath = $script:PlaybackHistory[-1]
            $script:PlaybackHistory = @($script:PlaybackHistory | Select-Object -First ($script:PlaybackHistory.Count - 1))
            $index = Find-TrackIndex $queue $previousPath
            if ($index -ge 0) { Play-Track $index -FromHistory -FromQueue; return }
        }
        $script:Player.Position = [TimeSpan]::Zero
    } else {
        $index = Find-TrackIndex $queue $script:LoadedTrackPath
        Play-Track ($index - 1) -FromQueue
    }
}

$script:Player.Add_MediaOpened({
    $script:MediaIsReady = $true
    $script:Player.SpeedRatio = $script:PlaybackSpeed
    if ($script:Player.NaturalDuration.HasTimeSpan) {
        $duration = $script:Player.NaturalDuration.TimeSpan.TotalSeconds
        $restore = [math]::Max(0.0, [math]::Min([math]::Max(0.0, $duration - 0.05), $script:PendingRestorePositionSeconds))
        $script:Player.Position = [TimeSpan]::FromSeconds($restore)
        $TxtCurrentTime.Text = '{0:mm\:ss}' -f $script:Player.Position
        $TxtTotalTime.Text = '{0:mm\:ss}' -f $script:Player.NaturalDuration.TimeSpan
        if ($duration -gt 0) { $ProgressSlider.Value = ($restore / $duration) * 100.0 }
    }
    $script:PendingRestorePositionSeconds = 0.0
    if ($script:PendingAutoPlay) { $script:Player.Play() } else { $script:Player.Pause() }
})

$script:Player.Add_MediaFailed({
    $script:PendingAutoPlay = $false
    $script:MediaIsReady = $false
    Set-PlaybackState $false
    Set-WidgetSubtitle 'audioUnreadable'
    $TxtTotalTime.Text = '0:00'
    $ProgressSlider.Value = 0
})

# Auto-play next track when current ends (guard against deletion-induced close)
$script:Player.Add_MediaEnded({
    if ($script:IsClosingTrack) { return }  # Suppress event during track deletion
    try {
        if ($script:RepeatMode -eq 1) {
            $script:Player.Position = [TimeSpan]::Zero
            $script:Player.Play()
        } else {
            Next-Track -Automatic
        }
    } catch { }
})

# --- Volume Management (Local music only) ---
function Toggle-LocalMute {
    if ($script:LocalVolume -le 0) {
        $script:LocalVolume = $script:LastAudibleVolume
        $script:IsMuted = $false
    } else {
        $script:IsMuted = -not $script:IsMuted
    }
    Update-LocalVolumeUI
}

function Update-LocalVolumeUI {
    param([bool]$FromSlider = $false)
    if ($FromSlider) {
        $script:LocalVolume = $VolumeSlider.Value / 100.0
    } else {
        $script:IsUpdatingVolSlider = $true
        $VolumeSlider.Value = [math]::Round($script:LocalVolume * 100)
        $script:IsUpdatingVolSlider = $false
    }
    
    $script:Player.Volume = if ($script:IsMuted) { 0.0 } else { $script:LocalVolume }
    if ($script:LocalVolume -gt 0) {$script:LastAudibleVolume=$script:LocalVolume}
    Sync-MinimalMute
    $pct = [math]::Round($script:LocalVolume * 100)
    $TxtVolumePercent.Text = "$pct%"

    if ($script:IsMuted -or $pct -eq 0) {
        $IconSpeaker.Visibility = [System.Windows.Visibility]::Collapsed
        $IconMuted.Visibility = [System.Windows.Visibility]::Visible
    } else {
        $IconSpeaker.Visibility = [System.Windows.Visibility]::Visible
        $IconMuted.Visibility = [System.Windows.Visibility]::Collapsed
    }

    Save-AppSettings
}

function Show-Widget {
    $script:GlassWebWantsVisible=$true
    $window.ShowActivated=$false
    if ($null -ne $script:GlassWebControl -and -not $script:GlassWebFailed -and -not $script:GlassWebPresentationReady) {
        if ($script:GlassWebNativeCloaked) {$window.Opacity=$script:GlassWebDesiredOpacity} else {$window.Opacity=0}
        Hide-GlassNativeControls
    } elseif ($null -ne $script:GlassWebDesiredOpacity) {
        $window.Opacity=$script:GlassWebDesiredOpacity
    }
    $window.Show()
    $window.WindowState = [System.Windows.WindowState]::Normal
    # Do NOT call Activate() - prevents stealing focus from games like Valorant
    $window.Topmost = $script:AlwaysOnTop
}

function Hide-Widget {
    Set-ShortcutCapture $false
    $window.Hide()
}

function Stop-Widget {
    $app=[System.Windows.Application]::Current
    if ($null -ne $app) {$app.Shutdown()} else {$window.Close()}
}

function Toggle-Widget {
    if ($window.IsVisible) {
        Hide-Widget
    } else {
        Show-Widget
    }
}

# --- Position par défaut : En haut à gauche de l'écran ---
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$window.Left = $screen.Left + 16
$window.Top  = $screen.Top + 16

# --- Empêcher le widget de sortir de l'écran (Screen Constraint Drag) ---
$script:IsDraggingWindow = $false

$window.Add_MouseLeftButtonDown({
    param($s, $e)
    if ($script:GlassWebActive) { return }
    if ($script:DesignMode -eq 'Minimalist') { return }
    # If any modal is open, do not start window drag
    if ($script:IsAppearanceOpen -or $script:IsModalOpen -or $script:IsUploadModalOpen -or $script:IsCreatePlaylistModalOpen -or $script:IsContextMenuOpen) {
        return
    }
    if ($ModalOverlay.Visibility -eq [System.Windows.Visibility]::Visible -or
        $UploadOverlay.Visibility -eq [System.Windows.Visibility]::Visible -or
        $CreatePlaylistOverlay.Visibility -eq [System.Windows.Visibility]::Visible -or
        $ContextMenuOverlay.Visibility -eq [System.Windows.Visibility]::Visible) { return }
    # Guard 3: playlist drag active
    if ($script:DragState.Active) { return }
    # Don't drag if the click source is inside the playlist drawer (track items)
    $src = $e.OriginalSource
    if ($src -is [System.Windows.Controls.TextBlock] -or
        $src -is [System.Windows.Shapes.Path] -or
        $src -is [System.Windows.Controls.ScrollViewer] -or
        $src -is [System.Windows.Controls.StackPanel]) { return }
    if ($e.ButtonState -eq [System.Windows.Input.MouseButtonState]::Pressed) {
        $script:IsDraggingWindow = $true
        try { $window.DragMove() } catch { }
        $script:IsDraggingWindow = $false
        Clamp-WindowToScreen
        Remember-CompactPosition
    }
})

function Clamp-WindowToScreen {
    if ($script:IsPositioning) { return }
    try {
        if ($script:GlassWebActive -and $script:GlassWebPane -in @('player','menu','volume') -and $script:GlassWebPlayerHeight -gt 0) {
            $inset=$script:GlassWebPlayerInset
            $screenPoint=$window.PointToScreen([System.Windows.Point]::new(20,$inset+20))
            $rect=[System.Windows.Forms.Screen]::FromPoint([System.Drawing.Point]::new([int]$screenPoint.X,[int]$screenPoint.Y)).WorkingArea
            $scale=[System.Windows.Media.VisualTreeHelper]::GetDpi($window)
            $left=[math]::Max($rect.Left/$scale.DpiScaleX,[math]::Min($rect.Right/$scale.DpiScaleX-$window.Width,$window.Left))
            $top=[math]::Max($rect.Top/$scale.DpiScaleY,[math]::Min($rect.Bottom/$scale.DpiScaleY-$script:GlassWebPlayerHeight,$window.Top+$inset))-$inset
            if ([math]::Abs($window.Left-$left) -gt 1) {$window.Left=$left}
            if ([math]::Abs($window.Top-$top) -gt 1) {$window.Top=$top}
            return
        }
        $curPt = [System.Drawing.Point]::new([int]$window.Left + 20, [int]$window.Top + 20)
        $scr = [System.Windows.Forms.Screen]::FromPoint($curPt).WorkingArea
        $minX = $scr.Left
        $maxX = [math]::Max($minX, $scr.Right - $window.ActualWidth)
        $minY = $scr.Top
        $maxY = [math]::Max($minY, $scr.Bottom - $window.ActualHeight)

        $clampedX = [math]::Max($minX, [math]::Min($maxX, $window.Left))
        $clampedY = [math]::Max($minY, [math]::Min($maxY, $window.Top))

        if ([math]::Abs($window.Left - $clampedX) -gt 1) { $window.Left = $clampedX }
        if ([math]::Abs($window.Top - $clampedY) -gt 1) { $window.Top = $clampedY }
    } catch { }
}

$window.Add_LocationChanged({
    if (-not $script:IsDraggingWindow) {
        Clamp-WindowToScreen
    }
})

# Layout-aware resizing; expensive text measurement happens once on release.
$ResizeGripThumb.Add_DragDelta({ param($s,$e); Resize-Player $e.HorizontalChange $e.VerticalChange })
$RightResizeThumb.Add_DragDelta({ param($s,$e); Resize-Player $e.HorizontalChange 0 })
$BottomResizeThumb.Add_DragDelta({ param($s,$e); Resize-Player 0 $e.VerticalChange })
foreach ($thumb in @($ResizeGripThumb,$RightResizeThumb,$BottomResizeThumb)) {
    $thumb.Add_DragCompleted({ Start-TitleMarqueeAnimation; Save-AppSettings })
}
$window.Add_MouseWheel({
    param($s,$e)
    if ($script:GlassWebActive) { return }
    if ($script:IsAppearanceOpen -or $script:IsModalOpen -or $script:IsUploadModalOpen -or $script:IsCreatePlaylistModalOpen -or $script:IsContextMenuOpen) { return }
    if ($e.OriginalSource -is [System.Windows.DependencyObject] -and $PlaylistDrawer.IsAncestorOf($e.OriginalSource)) { return }
    $script:LocalVolume=[math]::Max(0,[math]::Min(1,$script:LocalVolume + $(if ($e.Delta -gt 0) {0.03} else {-0.03})))
    Update-LocalVolumeUI
    $e.Handled=$true
})
# --- Bind Buttons & Events ---
$BtnPlay.Add_Click({ Toggle-PlayPause })
$BtnNext.Add_Click({ Next-Track })
$BtnPrev.Add_Click({ Prev-Track })
$BtnPlaylist.Add_Click({ Toggle-PlaylistDrawer })
$BtnHide.Add_Click({ Hide-Widget })

# --- Track Context Menu Functions ---
function Show-TrackContextMenu {
    param([int]$TrackIndex)
    if ($TrackIndex -lt 0 -or $TrackIndex -ge $script:Playlist.Count) { return }

    $script:ContextMenuTrackIndex = $TrackIndex
    $script:IsContextMenuOpen = $true

    $track = $script:Playlist[$TrackIndex]
    $TxtContextMenuTrackTitle.Text = [System.IO.Path]::GetFileNameWithoutExtension($track.Name)
    $PnlMenuPlaylistsSublist.Visibility = [System.Windows.Visibility]::Collapsed
    $IconMenuChevron.Data = [System.Windows.Media.Geometry]::Parse("M 8,4 L 14,10 L 8,16")

    $ContextMenuOverlay.Visibility = [System.Windows.Visibility]::Visible
}

function Hide-TrackContextMenu {
    $script:IsContextMenuOpen = $false
    $script:ContextMenuTrackIndex = -1
    $ContextMenuOverlay.Visibility = [System.Windows.Visibility]::Collapsed
}

function Toggle-MenuPlaylistsSublist {
    if ($PnlMenuPlaylistsSublist.Visibility -eq [System.Windows.Visibility]::Visible) {
        $PnlMenuPlaylistsSublist.Visibility = [System.Windows.Visibility]::Collapsed
        $IconMenuChevron.Data = [System.Windows.Media.Geometry]::Parse("M 8,4 L 14,10 L 8,16")
    } else {
        $PnlMenuPlaylistsSublist.Children.Clear()

        if ($script:CustomPlaylists.Count -eq 0) {
            $tbNone = New-Object System.Windows.Controls.TextBlock
            $tbNone.Text = T 'noPlaylists'
            $tbNone.FontFamily = [System.Windows.Media.FontFamily]::new("Segoe UI Variable Text, Segoe UI")
            $tbNone.FontSize = 9.5
            $tbNone.Foreground = $bc.ConvertFromString("#8E8E93")
            $tbNone.TextAlignment = [System.Windows.TextAlignment]::Center
            $tbNone.Margin = [System.Windows.Thickness]::new(0, 4, 0, 4)
            [void]$PnlMenuPlaylistsSublist.Children.Add($tbNone)
        } else {
            foreach ($p in $script:CustomPlaylists) {
                $pName = $p.name
                $btnPl = New-Object System.Windows.Controls.Button
                $btnPl.Style = $window.FindResource('UploadOptionBtn')
                $btnPl.Padding = [System.Windows.Thickness]::new(8, 5, 8, 5)
                $btnPl.Margin = [System.Windows.Thickness]::new(0, 1.5, 0, 1.5)
                $btnPl.Tag = $pName

                $g = New-Object System.Windows.Controls.Grid
                $c0 = New-Object System.Windows.Controls.ColumnDefinition; $c0.Width = [System.Windows.GridLength]::Auto
                $c1 = New-Object System.Windows.Controls.ColumnDefinition; $c1.Width = [System.Windows.GridLength]::new(1, [System.Windows.GridUnitType]::Star)
                [void]$g.ColumnDefinitions.Add($c0)
                [void]$g.ColumnDefinitions.Add($c1)

                $tbIcon = New-Object System.Windows.Controls.TextBlock
                $tbIcon.Text = "$([char]0xD83D)$([char]0xDCC1)"
                $tbIcon.FontSize = 10
                $tbIcon.Margin = [System.Windows.Thickness]::new(0, 0, 6, 0)
                $tbIcon.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
                [System.Windows.Controls.Grid]::SetColumn($tbIcon, 0)
                [void]$g.Children.Add($tbIcon)

                $tbP = New-Object System.Windows.Controls.TextBlock
                $tbP.Text = $pName
                $tbP.FontFamily = [System.Windows.Media.FontFamily]::new("Segoe UI Variable Text, Segoe UI")
                $tbP.FontSize = 10.5
                $tbP.Foreground = $bc.ConvertFromString($script:Themes[$script:CurrentThemeIdx].TxtTitle)
                $tbP.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
                [System.Windows.Controls.Grid]::SetColumn($tbP, 1)
                [void]$g.Children.Add($tbP)

                $btnPl.Content = $g

                $btnPl.Add_Click({
                    param($s, $e)
                    $targetPlName = [string]$s.Tag
                    Add-TrackToPlaylist -PlaylistName $targetPlName -TrackIndex $script:ContextMenuTrackIndex
                })

                [void]$PnlMenuPlaylistsSublist.Children.Add($btnPl)
            }
        }

        $PnlMenuPlaylistsSublist.Visibility = [System.Windows.Visibility]::Visible
        $IconMenuChevron.Data = [System.Windows.Media.Geometry]::Parse("M 4,8 L 10,14 L 16,8")
    }
}

function Add-TrackToPlaylist {
    param(
        [string]$PlaylistName,
        [int]$TrackIndex
    )
    if ($TrackIndex -lt 0 -or $TrackIndex -ge $script:Playlist.Count) { return }

    Set-PlaylistTrackMembership -PlaylistName $PlaylistName -Track $script:Playlist[$TrackIndex] -Included $true
    Hide-TrackContextMenu
}

function Set-PlaylistTrackMembership {
    param([string]$PlaylistName, [IO.FileInfo[]]$Track, [bool]$Included)

    $pl = $script:CustomPlaylists | Where-Object { $_.name -eq $PlaylistName } | Select-Object -First 1
    if ($null -eq $pl -or -not $Track) { return }

    $original = $pl.tracks
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if ($Included) {
        $updated = [Collections.ArrayList]::new(@($original))
        foreach ($name in $original) { [void]$names.Add($name) }
        foreach ($file in $Track) {
            if ($names.Add($file.Name)) { [void]$updated.Add($file.Name) }
        }
    } else {
        foreach ($file in $Track) { [void]$names.Add($file.Name) }
        $updated = [Collections.ArrayList]::new(@($original | Where-Object { -not $names.Contains($_) }))
    }
    if ($updated.Count -eq $original.Count) { return }
    $pl.tracks = $updated
    try {
        Save-CustomPlaylists -ThrowOnError
    } catch {
        $pl.tracks = $original
        throw (T 'playlistSaveFailed')
    }

    if ($script:ActivePlaylist -eq $pl.name) {
        Reload-Playlist
        Save-AppSettings
    } else {
        if ($script:PlaybackPlaylist -eq $pl.name) {
            $script:ShuffleBag = @()
            $script:ShuffleBagInitialized = $false
            Update-PlayingTrackSubtitle
        }
        Render-PlaylistShelf
    }
}

# --- Create Playlist Modal Functions ---
function Show-CreatePlaylistModal {
    $script:IsCreatePlaylistModalOpen = $true
    $TxtPlaylistNameInput.Text = ""
    $CreatePlaylistOverlay.Visibility = [System.Windows.Visibility]::Visible
    $window.Activate()
    $TxtPlaylistNameInput.Dispatcher.BeginInvoke(
        [System.Windows.Threading.DispatcherPriority]::Input,
        [Action]{
            $TxtPlaylistNameInput.Focus()
            [System.Windows.Input.Keyboard]::Focus($TxtPlaylistNameInput)
        }
    ) | Out-Null
}

function Hide-CreatePlaylistModal {
    $script:IsCreatePlaylistModalOpen = $false
    $CreatePlaylistOverlay.Visibility = [System.Windows.Visibility]::Collapsed
    $TxtPlaylistNameInput.Text = ""
}

function Create-NewPlaylist {
    $name = $TxtPlaylistNameInput.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($name)) { return }
    if ($name -eq 'ALL') { $TxtPlaylistNameInput.ToolTip=T 'reservedPlaylist'; return }

    # Check if already exists
    $exists = $script:CustomPlaylists | Where-Object { $_.name.ToLower() -eq $name.ToLower() }
    if ($exists) {
        Hide-CreatePlaylistModal
        Select-Playlist -Id $exists.name
        return
    }

    $newPl = [PSCustomObject]@{
        name   = $name
        tracks = [System.Collections.ArrayList]::new()
    }
    $script:CustomPlaylists = @($script:CustomPlaylists) + @($newPl)
    Save-CustomPlaylists

    Hide-CreatePlaylistModal
    Select-Playlist -Id $name
}
$BtnAddMusic.Add_Click({ Show-UploadModal })
$BtnUploadClose.Add_Click({ Hide-UploadModal })
$BtnUploadBack.Add_Click({ Set-UploadStep 1 })

# Option A: Local Downloads Folder Picker
$BtnChoiceLocal.Add_Click({
    try {
        $downloadsDir = [System.IO.Path]::Combine([Environment]::GetFolderPath('UserProfile'), 'Downloads')
        $dialog = New-Object Microsoft.Win32.OpenFileDialog
        $dialog.Multiselect  = $true
        $dialog.Title        = T 'pickFiles'
        $dialog.Filter       = ('{0}|*.mp3;*.wav;*.m4a;*.flac;*.aac;*.wma;*.ogg|{1}|*.*' -f (T 'audioFiles'),(T 'allFiles'))
        $dialog.InitialDirectory = if (Test-Path $downloadsDir) { $downloadsDir } else { [Environment]::GetFolderPath('MyMusic') }
        if ($dialog.ShowDialog($window) -eq $true) {
            $copied = 0
            foreach ($src in $dialog.FileNames) {
                $dest = Join-Path $script:MusicDir ([System.IO.Path]::GetFileName($src))
                if (-not (Test-Path -LiteralPath $dest)) {
                    Copy-Item -LiteralPath $src -Destination $dest -ErrorAction Stop
                    $copied++
                }
            }
            if ($copied -gt 0) {
                Reload-Playlist
                Hide-UploadModal
            }
        }
    } catch { }
})

# Option B: Switch to URL Converter Input
$BtnChoiceUrl.Add_Click({ Set-UploadStep 2 })

# Live URL text change -> auto-detect YouTube vs Spotify
$TxtUrlInput.Add_TextChanged({ Update-UrlPlatformBadge })

# Paste Button
$BtnPasteUrl.Add_Click({
    try {
        if ([System.Windows.Clipboard]::ContainsText()) {
            $TxtUrlInput.Text = [System.Windows.Clipboard]::GetText().Trim()
            Update-UrlPlatformBadge
        }
    } catch { }
})

# Download Action Button
$BtnStartDownload.Add_Click({ Start-MusicDownload })

# Delete Modal Buttons
$BtnModalCancel.Add_Click({ Hide-DeleteModal })
$BtnModalDelete.Add_Click({ Confirm-DeleteTrack })

# --- Playlist Management & Context Menu Event Bindings ---
$BtnNewPlaylist.Add_Click({ Show-CreatePlaylistModal })
$BtnCreatePlaylistClose.Add_Click({ Hide-CreatePlaylistModal })
$BtnCreatePlaylistConfirm.Add_Click({ Create-NewPlaylist })
$CreatePlaylistBackdrop.Add_MouseLeftButtonDown({ param($s, $e); Hide-CreatePlaylistModal })
$TxtPlaylistNameInput.Add_KeyDown({
    param($s, $e)
    if ($e.Key -eq [System.Windows.Input.Key]::Enter) {
        Create-NewPlaylist
    }
})

# Context Menu Button Bindings
$BtnMenuAddToPlaylist.Add_Click({ Toggle-MenuPlaylistsSublist })
$BtnMenuDeleteTrack.Add_Click({
    $idx = $script:ContextMenuTrackIndex
    Hide-TrackContextMenu
    Show-DeleteModal -TrackIndex $idx
})

# Swallow MouseLeftButtonDown inside modals backdrop
$ModalOverlay.Add_MouseLeftButtonDown({ param($s, $e); $e.Handled = $true })
$ModalBackdrop.Add_MouseLeftButtonDown({ param($s, $e); $e.Handled = $true })
$UploadOverlay.Add_MouseLeftButtonDown({ param($s, $e); $e.Handled = $true })
$UploadModalBackdrop.Add_MouseLeftButtonDown({ param($s, $e); $e.Handled = $true })
$ContextMenuOverlay.Add_MouseLeftButtonDown({ param($s, $e); $e.Handled = $true })
$ContextMenuBackdrop.Add_MouseLeftButtonDown({ param($s, $e); Hide-TrackContextMenu })

# Allow Escape key to cancel open modals
$window.Add_KeyDown({
    param($s, $e)
    if ($e.Key -eq [System.Windows.Input.Key]::Escape) {
        if ($script:IsContextMenuOpen) { Hide-TrackContextMenu }
        elseif ($script:IsCreatePlaylistModalOpen) { Hide-CreatePlaylistModal }
        elseif ($script:IsUploadModalOpen) { Hide-UploadModal }
        elseif ($script:IsModalOpen) { Hide-DeleteModal }
    }
})

# Switch Theme Click
$BtnTheme.Add_Click({
    Show-Appearance
})

$BtnMute.Add_Click({Toggle-LocalMute})

$VolumeSlider.Add_ValueChanged({
    if (-not $script:IsUpdatingVolSlider) {
        Update-LocalVolumeUI -FromSlider $true
    }
})

# Playback mode indicators use the current palette and text as well as color.
$BtnShuffle.Add_Click({
    $script:IsShuffle = -not $script:IsShuffle
    $script:ShuffleBag = @(); $script:ShuffleBagInitialized = $false
    Update-ModeIndicators
    Save-AppSettings
})
$BtnRepeat.Add_Click({
    $script:RepeatMode = ($script:RepeatMode + 1) % 3
    Update-ModeIndicators
    Save-AppSettings
})
# --- Direct Click & Drag Seeking on Timeline Scrubber ---
function Seek-To-MousePosition($mouseArgs) {
    if ($script:Player.NaturalDuration.HasTimeSpan) {
        $totalSec = $script:Player.NaturalDuration.TimeSpan.TotalSeconds
        if ($totalSec -gt 0) {
            $pt = $mouseArgs.GetPosition($ProgressSlider)
            $width = $ProgressSlider.ActualWidth
            if ($width -gt 0) {
                $ratio = [math]::Max(0.0, [math]::Min(1.0, $pt.X / $width))
                $ProgressSlider.Value = $ratio * 100.0
                $targetSec = $ratio * $totalSec
                $script:Player.Position = [TimeSpan]::FromSeconds($targetSec)
                $TxtCurrentTime.Text = "{0:mm\:ss}" -f $script:Player.Position
            }
        }
    }
}

$ProgressSlider.Add_PreviewMouseLeftButtonDown({
    param($s, $e)
    $script:IsSeeking = $true
    [void]$ProgressSlider.CaptureMouse()
    Seek-To-MousePosition $e
    $e.Handled = $true
})

$ProgressSlider.Add_PreviewMouseMove({
    param($s, $e)
    if ($script:IsSeeking -and $e.LeftButton -eq [System.Windows.Input.MouseButtonState]::Pressed) {
        Seek-To-MousePosition $e
    }
})

$ProgressSlider.Add_PreviewMouseLeftButtonUp({
    param($s, $e)
    Seek-To-MousePosition $e
    $script:IsSeeking = $false
    $ProgressSlider.ReleaseMouseCapture()
    $e.Handled = $true
    Save-AppSettings
})

$ProgressSlider.Add_LostMouseCapture({ $script:IsSeeking = $false })

# Only five cached RenderTransforms are updated; no height/layout changes.
$liveTimer = New-Object System.Windows.Threading.DispatcherTimer
$liveTimer.Interval = [TimeSpan]::FromMilliseconds(33)
$liveTimer.Add_Tick({
    if (-not $window.IsVisible -or -not $script:IsPlaying -or $script:ReducedMotion) { $liveTimer.Stop(); return }
    if (-not $MainCard.IsVisible) { $liveTimer.Stop(); return }
    if ($null -eq $script:AudioMeter) {
        Initialize-LegacyAudioTools
        $script:AudioMeter = New-Object CoreAudioMeter
    }
    $script:WavePhase += 0.22
    $energy = if ($script:IsMuted -or $script:LocalVolume -le 0) { 0.0 } else { [math]::Min(1.0, $script:AudioMeter.GetPeak() / [math]::Max(0.04,$script:LocalVolume)) }
    $bars = @($WaveBar1,$WaveBar2,$WaveBar3,$WaveBar4,$WaveBar5)
    for ($i=0; $i -lt 5; $i++) {
        $scale=0.15 + $energy*0.8*[math]::Abs([math]::Sin($script:WavePhase*(1.2+$i*0.3)+$i))
        $transform=$bars[$i].RenderTransform
        $transform.ScaleY += ($scale-$transform.ScaleY)*0.4
    }
})
$window.Add_Closed({
    Save-AppSettings -Flush
    $liveTimer.Stop(); $progressTimer.Stop(); $saveTimer.Stop()
    Stop-OnlineToolsMonitor
    if ($null -ne $script:HotKeyMgr) {
        $script:HotKeyMgr.Dispose()
    }
    if ($null -ne $script:Player) { $script:Player.Close() }
    if ($null -ne $script:ShowRequest) { $script:ShowRequest.Dispose() }
    if ($null -ne $script:TrayIcon) { $script:TrayIcon.Dispose() }
    if ($null -ne $script:TrayMenu) { $script:TrayMenu.Dispose() }
    if ($null -ne $script:Mutex) { $script:Mutex.ReleaseMutex() }
    $app = [System.Windows.Application]::Current
    if ($null -ne $app) { $app.Shutdown() }
})

# --- System Tray Icon ---
$script:TrayIcon = New-Object System.Windows.Forms.NotifyIcon
$script:TrayIcon.Text = 'Music Widget'

if (Test-Path $iconIcoPath) {
    try {
        $script:TrayIcon.Icon = New-Object System.Drawing.Icon($iconIcoPath)
    } catch {
        $script:TrayIcon.Icon = [System.Drawing.SystemIcons]::Application
    }
} else {
    $script:TrayIcon.Icon = [System.Drawing.SystemIcons]::Application
}

$menuToggle = [System.Windows.Forms.ToolStripMenuItem]::new((T 'toggleWidget'))
$menuToggle.Add_Click({ Toggle-Widget })

$menuAutostart = [System.Windows.Forms.ToolStripMenuItem]::new((T 'autostart'))
$startupPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Music Widget.lnk'
$menuAutostart.Checked = (Test-Path $startupPath)
$menuAutostart.Add_Click({
    $scriptAutostart = Join-Path $ScriptDir 'Install-Autostart.ps1'
    if (Test-Path $startupPath) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptAutostart -Remove | Out-Null
        $menuAutostart.Checked = $false
    } else {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptAutostart | Out-Null
        $menuAutostart.Checked = $true
    }
})

$menuFolder = [System.Windows.Forms.ToolStripMenuItem]::new((T 'openMusicFolder'))
$menuFolder.Add_Click({
    Start-Process 'explorer.exe' -ArgumentList "`"$($script:MusicDir)`""
})

$menuQuit = [System.Windows.Forms.ToolStripMenuItem]::new((T 'quit'))
$menuQuit.Add_Click({ Stop-Widget })

$menu = [MusicWidgetTrayMenu]::new($iconPngPath)
[void]$menu.Items.AddRange(@($menuToggle, $menuAutostart, $menuFolder, (New-Object System.Windows.Forms.ToolStripSeparator), $menuQuit))
$menu.Add_Opening({
    $menuToggle.Text=T $(if ($window.IsVisible) {'hideWidget'} else {'showWidget'})
    $menuAutostart.Checked=(Test-Path -LiteralPath $startupPath)
    $menu.RefreshLayout()
})
$menu.RefreshLayout()
$script:TrayMenu=$menu
$script:TrayIcon.Add_MouseUp({param($s,$e);if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Right) {$script:TrayMenu.ShowAtCursor()}})
$script:TrayIcon.Add_DoubleClick({ Toggle-Widget })
$script:TrayIcon.Visible = -not $NoRun

# --- Init & Run ---
. (Join-Path $LibDir 'Experience.ps1')
. (Join-Path $LibDir 'GlassWeb.ps1')
$savedCfg = Load-AppSettings
Restore-WidgetShortcuts $savedCfg.Shortcuts
Set-WidgetLanguage ([string]$savedCfg.Language)
Restore-ExperienceSettings $savedCfg
if (-not $NoRun) {
    $app=[System.Windows.Application]::new()
    $app.ShutdownMode=[System.Windows.ShutdownMode]::OnExplicitShutdown
    # Start the controller against a cloaked HWND while the local library loads.
    $showWhilePreparing=Initialize-GlassWeb
    if ($showWhilePreparing) {
        Show-Widget
        Start-GlassWebRenderer
    }
}

Load-CustomPlaylists
Reload-Playlist
Update-LocalVolumeUI
Apply-Theme $script:CurrentThemeIdx

Update-ModeIndicators

# Restore the last track independently of the playlist being browsed.
$savedTrack = if ($savedCfg.CurrentTrackName) {
    $script:LibraryFiles | Where-Object { $_.Name -eq $savedCfg.CurrentTrackName } | Select-Object -First 1
}
if ($savedTrack) {
    Load-Track -Track $savedTrack
    if ($script:LoadedTrackPath -and $null -ne $savedCfg.CurrentTrackPositionSeconds) {
        # MediaPlayer.Open is asynchronous; apply this after MediaOpened.
        $script:PendingRestorePositionSeconds = [math]::Max(0.0, [double]$savedCfg.CurrentTrackPositionSeconds)
    }
} elseif ($script:Playlist.Count -gt 0) {
    $script:PlaybackPlaylist = $script:ActivePlaylist
    Load-Track -Index 0
} else {
    $script:PlaybackPlaylist = ''
}

$script:IsInitializing = $false
Update-PlayerSize
Save-AppSettings
if ($NoRun) { return }
Initialize-WidgetShortcuts
if (-not $showWhilePreparing) { Show-Widget }
[void]$app.Run($window)
