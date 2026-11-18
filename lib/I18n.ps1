$script:Translations = @{}
foreach ($locale in @('en','fr')) {
    $file=Join-Path $PSScriptRoot "translations\$locale.json"
    $script:Translations[$locale]=[IO.File]::ReadAllText($file,[Text.Encoding]::UTF8) | ConvertFrom-Json
}

function Resolve-WidgetLanguage(
    [string]$Preference,
    [string]$DeviceLanguage=[Globalization.CultureInfo]::CurrentUICulture.Name
) {
    if ($Preference -cin @('en','fr')) {return $Preference}
    if (($DeviceLanguage -split '[-_]',2)[0] -ieq 'fr') {return 'fr'}
    return 'en'
}

$script:LanguagePreference='auto'
$script:WidgetLanguage=Resolve-WidgetLanguage $script:LanguagePreference
$script:SubtitleKey='emptySubtitle'
$script:SubtitleFields=@{}
$script:DownloadStatusKey='analyseLink'
$script:DownloadErrorKey=''
$script:DownloadSucceeded=$false

function T([string]$Key,[hashtable]$Fields=@{}) {
    $text=$script:Translations[$script:WidgetLanguage].$Key
    if ($null -eq $text) {$text=$script:Translations.en.$Key}
    if ($null -eq $text) {return $Key}
    foreach ($name in $Fields.Keys) { $text=$text.Replace('{' + $name + '}',[string]$Fields[$name]) }
    return $text
}

function Set-WidgetSubtitle([string]$Key,[hashtable]$Fields=@{}) {
    $script:SubtitleKey=$Key
    $script:SubtitleFields=$Fields
    $TxtSubtitle.Text=T $Key $Fields
}

function Set-WidgetDownloadStatus([string]$Key) {
    $script:DownloadStatusKey=$Key
    $TxtUploadStatus.Text=T $Key
}

function Set-WidgetDownloadError([string]$Key) {
    $script:DownloadErrorKey=$Key
    $TxtUploadError.Text=if ($Key) {T $Key} else {''}
    $TxtUploadError.Visibility=if ($Key) {'Visible'} else {'Collapsed'}
}

function Set-WidgetLanguage([string]$Preference) {
    if ($Preference -cnotin @('auto','en','fr')) {$Preference='auto'}
    $script:LanguagePreference=$Preference
    $script:WidgetLanguage=Resolve-WidgetLanguage $Preference
    $window.Language=[Windows.Markup.XmlLanguage]::GetLanguage($script:WidgetLanguage)
    # Publish one dictionary so WPF refreshes its dynamic resources once.
    $localized=[Windows.ResourceDictionary]::new()
    foreach ($entry in $script:Translations.en.PSObject.Properties) {
        $localized['i.'+$entry.Name]=T $entry.Name
    }
    $merged=$window.Resources.MergedDictionaries
    if ($null -eq $script:WidgetLocaleResources) {$merged.Add($localized)}
    else {$merged[$merged.IndexOf($script:WidgetLocaleResources)]=$localized}
    $script:WidgetLocaleResources=$localized
    if (-not $script:LoadedTrackPath) {$TxtTitle.Text=T 'emptyTitle'}
    Set-WidgetSubtitle $script:SubtitleKey $script:SubtitleFields
    Set-WidgetDownloadStatus $script:DownloadStatusKey
    Set-WidgetDownloadError $script:DownloadErrorKey
    $menuAutostart.Text=T 'autostart'
    $menuToggle.Text=T $(if ($window.IsVisible) {'hideWidget'} else {'showWidget'})
    $menuFolder.Text=T 'openMusicFolder'
    $menuQuit.Text=T 'quit'
    $script:TrayIcon.Text='Music Widget'
    $script:TrayMenu.RefreshLayout()
    Update-NativeLocale
    Update-ModeIndicators
    Update-MinimalView
    if (-not $script:IsInitializing) {Render-PlaylistUI; Render-PlaylistShelf}
    $script:GlassWebSearchDirty=$true
    Save-AppSettings
}
