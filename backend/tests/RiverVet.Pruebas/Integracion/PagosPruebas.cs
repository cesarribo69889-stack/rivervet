using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using RiverVet.Aplicacion.Pagos;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-017 (Yape), HU-019 (pasarela + webhook firmado) y pago en efectivo.</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class PagosPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private static string CodigoYape() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>Crea un pedido presencial pendiente de pago para el cliente indicado.</summary>
    private async Task<(int Id, decimal Total)> PedidoAsync(string emailCliente = Usuarios.Ana, decimal precio = 45.50m, int cantidad = 2)
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var producto = await Catalogo.CrearProductoAsync(fabrica, _api, admin.Token, stock: 20, precio: precio);
        var cliente = await fabrica.EscalarAsync<int>("SELECT id FROM clientes WHERE email = @e", ("@e", emailCliente));
        var r = await _api.PostAsync(
            "/api/pedidos/presencial",
            new { clienteId = cliente, items = new[] { new { productoId = producto, cantidad } } },
            recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created, r.Json.ToString());
        return (r.Data.GetProperty("id").GetInt32(), r.Data.GetProperty("total").GetDecimal());
    }

    private Task<string?> EstadoPedidoAsync(int pedidoId) =>
        fabrica.EscalarAsync<string>("SELECT estado FROM pedidos WHERE id = @id", ("@id", pedidoId));

    /// <summary>Envía un webhook como lo haría la pasarela: cuerpo JSON firmado con HMAC.</summary>
    private Task<Respuesta> WebhookAsync(object evento, string secreto = FabricaApi.SecretoWebhook, long? tiempo = null, byte[]? cuerpoEnviado = null)
    {
        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(evento);
        var t = tiempo ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var firma = FirmaWebhook.Calcular(secreto, t, cuerpo);
        var contenido = new ByteArrayContent(cuerpoEnviado ?? cuerpo);
        contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return _api.EnviarAsync(HttpMethod.Post, "/api/webhooks/pasarela", contenido, ajustar: p =>
        {
            p.Headers.Add(FirmaWebhook.CabeceraTiempo, t.ToString(CultureInfo.InvariantCulture));
            p.Headers.Add(FirmaWebhook.CabeceraFirma, firma);
        });
    }

    private async Task<(int PedidoId, decimal Total, string Referencia)> CheckoutAsync()
    {
        var (pedido, total) = await PedidoAsync();
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var r = await _api.PostAsync($"/api/pedidos/{pedido}/pagos/pasarela", null, ana.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created, r.Json.ToString());
        return (pedido, total, r.Data.GetProperty("referencia").GetString()!);
    }

    [Fact]
    public async Task Yape_queda_pendiente_de_verificacion_hasta_que_recepcion_lo_confirma()
    {
        var (pedido, total) = await PedidoAsync();
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);

        var datos = await _api.GetAsync("/api/pagos/yape", ana.Token);
        datos.Data.GetProperty("celular").GetString().ShouldBe("987000111");

        var codigo = CodigoYape();
        var r = await _api.PostAsync($"/api/pedidos/{pedido}/pagos/yape", new { codigoOperacion = codigo.ToLowerInvariant() }, ana.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("estado").GetString().ShouldBe("pendiente_verificacion");
        var pago = r.Data.GetProperty("pagos")[0];
        pago.GetProperty("estado").GetString().ShouldBe("pendiente_verificacion");
        pago.GetProperty("codigoOperacion").GetString().ShouldBe(codigo);
        // El monto es el del pedido guardado, no uno enviado por el cliente.
        pago.GetProperty("monto").GetDecimal().ShouldBe(total);

        var pendientes = await _api.GetAsync("/api/pagos/pendientes", recepcion.Token);
        var pagoId = pago.GetProperty("id").GetInt32();
        pendientes.Data.EnumerateArray().ShouldContain(p => p.GetProperty("pagoId").GetInt32() == pagoId);

        var verificado = await _api.PostAsync($"/api/pagos/{pagoId}/verificar", new { aprobado = true }, recepcion.Token);
        verificado.Estado.ShouldBe(HttpStatusCode.OK);
        verificado.Data.GetProperty("estado").GetString().ShouldBe("pagado");
        verificado.Data.GetProperty("pagos")[0].GetProperty("verificadoPorNombre").GetString().ShouldBe("Rosa Huamán");

        (await _api.PostAsync($"/api/pagos/{pagoId}/verificar", new { aprobado = true }, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Conflict);
        (await _api.PostAsync($"/api/pedidos/{pedido}/entregar", null, recepcion.Token)).Data.GetProperty("estado").GetString().ShouldBe("entregado");
    }

    [Fact]
    public async Task Yape_rechazado_devuelve_el_pedido_a_pendiente_de_pago()
    {
        var (pedido, _) = await PedidoAsync();
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var pagoId = (await _api.PostAsync($"/api/pedidos/{pedido}/pagos/yape", new { codigoOperacion = CodigoYape() }, ana.Token))
            .Data.GetProperty("pagos")[0].GetProperty("id").GetInt32();

        (await _api.PostAsync($"/api/pagos/{pagoId}/verificar", new { aprobado = false }, recepcion.Token))
            .CamposConError.ShouldContain("observacion");
        var r = await _api.PostAsync($"/api/pagos/{pagoId}/verificar", new { aprobado = false, observacion = "No llegó a la cuenta" }, recepcion.Token);
        r.Data.GetProperty("estado").GetString().ShouldBe("pendiente_pago");

        // Puede registrar el código correcto.
        (await _api.PostAsync($"/api/pedidos/{pedido}/pagos/yape", new { codigoOperacion = CodigoYape() }, ana.Token)).Estado.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Un_codigo_de_Yape_no_sirve_para_pagar_dos_pedidos()
    {
        var (primero, _) = await PedidoAsync();
        var (segundo, _) = await PedidoAsync();
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var codigo = CodigoYape();

        (await _api.PostAsync($"/api/pedidos/{primero}/pagos/yape", new { codigoOperacion = codigo }, ana.Token)).Estado.ShouldBe(HttpStatusCode.Created);
        var r = await _api.PostAsync($"/api/pedidos/{segundo}/pagos/yape", new { codigoOperacion = codigo }, ana.Token);
        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldBe("Ese número de operación de Yape ya fue registrado");
        (await EstadoPedidoAsync(segundo)).ShouldBe("pendiente_pago");
    }

    [Fact]
    public async Task Yape_control_de_propiedad_roles_y_estado()
    {
        var (pedidoDeAna, _) = await PedidoAsync();
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);

        (await _api.PostAsync($"/api/pedidos/{pedidoDeAna}/pagos/yape", new { codigoOperacion = CodigoYape() }, luis.Token))
            .Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.PostAsync($"/api/pedidos/{pedidoDeAna}/pagos/yape", new { codigoOperacion = "12 34" }, ana.Token))
            .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _api.PostAsync("/api/pagos/1/verificar", new { aprobado = true }, ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);

        (await _api.PostAsync($"/api/pedidos/{pedidoDeAna}/pagos/yape", new { codigoOperacion = CodigoYape() }, ana.Token)).Estado.ShouldBe(HttpStatusCode.Created);
        var otraVez = await _api.PostAsync($"/api/pedidos/{pedidoDeAna}/pagos/yape", new { codigoOperacion = CodigoYape() }, ana.Token);
        otraVez.Estado.ShouldBe(HttpStatusCode.Conflict);
        otraVez.Mensaje.ShouldBe("Este pedido ya tiene un pago en verificación");
    }

    [Fact]
    public async Task Pago_en_efectivo_lo_registra_recepcion()
    {
        var (pedido, total) = await PedidoAsync();
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);

        (await _api.PostAsync($"/api/pedidos/{pedido}/pagos/efectivo", null, ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        var r = await _api.PostAsync($"/api/pedidos/{pedido}/pagos/efectivo", null, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("estado").GetString().ShouldBe("pagado");
        r.Data.GetProperty("pagos")[0].GetProperty("monto").GetDecimal().ShouldBe(total);

        var cancelar = await _api.PostAsync($"/api/pedidos/{pedido}/cancelar", new { motivo = "Ya no" }, recepcion.Token);
        cancelar.Estado.ShouldBe(HttpStatusCode.Conflict);
        cancelar.Mensaje.ShouldBe("Un pedido pagado no se puede cancelar desde el sistema");
    }

    [Fact]
    public async Task Pasarela_checkout_y_webhook_firmado_marcan_el_pedido_como_pagado()
    {
        var (pedido, total, referencia) = await CheckoutAsync();
        referencia.ShouldStartWith("sim_");
        (await fabrica.EscalarAsync<string>("SELECT estado FROM pagos WHERE referencia_externa = @r", ("@r", referencia))).ShouldBe("pendiente");

        var evento = new { id = "evt_" + Guid.NewGuid().ToString("N"), tipo = "pago.aprobado", referencia, monto = total, moneda = "PEN" };
        var r = await WebhookAsync(evento);

        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("resultado").GetString().ShouldBe("aprobado");
        (await EstadoPedidoAsync(pedido)).ShouldBe("pagado");

        // La pasarela reintenta el mismo aviso: se reconoce y no se procesa dos veces.
        var repetido = await WebhookAsync(evento);
        repetido.Estado.ShouldBe(HttpStatusCode.OK);
        repetido.Data.GetProperty("resultado").GetString().ShouldBe("duplicado");
    }

    [Fact]
    public async Task Webhook_con_firma_falsa_o_cuerpo_alterado_se_rechaza_con_401()
    {
        var (pedido, total, referencia) = await CheckoutAsync();
        var evento = new { id = "evt_" + Guid.NewGuid().ToString("N"), tipo = "pago.aprobado", referencia, monto = total, moneda = "PEN" };

        var otroSecreto = await WebhookAsync(evento, secreto: "un-atacante-no-conoce-el-secreto-real-000");
        otroSecreto.Estado.ShouldBe(HttpStatusCode.Unauthorized);

        // Cuerpo firmado con monto 0.01 pero enviado con otro contenido: la firma ya no coincide.
        var alterado = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { evento.id, evento.tipo, referencia, monto = 0.01m, moneda = "PEN" }));
        (await WebhookAsync(evento, cuerpoEnviado: alterado)).Estado.ShouldBe(HttpStatusCode.Unauthorized);

        var sinFirma = await _api.EnviarAsync(HttpMethod.Post, "/api/webhooks/pasarela", evento);
        sinFirma.Estado.ShouldBe(HttpStatusCode.Unauthorized);

        (await EstadoPedidoAsync(pedido)).ShouldBe("pendiente_pago");
        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM webhook_eventos WHERE firma_valida = 0")).ShouldBeGreaterThanOrEqualTo(3);
        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM auditoria WHERE accion = 'WEBHOOK_FIRMA_INVALIDA'")).ShouldBeGreaterThanOrEqualTo(3);
        // El id del evento falso NO se guardó: no puede "gastar" el id de un aviso real.
        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM webhook_eventos WHERE evento_id = @id", ("@id", evento.id))).ShouldBe(0);
    }

    [Fact]
    public async Task Webhook_viejo_se_rechaza_aunque_la_firma_sea_valida()
    {
        var (_, total, referencia) = await CheckoutAsync();
        var evento = new { id = "evt_" + Guid.NewGuid().ToString("N"), tipo = "pago.aprobado", referencia, monto = total, moneda = "PEN" };
        var haceUnaHora = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        (await WebhookAsync(evento, tiempo: haceUnaHora)).Estado.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_con_otro_monto_no_aprueba_el_pago()
    {
        var (pedido, total, referencia) = await CheckoutAsync();
        var r = await WebhookAsync(new { id = "evt_" + Guid.NewGuid().ToString("N"), tipo = "pago.aprobado", referencia, monto = total - 1, moneda = "PEN" });
        r.Data.GetProperty("resultado").GetString().ShouldBe("monto_no_coincide");
        (await EstadoPedidoAsync(pedido)).ShouldBe("pendiente_pago");
    }

    [Fact]
    public async Task Si_el_pedido_se_cancelo_y_la_pasarela_igual_cobra_queda_marcado_para_devolucion()
    {
        var (pedido, total, referencia) = await CheckoutAsync();
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        (await _api.PostAsync($"/api/pedidos/{pedido}/cancelar", new { motivo = "Me arrepentí" }, ana.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await fabrica.EscalarAsync<string>("SELECT estado FROM pagos WHERE referencia_externa = @r", ("@r", referencia))).ShouldBe("rechazado");

        var r = await WebhookAsync(new { id = "evt_" + Guid.NewGuid().ToString("N"), tipo = "pago.aprobado", referencia, monto = total, moneda = "PEN" });
        r.Data.GetProperty("resultado").GetString().ShouldBe("requiere_devolucion");
        (await EstadoPedidoAsync(pedido)).ShouldBe("cancelado");
    }

    [Fact]
    public async Task Webhook_de_pago_rechazado_deja_el_pedido_pendiente()
    {
        var (pedido, total, referencia) = await CheckoutAsync();
        var r = await WebhookAsync(new { id = "evt_" + Guid.NewGuid().ToString("N"), tipo = "pago.rechazado", referencia, monto = total, moneda = "PEN" });
        r.Data.GetProperty("resultado").GetString().ShouldBe("rechazado");
        (await EstadoPedidoAsync(pedido)).ShouldBe("pendiente_pago");
    }

    [Fact]
    public async Task Solo_el_cliente_duenio_inicia_el_checkout()
    {
        var (pedido, _) = await PedidoAsync();
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        (await _api.PostAsync($"/api/pedidos/{pedido}/pagos/pasarela", null, luis.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.PostAsync($"/api/pedidos/{pedido}/pagos/pasarela", null, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }
}
