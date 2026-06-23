#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef UISourceDir
  #define UISourceDir "..\publish"
#endif
#ifndef HostSourceDir
  #define HostSourceDir "..\publish-host"
#endif

#define MyAppName     "Go Tracer"
#define MyAppExe      "GoTracer.exe"
#define MyServiceName "Go Tracer Host"
#define MyServiceExe  "ActiMetrics.Host.exe"
#define MyPublisher   "Go Tracer"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
AppId={{D7E8F9A0-B1C2-3D4E-5F6A-7B8C9D0E1F2A}
DefaultDirName={autopf}\Go Tracer
DisableProgramGroupPage=yes
OutputDir=..\releases\{#MyAppVersion}
OutputBaseFilename=GoTracer.Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=assets\AppIcon.ico
UninstallDisplayIcon={app}\UI\{#MyAppExe}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
MinVersion=10.0.17763
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#UISourceDir}\*";   DestDir: "{app}\UI";   Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#HostSourceDir}\*"; DestDir: "{app}\Host"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Iconos adicionales:"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\UI\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\UI\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\UI\{#MyAppExe}"; Description: "Iniciar {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]

function ServiceExists(ServiceName: String): Boolean;
var ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}') + '\sc.exe', 'query "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

procedure StopAndDeleteService(ServiceName: String);
var ResultCode, I: Integer;
begin
  Exec(ExpandConstant('{sys}') + '\sc.exe', 'stop "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  for I := 1 to 20 do
  begin
    if not ServiceExists(ServiceName) then Break;
    Sleep(400);
  end;
  Exec(ExpandConstant('{sys}') + '\sc.exe', 'delete "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
end;

procedure InstallService(ExePath: String);
var ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'create "{#MyServiceName}" binPath= "\"' + ExePath + '\"" ' +
    'start= delayed-auto DisplayName= "{#MyServiceName}" obj= LocalSystem',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
  begin
    MsgBox('Error al registrar el servicio (código ' + IntToStr(ResultCode) + ').', mbError, MB_OK);
    Exit;
  end;
  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'failure "{#MyServiceName}" reset= 86400 actions= restart/5000/restart/15000/restart/30000',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}') + '\sc.exe', 'start "{#MyServiceName}"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Limpia archivos de instalaciones anteriores en la raíz (antes de agregar \Host y \UI)
procedure LimpiarRutaAnterior();
var FindRec: TFindRec; RootDir: String;
begin
  RootDir := ExpandConstant('{autopf}') + '\Go Tracer\';
  if not FileExists(RootDir + '{#MyServiceExe}') then Exit;
  if ServiceExists('{#MyServiceName}') then
    StopAndDeleteService('{#MyServiceName}');
  if FindFirst(RootDir + '*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        DeleteFile(RootDir + FindRec.Name);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    LimpiarRutaAnterior();
    Exec(ExpandConstant('{sys}') + '\taskkill.exe', '/f /im {#MyAppExe}',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
    if ServiceExists('{#MyServiceName}') then
      StopAndDeleteService('{#MyServiceName}');
  end;

  if CurStep = ssPostInstall then
    InstallService(ExpandConstant('{app}') + '\Host\{#MyServiceExe}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{app}') + '\UI\{#MyAppExe}', '--checkInstall',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    Exec(ExpandConstant('{sys}') + '\schtasks.exe', '/delete /tn "Go Tracer" /f',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    RegDeleteValue(HKEY_CURRENT_USER,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'Go Tracer');
    if ServiceExists('{#MyServiceName}') then
      StopAndDeleteService('{#MyServiceName}');
  end;
end;
