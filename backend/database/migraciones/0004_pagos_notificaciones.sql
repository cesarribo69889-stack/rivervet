-- ═══════════════════════════════════════════════════════════════════════
-- 0004_pagos_notificaciones.sql — Sprint 4
-- Pagos por Yape (HU-017) y pasarela (HU-019), webhooks, y registro de
-- envíos de comprobante por WhatsApp (HU-020).
--
-- ★ ALCANCE PCI DSS ★
-- Esta base de datos NO tiene, ni tendrá, columnas para número de tarjeta,
-- CVV ni fecha de vencimiento. El navegador envía la tarjeta DIRECTAMENTE a
-- la pasarela (checkout hospedado o tokenización en el cliente) y nosotros
-- solo recibimos una referencia opaca (referencia_externa) y el resultado.
-- Si un sistema nunca almacena, procesa ni transmite datos de tarjeta, queda
-- fuera de casi todo el alcance de PCI DSS: el peor robo posible de esta base
-- no expone ninguna tarjeta.
--
-- HU-021 (triage) NO tiene tabla a propósito: por minimización de datos
-- (Ley N° 29733) los síntomas consultados no se guardan.
-- ═══════════════════════════════════════════════════════════════════════

CREATE TABLE dbo.pagos (
    id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pagos PRIMARY KEY,
    pedido_id          INT NOT NULL CONSTRAINT FK_pagos_pedidos REFERENCES dbo.pedidos(id),
    metodo             VARCHAR(15) NOT NULL
                       CONSTRAINT CK_pagos_metodo CHECK (metodo IN ('yape', 'pasarela', 'efectivo')),
    -- Yape queda en 'pendiente_verificacion' hasta que un recepcionista
    -- confirme en la app de Yape del negocio que el dinero llegó.
    estado             VARCHAR(25) NOT NULL
                       CONSTRAINT CK_pagos_estado
                       CHECK (estado IN ('pendiente', 'pendiente_verificacion', 'aprobado', 'rechazado')),
    monto              DECIMAL(10,2) NOT NULL CONSTRAINT CK_pagos_monto CHECK (monto > 0),
    moneda             CHAR(3) NOT NULL CONSTRAINT DF_pagos_moneda DEFAULT ('PEN')
                       CONSTRAINT CK_pagos_moneda CHECK (moneda = 'PEN'),
    -- Yape: solo el código de operación que el cliente ve en su app.
    codigo_operacion   VARCHAR(30) NULL,
    -- Pasarela: nombre del proveedor y el id del cargo/token que devolvió.
    proveedor          VARCHAR(30) NULL,
    referencia_externa VARCHAR(100) NULL,
    verificado_por     INT NULL CONSTRAINT FK_pagos_verificado_por REFERENCES dbo.usuarios(id),
    verificado_en      DATETIME2(0) NULL,
    creado_en          DATETIME2(0) NOT NULL CONSTRAINT DF_pagos_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en     DATETIME2(0) NOT NULL CONSTRAINT DF_pagos_actualizado_en DEFAULT (SYSUTCDATETIME())
);
GO

-- Un mismo código de operación de Yape no puede usarse para pagar dos pedidos
-- (fraude clásico: reutilizar la captura de un pago anterior).
CREATE UNIQUE INDEX UX_pagos_yape_operacion ON dbo.pagos(codigo_operacion)
    WHERE metodo = 'yape' AND codigo_operacion IS NOT NULL;
CREATE UNIQUE INDEX UX_pagos_referencia ON dbo.pagos(proveedor, referencia_externa)
    WHERE referencia_externa IS NOT NULL;
CREATE INDEX IX_pagos_pedido ON dbo.pagos(pedido_id);
GO

-- ── webhook_eventos: idempotencia de los avisos de la pasarela ──────────
-- La pasarela puede reenviar el mismo evento varias veces. La restricción
-- única (proveedor, evento_id) hace que procesarlo dos veces sea imposible.
CREATE TABLE dbo.webhook_eventos (
    id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_webhook_eventos PRIMARY KEY,
    proveedor    VARCHAR(30) NOT NULL,
    evento_id    VARCHAR(100) NOT NULL,
    tipo         VARCHAR(60) NOT NULL,
    firma_valida BIT NOT NULL,
    procesado    BIT NOT NULL CONSTRAINT DF_webhook_eventos_procesado DEFAULT (0),
    recibido_en  DATETIME2(3) NOT NULL CONSTRAINT DF_webhook_eventos_recibido_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UX_webhook_eventos_evento UNIQUE (proveedor, evento_id)
);
GO

-- ── envios_comprobante (HU-020) ─────────────────────────────────────────
CREATE TABLE dbo.envios_comprobante (
    id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_envios_comprobante PRIMARY KEY,
    pedido_id   INT NOT NULL CONSTRAINT FK_envios_comprobante_pedidos REFERENCES dbo.pedidos(id),
    canal       VARCHAR(15) NOT NULL CONSTRAINT DF_envios_comprobante_canal DEFAULT ('whatsapp')
                CONSTRAINT CK_envios_comprobante_canal CHECK (canal IN ('whatsapp')),
    telefono    CHAR(9) NOT NULL
                CONSTRAINT CK_envios_comprobante_telefono
                CHECK (telefono LIKE '9[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
    estado      VARCHAR(15) NOT NULL
                CONSTRAINT CK_envios_comprobante_estado CHECK (estado IN ('pendiente', 'enviado', 'fallido')),
    enviado_por INT NULL CONSTRAINT FK_envios_comprobante_usuarios REFERENCES dbo.usuarios(id),
    creado_en   DATETIME2(0) NOT NULL CONSTRAINT DF_envios_comprobante_creado_en DEFAULT (SYSUTCDATETIME())
);
GO

CREATE INDEX IX_envios_comprobante_pedido ON dbo.envios_comprobante(pedido_id);
GO

-- ==== REVERSIÓN (NO se ejecuta; copiar y correr a mano solo si es necesario) ====
-- DROP TABLE dbo.envios_comprobante;
-- DROP TABLE dbo.webhook_eventos;
-- DROP TABLE dbo.pagos;
-- DELETE FROM dbo.migraciones_aplicadas WHERE nombre = '0004_pagos_notificaciones.sql';
