using RiverVet.Aplicacion.Productos;

namespace RiverVet.Infraestructura.Datos;

public sealed class CategoriasRepositorio(SesionBd sesion) : RepositorioBase(sesion), ICategoriasRepositorio
{
    private const string Consulta = """
        SELECT cat.id, cat.nombre, cat.activo,
               (SELECT COUNT(*) FROM dbo.productos p
                WHERE p.categoria_id = cat.id AND p.eliminado = 0 AND p.activo = 1) AS totalProductos
        FROM dbo.categorias cat
        """;

    public Task<IReadOnlyList<CategoriaRespuesta>> ListarAsync(bool incluirInactivas, CancellationToken ct = default) =>
        ConsultarAsync<CategoriaRespuesta>(
            $"{Consulta} WHERE (@incluirInactivas = 1 OR cat.activo = 1) ORDER BY cat.nombre",
            new { incluirInactivas },
            ct);

    public Task<CategoriaRespuesta?> ObtenerAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<CategoriaRespuesta>($"{Consulta} WHERE cat.id = @id", new { id }, ct);

    public async Task<bool> ExisteActivaAsync(int id, CancellationToken ct = default) =>
        await EscalarOpcionalAsync<int?>("SELECT 1 FROM dbo.categorias WHERE id = @id AND activo = 1", new { id }, ct) is not null;

    public Task<int> CrearAsync(string nombre, CancellationToken ct = default) =>
        EscalarAsync<int>(
            "INSERT INTO dbo.categorias (nombre) OUTPUT INSERTED.id VALUES (@nombre)",
            new { nombre = Unicode(nombre, 60) },
            ct);

    public async Task<bool> ActualizarAsync(int id, string nombre, bool activo, CancellationToken ct = default) =>
        await EjecutarAsync(
            "UPDATE dbo.categorias SET nombre = @nombre, activo = @activo WHERE id = @id",
            new { id, nombre = Unicode(nombre, 60), activo },
            ct) > 0;
}

public sealed class ProductosRepositorio(SesionBd sesion) : RepositorioBase(sesion), IProductosRepositorio
{
    private const string ColumnasCatalogo = """
        p.id, p.sku, p.nombre, p.descripcion, p.precio, p.categoria_id AS categoriaId,
        cat.nombre AS categoriaNombre, p.imagen_url AS imagenUrl,
        CAST(CASE WHEN p.stock > 0 THEN 1 ELSE 0 END AS BIT) AS disponible
        """;

    private const string ColumnasInventario = """
        p.id, p.sku, p.nombre, p.descripcion, p.precio, p.categoria_id AS categoriaId,
        cat.nombre AS categoriaNombre, p.imagen_url AS imagenUrl,
        p.stock, p.stock_minimo AS stockMinimo,
        CAST(CASE WHEN p.stock <= p.stock_minimo THEN 1 ELSE 0 END AS BIT) AS stockBajo,
        p.activo, p.creado_en AS creadoEn, p.actualizado_en AS actualizadoEn
        """;

    // Catálogo público: solo lo que está a la venta. El orden elegido va
    // como parámetro dentro de CASE: nunca se concatena al SQL.
    public Task<IReadOnlyList<ProductoCatalogo>> ListarCatalogoAsync(ConsultaCatalogo consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultarAsync<ProductoCatalogo>(
            $"""
            SELECT {ColumnasCatalogo}, COUNT(*) OVER() AS total
            FROM dbo.productos p
            JOIN dbo.categorias cat ON cat.id = p.categoria_id AND cat.activo = 1
            WHERE p.eliminado = 0 AND p.activo = 1
              AND (@categoriaId IS NULL OR p.categoria_id = @categoriaId)
              AND (@precioMin IS NULL OR p.precio >= @precioMin)
              AND (@precioMax IS NULL OR p.precio <= @precioMax)
              AND (@soloDisponibles = 0 OR p.stock > 0)
              AND (@patron IS NULL
                   OR p.nombre      LIKE @patron ESCAPE '\'
                   OR p.descripcion LIKE @patron ESCAPE '\'
                   OR p.sku         LIKE @patron ESCAPE '\')
            ORDER BY CASE WHEN @orden = 'precio_asc'  THEN p.precio END ASC,
                     CASE WHEN @orden = 'precio_desc' THEN p.precio END DESC,
                     CASE WHEN @orden = 'recientes'   THEN p.creado_en END DESC,
                     p.nombre, p.id
            OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY
            """,
            new
            {
                consulta.CategoriaId,
                consulta.PrecioMin,
                consulta.PrecioMax,
                consulta.SoloDisponibles,
                patron = PatronBusqueda(consulta.Texto),
                orden = Ascii(consulta.Orden, 15),
                offset = consulta.Offset,
                limite = consulta.Limite,
            },
            ct);
    }

    public Task<ProductoCatalogo?> ObtenerCatalogoAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<ProductoCatalogo>(
            $"""
            SELECT {ColumnasCatalogo}
            FROM dbo.productos p
            JOIN dbo.categorias cat ON cat.id = p.categoria_id AND cat.activo = 1
            WHERE p.id = @id AND p.eliminado = 0 AND p.activo = 1
            """,
            new { id },
            ct);

    public Task<IReadOnlyList<ProductoInventario>> ListarInventarioAsync(ConsultaInventario consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultarAsync<ProductoInventario>(
            $"""
            SELECT {ColumnasInventario}, COUNT(*) OVER() AS total
            FROM dbo.productos p
            JOIN dbo.categorias cat ON cat.id = p.categoria_id
            WHERE p.eliminado = 0
              AND (@incluirInactivos = 1 OR p.activo = 1)
              AND (@categoriaId IS NULL OR p.categoria_id = @categoriaId)
              AND (@soloStockBajo = 0 OR p.stock <= p.stock_minimo)
              AND (@patron IS NULL
                   OR p.nombre LIKE @patron ESCAPE '\'
                   OR p.sku    LIKE @patron ESCAPE '\')
            ORDER BY p.nombre, p.id
            OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY
            """,
            new
            {
                consulta.IncluirInactivos,
                consulta.CategoriaId,
                consulta.SoloStockBajo,
                patron = PatronBusqueda(consulta.Texto),
                offset = consulta.Offset,
                limite = consulta.Limite,
            },
            ct);
    }

    public Task<ProductoInventario?> ObtenerInventarioAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<ProductoInventario>(
            $"""
            SELECT {ColumnasInventario}
            FROM dbo.productos p
            JOIN dbo.categorias cat ON cat.id = p.categoria_id
            WHERE p.id = @id AND p.eliminado = 0
            """,
            new { id },
            ct);

    public Task<int> CrearAsync(CrearProductoSolicitud datos, int? creadoPor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var parametros = Parametros(datos);
        parametros.Add("stock", datos.Stock);
        parametros.Add("creadoPor", creadoPor);
        return EscalarAsync<int>(
            """
            INSERT INTO dbo.productos (categoria_id, sku, nombre, descripcion, precio, stock, stock_minimo,
                                       imagen_url, activo, creado_por)
            OUTPUT INSERTED.id
            VALUES (@categoriaId, @sku, @nombre, @descripcion, @precio, @stock, @stockMinimo,
                    @imagenUrl, @activo, @creadoPor)
            """,
            parametros,
            ct);
    }

    // No toca el stock (ver ActualizarProductoSolicitud).
    public async Task<bool> ActualizarAsync(int id, ActualizarProductoSolicitud datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var parametros = Parametros(datos);
        parametros.Add("id", id);
        return await EjecutarAsync(
            """
            UPDATE dbo.productos
            SET categoria_id = @categoriaId, sku = @sku, nombre = @nombre, descripcion = @descripcion,
                precio = @precio, stock_minimo = @stockMinimo, imagen_url = @imagenUrl, activo = @activo,
                actualizado_en = SYSUTCDATETIME()
            WHERE id = @id AND eliminado = 0
            """,
            parametros,
            ct) > 0;
    }

    // La verificación "no queda negativo" y la suma son la MISMA sentencia.
    public Task<int?> AjustarStockAsync(int id, int cantidad, CancellationToken ct = default) =>
        EscalarOpcionalAsync<int?>(
            """
            UPDATE dbo.productos
            SET stock = stock + @cantidad, actualizado_en = SYSUTCDATETIME()
            OUTPUT INSERTED.stock
            WHERE id = @id AND eliminado = 0 AND stock + @cantidad >= 0
            """,
            new { id, cantidad },
            ct);

    public async Task<bool> EliminarAsync(int id, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.productos SET eliminado = 1, eliminado_en = SYSUTCDATETIME(), activo = 0
            WHERE id = @id AND eliminado = 0
            """,
            new { id },
            ct) > 0;

    private static Dapper.DynamicParameters Parametros(DatosProductoSolicitud datos) =>
        new(new
        {
            categoriaId = datos.CategoriaId,
            sku = Ascii(datos.Sku, 30),
            nombre = Unicode(datos.Nombre, 120),
            descripcion = Unicode(datos.Descripcion, 1000),
            precio = datos.Precio,
            stockMinimo = datos.StockMinimo,
            imagenUrl = Unicode(datos.ImagenUrl, 500),
            activo = datos.Activo ?? true,
        });
}
