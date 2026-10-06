-- ═══════════════════════════════════════════════════════════════════════
-- crear_usuarios_azure.sql — usuarios de Azure SQL Database (producción)
--
-- En Azure SQL se usan "usuarios contenidos": viven dentro de la base de
-- datos, sin login de servidor. Se ejecuta UNA vez, conectado a la base
-- rivervet_db (NO a master) con el administrador del servidor de Azure, por
-- ejemplo desde SSMS o Azure Data Studio.
--
-- Reemplaza las dos contraseñas antes de ejecutarlo y NO guardes el archivo
-- modificado. Esas mismas contraseñas van en:
--   - GitHub Secrets (AZURE_SQL_MIGRADOR_CONNECTION_STRING) -> migraciones en el CD
--   - App Service, variable ConnectionStrings__RiverVet -> para la app
--
-- IMPORTANTE — orden en Azure (el bloque 2 apunta a tablas que crean las
-- migraciones, así que no puede ir antes):
--   1) ejecutar SOLO el bloque 1 (migrador)
--   2) correr las migraciones con ese usuario (RiverVet.Migrador migrar, o el CD)
--   3) ejecutar el bloque 2 (aplicación)
-- ═══════════════════════════════════════════════════════════════════════

-- ── 1) Migrador ─────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rivervet_migrador')
    CREATE USER rivervet_migrador WITH PASSWORD = N'CAMBIA_ESTA_CLAVE_MIGRADOR';
ALTER ROLE db_ddladmin   ADD MEMBER rivervet_migrador;
ALTER ROLE db_datareader ADD MEMBER rivervet_migrador;
ALTER ROLE db_datawriter ADD MEMBER rivervet_migrador;
GO

-- ── 2) Aplicación (permisos mínimos, igual que en local) ────────────────
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rivervet_app')
    CREATE USER rivervet_app WITH PASSWORD = N'CAMBIA_ESTA_CLAVE_APP';
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rol_rivervet_app' AND type = 'R')
    CREATE ROLE rol_rivervet_app;
GO

GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO rol_rivervet_app;
GRANT DELETE ON OBJECT::dbo.carrito_items TO rol_rivervet_app;
DENY UPDATE, DELETE ON OBJECT::dbo.auditoria TO rol_rivervet_app;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.migraciones_aplicadas TO rol_rivervet_app;
ALTER ROLE rol_rivervet_app ADD MEMBER rivervet_app;
GO
