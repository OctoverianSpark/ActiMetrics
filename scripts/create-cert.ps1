param(
  [string]$Subject = "ActiMetrics",
  [string]$OutPath = "$PSScriptRoot\..\ActiMetrics.pfx",
  [int]   $Years = 5
)

$ErrorActionPreference = "Stop"

# ── 1. Solicitar contraseña ───────────────────────────────────────────────────
$pass = Read-Host -Prompt "Contraseña para el PFX" -AsSecureString

# ── 2. Crear certificado autofirmado de firma de código ───────────────────────
Write-Host "Creando certificado autofirmado para '$Subject'..." -ForegroundColor Cyan

$cert = New-SelfSignedCertificate `
  -Subject         "CN=$Subject" `
  -Type            CodeSigningCert `
  -KeyUsage        DigitalSignature `
  -FriendlyName    "$Subject Code Signing" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -NotAfter        (Get-Date).AddYears($Years) `
  -HashAlgorithm   SHA256

Write-Host "  Thumbprint : $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "  Válido hasta: $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Gray

# ── 3. Exportar a PFX ────────────────────────────────────────────────────────
$outFull = [System.IO.Path]::GetFullPath($OutPath)
Export-PfxCertificate -Cert $cert -FilePath $outFull -Password $pass | Out-Null
Write-Host "PFX exportado → $outFull" -ForegroundColor Green

# ── 4. Instalar en Trusted Publishers (reduce advertencias SmartScreen) ───────
$install = Read-Host "¿Instalar en 'Trusted Publishers' para reducir alertas de SmartScreen? (s/N)"
if ($install -match '^[sS]$') {
  $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
    "TrustedPublisher", "LocalMachine")
  $store.Open("ReadWrite")
  $store.Add($cert)
  $store.Close()
  Write-Host "Certificado instalado en Trusted Publishers (LocalMachine)" -ForegroundColor Green
}

# ── 5. Instrucciones finales ──────────────────────────────────────────────────
Write-Host ""
Write-Host "Listo. Para usar el certificado en create-release.ps1:" -ForegroundColor Yellow
Write-Host "  `$env:ACTIMETRICS_PFX_PASS = '<tu-contraseña>'" -ForegroundColor White
Write-Host "  .\scripts\create-release.ps1 -Version 1.0.2" -ForegroundColor White
