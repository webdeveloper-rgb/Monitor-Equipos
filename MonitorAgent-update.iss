; Instalador SOLO de actualización del agente.
; Guardar en el repositorio Monitor-Equipos como: installer/MonitorAgent-update.iss
;
; Hace:    copia MonitorAgent.exe, MonitorAgent.exe.config y Newtonsoft.Json.dll
;          en {autopf}\MonitorAgent (la misma carpeta que el instalador completo) y arranca el servicio.
; No hace: no instala RustDesk ni .NET, no ejecuta "--install" (que necesita el token) y no toca
;          config.json / state.json (están en C:\ProgramData\MonitorAgent). Por eso NO lleva token.
;
; No incluir monitor-secrets.iss: este .exe se publica en un repositorio público.
;
; Compilar a mano (después de: dotnet build -c Release):
;   & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" "/DMyAppVersion=1.3.6.0" "installer\MonitorAgent-update.iss"
; Resultado: installer\Output\SauberDesk-Agent-1.3.6.0-setup.exe

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define ServiceName       "MonitorAgent"
#define MonitorInstallDir "{autopf}\MonitorAgent"
; Carpeta con el agente compilado (Release). Orden de preferencia:
;   1) /DBuildDir=... pasado por línea de comandos
;   2) la carpeta local de este equipo, si existe
;   3) ruta relativa a este .iss (installer/), que es la que usa GitHub Actions
#ifndef BuildDir
  #define LocalBuildDir "C:\Users\Usuario\Desktop\Cosas Webs y CRM\webs\Monitor-Equipos\src\MonitorAgent\bin\Release\net48"
  #if FileExists(LocalBuildDir + "\MonitorAgent.exe")
    #define BuildDir LocalBuildDir
  #else
    #define BuildDir "..\src\MonitorAgent\bin\Release\net48"
  #endif
#endif

[Setup]
AppId={{E6DDF1B4-01C6-4B6E-8817-94B18634B24D}
AppName=Sauber Desk - Agente
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
CreateAppDir=no
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=no
CreateUninstallRegKey=no
OutputDir=Output
OutputBaseFilename=SauberDesk-Agent-{#MyAppVersion}-setup
SolidCompression=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#BuildDir}\MonitorAgent.exe";        DestDir: "{#MonitorInstallDir}"; Flags: ignoreversion
Source: "{#BuildDir}\MonitorAgent.exe.config"; DestDir: "{#MonitorInstallDir}"; Flags: ignoreversion
Source: "{#BuildDir}\Newtonsoft.Json.dll";     DestDir: "{#MonitorInstallDir}"; Flags: ignoreversion

[Run]
; Si el actualizador del agente ya lo arrancó, no pasa nada.
Filename: "{sys}\net.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden

[Code]
function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Solo actualiza instalaciones existentes: el registro del servicio y el token los gestiona
// el instalador completo (--install).
function InitializeSetup(): Boolean;
begin
  Result := ServiceExists();
  if not Result then
  begin
    Log('El servicio {#ServiceName} no existe: este instalador solo actualiza instalaciones previas.');
    SuppressibleMsgBox('El agente no está instalado en este equipo. Usa el instalador completo de SauberDesk.',
      mbError, MB_OK, IDOK);
  end;
end;

// El actualizador ya detiene el servicio; así también funciona si se ejecuta a mano.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName}', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
