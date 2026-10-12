# Exporta la base de PRODUCCION (estructura + datos) a un solo archivo .sql.
#
# Uso (en PowerShell):
#   powershell -ExecutionPolicy Bypass -File C:\Users\ricra\Desktop\RIVERVET\backend\herramientas\exportar-base.ps1
#
# SOLO LEE la base (SELECT). Pide la clave de rivervet_migrador sin mostrarla.
# Antes hay que abrir el firewall de Azure SQL para la IP de esta PC.
#
# El archivo queda en Descargas y trae, en orden:
#   1. la estructura: las migraciones de database/migraciones (0001, 0002, ...)
#      y la tabla de control del migrador;
#   2. los datos de todas las tablas (con IDENTITY_INSERT y las restricciones
#      apagadas mientras se cargan, y verificadas otra vez al final);
#   3. los contadores (SEQUENCE) en el mismo punto que en produccion.
# Para restaurar: crear una base vacia en SQL Server, abrir el archivo en SSMS
# sobre esa base y ejecutarlo (F5).
#
# OJO: el archivo tiene datos personales y hashes de contrasenas. No subirlo a
# GitHub ni compartirlo.
param([string]$Usuario = 'rivervet_migrador')
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$backend = Split-Path -Parent $PSScriptRoot
$servidor = 'rivervet-sql-5nua9h.database.windows.net'
$fecha = Get-Date -Format 'yyyy-MM-dd_HHmm'
$salida = Join-Path ([Environment]::GetFolderPath('UserProfile')) "Downloads\rivervet_db_$fecha.sql"

function Literal($v) {
  if ($null -eq $v -or $v -is [DBNull]) { return 'NULL' }
  if ($v -is [string]) {
    $s = $v.Replace("'", "''")
    if ($s -notmatch "[`r`n]") { return "N'" + $s + "'" }
    # Los saltos de linea van como NCHAR(13)/NCHAR(10): una linea que solo
    # dijera "GO" dentro de un texto partiria el script en dos.
    $s = $s.Replace("`r", "' + NCHAR(13) + N'").Replace("`n", "' + NCHAR(10) + N'")
    return "(CAST(N'' AS NVARCHAR(MAX)) + N'" + $s + "')"
  }
  if ($v -is [bool]) { if ($v) { return '1' } else { return '0' } }
  if ($v -is [datetime]) { return "'" + $v.ToString('yyyy-MM-ddTHH:mm:ss.fffffff', $inv) + "'" }
  if ($v -is [datetimeoffset]) { return "'" + $v.ToString('yyyy-MM-ddTHH:mm:ss.fffffffzzz', $inv) + "'" }
  if ($v -is [timespan]) { return "'" + $v.ToString('c', $inv) + "'" }
  if ($v -is [guid]) { return "'" + $v.ToString() + "'" }
  if ($v -is [byte[]]) { return '0x' + [BitConverter]::ToString($v).Replace('-', '') }
  if ($v -is [double] -or $v -is [single]) { return $v.ToString('R', $inv) }
  return [Convert]::ToString($v, $inv)
}

function Consultar($conexion, $sql) {
  $cmd = $conexion.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 120
  $tabla = New-Object Data.DataTable
  $lector = $cmd.ExecuteReader(); $tabla.Load($lector); $lector.Close()
  return , $tabla
}

$segura = Read-Host "Clave de $Usuario" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura)
try {
  $clave = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  $cadena = "Server=tcp:$servidor,1433;Database=rivervet_db;User ID=$Usuario;Password=$clave;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90"
  $conexion = New-Object Data.SqlClient.SqlConnection $cadena
  # La base se pausa tras una hora sin uso: el primer intento puede fallar mientras despierta.
  for ($i = 1; $i -le 4; $i++) {
    try { $conexion.Open(); break }
    catch { if ($i -eq 4) { throw }; Write-Host "La base esta despertando, reintento en 20 s... ($i/3)"; Start-Sleep 20 }
  }
} finally {
  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr); $clave = $null
}

$escritor = New-Object IO.StreamWriter($salida, $false, (New-Object Text.UTF8Encoding $true))
try {
  $tablas = Consultar $conexion @"
SELECT t.name AS tabla, SUM(p.rows) AS filas
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.schema_id = SCHEMA_ID('dbo') GROUP BY t.name ORDER BY t.name
"@
  $escritor.WriteLine("-- ===================================================================")
  $escritor.WriteLine("-- RiverVet: copia de la base rivervet_db (estructura + datos)")
  $escritor.WriteLine("-- Exportada de produccion el $(Get-Date -Format 'yyyy-MM-dd HH:mm') (hora de esta PC).")
  $escritor.WriteLine("-- Restaurar: crear una base VACIA, abrir este archivo en SSMS sobre ella y ejecutar (F5).")
  $escritor.WriteLine("-- Contiene datos personales y hashes de contrasenas: no subir a GitHub ni compartir.")
  $escritor.WriteLine("--")
  $escritor.WriteLine("-- Filas por tabla al exportar:")
  foreach ($t in $tablas.Rows) { $escritor.WriteLine(("--   {0,-24} {1,8}" -f $t.tabla, $t.filas)) }
  $escritor.WriteLine("-- ===================================================================")
  $escritor.WriteLine("SET QUOTED_IDENTIFIER ON;")
  $escritor.WriteLine("SET ANSI_NULLS ON;")
  $escritor.WriteLine("GO")

  # 1. Estructura: las migraciones tal cual estan en el repo.
  $escritor.WriteLine("")
  $escritor.WriteLine("-- ############################ ESTRUCTURA ############################")
  foreach ($m in Get-ChildItem (Join-Path $backend 'database\migraciones') -Filter '*.sql' | Sort-Object Name) {
    $escritor.WriteLine("")
    $escritor.WriteLine("-- ======== $($m.Name) ========")
    $escritor.WriteLine([IO.File]::ReadAllText($m.FullName, [Text.Encoding]::UTF8).TrimEnd())
    $escritor.WriteLine("GO")
  }
  $escritor.WriteLine("")
  $escritor.WriteLine("-- Tabla de control del migrador (la crea RiverVet.Migrador al aplicar migraciones).")
  $escritor.WriteLine("CREATE TABLE dbo.migraciones_aplicadas (")
  $escritor.WriteLine("    nombre      VARCHAR(200) NOT NULL CONSTRAINT PK_migraciones_aplicadas PRIMARY KEY,")
  $escritor.WriteLine("    hash        CHAR(64)     NOT NULL,")
  $escritor.WriteLine("    aplicada_en DATETIME2(0) NOT NULL CONSTRAINT DF_migraciones_aplicadas_en DEFAULT (SYSUTCDATETIME())")
  $escritor.WriteLine(");")
  $escritor.WriteLine("GO")

  # 2. Datos.
  $escritor.WriteLine("")
  $escritor.WriteLine("-- ############################## DATOS ##############################")
  $escritor.WriteLine("-- Restricciones apagadas mientras se carga (el orden entre tablas no importa).")
  foreach ($t in $tablas.Rows) { $escritor.WriteLine("ALTER TABLE dbo.[$($t.tabla)] NOCHECK CONSTRAINT ALL;") }
  $escritor.WriteLine("GO")

  $total = 0
  foreach ($t in $tablas.Rows) {
    $nombre = $t.tabla
    $columnas = Consultar $conexion @"
SELECT c.name, c.is_identity FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.$nombre') AND c.is_computed = 0
ORDER BY c.column_id
"@
    $lista = ($columnas.Rows | ForEach-Object { "[$($_.name)]" }) -join ', '
    $conIdentidad = @($columnas.Rows | Where-Object { $_.is_identity }).Count -gt 0
    $selectCols = ($columnas.Rows | ForEach-Object { "[$($_.name)]" }) -join ', '
    $datos = Consultar $conexion "SELECT $selectCols FROM dbo.[$nombre]"
    $escritor.WriteLine("")
    $escritor.WriteLine("-- dbo.$nombre ($($datos.Rows.Count) filas)")
    if ($datos.Rows.Count -eq 0) { continue }
    if ($conIdentidad) { $escritor.WriteLine("SET IDENTITY_INSERT dbo.[$nombre] ON;") }
    foreach ($fila in $datos.Rows) {
      $valores = ($fila.ItemArray | ForEach-Object { Literal $_ }) -join ', '
      $escritor.WriteLine("INSERT INTO dbo.[$nombre] ($lista) VALUES ($valores);")
      $total++
    }
    if ($conIdentidad) { $escritor.WriteLine("SET IDENTITY_INSERT dbo.[$nombre] OFF;") }
    $escritor.WriteLine("GO")
    Write-Host ("  {0,-24} {1,6} filas" -f $nombre, $datos.Rows.Count)
  }

  $escritor.WriteLine("")
  $escritor.WriteLine("-- Restricciones encendidas otra vez, verificando todos los datos cargados.")
  foreach ($t in $tablas.Rows) { $escritor.WriteLine("ALTER TABLE dbo.[$($t.tabla)] WITH CHECK CHECK CONSTRAINT ALL;") }
  $escritor.WriteLine("GO")

  # 3. Contadores: siguen desde donde iban en produccion (p. ej. codigos de pedido).
  $secuencias = Consultar $conexion "SELECT name, CAST(last_used_value AS BIGINT) AS ultimo, CAST(increment AS BIGINT) AS paso FROM sys.sequences WHERE schema_id = SCHEMA_ID('dbo')"
  if ($secuencias.Rows.Count -gt 0) {
    $escritor.WriteLine("")
    $escritor.WriteLine("-- ############################ CONTADORES ############################")
    foreach ($s in $secuencias.Rows) {
      if ($s.ultimo -isnot [DBNull]) { $escritor.WriteLine("ALTER SEQUENCE dbo.[$($s.name)] RESTART WITH $([long]$s.ultimo + [long]$s.paso);") }
    }
    $escritor.WriteLine("GO")
  }
  $escritor.WriteLine("")
  $escritor.WriteLine("PRINT 'Copia de RiverVet restaurada.';")
} finally {
  $escritor.Close(); $conexion.Close()
}
Write-Host ''
Write-Host "Listo: $total filas exportadas." -ForegroundColor Green
Write-Host "Archivo: $salida ($([math]::Round((Get-Item $salida).Length / 1KB)) KB)" -ForegroundColor Green
