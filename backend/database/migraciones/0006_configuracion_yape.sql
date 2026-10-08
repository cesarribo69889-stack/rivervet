-- ═══════════════════════════════════════════════════════════════════════
-- 0006_configuracion_yape.sql — Métodos de pago editables desde el panel
-- El administrador cambia el número y el titular del Yape del negocio y
-- sube la imagen de su QR. Antes vivían en la configuración del App
-- Service (Pagos__Yape__*) y en un archivo fijo de la web (yape-qr.png).
--
-- Una sola fila (id = 1). Mientras no exista, la API sigue usando la
-- configuración del App Service: la migración es compatible hacia atrás
-- y el orden de despliegue es 1) esta migración, 2) la API, 3) la web.
--
-- El QR se guarda en la base: el App Service no tiene un disco fiable
-- para archivos subidos (cada publicación reemplaza la carpeta) y la
-- imagen es chica (la API acepta hasta 500 KB).
--
-- Permisos: rol_rivervet_app tiene SELECT/INSERT/UPDATE sobre el esquema
-- dbo, que incluyen la tabla nueva; no hace falta un GRANT.
-- ═══════════════════════════════════════════════════════════════════════

CREATE TABLE dbo.configuracion_yape (
    id                INT NOT NULL CONSTRAINT PK_configuracion_yape PRIMARY KEY
                      CONSTRAINT CK_configuracion_yape_unica CHECK (id = 1),
    celular           CHAR(9) NULL
                      CONSTRAINT CK_configuracion_yape_celular CHECK (celular LIKE '9[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
    titular           NVARCHAR(100) NULL,
    qr                VARBINARY(MAX) NULL,
    qr_tipo           VARCHAR(20) NULL
                      CONSTRAINT CK_configuracion_yape_qr_tipo CHECK (qr_tipo IN ('image/png', 'image/jpeg', 'image/webp')),
    qr_actualizado_en DATETIME2(0) NULL,
    actualizado_por   INT NULL CONSTRAINT FK_configuracion_yape_usuarios REFERENCES dbo.usuarios(id),
    actualizado_en    DATETIME2(0) NOT NULL CONSTRAINT DF_configuracion_yape_actualizado_en DEFAULT (SYSUTCDATETIME()),
    -- La imagen y su tipo van juntos: sin tipo no se puede servir.
    CONSTRAINT CK_configuracion_yape_qr CHECK ((qr IS NULL AND qr_tipo IS NULL) OR (qr IS NOT NULL AND qr_tipo IS NOT NULL))
);
GO

-- ==== REVERSIÓN (NO se ejecuta; copiar y correr a mano solo si es necesario) ====
-- DROP TABLE dbo.configuracion_yape;
-- DELETE FROM dbo.migraciones_aplicadas WHERE nombre = '0006_configuracion_yape.sql';
