<#
═══════════════════════════════════════════════════════════════════════
 configurar-desarrollo.ps1 — deja RiverVet listo para ejecutarse en esta PC

 Ejecútalo UNA vez (o cuando quieras regenerar las credenciales), desde la
 carpeta backend, en PowerShell:

     .\configurar-desarrollo.ps1
     .\configurar-desarrollo.ps1 -Servidor "localhost\SQLEXPRESS"

 Qué hace:
   1. Genera contraseñas aleatorias para los logins rivervet_app y
      rivervet_migrador, un secreto JWT de 256 bits y el secreto de los
      webhooks de la pasarela de pagos SIMULADA (solo desarrollo).
   2. Crea la base rivervet_db y los dos logins (database/local/01_...sql).
   3. Guarda las cadenas de conexión y el secreto en los "User Secrets" de
      .NET (%APPDATA%\Microsoft\UserSecrets\rivervet-dev\secrets.json).
      Quedan FUERA del repositorio: nunca se suben a GitHub.
   4. Aplica las migraciones, da los permisos mínimos a la app
      (database/local/02_...sql) y carga los datos de prueba.

 Luego: abre RiverVet.slnx en Visual Studio, elige "IIS Express" y pulsa el
 botón verde. Se abrirá Swagger en el navegador.
═══════════════════════════════════════════════════════════════════════
#>
param(
    [string]$Servidor = ".\SQLEXPRESS",
    [string]$BaseDatos = "rivervet_db"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Nueva-Clave([int]$largo = 24) {
    # Generador criptográfico. Siempre incluye mayúscula, minúscula, número y
    # símbolo para cumplir la política de contraseñas de SQL Server.
    $alfabeto = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] $largo
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $cuerpo = -join ($bytes | ForEach-Object { $alfabeto[$_ % $alfabeto.Length] })
    return "Rv#9" + $cuerpo
}

Write-Host "1/5  Verificando SQL Server en $Servidor ..." -ForegroundColor Cyan
& sqlcmd -S $Servidor -E -C -b -Q "SET NOCOUNT ON; SELECT @@VERSION" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No se pudo conectar a $Servidor con autenticación de Windows." }
$modo = (& sqlcmd -S $Servidor -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS INT)").Trim()
if ($modo -eq "1") {
    throw "SQL Server solo acepta autenticación de Windows. Activa el modo mixto (SSMS > Propiedades del servidor > Seguridad) y reinicia el servicio."
}

$claveApp = Nueva-Clave
$claveMigrador = Nueva-Clave
$bytesJwt = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytesJwt)
$secretoJwt = -join ($bytesJwt | ForEach-Object { $_.ToString("x2") })

Write-Host "2/5  Creando la base $BaseDatos y los logins ..." -ForegroundColor Cyan
& sqlcmd -S $Servidor -E -C -I -b -v APP_PASSWORD="$claveApp" MIGRADOR_PASSWORD="$claveMigrador" -i "database\local\01_crear_bd_y_usuarios.sql"
if ($LASTEXITCODE -ne 0) { throw "Falló 01_crear_bd_y_usuarios.sql" }

Write-Host "3/5  Guardando credenciales en User Secrets (fuera del repositorio) ..." -ForegroundColor Cyan
$base = "Server=$Servidor;Database=$BaseDatos;Encrypt=True;TrustServerCertificate=True"
$proyecto = "src\RiverVet.Api\RiverVet.Api.csproj"
dotnet user-secrets set "ConnectionStrings:RiverVet" "$base;User Id=rivervet_app;Password=$claveApp" --project $proyecto | Out-Null
dotnet user-secrets set "ConnectionStrings:Migrador" "$base;User Id=rivervet_migrador;Password=$claveMigrador" --project $proyecto | Out-Null
dotnet user-secrets set "Jwt:Secreto" $secretoJwt --project $proyecto | Out-Null
# Pasarela SIMULADA (solo desarrollo) con su propio secreto de webhooks.
# Sirve para probar el pago con tarjeta de punta a punta sin mover dinero;
# el aviso de pago se simula con herramientas\simular-webhook.ps1.
$bytesWebhook = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytesWebhook)
$secretoWebhook = -join ($bytesWebhook | ForEach-Object { $_.ToString("x2") })
dotnet user-secrets set "Pagos:Pasarela:Proveedor" "simulada" --project $proyecto | Out-Null
dotnet user-secrets set "Pagos:Pasarela:SecretoWebhook" $secretoWebhook --project $proyecto | Out-Null

Write-Host "4/5  Aplicando migraciones y permisos ..." -ForegroundColor Cyan
dotnet run --project src\RiverVet.Migrador -- migrar
if ($LASTEXITCODE -ne 0) { throw "Fallaron las migraciones" }
& sqlcmd -S $Servidor -E -C -I -b -i "database\local\02_permisos_app.sql"
if ($LASTEXITCODE -ne 0) { throw "Falló 02_permisos_app.sql" }

Write-Host "5/5  Cargando datos de prueba ..." -ForegroundColor Cyan
dotnet run --project src\RiverVet.Migrador -- seed
if ($LASTEXITCODE -ne 0) { throw "Falló el seed" }
dotnet run --project src\RiverVet.Migrador -- esquema

Write-Host ""
Write-Host "Listo." -ForegroundColor Green
Write-Host "  Abre RiverVet.slnx en Visual Studio, elige 'IIS Express' y pulsa el botón verde."
Write-Host "  Usuarios de prueba (contraseña RiverVet#2026): admin@rivervet.pe, recepcion@rivervet.pe,"
Write-Host "  vet.lopez@rivervet.pe, ana.torres@correo.pe"
