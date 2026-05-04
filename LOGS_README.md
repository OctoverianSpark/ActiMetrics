# Sistema de Logs Local - ActiMetrics

## 📋 Descripción

ActiMetrics incluye un sistema robusto de logging local que registra toda la actividad de la aplicación en archivos de texto. Esto permite diagnosticar problemas sin necesidad de debuggear código remotamente.

## 📁 Ubicación de Logs

Los logs se guardan en la carpeta `logs/` dentro del directorio de instalación de la aplicación:

```
ActiMetrics/
├── ActiMetrics.exe
├── logs/
│   ├── actimetrics-20260427.log    ← Log del día actual
│   ├── actimetrics-20260426.log    ← Log del día anterior
│   └── actimetrics-20260425.log    ← Log más antiguo
└── ...
```

## 📄 Formato de Log

Cada entrada de log incluye:
- **Timestamp**: Fecha y hora con zona horaria
- **Nivel**: [INF] Información, [WRN] Advertencia, [ERR] Error, [FTL] Fatal
- **Contexto**: Clase/método que generó el log
- **Mensaje**: Descripción del evento
- **Excepción**: Detalles completos si hay error

### Ejemplo:
```
2026-04-27 14:30:15.123 -05:00 [INF] ActiMetrics.Service.Workers.UpdateWorker Nueva versión disponible: 1.2.0
2026-04-27 14:30:16.456 -05:00 [INF] ActiMetrics.Service.Workers.UpdateWorker Descargando 45%
2026-04-27 14:30:20.789 -05:00 [ERR] ActiMetrics.Service.Services.SyncService Error de conexión a API
System.Net.Http.HttpRequestException: No such host is known
   at System.Net.Http.HttpClient.SendAsync(...)
```

## 🔍 Niveles de Log

- **Information [INF]**: Eventos normales (inicio, estados, sincronizaciones)
- **Warning [WRN]**: Situaciones que requieren atención pero no son errores
- **Error [ERR]**: Errores recuperables
- **Fatal [FTL]**: Errores críticos que terminan la aplicación

## 🛠️ Cómo Diagnosticar Problemas

### 1. **Verificar Inicio de Aplicación**
Busca estas líneas al inicio del log:
```
[INF] Iniciando ActiMetrics...
[INF] ActiMetrics.Service.Workers.TimeWorker Servicio iniciado correctamente
```

### 2. **Problemas de Conexión API**
Busca errores como:
```
[ERR] Error de conexión a API
[ERR] Token expirado
```

### 3. **Problemas de Actualización**
Busca logs de UpdateWorker:
```
[INF] Verificando actualizaciones...
[INF] Nueva versión disponible: X.X.X
[ERR] Error descargando actualización
```

### 4. **Problemas de Base de Datos**
Busca errores de SQLite:
```
[ERR] Error en base de datos
```

### 5. **Problemas de Capturas de Pantalla**
Busca logs de ScreenshotService:
```
[ERR] Error capturando pantalla
[INF] Screenshot capturado: screenshot_001.jpg
```

## 📊 Logs Principales por Servicio

### TimerService
- Estados de actividad (Working, Idle, Lunch)
- Conteo de horas diarias
- Detección de horarios programados

### SyncService
- Sincronización con API
- Envío de capturas y estados
- Eliminación de archivos locales

### UpdateWorker
- Verificación de actualizaciones
- Descargas de nuevas versiones
- Reinicios automáticos

### ScreenshotService
- Captura de pantallas múltiples
- Compresión y guardado local
- Programación de intervalos

## 🔧 Configuración de Logs

Los logs se configuran automáticamente. Si necesitas modificar:

1. Edita `appsettings.json` en el directorio de instalación
2. Cambia el `MinimumLevel` para más/menos detalle
3. Modifica el `outputTemplate` para cambiar el formato

### Configuración Actual:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "logs/actimetrics-.log",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```

## 📈 Rotación de Logs

- **Rotación diaria**: Un archivo por día
- **Retención**: Los archivos antiguos se mantienen (configurable)
- **Tamaño**: No hay límite de tamaño por archivo

## 🚨 Logs de Error Críticos

Si encuentras estos logs, indica problemas graves:

### Aplicación no inicia:
```
[FTL] La aplicación terminó inesperadamente
[ERR] Error crítico en inicialización
```

### Servicio Windows falla:
```
[ERR] Error instalando servicio
[ERR] Servicio no puede iniciarse
```

### Problemas de memoria/disco:
```
[ERR] Espacio en disco insuficiente
[WRN] Memoria baja detectada
```

## 📞 Soporte Técnico

Para soporte técnico:
1. **Adjunta el log del día del problema**
2. **Incluye logs de 2-3 días anteriores** si es un problema recurrente
3. **Indica la versión de ActiMetrics** (visible en logs de inicio)
4. **Describe los pasos** que realizaste antes del error

Los logs contienen toda la información necesaria para diagnosticar problemas sin acceso remoto al equipo.