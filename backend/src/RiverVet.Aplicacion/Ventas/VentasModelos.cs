using System.Text.Json.Serialization;
using FluentValidation;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Ventas;

// ═══════════════════════════ Carrito (HU-015) ═══════════════════════════

/// <summary>
/// Un producto del carrito. El precio es el ACTUAL de la tabla productos: el
/// carrito no guarda precios (ver migración 0003).
/// </summary>
public sealed class ItemCarrito
{
    public int ProductoId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Nombre { get; init; } = string.Empty;

    public string? ImagenUrl { get; init; }

    public decimal PrecioUnitario { get; init; }

    public int Cantidad { get; init; }

    public decimal Subtotal { get; init; }

    [JsonIgnore]
    public bool Activo { get; init; }

    [JsonIgnore]
    public int Stock { get; init; }

    /// <summary>Aviso para el cliente si hoy no se podría comprar esta línea.</summary>
    public string? Advertencia => !Activo ? "Este producto ya no está disponible"
        : Stock == 0 ? "Agotado"
        : Stock < Cantidad ? $"Solo quedan {Stock} unidades"
        : null;
}

public sealed record CarritoRespuesta(IReadOnlyList<ItemCarrito> Items, int CantidadProductos, decimal Total, bool ListoParaConfirmar);

public sealed class AgregarItemSolicitud
{
    public int? ProductoId { get; set; }

    public int? Cantidad { get; set; }
}

public sealed class CambiarCantidadSolicitud
{
    public int? Cantidad { get; set; }
}

public sealed class AgregarItemValidador : AbstractValidator<AgregarItemSolicitud>
{
    public AgregarItemValidador()
    {
        RuleFor(x => x.ProductoId).IdValido();
        RuleFor(x => x.Cantidad).NotNull().WithMessage("Indica la cantidad")
            .InclusiveBetween(1, LimitesVenta.CantidadMaxima).WithMessage($"Entre 1 y {LimitesVenta.CantidadMaxima}");
    }
}

public sealed class CambiarCantidadValidador : AbstractValidator<CambiarCantidadSolicitud>
{
    public CambiarCantidadValidador() =>
        RuleFor(x => x.Cantidad).NotNull().WithMessage("Indica la cantidad")
            .InclusiveBetween(1, LimitesVenta.CantidadMaxima).WithMessage($"Entre 1 y {LimitesVenta.CantidadMaxima}");
}

// ═══════════════════════════ Pedidos (HU-016) ═══════════════════════════

public sealed class PedidoResumen : IConTotal
{
    public int Id { get; init; }

    /// <summary>Código público (RV-000123): es el que ve el cliente, no el id interno.</summary>
    public string Codigo { get; init; } = string.Empty;

    public int ClienteId { get; init; }

    public string ClienteNombre { get; init; } = string.Empty;

    public string Canal { get; init; } = string.Empty;

    public string Estado { get; init; } = string.Empty;

    /// <summary>Monto total en soles.</summary>
    public decimal Total { get; init; }

    public int CantidadItems { get; init; }

    public string? RegistradoPorNombre { get; init; }

    public DateTime CreadoEn { get; init; }

    public DateTime ActualizadoEn { get; init; }

    /// <summary>Filas del listado (COUNT(*) OVER()); "Total" ya es el monto del pedido.</summary>
    [JsonIgnore]
    public int TotalFilas { get; init; }

    int IConTotal.Total => TotalFilas;
}

public sealed class ItemPedido
{
    public int ProductoId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Nombre { get; init; } = string.Empty;

    public int Cantidad { get; init; }

    /// <summary>Precio AL MOMENTO de la venta (copiado de productos, nunca del body).</summary>
    public decimal PrecioUnitario { get; init; }

    public decimal Subtotal { get; init; }
}

/// <summary>Pago de un pedido (Sprint 4). Nunca trae datos de tarjeta: no existen en el sistema.</summary>
public sealed class PagoPedido
{
    public int Id { get; init; }

    public string Metodo { get; init; } = string.Empty;

    public string Estado { get; init; } = string.Empty;

    public decimal Monto { get; init; }

    public string? CodigoOperacion { get; init; }

    public string? Proveedor { get; init; }

    public string? VerificadoPorNombre { get; init; }

    public DateTime? VerificadoEn { get; init; }

    public DateTime CreadoEn { get; init; }
}

public sealed record PedidoRespuesta(PedidoResumen Pedido, IReadOnlyList<ItemPedido> Items, IReadOnlyList<PagoPedido> Pagos)
{
    // Se aplana para el JSON: { id, codigo, ..., items, pagos }.
    public int Id => Pedido.Id;

    public string Codigo => Pedido.Codigo;

    public int ClienteId => Pedido.ClienteId;

    public string ClienteNombre => Pedido.ClienteNombre;

    public string Canal => Pedido.Canal;

    public string Estado => Pedido.Estado;

    public decimal Total => Pedido.Total;

    public string? RegistradoPorNombre => Pedido.RegistradoPorNombre;

    public DateTime CreadoEn => Pedido.CreadoEn;

    public DateTime ActualizadoEn => Pedido.ActualizadoEn;

    [JsonIgnore]
    public PedidoResumen Pedido { get; } = Pedido;
}

public sealed class ConsultaPedidos : PaginacionConsulta, INormalizable
{
    public string? Estado { get; set; }

    public string? Canal { get; set; }

    /// <summary>Días de Lima 'AAAA-MM-DD' (ambos incluidos).</summary>
    public string? Desde { get; set; }

    public string? Hasta { get; set; }

    /// <summary>Solo para el personal: pedidos de un cliente.</summary>
    public int? ClienteId { get; set; }

    public void Normalizar()
    {
        Estado = Texto.Recortar(Estado);
        Canal = Texto.Recortar(Canal);
        Desde = Texto.Recortar(Desde);
        Hasta = Texto.Recortar(Hasta);
    }
}

/// <summary>Venta presencial registrada por recepción: el cliente y lo que se lleva.</summary>
public sealed class VentaPresencialSolicitud
{
    public int? ClienteId { get; set; }

    public IReadOnlyList<ItemVentaSolicitud>? Items { get; set; }
}

/// <summary>
/// Solo producto y cantidad. NO hay campo "precio": si alguien lo agrega al
/// JSON, la lista blanca responde 422. El precio se lee de la base.
/// </summary>
public sealed class ItemVentaSolicitud
{
    public int? ProductoId { get; set; }

    public int? Cantidad { get; set; }
}

public sealed class CancelarPedidoSolicitud : INormalizable
{
    public string? Motivo { get; set; }

    public void Normalizar() => Motivo = Texto.Libre(Motivo);
}

public sealed class VentaPresencialValidador : AbstractValidator<VentaPresencialSolicitud>
{
    public VentaPresencialValidador()
    {
        RuleFor(x => x.ClienteId).IdValido();
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Agrega al menos un producto")
            .Must(i => i is null || i.Count <= LimitesVenta.ItemsMaximos).WithMessage($"Máximo {LimitesVenta.ItemsMaximos} productos distintos")
            .Must(i => i is null || i.Select(x => x.ProductoId).Distinct().Count() == i.Count)
            .WithMessage("Hay productos repetidos: suma las cantidades en una sola línea");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductoId).IdValido();
            item.RuleFor(x => x.Cantidad).NotNull().WithMessage("Indica la cantidad")
                .InclusiveBetween(1, LimitesVenta.CantidadMaxima).WithMessage($"Entre 1 y {LimitesVenta.CantidadMaxima}");
        });
    }
}

public sealed class CancelarPedidoValidador : AbstractValidator<CancelarPedidoSolicitud>
{
    public CancelarPedidoValidador() =>
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Indica el motivo de la cancelación").MaximumLength(250).WithMessage("Máximo 250 caracteres");
}

public sealed class ConsultaPedidosValidador : AbstractValidator<ConsultaPedidos>
{
    public ConsultaPedidosValidador()
    {
        Include(new PaginacionValidador());
        RuleFor(x => x.Estado).Must(e => e is null || EstadosPedido.Todos.Contains(e)).WithMessage("Estado inválido");
        RuleFor(x => x.Canal).Must(c => c is null or CanalesPedido.Web or CanalesPedido.Presencial).WithMessage("Canal inválido: web o presencial");
        RuleFor(x => x.Desde).Must(d => HoraLima.LeerFecha(d) is not null).When(x => x.Desde is not null).WithMessage("Fecha inválida (AAAA-MM-DD)");
        RuleFor(x => x.Hasta).Must(d => HoraLima.LeerFecha(d) is not null).When(x => x.Hasta is not null).WithMessage("Fecha inválida (AAAA-MM-DD)");
        RuleFor(x => x.ClienteId).GreaterThan(0).When(x => x.ClienteId is not null).WithMessage("Id inválido");
    }
}

/// <summary>Producto vendido: datos leídos de la base en el MISMO UPDATE que descuenta el stock.</summary>
public sealed record ProductoVendido(int Id, string Sku, string Nombre, decimal Precio);

/// <summary>Estado actual de un producto, para explicar por qué no se pudo vender.</summary>
public sealed record EstadoStock(string Nombre, int Stock, bool Activo);

public sealed record PedidoCreado(int Id, string Codigo);

/// <summary>Pedido leído CON BLOQUEO (UPDLOCK) para cambiarle el estado sin carreras.</summary>
public sealed record PedidoBloqueado(int Id, string Codigo, string Estado, decimal Total, int ClienteId, string ClienteTelefono);

public sealed record LineaVenta(int ProductoId, int Cantidad);

public interface ICarritoRepositorio
{
    Task<int?> ObtenerIdAsync(int usuarioId, CancellationToken ct = default);

    Task<int> ObtenerOCrearAsync(int usuarioId, CancellationToken ct = default);

    /// <summary>Lee el carrito del usuario con UPDLOCK: dos "confirmar" simultáneos del mismo usuario se hacen en fila.</summary>
    Task<int?> BloquearAsync(int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<ItemCarrito>> ListarItemsAsync(int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<LineaVenta>> LineasAsync(int carritoId, CancellationToken ct = default);

    Task<int?> CantidadAsync(int carritoId, int productoId, CancellationToken ct = default);

    Task GuardarItemAsync(int carritoId, int productoId, int cantidad, CancellationToken ct = default);

    Task<bool> QuitarItemAsync(int carritoId, int productoId, CancellationToken ct = default);

    Task VaciarAsync(int carritoId, CancellationToken ct = default);
}

public interface IPedidosRepositorio
{
    Task<int?> ClienteIdDeUsuarioAsync(int usuarioId, CancellationToken ct = default);

    Task<bool> ClienteExisteAsync(int clienteId, CancellationToken ct = default);

    /// <summary>
    /// ★ HU-016 ★ Descuenta el stock SOLO si alcanza, y devuelve el precio
    /// actual, en UNA sentencia. null = no alcanzó (o el producto no está a la venta).
    /// </summary>
    Task<ProductoVendido?> DescontarStockAsync(int productoId, int cantidad, CancellationToken ct = default);

    Task<EstadoStock?> EstadoStockAsync(int productoId, CancellationToken ct = default);

    Task<PedidoCreado> CrearAsync(int clienteId, int? registradoPor, string canal, decimal total, CancellationToken ct = default);

    Task InsertarItemAsync(int pedidoId, int productoId, int cantidad, decimal precioUnitario, CancellationToken ct = default);

    Task<IReadOnlyList<PedidoResumen>> ListarAsync(
        ConsultaPedidos consulta, DateTime? desdeUtc, DateTime? hastaUtc, int? propietarioId, CancellationToken ct = default);

    Task<PedidoResumen?> ObtenerAsync(int id, int? propietarioId, CancellationToken ct = default);

    Task<IReadOnlyList<ItemPedido>> ItemsAsync(int pedidoId, CancellationToken ct = default);

    Task<IReadOnlyList<PagoPedido>> PagosAsync(int pedidoId, CancellationToken ct = default);

    Task<PedidoBloqueado?> BloquearAsync(int id, int? propietarioId, CancellationToken ct = default);

    /// <summary>Cambia el estado solo si sigue siendo el esperado (evita pisar un cambio simultáneo).</summary>
    Task<bool> CambiarEstadoAsync(int id, string estadoEsperado, string estadoNuevo, CancellationToken ct = default);

    /// <summary>Devuelve al inventario las unidades de un pedido cancelado.</summary>
    Task DevolverStockAsync(int pedidoId, CancellationToken ct = default);

    /// <summary>Marca como rechazados los pagos pendientes o en verificación del pedido.</summary>
    Task AnularPagosPendientesAsync(int pedidoId, CancellationToken ct = default);
}
