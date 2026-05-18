param(
  [string]$ServiceName = "ActiMetrics Host",
  [string]$InstallDir  = "$env:ProgramFiles\ActiMetrics\Host"
)

$ErrorActionPreference = "Stop"

# ── Auto-elevación UAC ────────────────────────────────────────────────────────
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  Start-Process pwsh -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -ServiceName `"$ServiceName`" -InstallDir `"$InstallDir`""
  exit
}

# ── Buscar el exe fuente ──────────────────────────────────────────────────────
$sourceExe = Join-Path $PSScriptRoot "ActiMetrics.Host.exe"
if (-not (Test-Path $sourceExe)) {
  Write-Error "No se encontró ActiMetrics.Host.exe junto a este script ($PSScriptRoot)."
  exit 1
}

Write-Host ""
Write-Host "Instalando ActiMetrics Host" -ForegroundColor Cyan
Write-Host "  Origen  : $PSScriptRoot"   -ForegroundColor Gray
Write-Host "  Destino : $InstallDir"     -ForegroundColor Gray
Write-Host ""

# ── Detener y eliminar servicio existente ─────────────────────────────────────
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
  Write-Host "Deteniendo servicio existente..." -ForegroundColor Yellow
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

  $deadline = (Get-Date).AddSeconds(10)
  while ((Get-Date) -lt $deadline) {
    $procs = Get-Process -Name "ActiMetrics.Host" -ErrorAction SilentlyContinue
    if (-not $procs) { break }
    Start-Sleep -Milliseconds 400
  }
  Get-Process -Name "ActiMetrics.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

  sc.exe delete $ServiceName | Out-Null
  Start-Sleep -Seconds 1
}

# ── Copiar binarios ───────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem $PSScriptRoot -File | Where-Object { $_.Name -ne "install-host.ps1" } | ForEach-Object {
  Copy-Item $_.FullName -Destination $InstallDir -Force
}
Write-Host "Archivos copiados." -ForegroundColor Gray

# ── Crear e iniciar servicio ──────────────────────────────────────────────────
$hostExe = Join-Path $InstallDir "ActiMetrics.Host.exe"

New-Service -Name $ServiceName `
            -BinaryPathName "`"$hostExe`"" `
            -StartupType AutomaticDelayed `
            -DisplayName $ServiceName `
            -Description "Servicio watchdog que mantiene ActiMetrics activo."

sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null

Start-Service -Name $ServiceName

$status = (Get-Service -Name $ServiceName).Status
Write-Host ""
Write-Host "Servicio '$ServiceName': $status" -ForegroundColor Green
Write-Host ""
