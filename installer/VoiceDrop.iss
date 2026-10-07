; Inno Setup script for VoiceDrop. Build with installer\build.ps1
#define AppName "VoiceDrop"
#define AppVersion "0.1.0"

[Setup]
AppId={{6E8C2F0A-5D4B-4B0E-9C57-3A2D7B1F4E91}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=remco59
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=VoiceDrop-Setup-{#AppVersion}
SetupIconFile=..\assets\voicedrop.ico
UninstallDisplayIcon={app}\VoiceDrop.exe
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "autostart"; Description: "Start VoiceDrop when I sign in to Windows"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\VoiceDrop.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\VoiceDrop.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VoiceDrop"; \
  ValueData: """{app}\VoiceDrop.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\VoiceDrop.exe"; Description: "Launch VoiceDrop"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM VoiceDrop.exe"; Flags: runhidden; RunOnceId: "KillVoiceDrop"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\VoiceDrop');
    if DirExists(DataDir) then
      if MsgBox('Also delete your VoiceDrop settings, history and downloaded speech models?' + #13#10 +
                '(Keep them if you plan to reinstall.)', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
