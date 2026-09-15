using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Ventas;

namespace RiverVet.Infraestructura.Datos;

/// <summary>Carrito (HU-015). Todo se busca por usuario_id: el de la sesión.</summary>
public sealed class CarritoRepositorio(SesionBd sesion) : RepositorioBase(sesion), ICarritoRepositorio
{
    public Task<int?> ObtenerIdAsync(int usuarioId, CancellationToken ct = default) =>
        EscalarOpcionalAsync<int?>("SELECT id FROM dbo.carritos WHERE usuario_id = @usuarioId", new { usuarioId }, ct);

    // El primer "agregar" crea el carrito. Si dos pestañas lo hacen a la vez,
    // el índice único UX_carritos_usuario deja pasar solo una inserción; la
    // otra recibe el duplicado y simplemente lee el carrito ya creado.
    public async Task<int> ObtenerOCrearAsync(int usuarioId, CancellationToken ct = default)
    {
        if (await ObtenerIdAsync(usuarioId, ct) is int existente)
        {
            return existente;
        }
        try
        {
            return await EscalarAsync<int>(
                "INSERT INTO dbo.carritos (usuario_id) OUTPUT INSERTED.id VALUES (@usuarioId)", new { usuarioId }, ct);
        }
        catch (RegistroDuplicadoException)
        {
            return await ObtenerIdAsync(usuarioId, ct) ?? throw new InvalidOperationException("No se pudo crear el carrito");
        }
    }

    public Task<int?> BloquearAsync(int usuarioId, CancellationToken ct = default) =>
        EscalarOpcionalAsync<int?>(
            "SELECT id FROM dbo.carritos WITH (UPDLOCK, ROWLOCK) WHERE usuario_id = @usuarioId", new { usuarioId }, ct);

    // Precio y stock ACTUALES de productos (el carrito no guarda precios).
    public Task<IReadOnlyList<ItemCarrito>> ListarItemsAsync(int usuarioId, CancellationToken ct = default) =>
        ConsultarAsync<ItemCarrito>(
            """
            SELECT p.id AS productoId, p.sku, p.nombre, p.imagen_url AS imagenUrl,
                   p.precio AS precioUnitario, ci.cantidad,
                   CAST(ci.cantidad * p.precio AS DECIMAL(10,2)) AS subtotal,
                   CAST(CASE WHEN p.activo = 1 AND p.eliminado = 0 AND cat.activo = 1 THEN 1 ELSE 0 END AS BIT) AS activo,
                   p.stock
            FROM dbo.carritos c
            JOIN dbo.carrito_items ci ON ci.carrito_id = c.id
            JOIN dbo.productos p ON p.id = ci.producto_id
            JOIN dbo.categorias cat ON cat.id = p.categoria_id
            WHERE c.usuario_id = @usuarioId
            ORDER BY ci.agregado_en, p.id
            """,
            new { usuarioId },
            ct);

    public Task<IReadOnlyList<LineaVenta>> LineasAsync(int carritoId, CancellationToken ct = default) =>
        ConsultarAsync<LineaVenta>(
            "SELECT producto_id AS productoId, cantidad FROM dbo.carrito_items WHERE carrito_id = @carritoId ORDER BY producto_id",
            new { carritoId },
            ct);

    public Task<int?> CantidadAsync(int carritoId, int productoId, CancellationToken ct = default) =>
        EscalarOpcionalAsync<int?>(
            "SELECT cantidad FROM dbo.carrito_items WHERE carrito_id = @carritoId AND producto_id = @productoId",
            new { carritoId, productoId },
            ct);

    public Task GuardarItemAsync(int carritoId, int productoId, int cantidad, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.carrito_items SET cantidad = @cantidad
            WHERE carrito_id = @carritoId AND producto_id = @productoId;

            IF @@ROWCOUNT = 0
                INSERT INTO dbo.carrito_items (carrito_id, producto_id, cantidad)
                VALUES (@carritoId, @productoId, @cantidad);

            UPDATE dbo.carritos SET actualizado_en = SYSUTCDATETIME() WHERE id = @carritoId;
            """,
            new { carritoId, productoId, cantidad },
            ct);

    // carrito_items es la única tabla con DELETE físico permitido (ver 02_permisos_app.sql).
    public async Task<bool> QuitarItemAsync(int carritoId, int productoId, CancellationToken ct = default) =>
        await EjecutarAsync(
            "DELETE FROM dbo.carrito_items WHERE carrito_id = @carritoId AND producto_id = @productoId",
            new { carritoId, productoId },
            ct) > 0;

    public Task VaciarAsync(int carritoId, CancellationToken ct = default) =>
        EjecutarAsync("DELETE FROM dbo.carrito_items WHERE carrito_id = @carritoId", new { carritoId }, ct);
}

/// <summary>
/// Pedidos (HU-016). ★ CONTROL DE PROPIEDAD ★ las lecturas filtran con
/// "(@propietarioId IS NULL OR c.usuario_id = @propietarioId)" en el SQL.
/// </summary>
public sealed class PedidosRepositorio(SesionBd sesion) : RepositorioBase(sesion), IPedidosRepositorio
{
    private const string ConsultaResumen = """
        SELECT pe.id, pe.codigo, pe.cliente_id AS clienteId, CONCAT(c.nombres, N' ', c.apellidos) AS clienteNombre,
               pe.canal, pe.estado, pe.total,
               (SELECT COUNT(*) FROM dbo.pedido_items pi WHERE pi.pedido_id = pe.id) AS cantidadItems,
               u.nombre AS registradoPorNombre, pe.creado_en AS creadoEn, pe.actualizado_en AS actualizadoEn
        FROM dbo.pedidos pe
        JOIN dbo.clientes c ON c.id = pe.cliente_id
        LEFT JOIN dbo.usuarios u ON u.id = pe.registrado_por
        """;

    public Task<int?> ClienteIdDeUsuarioAsync(int usuarioId, CancellationToken ct = default) =>
        EscalarOpcionalAsync<int?>(
            "SELECT id FROM dbo.clientes WHERE usuario_id = @usuarioId AND eliminado = 0", new { usuarioId }, ct);

    public async Task<bool> ClienteExisteAsync(int clienteId, CancellationToken ct = default) =>
        await EscalarOpcionalAsync<int?>("SELECT 1 FROM dbo.clientes WHERE id = @clienteId AND eliminado = 0", new { clienteId }, ct) is not null;

    // ★ HU-016 ★ Verifica, descuenta y lee el precio en UNA sentencia. El
    // UPDATE bloquea la fila: una compra simultánea del mismo producto espera
    // y luego evalúa "stock >= @cantidad" con el stock ya descontado.
    public Task<ProductoVendido?> DescontarStockAsync(int productoId, int cantidad, CancellationToken ct = default) =>
        InsertarYDevolverAsync<ProductoVendido>(
            """
            UPDATE p
            SET stock = p.stock - @cantidad, actualizado_en = SYSUTCDATETIME()
            OUTPUT INSERTED.id, INSERTED.sku, INSERTED.nombre, INSERTED.precio
            FROM dbo.productos p
            JOIN dbo.categorias cat ON cat.id = p.categoria_id AND cat.activo = 1
            WHERE p.id = @productoId AND p.eliminado = 0 AND p.activo = 1 AND p.stock >= @cantidad
            """,
            new { productoId, cantidad },
            ct);

    public Task<EstadoStock?> EstadoStockAsync(int productoId, CancellationToken ct = default) =>
        ConsultarUnoAsync<EstadoStock>(
            """
            SELECT p.nombre, p.stock,
                   CAST(CASE WHEN p.activo = 1 AND p.eliminado = 0 AND cat.activo = 1 THEN 1 ELSE 0 END AS BIT) AS activo
            FROM dbo.productos p
            JOIN dbo.categorias cat ON cat.id = p.categoria_id
            WHERE p.id = @productoId
            """,
            new { productoId },
            ct);

    // Código público RV-000001 a partir de una SECUENCIA: dos pedidos
    // simultáneos nunca obtienen el mismo número (y el índice único
    // UX_pedidos_codigo lo garantiza de todas formas).
    public async Task<PedidoCreado> CrearAsync(int clienteId, int? registradoPor, string canal, decimal total, CancellationToken ct = default) =>
        await InsertarYDevolverAsync<PedidoCreado>(
            """
            DECLARE @numero INT = NEXT VALUE FOR dbo.seq_pedidos_codigo;
            INSERT INTO dbo.pedidos (codigo, cliente_id, registrado_por, canal, total)
            OUTPUT INSERTED.id, INSERTED.codigo
            VALUES (CONCAT('RV-', FORMAT(@numero, 'D6')), @clienteId, @registradoPor, @canal, @total);
            """,
            new { clienteId, registradoPor, canal = Ascii(canal, 12), total },
            ct) ?? throw new InvalidOperationException("No se pudo crear el pedido");

    public Task InsertarItemAsync(int pedidoId, int productoId, int cantidad, decimal precioUnitario, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            INSERT INTO dbo.pedido_items (pedido_id, producto_id, cantidad, precio_unitario)
            VALUES (@pedidoId, @productoId, @cantidad, @precioUnitario)
            """,
            new { pedidoId, productoId, cantidad, precioUnitario },
            ct);

    public Task<IReadOnlyList<PedidoResumen>> ListarAsync(
        ConsultaPedidos consulta, DateTime? desdeUtc, DateTime? hastaUtc, int? propietarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultarAsync<PedidoResumen>(
            $"""
            SELECT resumen.*, COUNT(*) OVER() AS totalFilas
            FROM ({ConsultaResumen}
                  WHERE pe.eliminado = 0
                    AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
                    AND (@clienteId IS NULL OR pe.cliente_id = @clienteId)
                    AND (@estado IS NULL OR pe.estado = @estado)
                    AND (@canal IS NULL OR pe.canal = @canal)
                    AND (@desde IS NULL OR pe.creado_en >= @desde)
                    AND (@hasta IS NULL OR pe.creado_en < @hasta)) AS resumen
            ORDER BY resumen.creadoEn DESC, resumen.id DESC
            OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY
            """,
            new
            {
                propietarioId,
                consulta.ClienteId,
                estado = Ascii(consulta.Estado, 25),
                canal = Ascii(consulta.Canal, 12),
                desde = desdeUtc,
                hasta = hastaUtc,
                offset = consulta.Offset,
                limite = consulta.Limite,
            },
            ct);
    }

    public Task<PedidoResumen?> ObtenerAsync(int id, int? propietarioId, CancellationToken ct = default) =>
        ConsultarUnoAsync<PedidoResumen>(
            $"""
            {ConsultaResumen}
            WHERE pe.id = @id AND pe.eliminado = 0
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            """,
            new { id, propietarioId },
            ct);

    public Task<IReadOnlyList<ItemPedido>> ItemsAsync(int pedidoId, CancellationToken ct = default) =>
        ConsultarAsync<ItemPedido>(
            """
            SELECT pi.producto_id AS productoId, p.sku, p.nombre, pi.cantidad,
                   pi.precio_unitario AS precioUnitario, pi.subtotal
            FROM dbo.pedido_items pi
            JOIN dbo.productos p ON p.id = pi.producto_id
            WHERE pi.pedido_id = @pedidoId
            ORDER BY pi.id
            """,
            new { pedidoId },
            ct);

    public Task<IReadOnlyList<PagoPedido>> PagosAsync(int pedidoId, CancellationToken ct = default) =>
        ConsultarAsync<PagoPedido>(
            """
            SELECT pa.id, pa.metodo, pa.estado, pa.monto, pa.codigo_operacion AS codigoOperacion, pa.proveedor,
                   u.nombre AS verificadoPorNombre, pa.verificado_en AS verificadoEn, pa.creado_en AS creadoEn
            FROM dbo.pagos pa
            LEFT JOIN dbo.usuarios u ON u.id = pa.verificado_por
            WHERE pa.pedido_id = @pedidoId
            ORDER BY pa.id
            """,
            new { pedidoId },
            ct);

    public Task<PedidoBloqueado?> BloquearAsync(int id, int? propietarioId, CancellationToken ct = default) =>
        ConsultarUnoAsync<PedidoBloqueado>(
            """
            SELECT pe.id, pe.codigo, pe.estado, pe.total, pe.cliente_id AS clienteId, c.telefono AS clienteTelefono
            FROM dbo.pedidos pe WITH (UPDLOCK, ROWLOCK)
            JOIN dbo.clientes c ON c.id = pe.cliente_id
            WHERE pe.id = @id AND pe.eliminado = 0
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            """,
            new { id, propietarioId },
            ct);

    public async Task<bool> CambiarEstadoAsync(int id, string estadoEsperado, string estadoNuevo, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.pedidos SET estado = @estadoNuevo, actualizado_en = SYSUTCDATETIME()
            WHERE id = @id AND estado = @estadoEsperado AND eliminado = 0
            """,
            new { id, estadoEsperado = Ascii(estadoEsperado, 25), estadoNuevo = Ascii(estadoNuevo, 25) },
            ct) > 0;

    public Task DevolverStockAsync(int pedidoId, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE p SET stock = p.stock + pi.cantidad, actualizado_en = SYSUTCDATETIME()
            FROM dbo.productos p
            JOIN dbo.pedido_items pi ON pi.producto_id = p.id
            WHERE pi.pedido_id = @pedidoId
            """,
            new { pedidoId },
            ct);

    public Task AnularPagosPendientesAsync(int pedidoId, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.pagos SET estado = 'rechazado', actualizado_en = SYSUTCDATETIME()
            WHERE pedido_id = @pedidoId AND estado IN ('pendiente', 'pendiente_verificacion')
            """,
            new { pedidoId },
            ct);
}
