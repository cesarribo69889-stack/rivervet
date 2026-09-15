-- ═══════════════════════════════════════════════════════════════════════
-- 0002_citas_historial_vacunas.sql — Sprint 2
-- Citas (HU-008 a HU-010), historial clínico (HU-006, HU-007) y vacunas
-- (HU-011, HU-012).
-- ═══════════════════════════════════════════════════════════════════════

-- ── citas ───────────────────────────────────────────────────────────────
-- Las citas ocupan bloques FIJOS de 30 minutos que empiezan en :00 o :30.
-- Con bloques fijos, "dos citas se solapan" equivale a "dos citas tienen la
-- misma hora de inicio", y eso lo puede impedir un índice único. Con
-- duraciones libres haría falta comparar rangos, y una restricción única ya
-- no bastaría.
CREATE TABLE dbo.citas (
    id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_citas PRIMARY KEY,
    mascota_id         INT NOT NULL CONSTRAINT FK_citas_mascotas REFERENCES dbo.mascotas(id),
    veterinario_id     INT NOT NULL CONSTRAINT FK_citas_veterinario REFERENCES dbo.usuarios(id),
    -- Inicio del bloque, en UTC. Lima es UTC-5 todo el año (sin horario de
    -- verano), así que :00 y :30 en Lima también son :00 y :30 en UTC.
    fecha_hora         DATETIME2(0) NOT NULL,
    motivo             NVARCHAR(250) NOT NULL,
    estado             VARCHAR(15) NOT NULL CONSTRAINT DF_citas_estado DEFAULT ('programada')
                       CONSTRAINT CK_citas_estado
                       CHECK (estado IN ('programada', 'atendida', 'cancelada', 'no_asistio')),
    motivo_cancelacion NVARCHAR(250) NULL,
    cancelada_en       DATETIME2(0) NULL,
    cancelada_por      INT NULL CONSTRAINT FK_citas_cancelada_por REFERENCES dbo.usuarios(id),
    creado_por         INT NULL CONSTRAINT FK_citas_creado_por REFERENCES dbo.usuarios(id),
    creado_en          DATETIME2(0) NOT NULL CONSTRAINT DF_citas_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en     DATETIME2(0) NOT NULL CONSTRAINT DF_citas_actualizado_en DEFAULT (SYSUTCDATETIME()),
    eliminado          BIT NOT NULL CONSTRAINT DF_citas_eliminado DEFAULT (0),
    eliminado_en       DATETIME2(0) NULL,
    CONSTRAINT CK_citas_bloque
        CHECK (DATEPART(MINUTE, fecha_hora) IN (0, 30) AND DATEPART(SECOND, fecha_hora) = 0)
);
GO

-- ★ PIEZA CLAVE CONTRA EL DOBLE AGENDAMIENTO (HU-008) ★
-- Un veterinario no puede tener dos citas vigentes a la misma hora. Lo
-- garantiza la BASE DE DATOS, no un "SELECT para ver si está libre": entre
-- ese SELECT y el INSERT otra petición simultánea podría colarse. Con el
-- índice único, de dos INSERT simultáneos uno gana y el otro recibe el error
-- 2601, que la API traduce a 409 Conflict. Las canceladas no cuentan: liberan
-- el horario.
CREATE UNIQUE INDEX UX_citas_veterinario_horario
    ON dbo.citas(veterinario_id, fecha_hora)
    WHERE estado IN ('programada', 'atendida', 'no_asistio') AND eliminado = 0;

-- Tampoco la misma mascota en dos citas a la misma hora.
CREATE UNIQUE INDEX UX_citas_mascota_horario
    ON dbo.citas(mascota_id, fecha_hora)
    WHERE estado IN ('programada', 'atendida', 'no_asistio') AND eliminado = 0;

CREATE INDEX IX_citas_fecha ON dbo.citas(fecha_hora) INCLUDE (veterinario_id, estado);
GO

-- ── historial_clinico: una fila por atención ────────────────────────────
CREATE TABLE dbo.historial_clinico (
    id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_historial_clinico PRIMARY KEY,
    mascota_id     INT NOT NULL CONSTRAINT FK_historial_mascotas REFERENCES dbo.mascotas(id),
    veterinario_id INT NOT NULL CONSTRAINT FK_historial_veterinario REFERENCES dbo.usuarios(id),
    cita_id        INT NULL CONSTRAINT FK_historial_citas REFERENCES dbo.citas(id),
    fecha_atencion DATETIME2(0) NOT NULL CONSTRAINT DF_historial_fecha DEFAULT (SYSUTCDATETIME()),
    motivo         NVARCHAR(250) NOT NULL,
    diagnostico    NVARCHAR(1000) NOT NULL,
    tratamiento    NVARCHAR(1000) NOT NULL,
    observaciones  NVARCHAR(2000) NULL,
    peso_kg        DECIMAL(5,2) NULL CONSTRAINT CK_historial_peso CHECK (peso_kg > 0),
    temperatura_c  DECIMAL(4,1) NULL CONSTRAINT CK_historial_temperatura CHECK (temperatura_c BETWEEN 30 AND 45),
    creado_en      DATETIME2(0) NOT NULL CONSTRAINT DF_historial_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en DATETIME2(0) NOT NULL CONSTRAINT DF_historial_actualizado_en DEFAULT (SYSUTCDATETIME()),
    eliminado      BIT NOT NULL CONSTRAINT DF_historial_eliminado DEFAULT (0),
    eliminado_en   DATETIME2(0) NULL
);
GO

-- Una cita genera como máximo una atención.
CREATE UNIQUE INDEX UX_historial_cita ON dbo.historial_clinico(cita_id)
    WHERE cita_id IS NOT NULL AND eliminado = 0;
CREATE INDEX IX_historial_mascota_fecha ON dbo.historial_clinico(mascota_id, fecha_atencion DESC)
    WHERE eliminado = 0;
GO

-- ── vacunas ─────────────────────────────────────────────────────────────
CREATE TABLE dbo.vacunas (
    id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_vacunas PRIMARY KEY,
    mascota_id       INT NOT NULL CONSTRAINT FK_vacunas_mascotas REFERENCES dbo.mascotas(id),
    veterinario_id   INT NOT NULL CONSTRAINT FK_vacunas_veterinario REFERENCES dbo.usuarios(id),
    tipo_vacuna      NVARCHAR(100) NOT NULL,
    fecha_aplicacion DATE NOT NULL,
    lote             NVARCHAR(50) NOT NULL,
    proxima_dosis    DATE NULL,
    observaciones    NVARCHAR(500) NULL,
    creado_en        DATETIME2(0) NOT NULL CONSTRAINT DF_vacunas_creado_en DEFAULT (SYSUTCDATETIME()),
    eliminado        BIT NOT NULL CONSTRAINT DF_vacunas_eliminado DEFAULT (0),
    eliminado_en     DATETIME2(0) NULL,
    CONSTRAINT CK_vacunas_proxima_dosis
        CHECK (proxima_dosis IS NULL OR proxima_dosis > fecha_aplicacion)
);
GO

CREATE INDEX IX_vacunas_mascota ON dbo.vacunas(mascota_id, fecha_aplicacion DESC) WHERE eliminado = 0;
-- Para HU-012 (recordatorios): "vacunas cuya próxima dosis vence en N días".
CREATE INDEX IX_vacunas_proxima_dosis ON dbo.vacunas(proxima_dosis)
    WHERE proxima_dosis IS NOT NULL AND eliminado = 0;
GO

-- ==== REVERSIÓN (NO se ejecuta; copiar y correr a mano solo si es necesario) ====
-- DROP TABLE dbo.vacunas;
-- DROP TABLE dbo.historial_clinico;
-- DROP TABLE dbo.citas;
-- DELETE FROM dbo.migraciones_aplicadas WHERE nombre = '0002_citas_historial_vacunas.sql';
