# ActiMetrics — Guía de desarrollo

---

## Arquitectura general

ActiMetrics está dividido en dos procesos independientes que conviven en el mismo equipo:

| Proceso | Proyecto | Tipo | Ejecuta como |
|---|---|---|---|
| **ActiMetrics.exe** | `ActiMetrics.UI` | Windows Forms + .NET Generic Host | Usuario normal (bandeja del sistema) |
| **ActiMetrics.Host.exe** | `ActiMetrics.Host` | Windows Service | SYSTEM (servicio de Windows) |

El **Host** se encarga del monitoreo de bajo nivel que requiere privilegios elevados. La **UI** gestiona la interacción con el usuario, el registro de estados, la sincronización con la API y las actualizaciones automáticas.

---

## Estructura de proyectos

```
ActiMetrics/
├── ActiMetrics.UI/          # Aplicación de bandeja — punto de entrada del usuario
├── ActiMetrics.Host/        # Servicio de Windows
├── ActiMetrics.Service/     # Lógica de negocio compartida (workers y servicios)
├── ActiMetrics.Data/        # Repositorios y acceso a SQLite
├── ActiMetrics.Shared/      # Modelos, enums e interfaces compartidos
└── scripts/
    ├── create-release.ps1   # Script principal de build y empaquetado
    ├── create-cert.ps1      # Generación del certificado de firma
    ├── uninstall-host.ps1   # Desinstalación del servicio Host
    └── view-logs.ps1        # Consulta rápida de logs
```

---

## Proyectos en detalle

### ActiMetrics.Service

Contiene toda la lógica de negocio. Se referencia tanto desde `ActiMetrics.UI` como desde `ActiMetrics.Host`.

| Clase | Responsabilidad |
|---|---|
| `TimerService` | Motor principal: gestiona estados, detecta inactividad, controla fin de jornada |
| `ActivityService` | Detecta actividad de ratón y teclado (P/Invoke) |
| `AppTrackerService` | Registra qué aplicación está en foco y cuánto tiempo |
| `SyncService` | Sincroniza estados, app usage y screenshots con la API REST |
| `WebSocketService` | Mantiene conexión WebSocket con el servidor para notificaciones en tiempo real |
| `TokenService` | Gestiona la autenticación con la API |
| `ScreenshotService` | Toma capturas de pantalla periódicas |

**Workers** (BackgroundService):

| Worker | Qué hace |
|---|---|
| `TimeWorker` | Llama a `TimerService.Tick()` cada segundo |
| `SyncWorker` | Llama a `SyncService.SyncAsync()` periódicamente |
| `UpdateWorker` | Comprueba actualizaciones cada 2 horas y aplica si hay nueva versión |
| `WebSocketWorker` | Mantiene vivo el bucle del WebSocket |
| `ScreenWorker` | Dispara capturas de pantalla en intervalos configurados |

---

### ActiMetrics.Data

Acceso a SQLite mediante repositorios. La base de datos se crea automáticamente en `AppContext.BaseDirectory`.

| Repositorio | Tabla |
|---|---|
| `StateRepository` | Estados de trabajo (log de cambios de estado) |
| `AppUsageRepository` | Tiempo por aplicación en cada intervalo |
| `ScreenshotRepository` | Rutas de capturas y estado de sincronización |
| `Session` | Email del usuario autenticado |

---

### ActiMetrics.Shared

Modelos y enums usados en toda la solución:

- `WorkState` — enum de estados (`Working`, `Overtime`, `Break`, `WC`, `Lunch`, `Idle`, `Offline`)
- `StateCategory` — `Active`, `Neutral`, `Inactive`
- `StateType` — `Auto` (detectado) vs `Manual` (elegido por el usuario)
- `ITrayService` — interfaz que desacopla `TimerService` de la UI

---

## Flujo de estados

```
Inicio
  └─► InitializeAsync()
        ├─ Sin programación hoy → Working (modo libre)
        └─ Con programación    → Working (o tardanza si pasó la gracia de 5 min)

Cada segundo (TimeWorker.Tick)
  └─► ActivityService.IsIdle()
        ├─ true  + estado Active  → cambia a Idle (Auto)
        └─ false + estado Idle    → cambia a Working (Auto)

Menú de bandeja
  └─► TimerService.SetStateAsync(state) → registra como StateType.Manual
        (los estados manuales no se sobreescriben por detección automática)
```

---

## Sistema de actualización

El `UpdateWorker` comprueba la URL configurada en `appsettings.json → UpdateUrl` cada 2 horas:

1. Compara la versión instalada con la del servidor.
2. Si hay una versión más nueva, descarga el instalador a `%TEMP%\ActiMetrics-Update-Setup.exe`.
3. Registra el lanzamiento del instalador en `ApplicationStopped`.
4. Llama a `StopApplication()` — la app se cierra completamente.
5. Al cerrarse, el instalador arranca solo.

El instalador (Velopack) llama al ejecutable con `--checkInstall` antes de instalar:
- Deshabilita la tarea programada (evita que el Task Scheduler relance la UI durante la instalación).
- Cierra cualquier instancia activa de `ActiMetrics.exe`.
- Tras instalar, `OnAfterUpdateFastCallback` recrea la tarea programada.

> El servicio **Host no se toca** en ningún paso de la actualización de la UI.

---

## Crear una release

```powershell
# Release completa (UI + Host)
.\scripts\create-release.ps1 -Version "1.2.0" -Target Full

# Solo UI (actualización sin tocar el Host)
.\scripts\create-release.ps1 -Version "1.2.1" -Target UI

# Solo Host
.\scripts\create-release.ps1 -Version "1.2.0" -Target Service

# Con notas de versión
.\scripts\create-release.ps1 -Version "1.2.0" -Target UI -Notes "Corrección de estados manuales"
```

El script requiere permisos de administrador (los solicita automáticamente). Genera la release en `scripts/releases/<version>/`.

### Firma de código (opcional)

Coloca el certificado `.pfx` en la raíz del proyecto como `ActiMetrics.pfx` y exporta la contraseña:

```powershell
$env:ACTIMETRICS_PFX_PASS = "tu-contraseña"
.\scripts\create-release.ps1 -Version "1.2.0"
```

---

## Configuración (`appsettings.json`)

```json
{
  "UpdateUrl": "https://actimetrics.asistentevirtualsas.com/tracer/updates",
  "Secret": "<token de autenticación>"
}
```

---

## Logs

Los logs se escriben con **Serilog** en archivos rotativos diarios:

| Proceso | Ruta |
|---|---|
| UI | `<install>\logs\app\actimetrics-YYYYMMDD.log` |
| Host | `<install>\logs\host\actimetrics-YYYYMMDD.log` |
| Actualizaciones | `<install>\logs\updates.log` |

Nivel mínimo: `Information`. Para activar `Debug`, modificar `appsettings.json`:

```json
"MinimumLevel": { "Default": "Debug" }
```

---

## Requisitos de desarrollo

- .NET 10 SDK
- Windows 10/11 (x64)
- `vpk` CLI de Velopack (el script de release lo instala automáticamente en la versión correcta)
- PowerShell 7+ para los scripts

---

## Instalación del Host en desarrollo

```powershell
# Instalar el servicio Host manualmente (requiere admin)
.\scripts\create-release.ps1 -Version "1.0.0" -Target Service

# O directamente con sc.exe
sc.exe create "ActiMetrics Host" binPath= "\"<ruta>\ActiMetrics.Host.exe\"" start= delayed-auto
sc.exe start "ActiMetrics Host"
```

Para desinstalar:

```powershell
.\scripts\uninstall-host.ps1
```
