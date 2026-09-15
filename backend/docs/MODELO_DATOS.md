# Modelo de datos — RiverVet (4 sprints)

Base: **SQL Server** (`rivervet_db` en local, Azure SQL en producción). El esquema completo se crea con las migraciones de [`database/migraciones/`](../database/migraciones):

| Migración | Sprint | Tablas |
|---|---|---|
| `0001_esquema_inicial.sql` | 1 | usuarios, refresh_tokens, auditoria, clientes, mascotas |
| `0002_citas_historial_vacunas.sql` | 2 | citas, historial_clinico, vacunas |
| `0003_productos_ventas.sql` | 3 | categorias, productos, carritos, carrito_items, pedidos, pedido_items |
| `0004_pagos_notificaciones.sql` | 4 | pagos, webhook_eventos, envios_comprobante |

Las tablas de los sprints 2 a 4 ya existen, pero todavía no tienen endpoints. Si el modelo cambia, **se agrega una migración nueva**; nunca se edita una ya aplicada.

## Diagrama entidad-relación

```mermaid
erDiagram
    usuarios ||--o{ refresh_tokens : "tiene sesiones"
    usuarios ||--o{ auditoria : "realiza"
    usuarios |o--o| clientes : "cuenta web de"
    clientes ||--o{ mascotas : "es dueño de"
    mascotas ||--o{ citas : "tiene"
    usuarios ||--o{ citas : "atiende (veterinario)"
    mascotas ||--o{ historial_clinico : "tiene"
    citas |o--o| historial_clinico : "genera"
    usuarios ||--o{ historial_clinico : "registra (veterinario)"
    mascotas ||--o{ vacunas : "recibe"
    categorias ||--o{ productos : "agrupa"
    usuarios ||--o| carritos : "tiene"
    carritos ||--o{ carrito_items : "contiene"
    productos ||--o{ carrito_items : "está en"
    clientes ||--o{ pedidos : "hace"
    pedidos ||--|{ pedido_items : "contiene"
    productos ||--o{ pedido_items : "vendido en"
    pedidos ||--o{ pagos : "se paga con"
    pedidos ||--o{ envios_comprobante : "comprobante"

    usuarios {
        int id PK
        nvarchar email UK
        varchar password_hash "bcrypt, 60"
        nvarchar rol "CHECK 4 roles"
        bit activo
        int intentos_fallidos
        datetime2 bloqueado_hasta
        bit eliminado
    }
    refresh_tokens {
        bigint id PK
        int usuario_id FK
        char token_hash UK "SHA-256"
        uniqueidentifier familia_id
        datetime2 expira_en
        datetime2 revocado_en
        varchar motivo_revocacion
        bigint reemplazado_por_id FK
    }
    auditoria {
        bigint id PK
        int usuario_id FK
        varchar accion
        varchar recurso
        varchar recurso_id
        nvarchar detalle "JSON"
        varchar ip
        datetime2 creado_en
    }
    clientes {
        int id PK
        int usuario_id FK "UK filtrado"
        char dni "UK filtrado"
        nvarchar nombres
        nvarchar apellidos
        nvarchar email "UK filtrado"
        char telefono
        bit consentimiento_datos
        bit eliminado
    }
    mascotas {
        int id PK
        int cliente_id FK
        nvarchar nombre
        nvarchar especie "CHECK"
        date fecha_nacimiento
        decimal peso_kg
        bit eliminado
    }
    citas {
        int id PK
        int mascota_id FK
        int veterinario_id FK
        datetime2 fecha_hora "UTC, bloque 30 min"
        varchar estado "CHECK"
    }
    historial_clinico {
        int id PK
        int mascota_id FK
        int veterinario_id FK
        int cita_id FK "UK filtrado"
        nvarchar diagnostico
        nvarchar tratamiento
    }
    vacunas {
        int id PK
        int mascota_id FK
        nvarchar tipo_vacuna
        date fecha_aplicacion
        nvarchar lote
        date proxima_dosis
    }
    categorias {
        int id PK
        nvarchar nombre UK
    }
    productos {
        int id PK
        int categoria_id FK
        varchar sku "UK filtrado"
        decimal precio "10,2"
        int stock "CHECK >= 0"
    }
    carritos {
        int id PK
        int usuario_id FK "UK"
    }
    carrito_items {
        int carrito_id PK
        int producto_id PK
        int cantidad
    }
    pedidos {
        int id PK
        varchar codigo UK
        int cliente_id FK
        varchar canal
        varchar estado
        decimal total
    }
    pedido_items {
        int id PK
        int pedido_id FK
        int producto_id FK
        decimal precio_unitario "copia del precio"
        decimal subtotal "calculada"
    }
    pagos {
        int id PK
        int pedido_id FK
        varchar metodo
        varchar estado
        decimal monto
        varchar codigo_operacion "Yape, UK filtrado"
        varchar referencia_externa "pasarela"
    }
    webhook_eventos {
        bigint id PK
        varchar proveedor
        varchar evento_id "UK con proveedor"
    }
    envios_comprobante {
        int id PK
        int pedido_id FK
        char telefono
        varchar estado
    }
```

## Restricciones que protegen el negocio

La base de datos es la **última línea de defensa**: aunque el código tuviera un error, estas reglas no se pueden romper.

| Restricción | Tabla | Qué impide |
|---|---|---|
| `UX_citas_veterinario_horario` (índice único filtrado) | citas | **Doble agendamiento**: dos citas vigentes del mismo veterinario a la misma hora (HU-008) |
| `UX_citas_mascota_horario` | citas | La misma mascota en dos citas simultáneas |
| `CK_citas_bloque` | citas | Citas fuera de los bloques de :00 y :30 |
| `CK_productos_stock` (`stock >= 0`) | productos | **Stock negativo** (HU-016) |
| `UX_pagos_yape_operacion` | pagos | Usar el mismo código de operación de Yape para dos pedidos |
| `UX_webhook_eventos_evento` | webhook_eventos | Procesar dos veces el mismo aviso de la pasarela |
| `UX_refresh_tokens_hash` | refresh_tokens | Tokens de sesión repetidos |
| `UX_clientes_dni` (filtrado por `eliminado = 0`) | clientes | Dos clientes activos con el mismo DNI |
| `CK_clientes_dni`, `CK_clientes_telefono` | clientes | DNI que no sea de 8 dígitos; celular que no sea de 9 dígitos empezando por 9 |
| `CK_vacunas_proxima_dosis` | vacunas | Próxima dosis anterior a la aplicación |
| `CK_auditoria_detalle_json` | auditoria | Detalle que no sea JSON válido |

Todas se probaron contra SQL Server real (ver el job `base-de-datos` de la CI y las pruebas de integración).

## Decisiones de diseño

- **Borrado lógico** (`eliminado BIT` + `eliminado_en`). El usuario de aplicación **no tiene permiso DELETE** (salvo en `carrito_items`), así que ni una inyección SQL podría borrar datos físicamente.
- **Fechas en UTC** (`SYSUTCDATETIME()`). Se convierten a `America/Lima` al mostrarlas. Las columnas `DATE` (nacimiento, vacunas) viajan como texto `AAAA-MM-DD` para evitar corrimientos de zona horaria.
- **Índices únicos filtrados** (`WHERE eliminado = 0`): un cliente dado de baja puede volver a registrarse con su DNI.
- **Dinero en `DECIMAL(10,2)`**, nunca `FLOAT`.
- **`pedido_items.precio_unitario`** copia el precio al momento de la venta. El precio se lee de `productos`, nunca del body.
- **Sin columnas de tarjeta**: ni número, ni CVV, ni vencimiento (alcance PCI DSS, ver `0004`).
- **Triage sin tabla**: por minimización de datos (Ley N° 29733), los síntomas consultados no se guardan.
- **Auditoría de solo-agregar** para la app (`DENY UPDATE, DELETE`).
