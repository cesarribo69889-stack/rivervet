-- ═══════════════════════════════════════════════════════════════════════
-- 0005_receta_historial.sql — Sprint 2 (ampliación)
-- Receta de una atención: los medicamentos indicados, con dosis y
-- duración, separados del tratamiento (que describe el plan general).
--
-- Compatible hacia atrás: la columna admite NULL, así que la versión
-- anterior de la API sigue funcionando con la base ya migrada. Por eso
-- el orden de despliegue es: 1) esta migración, 2) la API, 3) la web
-- (la API rechaza con 422 un campo que no conoce, así que la web que
-- envía "receta" no puede salir antes que la API que lo acepta).
--
-- Permisos: rol_rivervet_app tiene SELECT/INSERT/UPDATE sobre el esquema
-- dbo, que incluyen las columnas nuevas; no hace falta un GRANT.
-- ═══════════════════════════════════════════════════════════════════════

ALTER TABLE dbo.historial_clinico ADD receta NVARCHAR(2000) NULL;
GO

-- ==== REVERSIÓN (NO se ejecuta; copiar y correr a mano solo si es necesario) ====
-- ALTER TABLE dbo.historial_clinico DROP COLUMN receta;
-- DELETE FROM dbo.migraciones_aplicadas WHERE nombre = '0005_receta_historial.sql';
