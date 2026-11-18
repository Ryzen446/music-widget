$script:ShortcutDefaults = [IO.File]::ReadAllText((Join-Path $LibDir 'shortcuts.json')) | ConvertFrom-Json
$script:ShortcutActions = @($script:ShortcutDefaults.PSObject.Properties.Name)
$script:Shortcuts = [ordered]@{}
$script:ShortcutInactive = [ordered]@{}
$script:ShortcutCapturing = $false
$script:ShortcutError = ''

function Test-WidgetShortcut($Value) {
    if ($null -eq $Value) {return $true}
    if ($Value -isnot [PSCustomObject]) {return $false}
    foreach ($property in $Value.PSObject.Properties.Name) {
        if ($property -cnotin @('modifiers','key')) {return $false}
    }
    if ($Value.modifiers -isnot [int] -and $Value.modifiers -isnot [long]) {return $false}
    if ($Value.modifiers -notin @(1,2,3,5,6,7)) {return $false}
    if ($Value.key -isnot [int] -and $Value.key -isnot [long]) {return $false}
    $key=$Value.key
    ($key -ge 48 -and $key -le 57) -or ($key -ge 65 -and $key -le 90) -or
    ($key -ge 112 -and $key -le 135 -and $key -ne 123) -or
    $key -in @(13,32,33,34,35,36,37,38,39,40,45,46)
}

function Restore-WidgetShortcuts($Saved) {
    foreach ($action in $script:ShortcutActions) {
        $entry=if ($null -ne $Saved) {$Saved.PSObject.Properties[$action]} else {$null}
        $script:Shortcuts[$action]=if ($null -ne $entry -and (Test-WidgetShortcut $entry.Value)) {$entry.Value} else {$script:ShortcutDefaults.$action}
    }
}

function Test-SameWidgetShortcut($First,$Second) {
    $null -ne $First -and $null -ne $Second -and $First.modifiers -eq $Second.modifiers -and $First.key -eq $Second.key
}

function Register-WidgetShortcuts {
    $script:ShortcutInactive.Clear()
    if ($null -eq $script:HotKeyMgr) {return}
    for ($index=0; $index -lt $script:ShortcutActions.Count; $index++) {
        $action=$script:ShortcutActions[$index]
        $binding=$script:Shortcuts[$action]
        if ($null -ne $binding -and -not $script:HotKeyMgr.Apply($index,[uint32]$binding.modifiers,[uint32]$binding.key)) {
            $script:ShortcutInactive[$action]=$true
        }
    }
}

function Set-ShortcutCapture([bool]$Active) {
    if ($script:ShortcutCapturing -eq $Active) {return}
    $script:ShortcutCapturing=$Active
    $script:ShortcutError=''
    if ($Active) {
        if ($null -ne $script:HotKeyMgr) {$script:HotKeyMgr.UnregisterAll()}
    } else {
        Register-WidgetShortcuts
    }
}

function Set-WidgetShortcut([string]$Action,$Binding) {
    Set-ShortcutCapture $false
    $script:ShortcutError=''
    foreach ($other in $script:ShortcutActions) {
        if ($other -ne $Action -and (Test-SameWidgetShortcut $Binding $script:Shortcuts[$other])) {
            $script:ShortcutError='shortcutDuplicate'
            return
        }
    }
    if ($null -ne $script:HotKeyMgr) {
        $index=[array]::IndexOf($script:ShortcutActions,$Action)
        $modifiers=if ($null -eq $Binding) {0} else {$Binding.modifiers}
        $key=if ($null -eq $Binding) {0} else {$Binding.key}
        if (-not $script:HotKeyMgr.Apply($index,[uint32]$modifiers,[uint32]$key)) {
            $script:ShortcutError='shortcutUnavailable'
            return
        }
    }
    $script:ShortcutInactive.Remove($Action)
    $script:Shortcuts[$Action]=$Binding
    Save-AppSettings
}

function Invoke-WidgetShortcut([string]$Action) {
    switch ($Action) {
        'quit' { Stop-Widget }
        'toggle' { Toggle-Widget }
        'volumeUp' { Set-WidgetVolume ($script:LocalVolume+.05) }
        'volumeDown' { Set-WidgetVolume ($script:LocalVolume-.05) }
        'pause' { Stop-TrackPreview -NoResume; Toggle-PlayPause }
        'next' { Stop-TrackPreview -NoResume; Next-Track }
    }
    if ($Action -ne 'quit' -and (Get-Command Publish-GlassWebState -ErrorAction SilentlyContinue)) {Publish-GlassWebState}
}

function Set-WidgetVolume([double]$Volume) {
    $script:LocalVolume=[math]::Max(0.0,[math]::Min(1.0,$Volume))
    if ($script:LocalVolume -gt 0) {$script:IsMuted=$false}
    Update-LocalVolumeUI
}

function Initialize-WidgetShortcuts {
    $script:HotKeyMgr=[GlobalHotKeyManager]::new()
    $script:HotKeyMgr.Add_Pressed({
        param([int]$ActionIndex)
        if (-not $script:ShortcutCapturing -and $ActionIndex -ge 0 -and $ActionIndex -lt $script:ShortcutActions.Count) {
            Invoke-WidgetShortcut $script:ShortcutActions[$ActionIndex]
        }
    })
    Register-WidgetShortcuts
}
