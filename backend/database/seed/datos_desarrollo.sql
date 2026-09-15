-- ═══════════════════════════════════════════════════════════════════════
-- datos_desarrollo.sql — datos de prueba (SOLO desarrollo y pruebas)
--
-- RiverVet.Migrador (comando seed) se niega a ejecutarlo en entorno Production.
-- Es idempotente: si el admin ya existe, no hace nada.
--
-- Contraseña de TODOS los usuarios de prueba:  RiverVet#2026
-- (ya hasheada con bcrypt, cost 12; cada hash tiene su propia sal, por eso
-- aunque la contraseña sea la misma los hashes son distintos).
-- ═══════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.usuarios WHERE email = N'admin@rivervet.pe')
BEGIN
    PRINT 'Seed ya aplicado: no se hace nada.';
    RETURN;
END;

-- ── Usuarios del personal ───────────────────────────────────────────────
INSERT INTO dbo.usuarios (nombre, email, password_hash, rol) VALUES
 (N'Administración RiverVet', N'admin@rivervet.pe',      '$2b$12$DQWUuV44oPYNgu4J4G/bBuZ599kKJCcfUfGxfbkaeoG.dA0XfnG96', N'admin'),
 (N'Dra. Carla López',        N'vet.lopez@rivervet.pe',  '$2b$12$ZzMChmQOliOGQrF419m8u.gfVpCIle1Z2SndotAE6ZP8pxQ.ndxvG', N'veterinario'),
 (N'Dr. Miguel Quispe',       N'vet.quispe@rivervet.pe', '$2b$12$oXcIBR.evMyO7ldcbYdot.rKdYVwf/7vJ1O6R59jcuDFrYD2jbRX6', N'veterinario'),
 (N'Rosa Huamán',             N'recepcion@rivervet.pe',  '$2b$12$MbJJUJYADcNTYy23eWD6RO5m/XLnZnnPKKFSp12Z8IStPT8R56tGu', N'recepcionista');

-- ── Usuarios cliente (cuentas web) ──────────────────────────────────────
INSERT INTO dbo.usuarios (nombre, email, password_hash, rol) VALUES
 (N'Ana Torres',      N'ana.torres@correo.pe',     '$2b$12$zx83HkiBe/OST0C.x71kWOuu0EjPjORpN76gkOgihOEILLxlGZntq', N'cliente'),
 (N'Luis Ramírez',    N'luis.ramirez@correo.pe',   '$2b$12$wKELSwfhYYwGaCGa29gjyudVLs5auyh0vIIwdEjNhxml9.pIQABye', N'cliente'),
 (N'María Flores',    N'maria.flores@correo.pe',   '$2b$12$gcY65dCBXQj4Kx1uwH1AbOjTdJ4wYkYQRnCMcq71F8RpUL.Gp7FwO', N'cliente'),
 (N'Jorge Castillo',  N'jorge.castillo@correo.pe', '$2b$12$Enxh1JNtNHHnTUDCbVvnvO2Pjj/w3RWfXkSpu6klq0VFXFdzWpTV2', N'cliente'),
 (N'Lucía Vargas',    N'lucia.vargas@correo.pe',   '$2b$12$9BDggIRsHamn/5oO4.fqA.DJ0vv2A9YYDKU0xtBEpdMgbzrAa2fGS', N'cliente');

DECLARE @recepcion INT = (SELECT id FROM dbo.usuarios WHERE email = N'recepcion@rivervet.pe');
DECLARE @admin     INT = (SELECT id FROM dbo.usuarios WHERE email = N'admin@rivervet.pe');

-- ── Clientes (vinculados a sus cuentas) ─────────────────────────────────
INSERT INTO dbo.clientes (usuario_id, dni, nombres, apellidos, email, telefono, direccion,
                          consentimiento_datos, consentimiento_en, creado_por)
SELECT u.id, v.dni, v.nombres, v.apellidos, u.email, v.telefono, v.direccion, 1, SYSUTCDATETIME(), @recepcion
FROM (VALUES
    (N'ana.torres@correo.pe',     '70123456', N'Ana',   N'Torres Medina',    '987654321', N'Av. Arequipa 1234, Lince'),
    (N'luis.ramirez@correo.pe',   '71234567', N'Luis',  N'Ramírez Soto',     '986543210', N'Jr. Huallaga 456, Cercado de Lima'),
    (N'maria.flores@correo.pe',   '72345678', N'María', N'Flores Paredes',   '985432109', N'Calle Las Begonias 789, San Isidro'),
    (N'jorge.castillo@correo.pe', '73456789', N'Jorge', N'Castillo Ñahui',   '984321098', N'Av. Brasil 2020, Pueblo Libre'),
    (N'lucia.vargas@correo.pe',   '74567890', N'Lucía', N'Vargas Chávez',    '983210987', N'Av. La Marina 1500, San Miguel')
) AS v(email, dni, nombres, apellidos, telefono, direccion)
JOIN dbo.usuarios u ON u.email = v.email;

-- ── Mascotas (8) ────────────────────────────────────────────────────────
INSERT INTO dbo.mascotas (cliente_id, nombre, especie, raza, sexo, fecha_nacimiento, peso_kg, color, esterilizado, creado_por)
SELECT c.id, v.nombre, v.especie, v.raza, v.sexo, v.nacimiento, v.peso, v.color, v.esterilizado, @recepcion
FROM (VALUES
    ('70123456', N'Firulais', N'perro',  N'Mestizo',          N'macho',  CAST('2019-03-15' AS DATE), CAST(18.50 AS DECIMAL(5,2)), N'Marrón',          CAST(1 AS BIT)),
    ('70123456', N'Mishi',    N'gato',   N'Siamés',           N'hembra', CAST('2021-07-02' AS DATE), CAST(4.20 AS DECIMAL(5,2)),  N'Crema',           CAST(1 AS BIT)),
    ('71234567', N'Rocky',    N'perro',  N'Pastor Alemán',    N'macho',  CAST('2018-11-20' AS DATE), CAST(32.00 AS DECIMAL(5,2)), N'Negro y fuego',   CAST(0 AS BIT)),
    ('72345678', N'Luna',     N'perro',  N'Shih Tzu',         N'hembra', CAST('2022-01-10' AS DATE), CAST(6.30 AS DECIMAL(5,2)),  N'Blanco',          CAST(1 AS BIT)),
    ('72345678', N'Pepe',     N'ave',    N'Periquito',        N'macho',  CAST('2023-05-05' AS DATE), CAST(0.04 AS DECIMAL(5,2)),  N'Verde',           NULL),
    ('73456789', N'Max',      N'perro',  N'Labrador',         N'macho',  CAST('2020-09-30' AS DATE), CAST(29.80 AS DECIMAL(5,2)), N'Dorado',          CAST(1 AS BIT)),
    ('74567890', N'Nala',     N'gato',   N'Mestizo',          N'hembra', CAST('2020-02-14' AS DATE), CAST(3.90 AS DECIMAL(5,2)),  N'Atigrado',        CAST(1 AS BIT)),
    ('74567890', N'Coco',     N'conejo', N'Cabeza de león',   N'hembra', CAST('2023-08-01' AS DATE), CAST(1.60 AS DECIMAL(5,2)),  N'Gris',            CAST(0 AS BIT))
) AS v(dni, nombre, especie, raza, sexo, nacimiento, peso, color, esterilizado)
JOIN dbo.clientes c ON c.dni = v.dni AND c.eliminado = 0;

-- ── Categorías y productos (10) ─────────────────────────────────────────
INSERT INTO dbo.categorias (nombre) VALUES
 (N'Alimentos'), (N'Accesorios'), (N'Higiene'), (N'Medicamentos'), (N'Juguetes');

INSERT INTO dbo.productos (categoria_id, sku, nombre, descripcion, precio, stock, stock_minimo, creado_por)
SELECT cat.id, v.sku, v.nombre, v.descripcion, v.precio, v.stock, v.minimo, @admin
FROM (VALUES
    (N'Alimentos',    'ALI-001', N'Croquetas perro adulto 15 kg', N'Alimento balanceado para perros adultos de raza mediana.', CAST(189.90 AS DECIMAL(10,2)), 25, 5),
    (N'Alimentos',    'ALI-002', N'Croquetas gato adulto 3 kg',   N'Alimento balanceado para gatos adultos, sabor pollo.',     CAST(64.50 AS DECIMAL(10,2)),  40, 8),
    (N'Alimentos',    'ALI-003', N'Snack dental para perro',      N'Paquete de 7 unidades. Ayuda a reducir el sarro.',          CAST(22.00 AS DECIMAL(10,2)),  3,  5),
    (N'Accesorios',   'ACC-001', N'Collar ajustable talla M',     N'Collar de nylon con hebilla de seguridad.',                 CAST(29.90 AS DECIMAL(10,2)),  15, 3),
    (N'Accesorios',   'ACC-002', N'Arnés antitirones talla L',    N'Arnés acolchado con anilla frontal.',                       CAST(79.00 AS DECIMAL(10,2)),  1,  2),
    (N'Higiene',      'HIG-001', N'Shampoo hipoalergénico 500 ml',N'Para pieles sensibles, sin parabenos.',                     CAST(35.50 AS DECIMAL(10,2)),  20, 5),
    (N'Higiene',      'HIG-002', N'Arena sanitaria 10 kg',        N'Arena aglomerante con control de olores.',                  CAST(42.90 AS DECIMAL(10,2)),  18, 5),
    (N'Medicamentos', 'MED-001', N'Antipulgas pipeta 10-20 kg',   N'Pipeta de uso externo. Consulte a su veterinario.',        CAST(55.00 AS DECIMAL(10,2)),  30, 10),
    (N'Medicamentos', 'MED-002', N'Desparasitante oral perro',    N'Tabletas. Dosis según peso: consulte a su veterinario.',   CAST(18.50 AS DECIMAL(10,2)),  50, 10),
    (N'Juguetes',     'JUG-001', N'Pelota de goma resistente',    N'Pelota de caucho natural, 7 cm.',                           CAST(15.90 AS DECIMAL(10,2)),  35, 5)
) AS v(categoria, sku, nombre, descripcion, precio, stock, minimo)
JOIN dbo.categorias cat ON cat.nombre = v.categoria;

PRINT 'Seed aplicado: 9 usuarios, 5 clientes, 8 mascotas, 10 productos.';
