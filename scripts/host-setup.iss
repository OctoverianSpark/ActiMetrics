#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish-host"
#endif

#define MyAppName    "ActiMetrics Host"
#define MyServiceName "ActiMetrics Host"
#define MyServiceExe  "ActiMetrics.Host.exe"
#define MyPublisher   "ActiMetrics"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
AppId={{F3A2B1C4-8D6E-4F0A-9B2C-3E5D7A1F8C4B}
DefaultDirName={autopf}\ActiMetrics\Host
DisableProgramGroupPage=yes
OutputDir=..\releases\{#MyAppVersion}
OutputBaseFilename=ActiMetrics.Host.Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyServiceExe}
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

[Code]

// ── Helpers de servicio ───────────────────────────────────────────────────────

function ServiceExists(ServiceName: String): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'query "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

procedure StopAndDeleteService(ServiceName: String);
var
  ResultCode: Integer;
  I: Integer;
begin
  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'stop "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Esperar hasta 8 s a que el proceso termine
  for I := 1 to 20 do
  begin
    if not ServiceExists(ServiceName) then Break;
    Sleep(400);
  end;

  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'delete "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
end;

procedure InstallService(ServiceName, DisplayName, ExePath: String);
var
  ResultCode: Integer;
begin
  // Crear con la ruta correctamente entrecomillada
  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'create "' + ServiceName + '" binPath= "\"' + ExePath + '\"" ' +
    'start= delayed-auto DisplayName= "' + DisplayName + '" obj= LocalSystem',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if ResultCode <> 0 then
  begin
    MsgBox('Error al crear el servicio (código ' + IntToStr(ResultCode) + ').'#13#10 +
           'Revisa que el exe esté en: ' + ExePath, mbError, MB_OK);
    Exit;
  end;

  // Acciones de recuperación ante fallos
  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'failure "' + ServiceName + '" reset= 86400 actions= restart/5000/restart/15000/restart/30000',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Iniciar
  Exec(ExpandConstant('{sys}') + '\sc.exe',
    'start "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// ── Limpieza de instalación anterior en raíz ──────────────────────────────────

procedure LimpiarRutaAnterior();
var
  RutaVieja: String;
  FindRec: TFindRec;
begin
  // La instalación vieja ponía los archivos directamente en {autopf}\ActiMetrics\
  RutaVieja := ExpandConstant('{autopf}') + '\ActiMetrics\';

  // Solo limpiar si existen archivos del Host sueltos en esa raíz
  if not FileExists(RutaVieja + '{#MyServiceExe}') then Exit;

  // Detener y eliminar el servicio viejo antes de borrar archivos
  if ServiceExists('{#MyServiceName}') then
    StopAndDeleteService('{#MyServiceName}');

  // Borrar archivos sueltos (no tocar subcarpetas como UI\, logs\, etc.)
  if FindFirst(RutaVieja + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
          DeleteFile(RutaVieja + FindRec.Name);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// ── Ciclo de instalación ──────────────────────────────────────────────────────

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExePath: String;
begin
  if CurStep = ssInstall then
    LimpiarRutaAnterior();

  if CurStep = ssPostInstall then
  begin
    ExePath := ExpandConstant('{app}') + '\{#MyServiceExe}';

    if ServiceExists('{#MyServiceName}') then
      StopAndDeleteService('{#MyServiceName}');

    InstallService('{#MyServiceName}', '{#MyServiceName}', ExePath);
  end;
end;

// ── Desinstalación ────────────────────────────────────────────────────────────

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    if ServiceExists('{#MyServiceName}') then
      StopAndDeleteService('{#MyServiceName}');
  end;
end;
