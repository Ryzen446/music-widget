#ifndef AppVersion
  #define AppVersion "6.1.4"
#endif

[Setup]
AppId={{1E38177A-0390-4FFC-82D4-2E59E78F7E98}
AppName=Music Widget
AppVersion={#AppVersion}
AppPublisher=Music Widget contributors
AppPublisherURL=https://github.com/Ryzen446/music-widget
AppSupportURL=https://github.com/Ryzen446/music-widget/issues
AppUpdatesURL=https://github.com/Ryzen446/music-widget/releases/latest
DefaultDirName={localappdata}\Programs\Music Widget
DefaultGroupName=Music Widget
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
DisableProgramGroupPage=yes
AllowNoIcons=yes
UninstallDisplayIcon={app}\MusicWidget.exe
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter=MusicWidget.exe,MusicWidget.Native.dll
SetupIconFile=..\assets\music_widget_glass.ico
OutputBaseFilename=MusicWidget-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#ifdef SmokeTest
CreateUninstallRegKey=no
#endif

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.OptionalFeatures=Optional features
fr.OptionalFeatures=Fonctions facultatives
en.OnlineTask=Listen to and download from YouTube (internet required)
fr.OnlineTask=Écouter et télécharger depuis YouTube (Internet requis)
en.EnableOnlineTools=Enable Music Widget online tools
fr.EnableOnlineTools=Activer les outils en ligne de Music Widget
en.LaunchWidget=Launch Music Widget
fr.LaunchWidget=Lancer Music Widget
en.InstallingOnlineTools=Installing online tools. This might take a few minutes…
fr.InstallingOnlineTools=Installation des outils en ligne. Cela peut prendre quelques minutes…
en.FrameworkMissing=Music Widget requires .NET Framework 4.8 or later. Install it from Microsoft, then run Setup again.
fr.FrameworkMissing=Music Widget nécessite .NET Framework 4.8 ou une version ultérieure. Installe-le depuis Microsoft, puis relance cet installateur.
en.OnlineToolsFailed=Music Widget is installed. Online tools could not be enabled this time. Try again from the Music Widget shortcuts in the Start menu.
fr.OnlineToolsFailed=Music Widget est installé. L’activation des outils en ligne a échoué. Tu peux réessayer depuis les raccourcis Music Widget du menu Démarrer.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "onlinetools"; Description: "{cm:OnlineTask}"; GroupDescription: "{cm:OptionalFeatures}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Music Widget"; Filename: "{app}\MusicWidget.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:EnableOnlineTools}"; Filename: "{app}\MusicWidget.exe"; Parameters: "--enable-online-tools"; WorkingDir: "{app}"
Name: "{userdesktop}\Music Widget"; Filename: "{app}\MusicWidget.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\MusicWidget.exe"; Description: "{cm:LaunchWidget}"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  FrameworkRelease: Cardinal;
begin
  Result := '';
  if RegQueryDWordValue(HKLM32, 'Software\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', FrameworkRelease) and
     (FrameworkRelease >= 528040) then
  begin
    Log('.NET Framework 4.8 or later is already installed.');
    Exit;
  end;
  Result := ExpandConstant('{cm:FrameworkMissing}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected('onlinetools') then
    Exit;

  WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallingOnlineTools}');
  if Exec(ExpandConstant('{app}\MusicWidget.exe'), '--enable-online-tools-quiet',
          ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0) then
    Log('Online tools installed.')
  else
    SuppressibleMsgBox(ExpandConstant('{cm:OnlineToolsFailed}'), mbInformation, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupPath, InstalledLauncher: String;
  Shell, Shortcut: Variant;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  StartupPath := ExpandConstant('{userstartup}\Music Widget.lnk');
  InstalledLauncher := ExpandConstant('{app}\MusicWidget.exe');
  if not FileExists(StartupPath) then
    Exit;

  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(StartupPath);
    if CompareText(Shortcut.TargetPath, InstalledLauncher) = 0 then
      DeleteFile(StartupPath);
  except
    Log(GetExceptionMessage);
  end;
end;
