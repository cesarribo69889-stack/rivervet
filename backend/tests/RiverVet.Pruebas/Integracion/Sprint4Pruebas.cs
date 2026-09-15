using System.Net;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-020 (comprobante por WhatsApp), HU-021 (triage) y exportación de datos (Ley N° 29733).</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class Sprint4Pruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private async Task<(int Id, string Codigo)> PedidoPagadoAsync(bool pagar = true)
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 10, precio: 55m);
        var cliente = await fabrica.EscalarAsync<int>("SELECT id FROM clientes WHERE email = @e", ("@e", Usuarios.Ana));
        var pedido = await _api.PostAsync(
            "/api/pedidos/presencial", new { clienteId = cliente, items = new[] { new { productoId = producto, cantidad = 1 } } }, recepcion.Token);
        var id = pedido.Data.GetProperty("id").GetInt32();
        if (pagar)
        {
            (await _api.PostAsync($"/api/pedidos/{id}/pagos/efectivo", null, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Created);
        }
        return (id, pedido.Data.GetProperty("codigo").GetString()!);
    }

    // ── HU-020: comprobante por WhatsApp ──────────────────────────────────

    [Fact]
    public async Task Comprobante_por_WhatsApp_arma_el_enlace_sin_datos_sensibles()
    {
        var (pedido, codigo) = await PedidoPagadoAsync();
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);

        var r = await _api.PostAsync($"/api/pedidos/{pedido}/comprobante/whatsapp", new { }, recepcion.Token);

        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("estado").GetString().ShouldBe("pendiente");
        r.Data.GetProperty("telefono").GetString().ShouldBe("987654321"); // celular de la ficha de Ana
        var mensaje = r.Data.GetProperty("mensaje").GetString()!;
        mensaje.ShouldContain(codigo);
        mensaje.ShouldContain("S/ 55.00");
        mensaje.ShouldNotContain("70123456"); // DNI
        mensaje.ShouldNotContain("Arequipa"); // dirección
        mensaje.ShouldNotContain("Producto"); // productos comprados
        r.Data.GetProperty("enlace").GetString().ShouldStartWith("https://wa.me/51987654321?text=RiverVet");
    }

    [Fact]
    public async Task Comprobante_solo_para_pedidos_pagados_con_limite_diario_y_telefono_valido()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var (sinPagar, _) = await PedidoPagadoAsync(pagar: false);
        var noPagado = await _api.PostAsync($"/api/pedidos/{sinPagar}/comprobante/whatsapp", new { }, recepcion.Token);
        noPagado.Estado.ShouldBe(HttpStatusCode.Conflict);

        var (pedido, _) = await PedidoPagadoAsync();
        (await _api.PostAsync($"/api/pedidos/{pedido}/comprobante/whatsapp", new { telefono = "12345" }, recepcion.Token))
            .CamposConError.ShouldBe(["telefono"]);
        for (var i = 0; i < 3; i++)
        {
            (await _api.PostAsync($"/api/pedidos/{pedido}/comprobante/whatsapp", new { telefono = "912345678" }, recepcion.Token))
                .Estado.ShouldBe(HttpStatusCode.Created);
        }
        (await _api.PostAsync($"/api/pedidos/{pedido}/comprobante/whatsapp", new { }, recepcion.Token))
            .Estado.ShouldBe(HttpStatusCode.TooManyRequests);

        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        (await _api.PostAsync($"/api/pedidos/{pedido}/comprobante/whatsapp", new { }, ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ── HU-021: triage ────────────────────────────────────────────────────

    [Fact]
    public async Task Triage_es_publico_y_siempre_trae_el_descargo()
    {
        var catalogo = await _api.GetAsync("/api/triage/sintomas");
        catalogo.Estado.ShouldBe(HttpStatusCode.OK);
        catalogo.Data.GetProperty("sintomas").GetArrayLength().ShouldBeGreaterThan(20);

        foreach (var (sintomas, esperada) in new[] { (new[] { "convulsiones" }, "alta"), (new[] { "tos" }, "media"), (new[] { "mal_aliento" }, "baja") })
        {
            var r = await _api.PostAsync("/api/triage", new { especie = "perro", sintomas, edadMeses = 36 });
            r.Estado.ShouldBe(HttpStatusCode.OK);
            r.Data.GetProperty("urgencia").GetString().ShouldBe(esperada);
            r.Data.GetProperty("descargo").GetString()!.ShouldContain("NO es un diagnóstico veterinario");
        }
    }

    [Fact]
    public async Task Triage_rechaza_sintomas_fuera_del_catalogo_y_texto_libre()
    {
        (await _api.PostAsync("/api/triage", new { especie = "gato", sintomas = new[] { "le duele la pancita" } })).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _api.PostAsync("/api/triage", new { especie = "gato", sintomas = Array.Empty<string>() })).CamposConError.ShouldContain("sintomas");
        (await _api.PostAsync("/api/triage", new { especie = "dragon", sintomas = new[] { "tos" } })).CamposConError.ShouldContain("especie");
        (await _api.PostAsync("/api/triage", new { especie = "gato", sintomas = new[] { "tos" }, descripcion = "texto libre" }))
            .CamposConError.ShouldContain("descripcion");
    }

    // ── Ley N° 29733: exportación de datos ────────────────────────────────

    [Fact]
    public async Task El_cliente_exporta_todos_sus_datos_y_solo_los_suyos()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        await PedidoPagadoAsync();

        var r = await _api.GetAsync("/api/clientes/yo/exportar", ana.Token);

        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Cabecera("Content-Disposition")!.ShouldContain("mis-datos-rivervet.json");
        r.Cabecera("Cache-Control").ShouldBe("no-store");
        r.Data.GetProperty("titular").GetProperty("dni").GetString().ShouldBe("70123456");
        r.Data.GetProperty("cuenta").GetProperty("email").GetString().ShouldBe(Usuarios.Ana);
        var mascotas = r.Data.GetProperty("mascotas").EnumerateArray().Select(m => m.GetProperty("nombre").GetString()).ToList();
        mascotas.ShouldContain("Firulais");
        mascotas.ShouldContain("Mishi");
        mascotas.ShouldNotContain("Rocky"); // es de Luis
        r.Data.GetProperty("pedidos").GetArrayLength().ShouldBeGreaterThan(0);
        r.Data.GetProperty("actividad").EnumerateArray().ShouldContain(a => a.GetProperty("accion").GetString() == "LOGIN");
        r.Data.GetProperty("finalidad").GetString().ShouldNotBeNullOrWhiteSpace();

        // Nunca viajan hashes de contraseña ni tokens.
        var texto = r.Json.GetRawText();
        texto.ShouldNotContain("passwordHash", Case.Insensitive);
        texto.ShouldNotContain("token", Case.Insensitive);
        texto.ShouldNotContain("$2b$");

        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM auditoria WHERE accion = 'EXPORTAR_DATOS' AND usuario_id = @u", ("@u", ana.UsuarioId)))
            .ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Solo_un_cliente_puede_exportar_y_solo_sus_datos()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        (await _api.GetAsync("/api/clientes/yo/exportar", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.GetAsync("/api/clientes/yo/exportar")).Estado.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
