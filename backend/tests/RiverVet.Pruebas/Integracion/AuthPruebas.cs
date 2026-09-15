using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-001: inicio de sesión seguro.</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class AuthPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    [Fact]
    public async Task Login_correcto_access_token_en_el_body_y_refresh_en_cookie_httpOnly()
    {
        var sesion = await _api.IniciarSesionAsync(Usuarios.Admin);
        var r = sesion.Login;

        r.Json.GetProperty("success").GetBoolean().ShouldBeTrue();
        r.Data.GetProperty("expiraEn").GetInt32().ShouldBe(900);
        r.Data.GetProperty("usuario").GetProperty("rol").GetString().ShouldBe("admin");
        // El refresh token NUNCA viaja en el body.
        r.Json.GetRawText().ShouldNotContain(sesion.Refresh);

        var cookieRefresh = r.SetCookies.Single(c => c.StartsWith("rv_refresh=", StringComparison.Ordinal));
        cookieRefresh.ShouldContain("httponly", Case.Insensitive);
        cookieRefresh.ShouldContain("samesite=strict", Case.Insensitive);
        cookieRefresh.ShouldContain("path=/api/auth", Case.Insensitive);

        r.SetCookies.Single(c => c.StartsWith("rv_csrf=", StringComparison.Ordinal)).ShouldNotContain("httponly", Case.Insensitive);
    }

    [Fact]
    public async Task Anti_enumeracion_email_inexistente_y_contrasena_incorrecta_responden_igual()
    {
        var inexistente = await _api.PostAsync("/api/auth/login", new { email = "nadie@rivervet.pe", password = "Cualquier#2026" });
        var incorrecta = await _api.PostAsync("/api/auth/login", new { email = Usuarios.Veterinario, password = "Incorrecta#2026" });

        inexistente.Estado.ShouldBe(HttpStatusCode.Unauthorized);
        incorrecta.Estado.ShouldBe(HttpStatusCode.Unauthorized);
        inexistente.Mensaje.ShouldBe(incorrecta.Mensaje);
        inexistente.Mensaje.ShouldNotContain("no existe", Case.Insensitive);
        await _api.IniciarSesionAsync(Usuarios.Veterinario); // deja su contador en cero
    }

    [Fact]
    public async Task Anti_enumeracion_el_tiempo_de_respuesta_es_similar_porque_bcrypt_siempre_corre()
    {
        async Task<long> MedirAsync(string email)
        {
            var reloj = Stopwatch.StartNew();
            await _api.PostAsync("/api/auth/login", new { email, password = "Incorrecta#2026" });
            return reloj.ElapsedMilliseconds;
        }
        await MedirAsync("calentamiento@rivervet.pe");
        var inexistente = await MedirAsync("fantasma@rivervet.pe");
        var existente = await MedirAsync(Usuarios.Recepcionista);
        // Ambos pagan el costo de bcrypt (cientos de ms). Si el inexistente no
        // lo pagara, respondería en pocos milisegundos.
        inexistente.ShouldBeGreaterThan((long)(existente * 0.4));
        await _api.IniciarSesionAsync(Usuarios.Recepcionista);
    }

    [Fact]
    public async Task Bloqueo_tras_5_intentos_fallidos_aun_con_la_contrasena_correcta()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var email = EmailUnico("bloqueo");
        (await _api.PostAsync("/api/usuarios", new { nombre = "Usuario Bloqueo", email, password = PasswordSeed, rol = "recepcionista" }, admin.Token))
            .Estado.ShouldBe(HttpStatusCode.Created);

        for (var i = 0; i < 5; i++)
        {
            (await _api.PostAsync("/api/auth/login", new { email, password = "Incorrecta#2026" })).Estado.ShouldBe(HttpStatusCode.Unauthorized);
        }
        var conCorrecta = await _api.PostAsync("/api/auth/login", new { email, password = PasswordSeed });
        conCorrecta.Estado.ShouldBe(HttpStatusCode.Unauthorized);
        // Mismo mensaje que cualquier otro fallo: no revela que está bloqueada.
        conCorrecta.Mensaje.ShouldBe("Email o contraseña incorrectos");

        var bloqueos = await fabrica.EscalarAsync<int>(
            "SELECT COUNT(*) FROM auditoria a JOIN usuarios u ON u.id = a.usuario_id WHERE u.email = @e AND a.accion = 'CUENTA_BLOQUEADA'",
            ("@e", email));
        bloqueos.ShouldBe(1);
    }

    [Fact]
    public async Task Los_intentos_fallidos_quedan_en_la_auditoria_sin_la_contrasena()
    {
        await _api.PostAsync("/api/auth/login", new { email = Usuarios.Luis, password = "OtraMala#2026" });
        var detalle = await fabrica.EscalarAsync<string>(
            "SELECT TOP 1 detalle FROM auditoria WHERE accion = 'LOGIN_FALLIDO' ORDER BY id DESC");
        detalle!.ShouldNotContain("OtraMala");
        JsonDocument.Parse(detalle!).RootElement.GetProperty("email").GetString().ShouldBe("lu***@correo.pe");
        await _api.IniciarSesionAsync(Usuarios.Luis);
    }

    [Fact]
    public async Task Yo_devuelve_el_usuario_de_la_sesion()
    {
        var sesion = await _api.IniciarSesionAsync(Usuarios.Ana);
        var r = await _api.GetAsync("/api/auth/yo", sesion.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("rol").GetString().ShouldBe("cliente");
        r.Data.GetProperty("clienteId").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Un_usuario_desactivado_pierde_el_acceso_de_inmediato()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var email = EmailUnico("desactivar");
        var creado = await _api.PostAsync("/api/usuarios", new { nombre = "Temporal", email, password = PasswordSeed, rol = "veterinario" }, admin.Token);
        var sesion = await _api.IniciarSesionAsync(email);

        (await _api.PutAsync($"/api/usuarios/{creado.Data.GetProperty("id").GetInt32()}",
            new { nombre = "Temporal", rol = "veterinario", activo = false }, admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);

        // Su access token aún no vence, pero el rol/estado se relee de la base.
        (await _api.GetAsync("/api/auth/yo", sesion.Token)).Estado.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_entrega_un_access_token_nuevo_y_ROTA_el_refresh_token()
    {
        var sesion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var r = await _api.PostConCookiesAsync("/api/auth/refresh", sesion.CabeceraCookies, sesion.Csrf);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
        r.Cookie("rv_refresh").ShouldNotBeNullOrEmpty();
        r.Cookie("rv_refresh").ShouldNotBe(sesion.Refresh);
    }

    [Fact]
    public async Task Reutilizar_un_refresh_token_ya_rotado_revoca_toda_la_sesion()
    {
        var sesion = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var primera = await _api.PostConCookiesAsync("/api/auth/refresh", sesion.CabeceraCookies, sesion.Csrf);
        primera.Estado.ShouldBe(HttpStatusCode.OK);
        var nuevo = primera.Cookie("rv_refresh")!;
        var csrfNuevo = primera.Cookie("rv_csrf")!;

        // Un atacante reutiliza el token viejo (robado).
        (await _api.PostConCookiesAsync("/api/auth/refresh", sesion.CabeceraCookies, sesion.Csrf)).Estado.ShouldBe(HttpStatusCode.Unauthorized);

        // Y ahora ni siquiera el token nuevo sirve: la familia quedó revocada.
        (await _api.PostConCookiesAsync("/api/auth/refresh", $"rv_refresh={nuevo}; rv_csrf={csrfNuevo}", csrfNuevo))
            .Estado.ShouldBe(HttpStatusCode.Unauthorized);

        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM auditoria WHERE accion = 'REUSO_REFRESH_TOKEN'")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Refresh_token_inventado_devuelve_401() =>
        (await _api.PostConCookiesAsync("/api/auth/refresh", "rv_refresh=inventado; rv_csrf=abc", "abc")).Estado.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task En_la_base_se_guarda_el_hash_del_refresh_token_nunca_el_token()
    {
        var sesion = await _api.IniciarSesionAsync(Usuarios.Admin);
        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM refresh_tokens WHERE token_hash = @t", ("@t", sesion.Refresh))).ShouldBe(0);
    }

    [Fact]
    public async Task Logout_revoca_el_refresh_token_en_la_base_de_datos()
    {
        var sesion = await _api.IniciarSesionAsync(Usuarios.Ana);
        var r = await _api.PostConCookiesAsync("/api/auth/logout", sesion.CabeceraCookies, sesion.Csrf);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.SetCookies.ShouldContain(c => c.StartsWith("rv_refresh=;", StringComparison.Ordinal));

        // La cookie copiada antes del logout ya no sirve.
        (await _api.PostConCookiesAsync("/api/auth/refresh", sesion.CabeceraCookies, sesion.Csrf)).Estado.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cambiar_contrasena_exige_la_actual_y_la_politica()
    {
        var sesion = await _api.IniciarSesionAsync(Usuarios.Luis);
        var incorrecta = await _api.PutAsync("/api/auth/password", new { passwordActual = "NoEsLaMia#2026", passwordNueva = "Nueva#Clave2026" }, sesion.Token);
        incorrecta.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        incorrecta.CamposConError.ShouldContain("passwordActual");

        (await _api.PutAsync("/api/auth/password", new { passwordActual = PasswordSeed, passwordNueva = "debil" }, sesion.Token))
            .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Al_cambiar_la_contrasena_se_cierran_todas_las_sesiones()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var email = EmailUnico("cambio");
        await _api.PostAsync("/api/usuarios", new { nombre = "Cambio Clave", email, password = PasswordSeed, rol = "recepcionista" }, admin.Token);
        var sesion = await _api.IniciarSesionAsync(email);

        (await _api.PutAsync("/api/auth/password", new { passwordActual = PasswordSeed, passwordNueva = "Nueva#Clave2026" }, sesion.Token))
            .Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.PostConCookiesAsync("/api/auth/refresh", sesion.CabeceraCookies, sesion.Csrf)).Estado.ShouldBe(HttpStatusCode.Unauthorized);
        await _api.IniciarSesionAsync(email, "Nueva#Clave2026");
    }
}
