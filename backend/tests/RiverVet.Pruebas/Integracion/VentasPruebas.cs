using System.Net;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>
/// HU-015 (carrito) y HU-016 (pedidos), incluida la prueba de CONCURRENCIA
/// del stock: compras simultáneas de la última unidad.
/// </summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class VentasPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private Task<int> ClienteIdAsync(string email) =>
        fabrica.EscalarAsync<int>("SELECT id FROM clientes WHERE email = @e", ("@e", email));

    private async Task<Respuesta> VentaPresencialAsync(string token, int clienteId, params (int ProductoId, int Cantidad)[] items) =>
        await _api.PostAsync(
            "/api/pedidos/presencial",
            new { clienteId, items = items.Select(i => new { productoId = i.ProductoId, cantidad = i.Cantidad }) },
            token);

    private async Task VaciarCarritoAsync(string token) =>
        (await _api.DeleteAsync("/api/carrito", token)).Estado.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Carrito_agregar_sumar_cambiar_y_quitar_con_precios_de_la_base()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        await VaciarCarritoAsync(ana.Token);
        var a = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 10, precio: 10.10m);
        var b = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 10, precio: 5.25m);

        (await _api.PostAsync("/api/carrito/items", new { productoId = a, cantidad = 1 }, ana.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.PostAsync("/api/carrito/items", new { productoId = a, cantidad = 2 }, ana.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        var r = await _api.PostAsync("/api/carrito/items", new { productoId = b, cantidad = 2 }, ana.Token);

        r.Data.GetProperty("cantidadProductos").GetInt32().ShouldBe(5);
        // 3 × 10.10 + 2 × 5.25 = 40.80 (decimal: sin errores de redondeo).
        r.Data.GetProperty("total").GetDecimal().ShouldBe(40.80m);
        r.Data.GetProperty("listoParaConfirmar").GetBoolean().ShouldBeTrue();

        var cambio = await _api.PutAsync($"/api/carrito/items/{a}", new { cantidad = 1 }, ana.Token);
        cambio.Data.GetProperty("total").GetDecimal().ShouldBe(20.60m);

        var quitar = await _api.DeleteAsync($"/api/carrito/items/{b}", ana.Token);
        quitar.Data.GetProperty("items").GetArrayLength().ShouldBe(1);
        (await _api.DeleteAsync($"/api/carrito/items/{b}", ana.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        await VaciarCarritoAsync(ana.Token);
    }

    [Fact]
    public async Task Carrito_rechaza_productos_agotados_inexistentes_o_mas_unidades_que_el_stock()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        var agotado = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 0);
        var pocos = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 2);

        var r = await _api.PostAsync("/api/carrito/items", new { productoId = agotado, cantidad = 1 }, luis.Token);
        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldEndWith("está agotado");

        var muchos = await _api.PostAsync("/api/carrito/items", new { productoId = pocos, cantidad = 3 }, luis.Token);
        muchos.Estado.ShouldBe(HttpStatusCode.Conflict);
        muchos.Mensaje.ShouldStartWith("Solo quedan 2 unidades");

        var inexistente = await _api.PostAsync("/api/carrito/items", new { productoId = 999999, cantidad = 1 }, luis.Token);
        inexistente.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        inexistente.CamposConError.ShouldBe(["productoId"]);

        (await _api.PostAsync("/api/carrito/items", new { productoId = pocos, cantidad = 100 }, luis.Token))
            .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Solo_el_rol_cliente_usa_el_carrito()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        (await _api.GetAsync("/api/carrito", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.GetAsync("/api/carrito")).Estado.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirmar_el_carrito_crea_el_pedido_descuenta_stock_y_vacia_el_carrito()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        await VaciarCarritoAsync(ana.Token);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 10, precio: 19.90m);
        await _api.PostAsync("/api/carrito/items", new { productoId = producto, cantidad = 3 }, ana.Token);

        var r = await _api.PostAsync("/api/pedidos", null, ana.Token);

        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("codigo").GetString()!.ShouldMatch("^RV-[0-9]{6,}$");
        r.Data.GetProperty("canal").GetString().ShouldBe("web");
        r.Data.GetProperty("estado").GetString().ShouldBe("pendiente_pago");
        r.Data.GetProperty("total").GetDecimal().ShouldBe(59.70m);
        var item = r.Data.GetProperty("items")[0];
        item.GetProperty("precioUnitario").GetDecimal().ShouldBe(19.90m);
        item.GetProperty("subtotal").GetDecimal().ShouldBe(59.70m);

        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(7);
        (await _api.GetAsync("/api/carrito", ana.Token)).Data.GetProperty("items").GetArrayLength().ShouldBe(0);

        var otraVez = await _api.PostAsync("/api/pedidos", null, ana.Token);
        otraVez.Estado.ShouldBe(HttpStatusCode.Conflict);
        otraVez.Mensaje.ShouldBe("Tu carrito está vacío");
    }

    /// <summary>
    /// ★ El precio se toma de la BASE al confirmar ★ Si el admin cambia el
    /// precio mientras el producto está en el carrito, se cobra el precio
    /// vigente; y nadie puede mandar un precio en la petición.
    /// </summary>
    [Fact]
    public async Task El_precio_sale_de_la_base_nunca_de_la_peticion()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 10, precio: 100m);

        var conPrecio = await _api.PostAsync(
            "/api/pedidos/presencial",
            new { clienteId = await ClienteIdAsync(Usuarios.Ana), items = new[] { new { productoId = producto, cantidad = 1, precioUnitario = 0.01m } } },
            recepcion.Token);
        conPrecio.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        conPrecio.CamposConError.ShouldContain(c => c.Contains("precioUnitario", StringComparison.Ordinal));
        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(10);

        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        await VaciarCarritoAsync(ana.Token);
        await _api.PostAsync("/api/carrito/items", new { productoId = producto, cantidad = 1 }, ana.Token);
        await fabrica.EscalarAsync<int>("UPDATE productos SET precio = 120 WHERE id = @id; SELECT 1", ("@id", producto));

        var pedido = await _api.PostAsync("/api/pedidos", null, ana.Token);
        pedido.Data.GetProperty("total").GetDecimal().ShouldBe(120m);
    }

    /// <summary>
    /// ★ HU-016 CRÍTICO ★ Queda UNA unidad y 10 ventas simultáneas intentan
    /// llevársela. Exactamente una gana; las otras 9 reciben 409 y el stock
    /// termina en 0, nunca negativo.
    /// </summary>
    [Fact]
    public async Task Concurrencia_diez_compras_simultaneas_de_la_ultima_unidad_solo_una_gana()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 1);
        var cliente = await ClienteIdAsync(Usuarios.Luis);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => VentaPresencialAsync(recepcion.Token, cliente, (producto, 1))));

        respuestas.Count(r => r.Estado == HttpStatusCode.Created).ShouldBe(1);
        respuestas.Count(r => r.Estado == HttpStatusCode.Conflict).ShouldBe(9);
        respuestas.Where(r => r.Estado == HttpStatusCode.Conflict).ShouldAllBe(r => r.Mensaje.EndsWith("está agotado", StringComparison.Ordinal));
        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(0);
        (await fabrica.EscalarAsync<int>("SELECT COALESCE(SUM(cantidad), 0) FROM pedido_items WHERE producto_id = @p", ("@p", producto)))
            .ShouldBe(1);
    }

    /// <summary>Con 5 unidades y 12 compras simultáneas de 1: se venden exactamente 5.</summary>
    [Fact]
    public async Task Concurrencia_con_varias_unidades_se_vende_exactamente_el_stock()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 5);
        var cliente = await ClienteIdAsync(Usuarios.Ana);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => VentaPresencialAsync(recepcion.Token, cliente, (producto, 1))));

        respuestas.Count(r => r.Estado == HttpStatusCode.Created).ShouldBe(5);
        respuestas.Count(r => r.Estado == HttpStatusCode.Conflict).ShouldBe(7);
        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(0);
    }

    /// <summary>Dos clientes web confirman a la vez sus carritos con la última unidad.</summary>
    [Fact]
    public async Task Concurrencia_dos_clientes_web_confirman_la_ultima_unidad()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        await VaciarCarritoAsync(ana.Token);
        await VaciarCarritoAsync(luis.Token);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 1);
        await _api.PostAsync("/api/carrito/items", new { productoId = producto, cantidad = 1 }, ana.Token);
        await _api.PostAsync("/api/carrito/items", new { productoId = producto, cantidad = 1 }, luis.Token);

        var respuestas = await Task.WhenAll(_api.PostAsync("/api/pedidos", null, ana.Token), _api.PostAsync("/api/pedidos", null, luis.Token));

        respuestas.Count(r => r.Estado == HttpStatusCode.Created).ShouldBe(1);
        respuestas.Count(r => r.Estado == HttpStatusCode.Conflict).ShouldBe(1);
        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(0);
        await VaciarCarritoAsync(ana.Token);
        await VaciarCarritoAsync(luis.Token);
    }

    /// <summary>Si un producto del pedido no alcanza, NO se descuenta ninguno (transacción).</summary>
    [Fact]
    public async Task Si_una_linea_no_tiene_stock_se_revierte_todo_el_pedido()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var hay = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 10);
        var falta = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 1);

        var r = await VentaPresencialAsync(recepcion.Token, await ClienteIdAsync(Usuarios.Ana), (hay, 4), (falta, 2));

        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldBe("Stock insuficiente de Producto de prueba: quedan 1 unidades");
        (await Catalogo.StockAsync(fabrica, hay)).ShouldBe(10);
        (await Catalogo.StockAsync(fabrica, falta)).ShouldBe(1);
    }

    [Fact]
    public async Task Venta_presencial_valida_items_repetidos_vacios_y_cliente()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var cliente = await ClienteIdAsync(Usuarios.Ana);

        var vacia = await VentaPresencialAsync(recepcion.Token, cliente);
        vacia.CamposConError.ShouldContain("items");

        var repetidos = await VentaPresencialAsync(recepcion.Token, cliente, (1, 1), (1, 2));
        repetidos.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        repetidos.CamposConError.ShouldContain("items");

        var cantidad = await VentaPresencialAsync(recepcion.Token, cliente, (1, 0));
        cantidad.CamposConError.ShouldContain("items[0].cantidad");

        var sinCliente = await VentaPresencialAsync(recepcion.Token, 999999, (1, 1));
        sinCliente.CamposConError.ShouldBe(["clienteId"]);

        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        (await VentaPresencialAsync(ana.Token, cliente, (1, 1))).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cancelar_devuelve_el_stock_y_el_cliente_solo_cancela_lo_suyo()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 6);
        var pedido = (await VentaPresencialAsync(recepcion.Token, await ClienteIdAsync(Usuarios.Ana), (producto, 4)))
            .Data.GetProperty("id").GetInt32();
        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(2);

        // Luis no puede ver ni cancelar el pedido de Ana: 404 (no 403).
        (await _api.GetAsync($"/api/pedidos/{pedido}", luis.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.PostAsync($"/api/pedidos/{pedido}/cancelar", new { motivo = "No lo quiero" }, luis.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);

        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var cancelado = await _api.PostAsync($"/api/pedidos/{pedido}/cancelar", new { motivo = "Me equivoqué de producto" }, ana.Token);
        cancelado.Estado.ShouldBe(HttpStatusCode.OK);
        cancelado.Data.GetProperty("estado").GetString().ShouldBe("cancelado");
        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(6);

        var dosVeces = await _api.PostAsync($"/api/pedidos/{pedido}/cancelar", new { motivo = "Otra vez" }, recepcion.Token);
        dosVeces.Estado.ShouldBe(HttpStatusCode.Conflict);
        dosVeces.Mensaje.ShouldBe("El pedido ya está cancelado");
        (await Catalogo.StockAsync(fabrica, producto)).ShouldBe(6);
    }

    [Fact]
    public async Task El_cliente_solo_lista_sus_pedidos_y_el_personal_ve_todos()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 5);
        var anaId = await ClienteIdAsync(Usuarios.Ana);
        (await VentaPresencialAsync(recepcion.Token, anaId, (producto, 1))).Estado.ShouldBe(HttpStatusCode.Created);

        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        var luisId = await ClienteIdAsync(Usuarios.Luis);
        var propios = await _api.GetAsync("/api/pedidos?limite=100", luis.Token);
        propios.Data.GetProperty("items").EnumerateArray().ShouldAllBe(p => p.GetProperty("clienteId").GetInt32() == luisId);

        var todos = await _api.GetAsync($"/api/pedidos?clienteId={anaId}&canal=presencial&limite=100", recepcion.Token);
        todos.Estado.ShouldBe(HttpStatusCode.OK);
        var items = todos.Data.GetProperty("items").EnumerateArray().ToList();
        items.ShouldNotBeEmpty();
        items.ShouldAllBe(p => p.GetProperty("clienteId").GetInt32() == anaId && p.GetProperty("canal").GetString() == "presencial");
        items[0].GetProperty("registradoPorNombre").GetString().ShouldBe("Rosa Huamán");

        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        (await _api.GetAsync("/api/pedidos", vet.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Solo_se_entregan_pedidos_pagados()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 5);
        var pedido = (await VentaPresencialAsync(recepcion.Token, await ClienteIdAsync(Usuarios.Ana), (producto, 1)))
            .Data.GetProperty("id").GetInt32();

        var r = await _api.PostAsync($"/api/pedidos/{pedido}/entregar", null, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldBe("Solo se entregan pedidos pagados");
    }
}
