-- ═══════════════════════════════════════════════════════════════════════
-- 0003_productos_ventas.sql — Sprint 3
-- Catálogo (HU-013, HU-014), carrito (HU-015) y pedidos/ventas (HU-016).
-- Montos en DECIMAL(10,2): nunca FLOAT para dinero (0.1 + 0.2 <> 0.3).
-- ═══════════════════════════════════════════════════════════════════════

CREATE TABLE dbo.categorias (
    id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_categorias PRIMARY KEY,
    nombre    NVARCHAR(60) NOT NULL,
    activo    BIT NOT NULL CONSTRAINT DF_categorias_activo DEFAULT (1),
    creado_en DATETIME2(0) NOT NULL CONSTRAINT DF_categorias_creado_en DEFAULT (SYSUTCDATETIME())
);
GO

CREATE UNIQUE INDEX UX_categorias_nombre ON dbo.categorias(nombre);
GO

CREATE TABLE dbo.productos (
    id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_productos PRIMARY KEY,
    categoria_id   INT NOT NULL CONSTRAINT FK_productos_categorias REFERENCES dbo.categorias(id),
    sku            VARCHAR(30) NOT NULL,
    nombre         NVARCHAR(120) NOT NULL,
    descripcion    NVARCHAR(1000) NULL,
    precio         DECIMAL(10,2) NOT NULL CONSTRAINT CK_productos_precio CHECK (precio >= 0),
    -- ★ Última línea de defensa contra el stock negativo (HU-016) ★
    -- Aunque el código tuviera un error, la base de datos rechaza cualquier
    -- UPDATE que deje el stock por debajo de cero.
    stock          INT NOT NULL CONSTRAINT DF_productos_stock DEFAULT (0)
                   CONSTRAINT CK_productos_stock CHECK (stock >= 0),
    stock_minimo   INT NOT NULL CONSTRAINT DF_productos_stock_minimo DEFAULT (5)
                   CONSTRAINT CK_productos_stock_minimo CHECK (stock_minimo >= 0),
    imagen_url     NVARCHAR(500) NULL,
    activo         BIT NOT NULL CONSTRAINT DF_productos_activo DEFAULT (1),
    creado_por     INT NULL CONSTRAINT FK_productos_creado_por REFERENCES dbo.usuarios(id),
    creado_en      DATETIME2(0) NOT NULL CONSTRAINT DF_productos_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en DATETIME2(0) NOT NULL CONSTRAINT DF_productos_actualizado_en DEFAULT (SYSUTCDATETIME()),
    eliminado      BIT NOT NULL CONSTRAINT DF_productos_eliminado DEFAULT (0),
    eliminado_en   DATETIME2(0) NULL
);
GO

CREATE UNIQUE INDEX UX_productos_sku ON dbo.productos(sku) WHERE eliminado = 0;
CREATE INDEX IX_productos_categoria ON dbo.productos(categoria_id) WHERE eliminado = 0 AND activo = 1;
CREATE INDEX IX_productos_nombre ON dbo.productos(nombre) WHERE eliminado = 0;
GO

-- ── carrito: uno por usuario cliente ────────────────────────────────────
CREATE TABLE dbo.carritos (
    id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_carritos PRIMARY KEY,
    usuario_id     INT NOT NULL CONSTRAINT FK_carritos_usuarios REFERENCES dbo.usuarios(id),
    creado_en      DATETIME2(0) NOT NULL CONSTRAINT DF_carritos_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en DATETIME2(0) NOT NULL CONSTRAINT DF_carritos_actualizado_en DEFAULT (SYSUTCDATETIME())
);
GO

CREATE UNIQUE INDEX UX_carritos_usuario ON dbo.carritos(usuario_id);
GO

-- El carrito NO guarda precios: el precio se lee de productos al confirmar
-- el pedido. Así nadie puede "congelar" un precio viejo ni mandarlo en el body.
-- Es la única tabla donde la app puede hacer DELETE físico (quitar un ítem no
-- es un dato de negocio que haya que conservar).
CREATE TABLE dbo.carrito_items (
    carrito_id  INT NOT NULL CONSTRAINT FK_carrito_items_carritos REFERENCES dbo.carritos(id),
    producto_id INT NOT NULL CONSTRAINT FK_carrito_items_productos REFERENCES dbo.productos(id),
    cantidad    INT NOT NULL CONSTRAINT CK_carrito_items_cantidad CHECK (cantidad BETWEEN 1 AND 99),
    agregado_en DATETIME2(0) NOT NULL CONSTRAINT DF_carrito_items_agregado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_carrito_items PRIMARY KEY (carrito_id, producto_id)
);
GO

-- ── pedidos (venta web o presencial) ────────────────────────────────────
-- Código público del pedido (RV-000123) generado con una secuencia. Se muestra
-- al cliente en lugar del id interno.
CREATE SEQUENCE dbo.seq_pedidos_codigo AS INT START WITH 1 INCREMENT BY 1;
GO

CREATE TABLE dbo.pedidos (
    id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pedidos PRIMARY KEY,
    codigo         VARCHAR(20) NOT NULL,
    cliente_id     INT NOT NULL CONSTRAINT FK_pedidos_clientes REFERENCES dbo.clientes(id),
    -- Recepcionista que registró la venta presencial; NULL si fue compra web.
    registrado_por INT NULL CONSTRAINT FK_pedidos_registrado_por REFERENCES dbo.usuarios(id),
    canal          VARCHAR(12) NOT NULL CONSTRAINT CK_pedidos_canal CHECK (canal IN ('web', 'presencial')),
    estado         VARCHAR(25) NOT NULL CONSTRAINT DF_pedidos_estado DEFAULT ('pendiente_pago')
                   CONSTRAINT CK_pedidos_estado
                   CHECK (estado IN ('pendiente_pago', 'pendiente_verificacion', 'pagado',
                                     'entregado', 'cancelado')),
    total          DECIMAL(10,2) NOT NULL CONSTRAINT CK_pedidos_total CHECK (total >= 0),
    creado_en      DATETIME2(0) NOT NULL CONSTRAINT DF_pedidos_creado_en DEFAULT (SYSUTCDATETIME()),
    actualizado_en DATETIME2(0) NOT NULL CONSTRAINT DF_pedidos_actualizado_en DEFAULT (SYSUTCDATETIME()),
    eliminado      BIT NOT NULL CONSTRAINT DF_pedidos_eliminado DEFAULT (0),
    eliminado_en   DATETIME2(0) NULL
);
GO

CREATE UNIQUE INDEX UX_pedidos_codigo ON dbo.pedidos(codigo);
CREATE INDEX IX_pedidos_cliente ON dbo.pedidos(cliente_id, creado_en DESC);
CREATE INDEX IX_pedidos_creado_en ON dbo.pedidos(creado_en) INCLUDE (estado, total);
GO

CREATE TABLE dbo.pedido_items (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pedido_items PRIMARY KEY,
    pedido_id       INT NOT NULL CONSTRAINT FK_pedido_items_pedidos REFERENCES dbo.pedidos(id),
    producto_id     INT NOT NULL CONSTRAINT FK_pedido_items_productos REFERENCES dbo.productos(id),
    cantidad        INT NOT NULL CONSTRAINT CK_pedido_items_cantidad CHECK (cantidad > 0),
    -- Copia del precio AL MOMENTO de la venta, leída de productos (nunca del
    -- body). Si mañana sube el precio, este pedido conserva el que se cobró.
    precio_unitario DECIMAL(10,2) NOT NULL CONSTRAINT CK_pedido_items_precio CHECK (precio_unitario >= 0),
    subtotal        AS CAST(cantidad * precio_unitario AS DECIMAL(10,2)) PERSISTED,
    CONSTRAINT UX_pedido_items_producto UNIQUE (pedido_id, producto_id)
);
GO

-- ==== REVERSIÓN (NO se ejecuta; copiar y correr a mano solo si es necesario) ====
-- DROP TABLE dbo.pedido_items;
-- DROP TABLE dbo.pedidos;
-- DROP SEQUENCE dbo.seq_pedidos_codigo;
-- DROP TABLE dbo.carrito_items;
-- DROP TABLE dbo.carritos;
-- DROP TABLE dbo.productos;
-- DROP TABLE dbo.categorias;
-- DELETE FROM dbo.migraciones_aplicadas WHERE nombre = '0003_productos_ventas.sql';
