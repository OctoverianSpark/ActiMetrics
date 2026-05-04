# ActiMetrics

Sistema de monitoreo de actividad y tiempo de trabajo con capturas de pantalla automáticas.

## 🚀 Características

- ✅ Monitoreo automático de actividad
- ✅ Capturas de pantalla programadas
- ✅ Sincronización con API web
- ✅ Actualizaciones automáticas
- ✅ **Sistema de logs local completo** 📋
- ✅ Interfaz de bandeja del sistema
- ✅ Instalación como servicio Windows

## 📋 Sistema de Logs

ActiMetrics incluye un **sistema robusto de logging local** para diagnosticar problemas sin debuggear remotamente.

### 📁 Ubicación de Logs
```
ActiMetrics/
└── logs/
    ├── actimetrics-20260427.log    ← Log del día actual
    ├── actimetrics-20260426.log    ← Día anterior
    └── ...
```

### 🔍 Ver Logs
```cmd
# Ver log de hoy
view-logs.bat

# Ver log de hace 2 días
view-logs.bat 2
```

### 📖 Documentación Completa
Ver [LOGS_README.md](LOGS_README.md) para información detallada sobre:
- Formato de logs
- Niveles de logging
- Diagnóstico de problemas
- Configuración avanzada

## 🛠️ Instalación

1. Descargar el instalador desde GitHub Releases
2. Ejecutar como administrador
3. Seguir el asistente de instalación

## 🔧 Uso

- La aplicación se ejecuta automáticamente al iniciar Windows
- Icono en bandeja del sistema para control manual
- Logs disponibles en carpeta `logs/` para diagnóstico

## 📊 Servicios Incluidos

- **ActiMetrics.exe**: Interfaz principal y servicios en segundo plano
- **ActiMetrics.Host.exe**: Servicio Windows (opcional)

## 🆘 Soporte

Para problemas técnicos:
1. Revisar logs en carpeta `logs/`
2. Adjuntar log del día del problema
3. Describir pasos que causaron el error

