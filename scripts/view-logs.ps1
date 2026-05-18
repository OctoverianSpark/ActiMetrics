param(
  [ValidateSet("host", "app", "events")]
  [string]$Target = ""
)

$Root = Resolve-Path "$PSScriptRoot\.."

# ── Rutas de búsqueda ─────────────────────────────────────────────────────────
$HostLogDir = "$env:LOCALAPPDATA\ActiMetrics\current\logs\host"
$AppLogDirs  = @(
  "$Root\ActiMetrics.UI\bin\Debug\net10.0-windows\logs\app",
  "$Root\ActiMetrics.UI\bin\Release\net10.0-windows\logs\app",
  "$env:LOCALAPPDATA\ActiMetrics\current\logs\app",
  "$env:LOCALAPPDATA\ActiMetrics\app-*\logs\app"
)

function Get-LatestLog([string]$Dir) {
  if (-not (Test-Path $Dir)) { return $null }
  Get-ChildItem $Dir -Filter "*.log" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
}

function Show-Log([System.IO.FileInfo]$File) {
  if ($null -eq $File) { Write-Host "  (sin logs todavía)" -ForegroundColor DarkGray; return }
  Write-Host "  → $($File.FullName)" -ForegroundColor DarkGray
  Write-Host ""
  Get-Content $File.FullName -Wait -Tail 40
}

function Show-Events {
  try {
    $events = Get-EventLog -LogName Application -Source "ActiMetrics Host" -Newest 50 -ErrorAction Stop
    if (-not $events) { Write-Host "  Sin eventos registrados." -ForegroundColor DarkGray; return }
    $events | Format-Table TimeGenerated, EntryType, Message -AutoSize -Wrap
  } catch {
    Write-Host "  No se encontraron eventos para 'ActiMetrics Host'." -ForegroundColor DarkGray
    Write-Host "  (el servicio debe haberse ejecutado al menos una vez)" -ForegroundColor DarkGray
  }
}

# ── Menú ─────────────────────────────────────────────────────────────────────
if (-not $Target) {
  Write-Host ""
  Write-Host "  ActiMetrics — Ver logs" -ForegroundColor Cyan
  Write-Host "  ─────────────────────" -ForegroundColor DarkGray
  Write-Host "  [1] Host (servicio watchdog)"
  Write-Host "  [2] App  (interfaz de usuario)"
  Write-Host "  [3] Windows Event Log"
  Write-Host ""
  $op = Read-Host "  Selecciona"

  $Target = switch ($op) {
    "1" { "host"   }
    "2" { "app"    }
    "3" { "events" }
    default { Write-Host "Opción inválida." -ForegroundColor Red; exit 1 }
  }
}

Write-Host ""

switch ($Target) {
  "host" {
    Write-Host "Host logs (Ctrl+C para salir):" -ForegroundColor Cyan
    $log = Get-LatestLog $HostLogDir
    Show-Log $log
  }

  "app" {
    Write-Host "App logs (Ctrl+C para salir):" -ForegroundColor Cyan
    $log = $null
    foreach ($dir in $AppLogDirs) {
      $expanded = [System.IO.Path]::GetFullPath($dir)
      $log = Get-LatestLog $expanded
      if ($log) { break }
    }
    Show-Log $log
  }

  "events" {
    Write-Host "Windows Event Log — ActiMetrics Host:" -ForegroundColor Cyan
    Show-Events
  }
}
