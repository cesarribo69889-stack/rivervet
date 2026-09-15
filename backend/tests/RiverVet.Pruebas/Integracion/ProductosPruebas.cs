using System.Net;
using System.Text.Json;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-013 (registrar y editar productos) y HU-014 (catálogo público).</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class ProductosPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private static object Producto(int categoriaId, string sku, decimal precio = 30m, int stock = 10) => new
    {
        categoriaId,
        sku,
        nombre = "Collar reflectivo",
        descripcion = "Para paseos nocturnos",
        precio,
        stock,
        stockMinimo = 3,
    };

    [Fact]
    public async Task El_catalogo_es_publico_paginado_y_no_revela_el_stock_exacto()
    {
        var r = await _api.GetAsync("/api/productos?pagina=1&limite=5");

        r.Estado.ShouldBe(HttpStatusCode.OK);
        var items = r.Data.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBeInRange(1, 5);
        r.Data.GetProperty("paginacion").GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(10);
        foreach (var p in items)
        {
            p.TryGetProperty("stock", out _).ShouldBeFalse("el público no debe ver el stock exacto");
            p.TryGetProperty("disponible", out _).ShouldBeTrue();
            p.TryGetProperty("precio", out _).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Filtros_de_categoria_texto_precio_y_orden()
    {
        var alimentos = await Catalogo.CategoriaAsync(fabrica);
        var r = await _api.GetAsync($"/api/productos?categoriaId={alimentos}&orden=precio_asc&limite=50");
        var items = r.Data.GetProperty("items").EnumerateArray().ToList();
        items.ShouldAllBe(p => p.GetProperty("categoriaId").GetInt32() == alimentos);
        var precios = items.Select(p => p.GetProperty("precio").GetDecimal()).ToList();
        precios.ShouldBe(precios.Order().ToList());

        var texto = await _api.GetAsync("/api/productos?texto=croquetas");
        texto.Data.GetProperty("items").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("nombre").GetString()!.Contains("Croquetas", StringComparison.OrdinalIgnoreCase));

        var rango = await _api.GetAsync("/api/productos?precioMin=20&precioMax=40&limite=50");
        rango.Data.GetProperty("items").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("precio").GetDecimal() >= 20 && p.GetProperty("precio").GetDecimal() <= 40);

        (await _api.GetAsync("/api/productos?orden=cualquiera")).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _api.GetAsync("/api/productos?precioMin=50&precioMax=10")).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Buscar_un_comodin_no_trae_todo_el_catalogo()
    {
        var r = await _api.GetAsync("/api/productos?texto=%25");
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("paginacion").GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Admin_crea_un_producto_y_un_SKU_repetido_da_409()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var categoria = await Catalogo.CategoriaAsync(fabrica);
        var sku = Catalogo.SkuUnico().ToLowerInvariant();

        var r = await _api.PostAsync("/api/productos", Producto(categoria, sku), admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("sku").GetString().ShouldBe(sku.ToUpperInvariant());
        r.Data.GetProperty("stock").GetInt32().ShouldBe(10);
        r.Data.GetProperty("activo").GetBoolean().ShouldBeTrue();

        var duplicado = await _api.PostAsync("/api/productos", Producto(categoria, sku), admin.Token);
        duplicado.Estado.ShouldBe(HttpStatusCode.Conflict);
        duplicado.Mensaje.ShouldBe("Ya existe un producto con ese SKU");
    }

    [Fact]
    public async Task Validaciones_de_precio_imagen_y_categoria()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var categoria = await Catalogo.CategoriaAsync(fabrica);

        var r = await _api.PostAsync(
            "/api/productos",
            new { categoriaId = categoria, sku = Catalogo.SkuUnico(), nombre = "X", precio = 10.555m, stock = 1, stockMinimo = 0, imagenUrl = "javascript:alert(1)" },
            admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("precio");
        r.CamposConError.ShouldContain("imagenUrl");

        var sinCategoria = await _api.PostAsync("/api/productos", Producto(999999, Catalogo.SkuUnico()), admin.Token);
        sinCategoria.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        sinCategoria.CamposConError.ShouldBe(["categoriaId"]);
    }

    /// <summary>
    /// El stock NO se edita con PUT (lista blanca): solo con ajustes
    /// relativos, que no pisan una venta hecha mientras tanto.
    /// </summary>
    [Fact]
    public async Task Editar_no_acepta_stock_y_el_ajuste_relativo_no_deja_stock_negativo()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var id = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 4);
        var categoria = await Catalogo.CategoriaAsync(fabrica);

        var conStock = await _api.PutAsync(
            $"/api/productos/{id}",
            new { categoriaId = categoria, sku = Catalogo.SkuUnico(), nombre = "Editado", precio = 12m, stockMinimo = 1, activo = true, stock = 500 },
            admin.Token);
        conStock.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        conStock.CamposConError.ShouldContain("stock");

        var editado = await _api.PutAsync(
            $"/api/productos/{id}",
            new { categoriaId = categoria, sku = Catalogo.SkuUnico(), nombre = "Editado", precio = 12m, stockMinimo = 1, activo = true },
            admin.Token);
        editado.Estado.ShouldBe(HttpStatusCode.OK);
        editado.Data.GetProperty("precio").GetDecimal().ShouldBe(12m);
        editado.Data.GetProperty("stock").GetInt32().ShouldBe(4);

        var repone = await _api.PostAsync($"/api/productos/{id}/stock", new { cantidad = 6, motivo = "Llegó el proveedor" }, admin.Token);
        repone.Estado.ShouldBe(HttpStatusCode.OK);
        repone.Data.GetProperty("stockAnterior").GetInt32().ShouldBe(4);
        repone.Data.GetProperty("stockActual").GetInt32().ShouldBe(10);

        var negativo = await _api.PostAsync($"/api/productos/{id}/stock", new { cantidad = -11, motivo = "Merma" }, admin.Token);
        negativo.Estado.ShouldBe(HttpStatusCode.Conflict);
        negativo.Mensaje.ShouldBe("El stock no puede quedar negativo (hay 10 unidades)");
        (await Catalogo.StockAsync(fabrica, id)).ShouldBe(10);

        (await fabrica.EscalarAsync<int>(
            "SELECT COUNT(*) FROM auditoria WHERE accion = 'AJUSTE_STOCK' AND recurso_id = @id", ("@id", id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Un_producto_inactivo_o_eliminado_no_aparece_en_el_catalogo_publico()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var inactivo = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 5, activo: false);
        (await _api.GetAsync($"/api/productos/{inactivo}")).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.GetAsync($"/api/productos/{inactivo}/inventario", admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);

        var eliminado = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 5);
        (await _api.GetAsync($"/api/productos/{eliminado}")).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.DeleteAsync($"/api/productos/{eliminado}", admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.GetAsync($"/api/productos/{eliminado}")).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await fabrica.EscalarAsync<bool>("SELECT eliminado FROM productos WHERE id = @id", ("@id", eliminado))).ShouldBeTrue();
    }

    [Fact]
    public async Task Inventario_muestra_stock_y_filtra_stock_bajo()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var bajo = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 1, stockMinimo: 5);

        var r = await _api.GetAsync("/api/productos/inventario?soloStockBajo=true&limite=100", recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        var items = r.Data.GetProperty("items").EnumerateArray().ToList();
        items.ShouldContain(p => p.GetProperty("id").GetInt32() == bajo);
        items.ShouldAllBe(p => p.GetProperty("stockBajo").GetBoolean());
    }

    [Fact]
    public async Task RBAC_de_productos()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var categoria = await Catalogo.CategoriaAsync(fabrica);

        (await _api.PostAsync("/api/productos", Producto(categoria, Catalogo.SkuUnico()))).Estado.ShouldBe(HttpStatusCode.Unauthorized);
        (await _api.PostAsync("/api/productos", Producto(categoria, Catalogo.SkuUnico()), recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.GetAsync("/api/productos/inventario", ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.GetAsync("/api/productos/inventario")).Estado.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Categorias_publicas_y_gestion_por_admin()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var publicas = await _api.GetAsync("/api/categorias");
        publicas.Estado.ShouldBe(HttpStatusCode.OK);
        publicas.Data.EnumerateArray().Select(c => c.GetProperty("nombre").GetString()).ShouldContain("Alimentos");

        var nombre = "Categoría " + Guid.NewGuid().ToString("N")[..6];
        var creada = await _api.PostAsync("/api/categorias", new { nombre }, admin.Token);
        creada.Estado.ShouldBe(HttpStatusCode.Created);
        (await _api.PostAsync("/api/categorias", new { nombre }, admin.Token)).Estado.ShouldBe(HttpStatusCode.Conflict);

        var id = creada.Data.GetProperty("id").GetInt32();
        (await _api.PutAsync($"/api/categorias/{id}", new { nombre, activo = false }, admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.GetAsync("/api/categorias")).Data.EnumerateArray().ShouldNotContain(c => c.GetProperty("id").GetInt32() == id);
        (await _api.GetAsync("/api/categorias/todas", admin.Token)).Data.EnumerateArray().ShouldContain(c => c.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task El_texto_libre_del_producto_se_guarda_sin_HTML()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var r = await _api.PostAsync(
            "/api/productos",
            new
            {
                categoriaId = await Catalogo.CategoriaAsync(fabrica),
                sku = Catalogo.SkuUnico(),
                nombre = "Hueso <script>alert(1)</script>masticable",
                descripcion = "<img src=x onerror=alert(1)>Resistente",
                precio = 9.90m,
                stock = 3,
                stockMinimo = 1,
            },
            admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("nombre").GetString().ShouldBe("Hueso alert(1)masticable");
        r.Data.GetProperty("descripcion").GetString().ShouldBe("Resistente");
        r.Data.GetProperty("descripcion").ValueKind.ShouldBe(JsonValueKind.String);
    }
}
