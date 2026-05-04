@echo off
REM Script para ver logs de ActiMetrics
REM Uso: view-logs.bat [dias_atras]

if "%1"=="" (
    set DAYS=0
) else (
    set DAYS=%1
)

echo === ActiMetrics - Visor de Logs ===
echo.

REM Calcular fecha
for /f "tokens=2 delims==" %%i in ('wmic os get localdatetime /value') do set datetime=%%i
set year=%datetime:~0,4%
set month=%datetime:~4,2%
set day=%datetime:~6,2%

REM Calcular fecha del día solicitado
set /a target_day = %day% - %DAYS%
set target_date=%year%%month%%target_day%

echo Buscando logs del día: %target_date%
echo.

REM Buscar archivo de log
set LOG_FILE=logs\actimetrics-%target_date%.log

if exist "%LOG_FILE%" (
    echo Archivo encontrado: %LOG_FILE%
    echo.
    echo === CONTENIDO DEL LOG ===
    type "%LOG_FILE%"
) else (
    echo ❌ No se encontró el archivo de log: %LOG_FILE%
    echo.
    echo 📁 Archivos de log disponibles:
    dir /b logs\*.log 2>nul || echo Ningún archivo de log encontrado
)

echo.
echo === Información del Sistema ===
echo Fecha actual: %year%-%month%-%day%
echo Directorio: %CD%
echo.

pause