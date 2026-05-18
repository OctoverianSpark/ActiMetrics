#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define MyAppName    "ActiMetrics"
#define MyAppExe     "ActiMetrics.exe"
#define MyPublisher  "ActiMetrics"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
AppId={{B4C5D6E7-F8A9-0B1C-2D3E-4F5A6B7C8D9E}
DefaultDirName={autopf}\ActiMetrics\UI
DisableProgramGroupPage=yes
OutputDir=..\releases\{#MyAppVersion}
OutputBaseFilename=ActiMetrics.Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExe}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
MinVersion=10.0.17763
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Iconos adicionales:"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Iniciar {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]

procedure KillRunningInstance();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}') + '\taskkill.exe',
    '/f /im {#MyAppExe}',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
    KillRunningInstance();

  // En instalación silenciosa (actualización automática) reiniciar la app
  if (CurStep = ssPostInstall) and WizardSilent() then
    Exec(ExpandConstant('{app}') + '\{#MyAppExe}', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Deshabilita el inicio automático y mata instancias activas
    Exec(ExpandConstant('{app}') + '\{#MyAppExe}',
      '--checkInstall', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    // Elimina la tarea programada directamente
    Exec(ExpandConstant('{sys}') + '\schtasks.exe',
      '/delete /tn "ActiMetrics" /f',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Elimina la entrada de registro Run
    RegDeleteValue(HKEY_CURRENT_USER,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'ActiMetrics');
  end;
end;
