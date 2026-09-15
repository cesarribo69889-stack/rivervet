-- ═══════════════════════════════════════════════════════════════════════
-- 01_crear_bd_y_usuarios.sql — SOLO PARA TU SQL SERVER LOCAL (paso 1 de 2)
--
-- Crea la base rivervet_db y DOS logins:
--   rivervet_migrador -> crea y altera tablas. Solo lo usa RiverVet.Migrador.
--   rivervet_app      -> el que usa la API. Sus permisos (mínimos) se dan en
--                        02_permisos_app.sql, DESPUÉS de correr las migraciones,
--                        porque algunos permisos apuntan a tablas concretas que
--                        todavía no existen.
--
-- ¿Por qué la app nunca usa 'sa'? Si alguien lograra una inyección SQL (o
-- robara el .env), con 'sa' podría borrar TODAS las bases del servidor, crear
-- logins o leer archivos del disco. Con rivervet_app lo máximo que puede hacer
-- es lo que la app ya hace: leer y escribir filas de rivervet_db.
--
-- Lo más simple es NO ejecutarlo a mano: configurar-desarrollo.ps1 lo corre
-- con contraseñas aleatorias y las guarda en los User Secrets de .NET.
--
-- Las contraseñas NO están en el archivo: se pasan como variables de sqlcmd.
-- Para ejecutarlo a mano, desde la carpeta backend/ en PowerShell (Windows auth, -E):
--
--   sqlcmd -S "localhost\SQLEXPRESS" -E -C -I `
--     -v APP_PASSWORD="UnaClaveLarga#App2026" MIGRADOR_PASSWORD="OtraClaveLarga#Mig2026" `
--     -i database\local\01_crear_bd_y_usuarios.sql
--
-- Es idempotente: se puede correr varias veces sin error.
-- ═══════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
USE master;
GO

IF DB_ID(N'rivervet_db') IS NULL
BEGIN
    CREATE DATABASE rivervet_db;
    PRINT 'Base rivervet_db creada.';
END
GO

-- ── Logins a nivel de servidor ──────────────────────────────────────────
-- CHECK_POLICY = ON aplica la política de contraseñas de Windows.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'rivervet_migrador')
    CREATE LOGIN rivervet_migrador WITH PASSWORD = N'$(MIGRADOR_PASSWORD)',
        DEFAULT_DATABASE = rivervet_db, CHECK_POLICY = ON;
ELSE
    ALTER LOGIN rivervet_migrador WITH PASSWORD = N'$(MIGRADOR_PASSWORD)';

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'rivervet_app')
    CREATE LOGIN rivervet_app WITH PASSWORD = N'$(APP_PASSWORD)',
        DEFAULT_DATABASE = rivervet_db, CHECK_POLICY = ON;
ELSE
    ALTER LOGIN rivervet_app WITH PASSWORD = N'$(APP_PASSWORD)';
GO

USE rivervet_db;
GO

-- ── Usuario migrador: puede cambiar el esquema, pero solo de ESTA base ──
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rivervet_migrador')
    CREATE USER rivervet_migrador FOR LOGIN rivervet_migrador;
ALTER ROLE db_ddladmin    ADD MEMBER rivervet_migrador;
ALTER ROLE db_datareader  ADD MEMBER rivervet_migrador;
ALTER ROLE db_datawriter  ADD MEMBER rivervet_migrador;
GO

-- ── Usuario de aplicación (todavía sin permisos) ────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rivervet_app')
    CREATE USER rivervet_app FOR LOGIN rivervet_app;
GO

PRINT 'Paso 1 listo. Ahora: aplicar migraciones (RiverVet.Migrador migrar) y luego 02_permisos_app.sql';
GO
