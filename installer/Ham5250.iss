; Ham 5250 installer (Inno Setup 6). Built by GitHub Actions; see .github/workflows/build.yml.
;
; Interactive: asks for the IBM i host.
; Silent (GPO / Intune / PDQ):
;   Ham5250-Setup-x.y.z.exe /VERYSILENT /HOST=hamibm.corp.ham.com /PORT=992 /PREFIX=H /KIOSK=0 /LOCK=1
;     /HOST   IBM i host name or IP (writes the machine-wide default profile)
;     /PORT   992 = TLS (default), 23 = unencrypted
;     /PREFIX device-name prefix; the device becomes PREFIX + PC name, max 10 characters
;     /KIOSK  1 = open full screen and keep it open (Ctrl+Alt+Shift+F12 exits for IT)
;     /LOCK   1 = users can't change connection settings

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B5C1E2A4-5250-4F5A-9C3D-7A1E2B3C4D5E}
AppName=Ham 5250
AppVersion={#AppVersion}
AppVerName=Ham 5250 {#AppVersion}
AppPublisher=Night Tech Solutions
AppPublisherURL=https://nighttech.solutions
AppComments=IBM i 5250 terminal for Hamrick's, Inc.
DefaultDirName={autopf}\Ham 5250
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=Ham5250-Setup-{#AppVersion}
SetupIconFile=..\src\Hamricks5250.App\Assets\hamricks.ico
UninstallDisplayIcon={app}\Ham5250.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Put a Ham 5250 shortcut on the desktop"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\Ham5250.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Ham 5250"; Filename: "{app}\Ham5250.exe"
Name: "{autodesktop}\Ham 5250"; Filename: "{app}\Ham5250.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Ham5250.exe"; Description: "Open Ham 5250"; Flags: nowait postinstall skipifsilent

[Code]
var
  HostPage: TInputQueryWizardPage;

function ParamOr(const Name, Default: String): String;
begin
  Result := ExpandConstant('{param:' + Name + '|' + Default + '}');
end;

function JsonEscape(S: String): String;
begin
  StringChangeEx(S, '\', '\\', True);
  StringChangeEx(S, '"', '\"', True);
  Result := S;
end;

procedure InitializeWizard;
begin
  HostPage := CreateInputQueryPage(wpSelectTasks,
    'IBM i connection',
    'Which IBM i should Ham 5250 connect to?',
    'This becomes the default for everyone on this PC. Leave the host blank to set it later in Settings.');
  HostPage.Add('IBM i host name or IP address:', False);
  HostPage.Add('Port (992 = encrypted TLS, 23 = not encrypted):', False);
  HostPage.Values[0] := ParamOr('HOST', '');
  HostPage.Values[1] := ParamOr('PORT', '992');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Host, Tls, Kiosk, Lock, Json, Dir: String;
  Port: Integer;
begin
  if CurStep <> ssPostInstall then exit;
  Host := Trim(HostPage.Values[0]);
  if Host = '' then exit;
  Port := StrToIntDef(Trim(HostPage.Values[1]), 992);
  if Port = 23 then Tls := 'false' else Tls := 'true';
  if ParamOr('KIOSK', '0') = '1' then Kiosk := 'true' else Kiosk := 'false';
  if ParamOr('LOCK', '0') = '1' then Lock := 'true' else Lock := 'false';
  Dir := ExpandConstant('{commonappdata}\Hamricks5250');
  ForceDirectories(Dir);
  Json := '{' + #13#10 +
    '  "Profiles": [' + #13#10 +
    '    { "Name": "Hamrick''s IBM i", "Host": "' + JsonEscape(Host) + '", "Port": ' + IntToStr(Port) +
    ', "UseTls": ' + Tls + ', "DevicePrefix": "' + JsonEscape(ParamOr('PREFIX', 'H')) + '" }' + #13#10 +
    '  ],' + #13#10 +
    '  "DefaultProfile": "Hamrick''s IBM i",' + #13#10 +
    '  "Kiosk": ' + Kiosk + ',' + #13#10 +
    '  "LockProfiles": ' + Lock + #13#10 +
    '}' + #13#10;
  SaveStringToFile(Dir + '\settings.json', Json, False);
end;
