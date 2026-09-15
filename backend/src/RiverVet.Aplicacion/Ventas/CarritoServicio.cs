using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Productos;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Ventas;

/// <summary>
/// HU-015: carrito del cliente. Cada usuario tiene UN carrito y solo puede
/// tocar el suyo: todas las operaciones buscan el carrito por el usuario de
/// la sesión, nunca por un id que venga en la petición (así no hay IDOR posible).
///
/// El carrito no reserva stock: avisa si algo se agotó, pero la verificación
/// que cuenta es la de confirmar el pedido.
/// </summary>
public sealed class CarritoServicio(
    ICarritoRepositorio carritos,
    IProductosRepositorio productos,
    IContextoPeticion contexto)
{
    private int UsuarioId => contexto.UsuarioId ?? throw new NoAutenticadoException();

    public async Task<CarritoRespuesta> ObtenerAsync(CancellationToken ct = default)
    {
        var items = await carritos.ListarItemsAsync(UsuarioId, ct);
        return new CarritoRespuesta(
            items,
            items.Sum(i => i.Cantidad),
            items.Sum(i => i.Subtotal),
            ListoParaConfirmar: items.Count > 0 && items.All(i => i.Advertencia is null));
    }

    /// <summary>Agrega unidades; si el producto ya estaba, suma a la cantidad que había.</summary>
    public async Task<CarritoRespuesta> AgregarAsync(AgregarItemSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var producto = await ProductoALaVentaAsync(solicitud.ProductoId!.Value, ct);
        var carritoId = await carritos.ObtenerOCrearAsync(UsuarioId, ct);
        var nueva = (await carritos.CantidadAsync(carritoId, producto.Id, ct) ?? 0) + solicitud.Cantidad!.Value;
        if (nueva > LimitesVenta.CantidadMaxima)
        {
            throw new ValidacionException("cantidad", $"Máximo {LimitesVenta.CantidadMaxima} unidades por producto");
        }
        ValidarStock(producto, nueva);
        await carritos.GuardarItemAsync(carritoId, producto.Id, nueva, ct);
        return await ObtenerAsync(ct);
    }

    public async Task<CarritoRespuesta> CambiarCantidadAsync(int productoId, CambiarCantidadSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var carritoId = await carritos.ObtenerIdAsync(UsuarioId, ct);
        if (carritoId is null || await carritos.CantidadAsync(carritoId.Value, productoId, ct) is null)
        {
            throw new NoEncontradoException("Ese producto no está en tu carrito");
        }
        var producto = await ProductoALaVentaAsync(productoId, ct);
        ValidarStock(producto, solicitud.Cantidad!.Value);
        await carritos.GuardarItemAsync(carritoId.Value, productoId, solicitud.Cantidad.Value, ct);
        return await ObtenerAsync(ct);
    }

    public async Task<CarritoRespuesta> QuitarAsync(int productoId, CancellationToken ct = default)
    {
        var carritoId = await carritos.ObtenerIdAsync(UsuarioId, ct);
        if (carritoId is null || !await carritos.QuitarItemAsync(carritoId.Value, productoId, ct))
        {
            throw new NoEncontradoException("Ese producto no está en tu carrito");
        }
        return await ObtenerAsync(ct);
    }

    public async Task<CarritoRespuesta> VaciarAsync(CancellationToken ct = default)
    {
        if (await carritos.ObtenerIdAsync(UsuarioId, ct) is int carritoId)
        {
            await carritos.VaciarAsync(carritoId, ct);
        }
        return await ObtenerAsync(ct);
    }

    private async Task<ProductoInventario> ProductoALaVentaAsync(int productoId, CancellationToken ct)
    {
        var producto = await productos.ObtenerInventarioAsync(productoId, ct);
        if (producto is null || !producto.Activo)
        {
            throw new ValidacionException("productoId", "El producto no existe o no está a la venta");
        }
        return producto;
    }

    private static void ValidarStock(ProductoInventario producto, int cantidad)
    {
        if (producto.Stock == 0)
        {
            throw new ConflictoException($"{producto.Nombre} está agotado");
        }
        if (cantidad > producto.Stock)
        {
            throw new ConflictoException($"Solo quedan {producto.Stock} unidades de {producto.Nombre}");
        }
    }
}
