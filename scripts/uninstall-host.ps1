param([string]$ServiceName = "ActiMetrics Host")

$ErrorActionPreference = "Stop"

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
  Write-Warning "El servicio '$ServiceName' no existe."
  exit 0
}

if ($svc.Status -ne "Stopped") {
  Write-Host "Deteniendo '$ServiceName'..." -ForegroundColor Yellow
  Stop-Service -Name $ServiceName -Force
}

sc.exe delete $ServiceName | Out-Null
Write-Host "Servicio '$ServiceName' eliminado." -ForegroundColor Green
