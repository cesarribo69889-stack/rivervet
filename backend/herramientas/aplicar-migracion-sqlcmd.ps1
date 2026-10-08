# Aplica UNA migracion en la base de PRODUCCION con sqlcmd.
#
# Para cuando Windows Smart App Control bloquea RiverVet.Migrador.dll en la PC
# ("Una directiva de Control de aplicaciones bloqueo este archivo"): sqlcmd es
# un programa firmado por Microsoft y no se bloquea.
#
# Uso (en PowerShell):
#   powershell -ExecutionPolicy Bypass -File C:\Users\ricra\Desktop\RIVERVET\backend\herramientas\aplicar-migracion-sqlcmd.ps1 0006_configuracion_yape.sql
#
# Hace lo mismo que el migrador: ejecuta la migracion y registra su nombre y
# su hash en dbo.migraciones_aplicadas, todo en UNA transaccion (si algo
# falla, no queda nada a medias). El hash se calcula igual que el migrador
# (SHA-256 del contenido sin BOM y con CRLF -> LF), asi despues lo reconoce.
# sqlcmd pide la clave de rivervet_migrador sin mostrarla.
# Antes hay que abrir el firewall de Azure SQL para la IP de esta PC.
param([Parameter(Mandatory = $true)][string]$Migracion)
$ErrorActionPreference = 'Stop'
$backend = Split-Path -Parent $PSScriptRoot
$archivo = Join-Path $backend "database\migraciones\$Migracion"
if (-not (Test-Path $archivo)) { throw "No existe $archivo" }
if ($Migracion -notmatch '^\d{4}_[a-z0-9_]+\.sql$') { throw "Nombre de migracion invalido: $Migracion" }

$contenido = [IO.File]::ReadAllText($archivo, [Text.Encoding]::UTF8)
if ($contenido.Length -gt 0 -and $contenido[0] -eq [char]0xFEFF) { $contenido = $contenido.Substring(1) }
$contenido = $contenido.Replace("`r`n", "`n")
$sha = [Security.Cryptography.SHA256]::Create()
$hash = -join ($sha.ComputeHash((New-Object Text.UTF8Encoding $false).GetBytes($contenido)) | ForEach-Object { $_.ToString('x2') })
Write-Host "Migracion: $Migracion"
Write-Host "Hash:      $hash"

$guion = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF EXISTS (SELECT 1 FROM dbo.migraciones_aplicadas WHERE nombre = '$Migracion')
    THROW 50001, 'Esa migracion ya estaba aplicada: no se hizo nada.', 1;
BEGIN TRANSACTION;
GO
:r "$archivo"
INSERT INTO dbo.migraciones_aplicadas (nombre, hash) VALUES ('$Migracion', '$hash');
COMMIT;
SELECT nombre, hash, aplicada_en FROM dbo.migraciones_aplicadas WHERE nombre = '$Migracion';
GO
"@
$temporal = Join-Path $env:TEMP "rivervet-aplicar-$Migracion"
[IO.File]::WriteAllText($temporal, $guion, (New-Object Text.UTF8Encoding $false))
try {
  & sqlcmd -S 'tcp:rivervet-sql-5nua9h.database.windows.net,1433' -d rivervet_db -U rivervet_migrador -N -l 60 -b -I -f 65001 -i $temporal
  if ($LASTEXITCODE -ne 0) { throw "sqlcmd termino con codigo $($LASTEXITCODE). La migracion NO se aplico." }
  Write-Host ''
  Write-Host "Listo: $Migracion aplicada y registrada." -ForegroundColor Green
} finally {
  Remove-Item $temporal -ErrorAction SilentlyContinue
}
