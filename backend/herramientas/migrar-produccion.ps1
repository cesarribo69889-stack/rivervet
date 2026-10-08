# Aplica las migraciones pendientes en la base de PRODUCCION (Azure SQL).
#
# Uso (en PowerShell, desde cualquier carpeta):
#   powershell -ExecutionPolicy Bypass -File C:\Users\ricra\Desktop\RIVERVET\backend\herramientas\migrar-produccion.ps1
#
# Pide la clave del usuario rivervet_migrador (MIGRADOR_PASSWORD en el gestor
# de contrasenas) sin mostrarla ni guardarla: solo vive en esta ventana.
# Antes hay que abrir el firewall de Azure SQL para la IP de esta PC.
# Hace tres pasos y se detiene en el primero que falle:
#   verificar -> las migraciones ya aplicadas no cambiaron (hash)
#   migrar    -> aplica las pendientes
#   esquema   -> tablas y restricciones criticas en su lugar
$ErrorActionPreference = 'Stop'
$servidor = 'rivervet-sql-5nua9h.database.windows.net'
$backend = Split-Path -Parent $PSScriptRoot

$segura = Read-Host 'Clave del usuario rivervet_migrador (MIGRADOR_PASSWORD)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura)
try {
  $clave = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  $env:ConnectionStrings__Migrador = "Server=tcp:$servidor,1433;Database=rivervet_db;User Id=rivervet_migrador;Password=$clave;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60"
  $env:DOTNET_ENVIRONMENT = 'Production'
  Set-Location $backend
  foreach ($paso in 'verificar', 'migrar', 'esquema') {
    Write-Host ''
    Write-Host "== $paso ==" -ForegroundColor Cyan
    & dotnet run --project src\RiverVet.Migrador --configuration Release -- $paso
    if ($LASTEXITCODE -ne 0) { throw "El paso '$paso' fallo (codigo $LASTEXITCODE). No se siguio con los demas." }
  }
  Write-Host ''
  Write-Host 'Listo: migraciones aplicadas y esquema verificado.' -ForegroundColor Green
} finally {
  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
  Remove-Item Env:ConnectionStrings__Migrador, Env:DOTNET_ENVIRONMENT -ErrorAction SilentlyContinue
  $clave = $null
}
