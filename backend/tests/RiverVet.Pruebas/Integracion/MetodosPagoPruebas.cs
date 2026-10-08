using System.Net;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>
/// "Métodos de pago" del panel: el administrador cambia el número y el
/// titular de Yape y sube el QR (migración 0006). Mientras no guarde nada,
/// rige la configuración del servidor (Pagos:Yape:*), como antes.
///
/// Cada prueba borra la fila al terminar: otras pruebas de la colección
/// (PagosPruebas) esperan el Yape de la configuración (987000111).
/// </summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class MetodosPagoPruebas(FabricaApi fabrica)
{
    // PNG de 1×1 píxel.
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private Task<int> BorrarConfiguracionAsync() =>
        fabrica.EscalarAsync<int>("DELETE FROM dbo.configuracion_yape; SELECT @@ROWCOUNT")!;

    [Fact]
    public async Task Sin_cambios_en_el_panel_rige_el_yape_del_servidor_y_no_hay_qr()
    {
        await BorrarConfiguracionAsync();
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);

        var datos = await _api.GetAsync("/api/pagos/yape", ana.Token);
        datos.Data.GetProperty("celular").GetString().ShouldBe("987000111");
        datos.Data.GetProperty("tieneQr").GetBoolean().ShouldBeFalse();
        (await _api.Http.GetAsync("/api/pagos/yape/qr")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Solo_el_admin_cambia_el_numero_y_el_titular_y_queda_auditado()
    {
        try
        {
            var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
            var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
            var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
            var cambio = new { celular = "912 345 678", titular = "Clínica RiverVet" };

            (await _api.PutAsync("/api/pagos/yape/configuracion", cambio, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
            (await _api.PutAsync("/api/pagos/yape/configuracion", cambio, ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
            (await _api.GetAsync("/api/pagos/yape/configuracion", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);

            var r = await _api.PutAsync("/api/pagos/yape/configuracion", cambio, admin.Token);
            r.Estado.ShouldBe(HttpStatusCode.OK);
            r.Data.GetProperty("celular").GetString().ShouldBe("912345678");

            // El cliente ve el número nuevo al pagar.
            var datos = await _api.GetAsync("/api/pagos/yape", ana.Token);
            datos.Data.GetProperty("celular").GetString().ShouldBe("912345678");
            datos.Data.GetProperty("titular").GetString().ShouldBe("Clínica RiverVet");

            var auditados = await fabrica.EscalarAsync<int>(
                "SELECT COUNT(*) FROM auditoria WHERE recurso = 'configuracion_pagos' AND recurso_id = 'yape' AND detalle LIKE '%987000111%'");
            auditados.ShouldBeGreaterThan(0); // guarda el número anterior

            var invalido = await _api.PutAsync("/api/pagos/yape/configuracion", new { celular = "812345678", titular = "<b>X</b>" }, admin.Token);
            invalido.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
            invalido.CamposConError.ShouldContain("celular");
            invalido.CamposConError.ShouldContain("titular");
        }
        finally
        {
            await BorrarConfiguracionAsync();
        }
    }

    [Fact]
    public async Task El_qr_se_sube_se_sirve_sin_sesion_y_solo_si_es_una_imagen_real()
    {
        try
        {
            var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
            var ana = await _api.IniciarSesionAsync(Usuarios.Ana);

            (await _api.PutAsync("/api/pagos/yape/qr", new { imagen = $"data:image/png;base64,{PngBase64}" }, ana.Token))
                .Estado.ShouldBe(HttpStatusCode.Forbidden);

            var r = await _api.PutAsync("/api/pagos/yape/qr", new { imagen = $"data:image/png;base64,{PngBase64}" }, admin.Token);
            r.Estado.ShouldBe(HttpStatusCode.OK);
            r.Data.GetProperty("tieneQr").GetBoolean().ShouldBeTrue();
            var version = r.Data.GetProperty("qrVersion").GetString();
            version.ShouldNotBeNullOrEmpty();

            // Sin token (lo pide un <img>), con el mismo contenido y cacheable.
            var qr = await _api.Http.GetAsync($"/api/pagos/yape/qr?v={version}");
            qr.StatusCode.ShouldBe(HttpStatusCode.OK);
            qr.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            (await qr.Content.ReadAsByteArrayAsync()).ShouldBe(Convert.FromBase64String(PngBase64));
            qr.Headers.CacheControl!.Public.ShouldBeTrue();

            var datos = await _api.GetAsync("/api/pagos/yape", ana.Token);
            datos.Data.GetProperty("tieneQr").GetBoolean().ShouldBeTrue();
            datos.Data.GetProperty("qrVersion").GetString().ShouldBe(version);

            // Tipo no permitido, contenido que no es lo que dice ser y tamaño.
            (await _api.PutAsync("/api/pagos/yape/qr", new { imagen = "data:text/html;base64,PGgxPmhvbGE8L2gxPg==" }, admin.Token))
                .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await _api.PutAsync("/api/pagos/yape/qr", new { imagen = "data:image/png;base64,PGgxPmhvbGE8L2gxPg==" }, admin.Token))
                .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
            var grande = Convert.ToBase64String(new byte[600 * 1024]);
            (await _api.PutAsync("/api/pagos/yape/qr", new { imagen = $"data:image/png;base64,{grande}" }, admin.Token))
                .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await BorrarConfiguracionAsync();
        }
    }
}
