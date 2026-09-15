using System.Text.Json.Serialization;
using FluentValidation;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Productos;

public sealed class CategoriaRespuesta
{
    public int Id { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public bool Activo { get; init; }

    public int TotalProductos { get; init; }
}

/// <summary>
/// Producto tal como lo ve el PÚBLICO en el catálogo (HU-014). No trae el
/// stock exacto ni datos internos: solo si hay unidades disponibles.
/// </summary>
public sealed class ProductoCatalogo : IConTotal
{
    public int Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Nombre { get; init; } = string.Empty;

    public string? Descripcion { get; init; }

    public decimal Precio { get; init; }

    public int CategoriaId { get; init; }

    public string CategoriaNombre { get; init; } = string.Empty;

    public string? ImagenUrl { get; init; }

    public bool Disponible { get; init; }

    [JsonIgnore]
    public int Total { get; init; }
}

/// <summary>Producto con sus datos de inventario (admin y recepción).</summary>
public sealed class ProductoInventario : IConTotal
{
    public int Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Nombre { get; init; } = string.Empty;

    public string? Descripcion { get; init; }

    public decimal Precio { get; init; }

    public int CategoriaId { get; init; }

    public string CategoriaNombre { get; init; } = string.Empty;

    public string? ImagenUrl { get; init; }

    public int Stock { get; init; }

    public int StockMinimo { get; init; }

    /// <summary>stock &lt;= stockMinimo: hay que reponer.</summary>
    public bool StockBajo { get; init; }

    public bool Activo { get; init; }

    public DateTime CreadoEn { get; init; }

    public DateTime ActualizadoEn { get; init; }

    [JsonIgnore]
    public int Total { get; init; }
}

/// <summary>
/// HU-014: filtros del catálogo público. Van en la URL (GET) porque buscar
/// "croquetas" no es un dato personal y así un enlace al catálogo filtrado se
/// puede compartir. Las búsquedas de PERSONAS (DNI, nombres) siguen yendo en
/// el body de un POST.
/// </summary>
public sealed class ConsultaCatalogo : PaginacionConsulta, INormalizable
{
    public static readonly IReadOnlyList<string> Ordenes = ["nombre", "precio_asc", "precio_desc", "recientes"];

    public string? Texto { get; set; }

    public int? CategoriaId { get; set; }

    public decimal? PrecioMin { get; set; }

    public decimal? PrecioMax { get; set; }

    public bool SoloDisponibles { get; set; }

    public string? Orden { get; set; }

    // "Comun.Texto": aquí "Texto" a secas es la propiedad, no la clase.
    public void Normalizar()
    {
        Texto = Comun.Texto.Recortar(Texto);
        Orden = Comun.Texto.Recortar(Orden) ?? "nombre";
    }
}

public sealed class ConsultaInventario : PaginacionConsulta, INormalizable
{
    public string? Texto { get; set; }

    public int? CategoriaId { get; set; }

    public bool SoloStockBajo { get; set; }

    public bool IncluirInactivos { get; set; }

    public void Normalizar() => Texto = Comun.Texto.Recortar(Texto);
}

public sealed class DatosCategoriaSolicitud : INormalizable
{
    public string? Nombre { get; set; }

    /// <summary>Solo al editar. Una categoría inactiva desaparece del catálogo junto con sus productos.</summary>
    public bool? Activo { get; set; }

    public void Normalizar() => Nombre = Texto.Libre(Nombre);
}

/// <summary>Campos comunes de alta y edición de un producto (HU-013).</summary>
public abstract class DatosProductoSolicitud : INormalizable
{
    public int? CategoriaId { get; set; }

    public string? Sku { get; set; }

    public string? Nombre { get; set; }

    public string? Descripcion { get; set; }

    public decimal? Precio { get; set; }

    public int? StockMinimo { get; set; }

    public string? ImagenUrl { get; set; }

    public bool? Activo { get; set; }

    public void Normalizar()
    {
        Sku = Texto.Recortar(Sku)?.ToUpperInvariant();
        Nombre = Texto.Libre(Nombre);
        Descripcion = Texto.Libre(Descripcion);
        ImagenUrl = Texto.Recortar(ImagenUrl);
    }
}

/// <summary>Alta: incluye el stock INICIAL.</summary>
public sealed class CrearProductoSolicitud : DatosProductoSolicitud
{
    public int? Stock { get; set; }
}

/// <summary>
/// Edición: NO acepta "stock" (la lista blanca lo rechaza con 422). Si el
/// admin escribiera el stock absoluto ("ahora hay 15"), podría pisar una venta
/// hecha mientras editaba: leyó 10, alguien compró 1, y su "15" borraría esa
/// venta del inventario. El stock solo cambia con ajustes relativos (+5, -2)
/// en POST /productos/{id}/stock, que son atómicos en la base.
/// </summary>
public sealed class ActualizarProductoSolicitud : DatosProductoSolicitud;

/// <summary>Ajuste relativo de stock: +N al reponer, -N por merma o pérdida.</summary>
public sealed class AjusteStockSolicitud : INormalizable
{
    public int? Cantidad { get; set; }

    public string? Motivo { get; set; }

    public void Normalizar() => Motivo = Texto.Libre(Motivo);
}

public sealed record AjusteStockRespuesta(int ProductoId, int StockAnterior, int StockActual);

public sealed class DatosCategoriaValidador : AbstractValidator<DatosCategoriaSolicitud>
{
    public DatosCategoriaValidador() =>
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("El nombre es obligatorio").MaximumLength(60).WithMessage("Máximo 60 caracteres");
}

public abstract class DatosProductoValidador<T> : AbstractValidator<T>
    where T : DatosProductoSolicitud
{
    protected DatosProductoValidador()
    {
        RuleFor(x => x.CategoriaId).IdValido();
        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("El SKU es obligatorio")
            .Matches("^[A-Z0-9][A-Z0-9-]{2,29}$").WithMessage("De 3 a 30 caracteres: letras, números y guiones");
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("El nombre es obligatorio").MaximumLength(120).WithMessage("Máximo 120 caracteres");
        RuleFor(x => x.Descripcion).MaximumLength(1000).WithMessage("Máximo 1000 caracteres");
        RuleFor(x => x.Precio)
            .NotNull().WithMessage("El precio es obligatorio")
            .GreaterThan(0).WithMessage("El precio debe ser mayor a 0")
            .LessThanOrEqualTo(99999.99m).WithMessage("Máximo S/ 99 999.99")
            .PrecisionScale(7, 2, true).WithMessage("Máximo dos decimales");
        RuleFor(x => x.StockMinimo)
            .NotNull().WithMessage("Indica el stock mínimo")
            .InclusiveBetween(0, 10000).WithMessage("Debe estar entre 0 y 10 000");
        // Solo https: una URL "javascript:..." o "http://" en un <img> sería
        // un vector de XSS o de contenido mixto.
        RuleFor(x => x.ImagenUrl)
            .MaximumLength(500).WithMessage("Máximo 500 caracteres")
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Debe ser una URL https://")
            .When(x => x.ImagenUrl is not null);
    }
}

public sealed class CrearProductoValidador : DatosProductoValidador<CrearProductoSolicitud>
{
    public CrearProductoValidador() =>
        RuleFor(x => x.Stock).NotNull().WithMessage("Indica el stock inicial")
            .InclusiveBetween(0, 100000).WithMessage("Debe estar entre 0 y 100 000");
}

public sealed class ActualizarProductoValidador : DatosProductoValidador<ActualizarProductoSolicitud>
{
    public ActualizarProductoValidador() => RuleFor(x => x.Activo).NotNull().WithMessage("Indica si el producto está activo");
}

public sealed class AjusteStockValidador : AbstractValidator<AjusteStockSolicitud>
{
    public AjusteStockValidador()
    {
        RuleFor(x => x.Cantidad)
            .NotNull().WithMessage("Indica la cantidad (+ para reponer, - para descontar)")
            .NotEqual(0).WithMessage("La cantidad no puede ser 0")
            .InclusiveBetween(-10000, 10000).WithMessage("Debe estar entre -10 000 y 10 000");
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Indica el motivo del ajuste").MaximumLength(250).WithMessage("Máximo 250 caracteres");
    }
}

public sealed class ConsultaCatalogoValidador : AbstractValidator<ConsultaCatalogo>
{
    public ConsultaCatalogoValidador()
    {
        Include(new PaginacionValidador());
        RuleFor(x => x.Texto).MaximumLength(100).WithMessage("Máximo 100 caracteres");
        RuleFor(x => x.CategoriaId).GreaterThan(0).When(x => x.CategoriaId is not null).WithMessage("Id inválido");
        RuleFor(x => x.PrecioMin).GreaterThanOrEqualTo(0).When(x => x.PrecioMin is not null).WithMessage("No puede ser negativo");
        RuleFor(x => x.PrecioMax)
            .GreaterThanOrEqualTo(x => x.PrecioMin ?? 0).When(x => x.PrecioMax is not null)
            .WithMessage("Debe ser mayor o igual al precio mínimo");
        RuleFor(x => x.Orden).Must(o => o is null || ConsultaCatalogo.Ordenes.Contains(o))
            .WithMessage("Orden inválido: " + string.Join(", ", ConsultaCatalogo.Ordenes));
    }
}

public sealed class ConsultaInventarioValidador : AbstractValidator<ConsultaInventario>
{
    public ConsultaInventarioValidador()
    {
        Include(new PaginacionValidador());
        RuleFor(x => x.Texto).MaximumLength(100).WithMessage("Máximo 100 caracteres");
        RuleFor(x => x.CategoriaId).GreaterThan(0).When(x => x.CategoriaId is not null).WithMessage("Id inválido");
    }
}

public interface ICategoriasRepositorio
{
    Task<IReadOnlyList<CategoriaRespuesta>> ListarAsync(bool incluirInactivas, CancellationToken ct = default);

    Task<CategoriaRespuesta?> ObtenerAsync(int id, CancellationToken ct = default);

    Task<bool> ExisteActivaAsync(int id, CancellationToken ct = default);

    Task<int> CrearAsync(string nombre, CancellationToken ct = default);

    Task<bool> ActualizarAsync(int id, string nombre, bool activo, CancellationToken ct = default);
}

public interface IProductosRepositorio
{
    /// <summary>Solo productos activos, no eliminados y de categorías activas.</summary>
    Task<IReadOnlyList<ProductoCatalogo>> ListarCatalogoAsync(ConsultaCatalogo consulta, CancellationToken ct = default);

    Task<ProductoCatalogo?> ObtenerCatalogoAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<ProductoInventario>> ListarInventarioAsync(ConsultaInventario consulta, CancellationToken ct = default);

    Task<ProductoInventario?> ObtenerInventarioAsync(int id, CancellationToken ct = default);

    Task<int> CrearAsync(CrearProductoSolicitud datos, int? creadoPor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(int id, ActualizarProductoSolicitud datos, CancellationToken ct = default);

    /// <summary>
    /// stock = stock + cantidad, en UNA sentencia y solo si no queda negativo.
    /// Devuelve el stock nuevo, o null si el producto no existe o quedaría negativo.
    /// </summary>
    Task<int?> AjustarStockAsync(int id, int cantidad, CancellationToken ct = default);

    Task<bool> EliminarAsync(int id, CancellationToken ct = default);
}
