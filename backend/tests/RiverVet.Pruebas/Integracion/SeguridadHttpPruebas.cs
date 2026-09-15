using System.Net;
using System.Net.Http.Headers;
using System.Text;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>Cabeceras, CORS, CSRF, formas de error, límites de tamaño y rate limiting.</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class SeguridadHttpPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    [Fact]
    public async Task Cabeceras_de_seguridad_y_traceId_en_toda_respuesta()
    {
        var r = await _api.GetAsync("/api/no-existe");
        r.Cabecera("Content-Security-Policy")!.ShouldContain("default-src 'none'");
        r.Cabecera("X-Frame-Options").ShouldBe("DENY");
        r.Cabecera("X-Content-Type-Options").ShouldBe("nosniff");
        r.Cabecera("X-Powered-By").ShouldBeNull();
        r.Cabecera("Server").ShouldBeNull();
        var traza = r.Cabecera("X-Trace-Id");
        traza.ShouldNotBeNullOrEmpty();
        r.Json.GetProperty("traceId").GetString().ShouldBe(traza);
    }

    [Fact]
    public async Task Ruta_inexistente_404_con_la_forma_estandar()
    {
        var r = await _api.GetAsync("/api/no-existe");
        r.Estado.ShouldBe(HttpStatusCode.NotFound);
        r.Json.GetProperty("success").GetBoolean().ShouldBeFalse();
        r.Mensaje.ShouldBe("La ruta solicitada no existe");
        r.Json.GetProperty("errors").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task JSON_mal_formado_400()
    {
        var r = await _api.PostAsync("/api/auth/login", new StringContent("{\"email\": ", Encoding.UTF8, "application/json"));
        r.Estado.ShouldBe(HttpStatusCode.BadRequest);
        r.Mensaje.ShouldBe("El cuerpo de la solicitud no es un JSON válido");
    }

    [Fact]
    public async Task Body_de_mas_de_1_MB_413()
    {
        var grande = "{\"email\":\"a@b.pe\",\"password\":\"" + new string('x', 1024 * 1024 + 10) + "\"}";
        var r = await _api.PostAsync("/api/auth/login", new StringContent(grande, Encoding.UTF8, "application/json"));
        r.Estado.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Login_con_campos_extra_422_lista_blanca()
    {
        var r = await _api.PostAsync("/api/auth/login", new { email = "a@b.pe", password = "x", rol = "admin" });
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldBe(["rol"]);
    }

    [Fact]
    public async Task Tipo_de_dato_incorrecto_422()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var r = await _api.PostAsync("/api/mascotas", new { clienteId = "uno", nombre = "Luna", especie = "gato" }, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("clienteId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer no.es.un.jwt")]
    [InlineData("Basic YWRtaW46YWRtaW4=")]
    public async Task Sin_token_valido_401(string? autorizacion)
    {
        var r = await _api.EnviarAsync(HttpMethod.Get, "/api/clientes", ajustar: p =>
        {
            if (autorizacion is not null)
            {
                p.Headers.Authorization = AuthenticationHeaderValue.Parse(autorizacion);
            }
        });
        r.Estado.ShouldBe(HttpStatusCode.Unauthorized);
        r.Json.GetProperty("success").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_sin_CSRF_403_y_con_CSRF_distinto_403()
    {
        (await _api.PostConCookiesAsync("/api/auth/refresh", "rv_refresh=abc", null)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.PostConCookiesAsync("/api/auth/logout", "rv_refresh=abc; rv_csrf=uno", "otro")).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CORS_origen_permitido_con_credenciales_y_origen_ajeno_sin_cabeceras()
    {
        async Task<Respuesta> PreflightAsync(string origen) =>
            await _api.EnviarAsync(HttpMethod.Options, "/api/auth/login", ajustar: p =>
            {
                p.Headers.Add("Origin", origen);
                p.Headers.Add("Access-Control-Request-Method", "POST");
            });

        var permitido = await PreflightAsync("http://localhost:5173");
        permitido.Cabecera("Access-Control-Allow-Origin").ShouldBe("http://localhost:5173");
        permitido.Cabecera("Access-Control-Allow-Credentials").ShouldBe("true");

        (await PreflightAsync("https://sitio-malicioso.com")).Cabecera("Access-Control-Allow-Origin").ShouldBeNull();
    }

    [Fact]
    public async Task Health_responde_ok_con_la_base_de_datos()
    {
        var r = await _api.GetAsync("/api/health");
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("baseDatos").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task Rate_limit_global_429_y_el_health_check_no_se_limita()
    {
        await using var limitada = FabricaApi.ConConfiguracion(new() { ["Seguridad:GlobalMax"] = "2" });
        var api = limitada.CrearClienteApi();
        await api.GetAsync("/api/clientes");
        await api.GetAsync("/api/clientes");
        var bloqueada = await api.GetAsync("/api/clientes");
        bloqueada.Estado.ShouldBe(HttpStatusCode.TooManyRequests);
        bloqueada.Json.GetProperty("success").GetBoolean().ShouldBeFalse();
        (await api.GetAsync("/api/health")).Estado.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rate_limit_de_login_por_IP_y_email_sin_bloquear_a_otro_usuario()
    {
        await using var limitada = FabricaApi.ConConfiguracion(new() { ["Seguridad:LoginIntentosMax"] = "2" });
        var api = limitada.CrearClienteApi();
        var email = EmailUnico("limite");
        (await api.PostAsync("/api/auth/login", new { email, password = "Mala#12345" })).Estado.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.PostAsync("/api/auth/login", new { email, password = "Mala#12345" })).Estado.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.PostAsync("/api/auth/login", new { email, password = "Mala#12345" })).Estado.ShouldBe(HttpStatusCode.TooManyRequests);
        // Otro usuario desde la misma IP puede seguir entrando.
        (await api.PostAsync("/api/auth/login", new { email = Usuarios.Ana, password = PasswordSeed })).Estado.ShouldBe(HttpStatusCode.OK);
    }
}
