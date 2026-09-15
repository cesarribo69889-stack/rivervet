using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Productos;

/// <summary>HU-013 (registrar y editar productos) y HU-014 (catálogo público).</summary>
public sealed class ProductosServicio(
    IProductosRepositorio productos,
    ICategoriasRepositorio categorias,
    IUnidadTrabajo unidadTrabajo,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    private const string NoEncontrado = "Producto no encontrado";

    private static readonly Dictionary<string, string> _mensajesUnicos = new()
    {
        ["UX_productos_sku"] = "Ya existe un producto con ese SKU",
        ["UX_categorias_nombre"] = "Ya existe una categoría con ese nombre",
    };

    // ── Catálogo público (HU-014) ─────────────────────────────────────────

    public Task<IReadOnlyList<CategoriaRespuesta>> ListarCategoriasAsync(bool incluirInactivas, CancellationToken ct = default) =>
        categorias.ListarAsync(incluirInactivas, ct);

    public async Task<ResultadoPaginado<ProductoCatalogo>> CatalogoAsync(ConsultaCatalogo consulta, CancellationToken ct = default) =>
        ResultadoPaginado.Crear(await productos.ListarCatalogoAsync(consulta, ct), consulta);

    public async Task<ProductoCatalogo> ObtenerDelCatalogoAsync(int id, CancellationToken ct = default) =>
        await productos.ObtenerCatalogoAsync(id, ct) ?? throw new NoEncontradoException(NoEncontrado);

    // ── Inventario (admin y recepción) ────────────────────────────────────

    public async Task<ResultadoPaginado<ProductoInventario>> InventarioAsync(ConsultaInventario consulta, CancellationToken ct = default) =>
        ResultadoPaginado.Crear(await productos.ListarInventarioAsync(consulta, ct), consulta);

    public async Task<ProductoInventario> ObtenerDelInventarioAsync(int id, CancellationToken ct = default) =>
        await productos.ObtenerInventarioAsync(id, ct) ?? throw new NoEncontradoException(NoEncontrado);

    // ── Categorías (admin) ────────────────────────────────────────────────

    public async Task<CategoriaRespuesta> CrearCategoriaAsync(DatosCategoriaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        int id;
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            id = await categorias.CrearAsync(solicitud.Nombre!, ct);
            await auditoria.RegistrarAsync(AccionesAuditoria.Crear, RecursosAuditoria.Categoria, id, new { nombre = solicitud.Nombre }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await categorias.ObtenerAsync(id, ct) ?? throw new NoEncontradoException("Categoría no encontrada");
    }

    public async Task<CategoriaRespuesta> ActualizarCategoriaAsync(int id, DatosCategoriaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            if (!await categorias.ActualizarAsync(id, solicitud.Nombre!, solicitud.Activo ?? true, ct))
            {
                throw new NoEncontradoException("Categoría no encontrada");
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar, RecursosAuditoria.Categoria, id, new { solicitud.Nombre, solicitud.Activo }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await categorias.ObtenerAsync(id, ct) ?? throw new NoEncontradoException("Categoría no encontrada");
    }

    // ── Productos (admin) — HU-013 ────────────────────────────────────────

    public async Task<ProductoInventario> CrearAsync(CrearProductoSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await ValidarCategoriaAsync(solicitud.CategoriaId!.Value, ct);
        int id;
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            id = await productos.CrearAsync(solicitud, contexto.UsuarioId, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Crear, RecursosAuditoria.Producto, id,
                new { solicitud.Sku, solicitud.Precio, stockInicial = solicitud.Stock }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await ObtenerDelInventarioAsync(id, ct);
    }

    public async Task<ProductoInventario> ActualizarAsync(int id, ActualizarProductoSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await ValidarCategoriaAsync(solicitud.CategoriaId!.Value, ct);
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            var antes = await productos.ObtenerInventarioAsync(id, ct) ?? throw new NoEncontradoException(NoEncontrado);
            await productos.ActualizarAsync(id, solicitud, ct);
            // El cambio de precio queda auditado con el valor anterior: es un
            // dato de negocio que alguien podría querer revisar después.
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar, RecursosAuditoria.Producto, id,
                new { precioAnterior = antes.Precio, precioNuevo = solicitud.Precio, activo = solicitud.Activo }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await ObtenerDelInventarioAsync(id, ct);
    }

    /// <summary>
    /// Ajuste relativo (+ reponer / - merma). La suma se hace EN la base
    /// ("stock = stock + @cantidad ... WHERE stock + @cantidad >= 0"): si justo
    /// en ese momento se está vendiendo el producto, ninguna de las dos
    /// operaciones pisa a la otra.
    /// </summary>
    public async Task<AjusteStockRespuesta> AjustarStockAsync(int id, AjusteStockSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        var cantidad = solicitud.Cantidad!.Value;
        var nuevo = await productos.AjustarStockAsync(id, cantidad, ct);
        if (nuevo is null)
        {
            var actual = await productos.ObtenerInventarioAsync(id, ct) ?? throw new NoEncontradoException(NoEncontrado);
            throw new ConflictoException($"El stock no puede quedar negativo (hay {actual.Stock} unidades)");
        }
        await auditoria.RegistrarAsync(
            AccionesAuditoria.AjusteStock, RecursosAuditoria.Producto, id,
            new { cantidad, motivo = solicitud.Motivo, stockResultante = nuevo }, ct: ct);
        await tx.ConfirmarAsync(ct);
        return new AjusteStockRespuesta(id, nuevo.Value - cantidad, nuevo.Value);
    }

    /// <summary>Baja lógica: los pedidos antiguos conservan su referencia al producto.</summary>
    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        if (!await productos.EliminarAsync(id, ct))
        {
            throw new NoEncontradoException(NoEncontrado);
        }
        await auditoria.RegistrarAsync(AccionesAuditoria.Eliminar, RecursosAuditoria.Producto, id, ct: ct);
        await tx.ConfirmarAsync(ct);
    }

    private async Task ValidarCategoriaAsync(int categoriaId, CancellationToken ct)
    {
        if (!await categorias.ExisteActivaAsync(categoriaId, ct))
        {
            throw new ValidacionException("categoriaId", "La categoría no existe o está inactiva");
        }
    }
}
