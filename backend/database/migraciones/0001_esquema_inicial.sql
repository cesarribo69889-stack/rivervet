-- ═══════════════════════════════════════════════════════════════════════
-- 0001_esquema_inicial.sql — Sprint 1
-- Usuarios, refresh tokens, auditoría, clientes y mascotas.
--
-- REGLA: esta migración es INMUTABLE una vez aplicada. Si el modelo cambia,
-- se crea una migración nueva (0005_..., 0006_...). El runner guarda el hash
-- SHA-256 de este archivo y se niega a continuar si alguien lo edita.
--
-- Convenciones:
--  * Fechas en UTC con SYSUTCDATETIME(). SYSDATETIME() devolvería la hora
--    local del servidor (Lima = UTC-5 en local, UTC en Azure): mezclar ambas
--    corrompe los horarios de las citas. Se convierte a hora de Lima al mostrar.
--  * Todas las restricciones tienen nombre (PK_, FK_, CK_, DF_, UX_, IX_) para
--    poder traducir errores a mensajes claros y para poder revertir.
--  * Borrado lógico: columna eliminado BIT. No se hace DELETE físico.
--  * VARCHAR solo en datos que por definición son ASCII (hashes, códigos);
--    NVARCHAR en todo texto humano (tildes y ñ).
-- ═══════════════════════════════════════════════════════════════════════

-- ── usuarios: cuentas que pueden iniciar sesión (los 4 roles) ───────────
CREATE TABLE dbo.usuarios (
    id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_usuarios PRIMARY KEY,
    nombre            NVARCHAR(100) NOT NULL,
    email             NVARCHAR(254) NOT NULL,
    -- bcrypt produce siempre 60 caracteres ASCII: VARCHAR(60) es exacto.
    password_hash     VARCHAR(60)   NOT NULL,
    rol               NVARCHAR(20)  NOT NULL
                      CONSTRAINT CK_usuarios_rol
                      CHECK (rol IN (N'admin', N'veterinario', N'recepcionista', N'cliente')),
    activo            BIT NOT NULL CONSTRAINT DF_usuarios_activo DEFAULT (1),
    -- Protección contra fuerza bruta: se bloquea tras N intentos fallidos.
    intentos_fallidos INT NOT NULL CONSTRAINT DF_usuarios_intentos DEFAULT (0)
                      CONSTRAINT CK_usuarios_intentos CHECK (intentos_fallidos >= 0),
    bloqueado_hasta   DATETIME2(0) NULL,
    ultimo_login_en   DATETIME2(0) NULL,
    creado_en         DATETIME2(0) NOT NULL CONSTRAINT DF_usuarios_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en    DATETIME2(0) NOT NULL CONSTRAINT DF_usuarios_actualizado_en DEFAULT (SYSUTCDATETIME()),
    eliminado         BIT NOT NULL CONSTRAINT DF_usuarios_eliminado DEFAULT (0),
    eliminado_en      DATETIME2(0) NULL
);
GO

-- El email es único incluso entre usuarios eliminados: así un registro de
-- auditoría antiguo nunca apunta a "otra persona" con el mismo email.
CREATE UNIQUE INDEX UX_usuarios_email ON dbo.usuarios(email);
GO

-- ── refresh_tokens: sesiones de larga duración (7 días) ─────────────────
-- Se guarda el HASH SHA-256 del token, nunca el token. Si alguien roba una
-- copia de la base de datos, no puede usar esos hashes para iniciar sesión.
-- (SHA-256 basta aquí, a diferencia de las contraseñas: el token son 256 bits
-- aleatorios, no una palabra que se pueda adivinar con un diccionario.)
CREATE TABLE dbo.refresh_tokens (
    id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_refresh_tokens PRIMARY KEY,
    usuario_id         INT NOT NULL
                       CONSTRAINT FK_refresh_tokens_usuarios REFERENCES dbo.usuarios(id),
    token_hash         CHAR(64) NOT NULL,
    -- Todos los tokens nacidos de un mismo login comparten familia. Si se
    -- detecta la reutilización de un token ya rotado, se revoca la familia
    -- completa: el ladrón y la víctima pierden la sesión y la víctima vuelve
    -- a iniciar sesión (el ladrón no puede).
    familia_id         UNIQUEIDENTIFIER NOT NULL,
    expira_en          DATETIME2(0) NOT NULL,
    creado_en          DATETIME2(0) NOT NULL CONSTRAINT DF_refresh_tokens_creado_en DEFAULT (SYSUTCDATETIME()),
    creado_ip          VARCHAR(45) NULL,
    user_agent         NVARCHAR(300) NULL,
    revocado_en        DATETIME2(0) NULL,
    motivo_revocacion  VARCHAR(30) NULL
                       CONSTRAINT CK_refresh_tokens_motivo
                       CHECK (motivo_revocacion IN ('rotado', 'logout', 'reuso_detectado',
                                                    'cambio_password', 'usuario_desactivado')),
    reemplazado_por_id BIGINT NULL
                       CONSTRAINT FK_refresh_tokens_reemplazo REFERENCES dbo.refresh_tokens(id)
);
GO

CREATE UNIQUE INDEX UX_refresh_tokens_hash ON dbo.refresh_tokens(token_hash);
CREATE INDEX IX_refresh_tokens_familia ON dbo.refresh_tokens(familia_id) INCLUDE (revocado_en);
CREATE INDEX IX_refresh_tokens_usuario_activos ON dbo.refresh_tokens(usuario_id) WHERE revocado_en IS NULL;
GO

-- ── auditoria: quién hizo qué, sobre qué, cuándo y desde dónde ──────────
-- El usuario de aplicación tiene DENY UPDATE/DELETE sobre esta tabla (ver
-- database/local/01_crear_bd_y_usuarios.sql): la app solo puede agregar filas.
CREATE TABLE dbo.auditoria (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_auditoria PRIMARY KEY,
    usuario_id  INT NULL CONSTRAINT FK_auditoria_usuarios REFERENCES dbo.usuarios(id),
    accion      VARCHAR(40) NOT NULL,
    recurso     VARCHAR(40) NOT NULL,
    recurso_id  VARCHAR(40) NULL,
    -- Detalle en JSON, ya pasado por el sanitizador (sin contraseñas ni tokens).
    detalle     NVARCHAR(2000) NULL
                CONSTRAINT CK_auditoria_detalle_json CHECK (detalle IS NULL OR ISJSON(detalle) = 1),
    ip          VARCHAR(45) NULL,
    user_agent  NVARCHAR(300) NULL,
    creado_en   DATETIME2(3) NOT NULL CONSTRAINT DF_auditoria_creado_en DEFAULT (SYSUTCDATETIME())
);
GO

CREATE INDEX IX_auditoria_creado_en ON dbo.auditoria(creado_en DESC);
CREATE INDEX IX_auditoria_usuario ON dbo.auditoria(usuario_id, creado_en DESC);
CREATE INDEX IX_auditoria_recurso ON dbo.auditoria(recurso, recurso_id);
GO

-- ── clientes: dueños de mascotas ────────────────────────────────────────
-- Un cliente puede existir sin cuenta web (lo registra recepción) o tener una
-- cuenta de rol 'cliente' vinculada por usuario_id. Ese vínculo es la base del
-- CONTROL DE PROPIEDAD: "mis mascotas" = mascotas cuyo cliente tiene mi
-- usuario_id.
CREATE TABLE dbo.clientes (
    id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_clientes PRIMARY KEY,
    usuario_id           INT NULL CONSTRAINT FK_clientes_usuarios REFERENCES dbo.usuarios(id),
    dni                  CHAR(8) NOT NULL
                         CONSTRAINT CK_clientes_dni CHECK (dni NOT LIKE '%[^0-9]%' AND LEN(dni) = 8),
    nombres              NVARCHAR(100) NOT NULL,
    apellidos            NVARCHAR(100) NOT NULL,
    email                NVARCHAR(254) NULL,
    -- Celular peruano: 9 dígitos, empieza en 9.
    telefono             CHAR(9) NOT NULL
                         CONSTRAINT CK_clientes_telefono
                         CHECK (telefono LIKE '9[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
    direccion            NVARCHAR(250) NULL,
    -- Ley N° 29733: consentimiento expreso para el tratamiento de datos.
    consentimiento_datos BIT NOT NULL CONSTRAINT DF_clientes_consentimiento DEFAULT (0),
    consentimiento_en    DATETIME2(0) NULL,
    creado_por           INT NULL CONSTRAINT FK_clientes_creado_por REFERENCES dbo.usuarios(id),
    creado_en            DATETIME2(0) NOT NULL CONSTRAINT DF_clientes_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en       DATETIME2(0) NOT NULL CONSTRAINT DF_clientes_actualizado_en DEFAULT (SYSUTCDATETIME()),
    eliminado            BIT NOT NULL CONSTRAINT DF_clientes_eliminado DEFAULT (0),
    eliminado_en         DATETIME2(0) NULL
);
GO

-- Índices únicos FILTRADOS: la unicidad solo aplica a clientes no eliminados,
-- así un cliente dado de baja puede volver a registrarse con el mismo DNI.
CREATE UNIQUE INDEX UX_clientes_dni ON dbo.clientes(dni) WHERE eliminado = 0;
CREATE UNIQUE INDEX UX_clientes_email ON dbo.clientes(email) WHERE email IS NOT NULL AND eliminado = 0;
CREATE UNIQUE INDEX UX_clientes_usuario ON dbo.clientes(usuario_id) WHERE usuario_id IS NOT NULL;
CREATE INDEX IX_clientes_apellidos ON dbo.clientes(apellidos, nombres) WHERE eliminado = 0;
GO

-- ── mascotas ────────────────────────────────────────────────────────────
CREATE TABLE dbo.mascotas (
    id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_mascotas PRIMARY KEY,
    cliente_id         INT NOT NULL CONSTRAINT FK_mascotas_clientes REFERENCES dbo.clientes(id),
    nombre             NVARCHAR(60) NOT NULL,
    especie            NVARCHAR(20) NOT NULL
                       CONSTRAINT CK_mascotas_especie
                       CHECK (especie IN (N'perro', N'gato', N'ave', N'conejo', N'roedor', N'reptil', N'otro')),
    raza               NVARCHAR(60) NULL,
    sexo               NVARCHAR(10) NULL CONSTRAINT CK_mascotas_sexo CHECK (sexo IN (N'macho', N'hembra')),
    -- Se guarda la fecha de nacimiento y no la edad: la edad cambia sola.
    fecha_nacimiento   DATE NULL,
    peso_kg            DECIMAL(5,2) NULL CONSTRAINT CK_mascotas_peso CHECK (peso_kg > 0),
    color              NVARCHAR(40) NULL,
    senas_particulares NVARCHAR(250) NULL,
    esterilizado       BIT NULL,
    creado_por         INT NULL CONSTRAINT FK_mascotas_creado_por REFERENCES dbo.usuarios(id),
    creado_en          DATETIME2(0) NOT NULL CONSTRAINT DF_mascotas_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en     DATETIME2(0) NOT NULL CONSTRAINT DF_mascotas_actualizado_en DEFAULT (SYSUTCDATETIME()),
    eliminado          BIT NOT NULL CONSTRAINT DF_mascotas_eliminado DEFAULT (0),
    eliminado_en       DATETIME2(0) NULL
);
GO

CREATE INDEX IX_mascotas_cliente ON dbo.mascotas(cliente_id) WHERE eliminado = 0;
CREATE INDEX IX_mascotas_nombre ON dbo.mascotas(nombre) WHERE eliminado = 0;
GO

-- ==== REVERSIÓN (NO se ejecuta; copiar y correr a mano solo si es necesario) ====
-- DROP TABLE dbo.mascotas;
-- DROP TABLE dbo.clientes;
-- DROP TABLE dbo.auditoria;
-- DROP TABLE dbo.refresh_tokens;
-- DROP TABLE dbo.usuarios;
-- DELETE FROM dbo.migraciones_aplicadas WHERE nombre = '0001_esquema_inicial.sql';
