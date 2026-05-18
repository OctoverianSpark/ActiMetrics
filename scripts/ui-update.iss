#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef UISourceDir
  #define UISourceDir "..\publish"
#endif

#define MyAppName "ActiMetrics"
#define MyAppExe  "ActiMetrics.exe"
#define MyPublisher "ActiMetrics"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
AppId={{D7E8F9A0-B1C2-3D4E-5F6A-7B8C9D0E1F2A}
DefaultDirName={autopf}\ActiMetrics
DisableProgramGroupPage=yes
DisableWelcomePage=yes
DisableDirPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
OutputDir=..\releases\{#MyAppVersion}
OutputBaseFilename=ActiMetrics.UIUpdate-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=assets\AppIcon.ico
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
MinVersion=10.0.17763
CloseApplications=no
CreateUninstallRegKey=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#UISourceDir}\*"; DestDir: "{app}\UI"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]

procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec(ExpandConstant('{sys}') + '\taskkill.exe', '/f /im {#MyAppExe}',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;

  if CurStep = ssPostInstall then
    Exec(ExpandConstant('{app}') + '\UI\{#MyAppExe}', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;
