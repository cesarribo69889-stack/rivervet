using System.Net;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-018: dashboard de indicadores (solo admin).</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class IndicadoresPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    [Fact]
    public async Task El_admin_ve_los_indicadores_del_periodo()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var bajo = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 1, stockMinimo: 0);
        // Dejamos este producto en stock 0 y mínimo 0: aparece como stock bajo.
        var cliente = await fabrica.EscalarAsync<int>("SELECT id FROM clientes WHERE email = @e", ("@e", Usuarios.Ana));
        (await _api.PostAsync("/api/pedidos/presencial", new { clienteId = cliente, items = new[] { new { productoId = bajo, cantidad = 1 } } }, recepcion.Token))
            .Estado.ShouldBe(HttpStatusCode.Created);

        var r = await _api.GetAsync("/api/indicadores", admin.Token);

        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("desde").GetString().ShouldEndWith("-01");
        r.Data.GetProperty("ventas").GetProperty("pendientesDeCobro").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        r.Data.GetProperty("clientesNuevos").GetInt32().ShouldBeGreaterThanOrEqualTo(5);
        // Otras pruebas también dejan productos en 0 y la lista tiene tope de
        // 20: se verifica que no esté vacía y que todo lo listado esté bajo.
        var stockBajo = r.Data.GetProperty("productosStockBajo").EnumerateArray().ToList();
        stockBajo.ShouldNotBeEmpty();
        stockBajo.ShouldAllBe(p => p.GetProperty("stock").GetInt32() <= p.GetProperty("stockMinimo").GetInt32());
        r.Data.TryGetProperty("citas", out var citas).ShouldBeTrue();
        citas.TryGetProperty("atendidas", out _).ShouldBeTrue();
        r.Data.GetProperty("ventasPorDia").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
    }

    [Fact]
    public async Task Solo_el_admin_y_con_un_periodo_valido()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        (await _api.GetAsync("/api/indicadores", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.GetAsync("/api/indicadores?desde=2026-02-01&hasta=2026-01-01", admin.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _api.GetAsync("/api/indicadores?desde=2024-01-01&hasta=2026-01-01", admin.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _api.GetAsync("/api/indicadores?desde=2026-01-01&hasta=2026-01-31", admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);
    }
}
