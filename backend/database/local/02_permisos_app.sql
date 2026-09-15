-- ═══════════════════════════════════════════════════════════════════════
-- 02_permisos_app.sql — permisos MÍNIMOS del usuario de aplicación (paso 2 de 2)
--
-- Ejecutar DESPUÉS de aplicar las migraciones
-- (dotnet run --project src\RiverVet.Migrador -- migrar):
--
--   sqlcmd -S "localhost\SQLEXPRESS" -E -C -I -i database\local\02_permisos_app.sql
--
-- Es idempotente. Si una migración futura agrega una tabla, rivervet_app la
-- puede leer y escribir automáticamente (el GRANT es sobre el esquema dbo);
-- solo hay que volver a correr este script si la tabla nueva necesita una
-- excepción (un DENY o un GRANT DELETE).
-- ═══════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
USE rivervet_db;
GO

-- Los permisos se dan a un ROL y el usuario entra al rol: si mañana hay un
-- segundo usuario de app (p. ej. un worker), recibe exactamente lo mismo.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rol_rivervet_app' AND type = 'R')
    CREATE ROLE rol_rivervet_app;
GO

-- Leer, insertar y actualizar filas del esquema dbo (incluye usar la
-- secuencia de códigos de pedido). Nada de DDL (CREATE/ALTER/DROP), nada de
-- otras bases.
GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO rol_rivervet_app;

-- Sin DELETE general: usamos borrado lógico (eliminado = 1). Si un atacante
-- consiguiera ejecutar SQL como la app, no podría borrar físicamente datos.
-- Única excepción: los ítems del carrito, que no son un registro de negocio.
GRANT DELETE ON OBJECT::dbo.carrito_items TO rol_rivervet_app;

-- La auditoría es de solo-agregar para la app: no puede modificar ni borrar
-- el rastro de lo que hizo.
DENY UPDATE, DELETE ON OBJECT::dbo.auditoria TO rol_rivervet_app;

-- La app no tiene nada que hacer con el control de migraciones.
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.migraciones_aplicadas TO rol_rivervet_app;
GO

IF IS_ROLEMEMBER(N'rol_rivervet_app', N'rivervet_app') = 0
    ALTER ROLE rol_rivervet_app ADD MEMBER rivervet_app;
GO

PRINT 'Permisos de rivervet_app aplicados.';
GO
