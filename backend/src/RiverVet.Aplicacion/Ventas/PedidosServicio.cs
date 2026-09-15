using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Ventas;

/// <summary>
/// HU-016: confirmar pedido (web) o registrar venta (presencial), consultar,
/// cancelar y marcar como entregado.
///
/// ★ CÓMO SE EVITA EL STOCK NEGATIVO ★
/// Dos clientes compran la ÚLTIMA unidad al mismo tiempo. La solución
/// ingenua ("SELECT stock; si hay, UPDATE stock - 1") falla: los dos leen
/// stock = 1 antes de que cualquiera descuente, y los dos venden.
///
/// Aquí no hay "leer y después escribir". Cada producto se descuenta con UNA
/// sentencia que verifica y descuenta a la vez:
///
///     UPDATE productos SET stock = stock - @cantidad
///     WHERE id = @id AND stock >= @cantidad
///
/// SQL Server toma un bloqueo exclusivo sobre la fila para ese UPDATE. La
/// segunda compra ESPERA a que la primera termine su transacción y entonces
/// evalúa "stock >= 1" contra el valor ya descontado (0): no actualiza
/// ninguna fila, y el servicio responde 409 y revierte TODO el pedido.
///
/// Por eso basta el nivel de aislamiento READ COMMITTED (el predeterminado):
/// el bloqueo de la fila hace la sincronización. SERIALIZABLE agregaría
/// bloqueos de rango innecesarios y más riesgo de deadlocks sin proteger más.
/// Vale también en Azure SQL, que activa READ_COMMITTED_SNAPSHOT: con esa
/// opción los SELECT leen versiones, pero un UPDATE sigue buscando sus filas
/// con bloqueos (U) sobre el dato actual, así que la verificación del stock
/// siempre ve el valor vigente.
/// Y si todo lo anterior fallara, el CHECK (stock >= 0) de la tabla rechaza
/// la escritura: la base nunca guarda un stock negativo.
///
/// Los productos se descuentan ordenados por id: si dos pedidos llevan los
/// mismos productos, ambos los bloquean en el mismo orden y no pueden quedar
/// esperándose mutuamente (deadlock).
/// </summary>
public sealed class PedidosServicio(
    IPedidosRepositorio pedidos,
    ICarritoRepositorio carritos,
    IUnidadTrabajo unidadTrabajo,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    private const string NoEncontrado = "Pedido no encontrado";

    public async Task<ResultadoPaginado<PedidoResumen>> ListarAsync(ConsultaPedidos consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        DateTime? desde = HoraLima.LeerFecha(consulta.Desde) is DateOnly d ? HoraLima.InicioDelDiaUtc(d) : null;
        DateTime? hasta = HoraLima.LeerFecha(consulta.Hasta) is DateOnly h ? HoraLima.InicioDelDiaUtc(h.AddDays(1)) : null;
        return ResultadoPaginado.Crear(await pedidos.ListarAsync(consulta, desde, hasta, contexto.PropietarioId, ct), consulta);
    }

    /// <summary>Detalle con productos y pagos. Un cliente solo ve los suyos (ajeno = 404).</summary>
    public async Task<PedidoRespuesta> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var pedido = await pedidos.ObtenerAsync(id, contexto.PropietarioId, ct) ?? throw new NoEncontradoException(NoEncontrado);
        return new PedidoRespuesta(pedido, await pedidos.ItemsAsync(id, ct), await pedidos.PagosAsync(id, ct));
    }

    /// <summary>
    /// Compra web del cliente: convierte SU carrito en un pedido. No recibe
    /// productos ni precios en la petición: todo sale del carrito guardado y
    /// de la tabla productos.
    /// </summary>
    public async Task<PedidoRespuesta> ConfirmarCarritoAsync(CancellationToken ct = default)
    {
        var usuarioId = contexto.UsuarioId ?? throw new NoAutenticadoException();
        PedidoCreado creado;
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            var carritoId = await carritos.BloquearAsync(usuarioId, ct);
            var lineas = carritoId is int c ? await carritos.LineasAsync(c, ct) : [];
            if (lineas.Count == 0)
            {
                throw new ConflictoException("Tu carrito está vacío");
            }
            var clienteId = await pedidos.ClienteIdDeUsuarioAsync(usuarioId, ct)
                ?? throw new ConflictoException("Tu cuenta no tiene una ficha de cliente. Acércate a recepción.");

            creado = await RegistrarVentaAsync(clienteId, null, CanalesPedido.Web, lineas, ct);
            // El carrito se vacía en la MISMA transacción: si el pedido se
            // revierte, el carrito queda como estaba.
            await carritos.VaciarAsync(carritoId!.Value, ct);
            await tx.ConfirmarAsync(ct);
        }
        return await ObtenerSinFiltroAsync(creado.Id, ct);
    }

    /// <summary>Venta presencial: la registra recepción para un cliente.</summary>
    public async Task<PedidoRespuesta> RegistrarPresencialAsync(VentaPresencialSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        PedidoCreado creado;
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            if (!await pedidos.ClienteExisteAsync(solicitud.ClienteId!.Value, ct))
            {
                throw new ValidacionException("clienteId", "El cliente no existe");
            }
            var lineas = solicitud.Items!.Select(i => new LineaVenta(i.ProductoId!.Value, i.Cantidad!.Value)).ToList();
            creado = await RegistrarVentaAsync(solicitud.ClienteId.Value, contexto.UsuarioId, CanalesPedido.Presencial, lineas, ct);
            await tx.ConfirmarAsync(ct);
        }
        return await ObtenerSinFiltroAsync(creado.Id, ct);
    }

    /// <summary>
    /// Cancela un pedido aún no pagado y DEVUELVE su stock, todo en una
    /// transacción. El cliente solo cancela los suyos y solo si siguen
    /// pendientes de pago; recepción también los que esperan verificación de Yape.
    /// </summary>
    public async Task<PedidoRespuesta> CancelarAsync(int id, CancelarPedidoSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var esCliente = contexto.PropietarioId is not null;
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            var pedido = await pedidos.BloquearAsync(id, contexto.PropietarioId, ct) ?? throw new NoEncontradoException(NoEncontrado);
            var cancelable = pedido.Estado == EstadosPedido.PendientePago
                || (!esCliente && pedido.Estado == EstadosPedido.PendienteVerificacion);
            if (!cancelable)
            {
                throw new ConflictoException(pedido.Estado switch
                {
                    EstadosPedido.Cancelado => "El pedido ya está cancelado",
                    EstadosPedido.PendienteVerificacion => "Tu pago está en verificación. Comunícate con la clínica para cancelarlo.",
                    _ => "Un pedido pagado no se puede cancelar desde el sistema",
                });
            }
            await pedidos.CambiarEstadoAsync(id, pedido.Estado, EstadosPedido.Cancelado, ct);
            await pedidos.DevolverStockAsync(id, ct);
            // Un Yape en verificación o un pago de pasarela sin confirmar ya
            // no aplica: quedan rechazados para que nadie los apruebe después.
            await pedidos.AnularPagosPendientesAsync(id, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Cancelar, RecursosAuditoria.Pedido, id,
                new { pedido.Codigo, estadoAnterior = pedido.Estado, motivo = solicitud.Motivo }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await ObtenerSinFiltroAsync(id, ct);
    }

    /// <summary>Recepción entrega los productos de un pedido ya pagado.</summary>
    public async Task<PedidoRespuesta> EntregarAsync(int id, CancellationToken ct = default)
    {
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            var pedido = await pedidos.BloquearAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrado);
            if (pedido.Estado != EstadosPedido.Pagado)
            {
                throw new ConflictoException("Solo se entregan pedidos pagados");
            }
            await pedidos.CambiarEstadoAsync(id, EstadosPedido.Pagado, EstadosPedido.Entregado, ct);
            await auditoria.RegistrarAsync(AccionesAuditoria.Entregar, RecursosAuditoria.Pedido, id, new { pedido.Codigo }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await ObtenerSinFiltroAsync(id, ct);
    }

    /// <summary>
    /// Descuenta el stock de cada línea (ordenadas por producto), crea el
    /// pedido con los precios leídos de la base y lo audita. Se llama DENTRO
    /// de una transacción: si una línea falla, se revierte todo.
    /// </summary>
    private async Task<PedidoCreado> RegistrarVentaAsync(
        int clienteId, int? registradoPor, string canal, IReadOnlyList<LineaVenta> lineas, CancellationToken ct)
    {
        var vendidos = new List<(ProductoVendido Producto, int Cantidad)>();
        foreach (var linea in lineas.OrderBy(l => l.ProductoId))
        {
            var vendido = await pedidos.DescontarStockAsync(linea.ProductoId, linea.Cantidad, ct);
            if (vendido is null)
            {
                throw await SinStockAsync(linea, ct);
            }
            vendidos.Add((vendido, linea.Cantidad));
        }

        // decimal: aritmética exacta en base 10 (con double, 0.1 + 0.2 no da 0.3).
        var total = vendidos.Sum(v => v.Producto.Precio * v.Cantidad);
        var creado = await pedidos.CrearAsync(clienteId, registradoPor, canal, total, ct);
        foreach (var (producto, cantidad) in vendidos)
        {
            await pedidos.InsertarItemAsync(creado.Id, producto.Id, cantidad, producto.Precio, ct);
        }
        await auditoria.RegistrarAsync(
            AccionesAuditoria.Crear, RecursosAuditoria.Pedido, creado.Id,
            new { creado.Codigo, canal, total, productos = vendidos.Count }, ct: ct);
        return creado;
    }

    private async Task<Exception> SinStockAsync(LineaVenta linea, CancellationToken ct)
    {
        var estado = await pedidos.EstadoStockAsync(linea.ProductoId, ct);
        if (estado is null)
        {
            return new ValidacionException("productoId", $"El producto {linea.ProductoId} no existe");
        }
        if (!estado.Activo)
        {
            return new ConflictoException($"{estado.Nombre} ya no está a la venta");
        }
        return estado.Stock == 0
            ? new ConflictoException($"{estado.Nombre} está agotado")
            : new ConflictoException($"Stock insuficiente de {estado.Nombre}: quedan {estado.Stock} unidades");
    }

    private async Task<PedidoRespuesta> ObtenerSinFiltroAsync(int id, CancellationToken ct)
    {
        var pedido = await pedidos.ObtenerAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrado);
        return new PedidoRespuesta(pedido, await pedidos.ItemsAsync(id, ct), await pedidos.PagosAsync(id, ct));
    }
}
