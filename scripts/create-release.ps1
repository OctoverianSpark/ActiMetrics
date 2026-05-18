param(
  [string]$Version = "1.0.0",
  [string]$Notes = ""
)

$ErrorActionPreference = "Stop"

$Root           = Resolve-Path "$PSScriptRoot\.."
$PublishDir     = "$Root\publish"
$HostPublishDir = "$Root\publish-host"
$ReleasesDir    = "$Root\releases"
$VersionDir     = "$ReleasesDir\$Version"

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ActiMetrics  v$Version" -ForegroundColor White
if ($Notes) { Write-Host "  Notas: $Notes" -ForegroundColor Gray }
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

New-Item -ItemType Directory -Force -Path $VersionDir | Out-Null

# ── 0. Sincronizar vpk CLI con la versión del paquete Velopack ────────────────
$csprojXml = [xml](Get-Content "$Root\ActiMetrics.UI\ActiMetrics.UI.csproj")
$velopackVersion = ($csprojXml.Project.ItemGroup.PackageReference |
  Where-Object { $_.Include -eq "Velopack" }).Version

if ($velopackVersion) {
  Write-Host "Sincronizando vpk CLI v$velopackVersion..." -ForegroundColor Cyan
  dotnet tool update -g vpk --version $velopackVersion 2>$null
  if ($LASTEXITCODE -ne 0) {
    dotnet tool install -g vpk --version $velopackVersion
    if ($LASTEXITCODE -ne 0) { throw "No se pudo instalar/actualizar vpk CLI" }
  }
} else {
  Write-Host "Versión de Velopack no encontrada; se usará vpk instalado." -ForegroundColor Yellow
}

# ── 1. Actualizar versión ─────────────────────────────────────────────────────
$manifestPath = "$Root\ActiMetrics.UI\app.manifest"
(Get-Content $manifestPath -Raw) -replace 'version="[^"]*"(\s+name="ActiMetrics\.app")', "version=""$Version.0""`$1" |
Set-Content $manifestPath -NoNewline
Write-Host "app.manifest → $Version.0" -ForegroundColor Cyan

$csprojPath = "$Root\ActiMetrics.UI\ActiMetrics.UI.csproj"
$csprojContent = Get-Content $csprojPath -Raw
$csprojContent = if ($csprojContent -match '<Version>') {
  $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
} else {
  $csprojContent -replace '(<PropertyGroup>)', "`$1`n    <Version>$Version</Version>"
}
Set-Content $csprojPath -Value $csprojContent -NoNewline
Write-Host "ActiMetrics.UI.csproj → $Version" -ForegroundColor Cyan

# ── 2. Publicar ───────────────────────────────────────────────────────────────
Write-Host "Publicando ActiMetrics.UI..." -ForegroundColor Cyan
dotnet publish "$Root\ActiMetrics.UI" -c Release -r win-x64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish UI falló" }

Write-Host "Publicando ActiMetrics.Host..." -ForegroundColor Cyan
dotnet publish "$Root\ActiMetrics.Host" -c Release -r win-x64 --self-contained true -o $HostPublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Host falló" }

# ── 3. Empaquetar UI con Velopack ─────────────────────────────────────────────
Write-Host "Empaquetando UI con Velopack..." -ForegroundColor Cyan
vpk pack `
  -u ActiMetrics `
  -v $Version `
  --packDir $PublishDir `
  -o $VersionDir `
  --mainExe ActiMetrics.exe `
  --icon "$PSScriptRoot\assets\AppIcon.ico"
if ($LASTEXITCODE -ne 0) { throw "vpk pack falló" }
Write-Host "  → ActiMetrics-$Version-Setup.exe (Velopack)" -ForegroundColor Green

# ── 4. Localizar Inno Setup ───────────────────────────────────────────────────
$iscc = @(
  "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  (Get-Command ISCC.exe -ErrorAction SilentlyContinue)?.Source
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
  throw "Inno Setup no encontrado. Instálalo en: https://jrsoftware.org/isdl.php"
}
Write-Host "Inno Setup: $iscc" -ForegroundColor Cyan

# ── 5. Instalador del Host ────────────────────────────────────────────────────
Write-Host "Generando instalador del Host..." -ForegroundColor Cyan
& $iscc `
  "/DMyAppVersion=$Version" `
  "/DSourceDir=$HostPublishDir" `
  "$PSScriptRoot\host-setup.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe falló (host-setup)" }
Write-Host "  → ActiMetrics.Host.Setup-$Version.exe" -ForegroundColor Green

# ── Limpieza ──────────────────────────────────────────────────────────────────
Remove-Item -Recurse -Force $PublishDir     -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $HostPublishDir -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Release $Version lista en: $VersionDir" -ForegroundColor Green
Write-Host "  UI   (Velopack) : $VersionDir\ActiMetrics-$Version-Setup.exe"      -ForegroundColor Gray
Write-Host "  Host (Inno)     : $VersionDir\ActiMetrics.Host.Setup-$Version.exe" -ForegroundColor Gray
if ($Notes) { Write-Host "Notas: $Notes" -ForegroundColor Gray }
Write-Host ""
