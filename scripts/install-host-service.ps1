$ServiceName = "ActiMetrics Host"

# Auto-elevación UAC si no se está corriendo como administrador
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

# Obtener el usuario real de la sesión activa (no el admin elevado)
$explorer = Get-CimInstance Win32_Process -Filter "name='explorer.exe'" |
    Select-Object -First 1

if (-not $explorer) {
    Write-Error "No se encontró explorer.exe — no hay sesión de usuario activa."
    exit 1
}

$owner      = Invoke-CimMethod -InputObject $explorer -MethodName GetOwner
$perfil     = (Get-CimInstance Win32_UserProfile |
                Where-Object { $_.LocalPath -match [regex]::Escape($owner.User) } |
                Select-Object -First 1).LocalPath

if (-not $perfil) {
    Write-Error "No se encontró el perfil del usuario '$($owner.User)'."
    exit 1
}

$localAppData = Join-Path $perfil "AppData\Local"
Write-Host "Usuario real : $($owner.Domain)\$($owner.User)"
Write-Host "LocalAppData : $localAppData"

# Buscar el ejecutable en el perfil real del usuario
$candidatos = @(
    (Join-Path $localAppData "ActiMetrics\current\ActiMetrics.Host.exe"),
    (Join-Path (Split-Path $PSCommandPath) "..\ActiMetrics.Host\bin\Release\net10.0-windows\ActiMetrics.Host.exe"),
    (Join-Path (Split-Path $PSCommandPath) "..\ActiMetrics.Host\bin\Debug\net10.0-windows\ActiMetrics.Host.exe")
)

$hostExe = $candidatos | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $hostExe) {
    Write-Error "No se encontró ActiMetrics.Host.exe."
    exit 1
}

Write-Host "Exe : $hostExe"

# Detener y eliminar servicio existente
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
sc.exe delete $ServiceName 2>$null
Start-Sleep -Seconds 1

# Crear e iniciar
New-Service -Name $ServiceName -BinaryPathName "`"$hostExe`"" -StartupType AutomaticDelayed -DisplayName $ServiceName
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/30000
Start-Service -Name $ServiceName

Get-Service -Name $ServiceName | Format-List Name, Status, StartType
