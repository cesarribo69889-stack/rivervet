using System.Net;
using System.Text.Json;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-002 (registro de cliente) y HU-003 (consulta y actualización).</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class ClientesPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private static Dictionary<string, object?> NuevoCliente(params (string Clave, object? Valor)[] cambios)
    {
        var datos = new Dictionary<string, object?>
        {
            ["dni"] = DniUnico(),
            ["nombres"] = "Carmen Rosa",
            ["apellidos"] = "Quispe Mamani",
            ["telefono"] = CelularUnico(),
            ["email"] = EmailUnico("cliente"),
            ["direccion"] = "Av. Túpac Amaru 123, Comas",
            ["consentimientoDatos"] = true,
        };
        foreach (var (clave, valor) in cambios)
        {
            datos[clave] = valor;
        }
        return datos;
    }

    private Task<Sesion> RecepcionAsync() => _api.IniciarSesionAsync(Usuarios.Recepcionista);

    [Fact]
    public async Task Recepcion_registra_un_cliente_sin_cuenta_web_y_queda_auditado()
    {
        var recepcion = await RecepcionAsync();
        var r = await _api.PostAsync("/api/clientes", NuevoCliente(), recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("tieneCuenta").GetBoolean().ShouldBeFalse();
        r.Data.GetProperty("consentimientoDatos").GetBoolean().ShouldBeTrue();
        r.Data.TryGetProperty("usuarioId", out _).ShouldBeFalse();

        var id = r.Data.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
        (await fabrica.EscalarAsync<int>(
            "SELECT COUNT(*) FROM auditoria WHERE recurso = 'cliente' AND accion = 'CREAR' AND recurso_id = @id", ("@id", id))).ShouldBe(1);
    }

    [Fact]
    public async Task Registra_un_cliente_con_cuenta_web_y_ese_cliente_puede_iniciar_sesion()
    {
        var recepcion = await RecepcionAsync();
        var datos = NuevoCliente(("crearCuenta", true), ("password", PasswordSeed));
        var r = await _api.PostAsync("/api/clientes", datos, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("tieneCuenta").GetBoolean().ShouldBeTrue();
        var sesion = await _api.IniciarSesionAsync((string)datos["email"]!);
        sesion.Usuario.GetProperty("rol").GetString().ShouldBe("cliente");
    }

    [Fact]
    public async Task Sin_consentimiento_Ley_29733_devuelve_422()
    {
        var r = await _api.PostAsync("/api/clientes", NuevoCliente(("consentimientoDatos", false)), (await RecepcionAsync()).Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("consentimientoDatos");
    }

    [Fact]
    public async Task DNI_duplicado_devuelve_409_con_mensaje_claro()
    {
        var recepcion = await RecepcionAsync();
        var datos = NuevoCliente();
        (await _api.PostAsync("/api/clientes", datos, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Created);
        var r = await _api.PostAsync("/api/clientes", NuevoCliente(("dni", datos["dni"])), recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldBe("Ya existe un cliente con ese DNI");
    }

    [Fact]
    public async Task Si_falla_la_cuenta_no_queda_la_ficha_a_medias_transaccion()
    {
        var datos = NuevoCliente(("email", Usuarios.Admin), ("crearCuenta", true), ("password", PasswordSeed));
        (await _api.PostAsync("/api/clientes", datos, (await RecepcionAsync()).Token)).Estado.ShouldBe(HttpStatusCode.Conflict);
        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM clientes WHERE dni = @dni", ("@dni", (string)datos["dni"]!))).ShouldBe(0);
    }

    [Fact]
    public async Task Mass_assignment_no_se_puede_inyectar_usuarioId()
    {
        var r = await _api.PostAsync("/api/clientes", NuevoCliente(("usuarioId", 1)), (await RecepcionAsync()).Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.Json.GetProperty("errors")[0].GetProperty("mensaje").GetString().ShouldBe("Campo no permitido");
        r.CamposConError.ShouldContain("usuarioId");
    }

    [Fact]
    public async Task Formatos_peruanos_invalidos_devuelven_422_por_campo()
    {
        var r = await _api.PostAsync("/api/clientes", NuevoCliente(("dni", "123"), ("telefono", "12345")), (await RecepcionAsync()).Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("dni");
        r.CamposConError.ShouldContain("telefono");
    }

    [Fact]
    public async Task RBAC_un_veterinario_no_registra_clientes_pero_si_los_consulta()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        (await _api.PostAsync("/api/clientes", NuevoCliente(), vet.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.GetAsync("/api/clientes", vet.Token)).Estado.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Listado_paginado_con_la_forma_estandar()
    {
        var r = await _api.GetAsync("/api/clientes?pagina=1&limite=3", (await RecepcionAsync()).Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("items").GetArrayLength().ShouldBeLessThanOrEqualTo(3);
        r.Data.GetProperty("paginacion").GetProperty("total").GetInt32().ShouldBeGreaterThan(0);
        r.Data.GetProperty("items")[0].TryGetProperty("totalMascotas", out _).ShouldBeTrue();
        (await _api.GetAsync("/api/clientes?limite=500", (await RecepcionAsync()).Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Busqueda_por_DNI_por_POST()
    {
        var r = await _api.PostAsync("/api/clientes/buscar", new { texto = "70123456" }, (await RecepcionAsync()).Token);
        r.Data.GetProperty("items").GetArrayLength().ShouldBe(1);
        r.Data.GetProperty("items")[0].GetProperty("apellidos").GetString().ShouldBe("Torres Medina");
    }

    [Fact]
    public async Task Inyeccion_SQL_en_la_busqueda_se_busca_literalmente()
    {
        var r = await _api.PostAsync("/api/clientes/buscar", new { texto = "' OR 1=1; DROP TABLE clientes; --" }, (await RecepcionAsync()).Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("items").GetArrayLength().ShouldBe(0);
        (await fabrica.EscalarAsync<int>("SELECT COUNT(*) FROM clientes")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Buscar_porcentaje_no_trae_todo_comodines_escapados() =>
        (await _api.PostAsync("/api/clientes/buscar", new { texto = "%" }, (await RecepcionAsync()).Token))
            .Data.GetProperty("items").GetArrayLength().ShouldBe(0);

    [Fact]
    public async Task Un_cliente_no_lista_a_los_demas_pero_ve_su_propia_ficha()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        (await _api.GetAsync("/api/clientes", ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.PostAsync("/api/clientes/buscar", new { }, ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        var yo = await _api.GetAsync("/api/clientes/yo", ana.Token);
        yo.Data.GetProperty("dni").GetString().ShouldBe("70123456");
    }

    [Fact]
    public async Task Id_inexistente_404_y_no_numerico_422()
    {
        var recepcion = await RecepcionAsync();
        (await _api.GetAsync("/api/clientes/999999", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.GetAsync("/api/clientes/abc", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Actualiza_y_audita_que_campos_cambiaron_sin_los_valores()
    {
        var recepcion = await RecepcionAsync();
        var creado = (await _api.PostAsync("/api/clientes", NuevoCliente(), recepcion.Token)).Data;
        var id = creado.GetProperty("id").GetInt32();
        var r = await _api.PutAsync($"/api/clientes/{id}", new
        {
            dni = creado.GetProperty("dni").GetString(),
            nombres = creado.GetProperty("nombres").GetString(),
            apellidos = creado.GetProperty("apellidos").GetString(),
            email = creado.GetProperty("email").GetString(),
            telefono = "999888777",
            direccion = "Nueva dirección",
        }, recepcion.Token);
        r.Data.GetProperty("telefono").GetString().ShouldBe("999888777");

        var detalle = await fabrica.EscalarAsync<string>(
            "SELECT TOP 1 detalle FROM auditoria WHERE recurso = 'cliente' AND accion = 'ACTUALIZAR' AND recurso_id = @id ORDER BY id DESC",
            ("@id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var campos = JsonDocument.Parse(detalle!).RootElement.GetProperty("campos").EnumerateArray().Select(c => c.GetString()).ToList();
        campos.ShouldContain("telefono");
        campos.ShouldContain("direccion");
        detalle!.ShouldNotContain("999888777");
    }

    [Fact]
    public async Task El_texto_libre_se_guarda_sin_etiquetas_HTML()
    {
        var r = await _api.PostAsync("/api/clientes",
            NuevoCliente(("direccion", "Calle 1 <script>alert(\"xss\")</script><img src=x onerror=alert(1)>")),
            (await RecepcionAsync()).Token);
        r.Data.GetProperty("direccion").GetString().ShouldBe("Calle 1 alert(\"xss\")");
    }

    [Fact]
    public async Task Baja_logica_solo_admin_y_la_ficha_sigue_en_la_base()
    {
        var recepcion = await RecepcionAsync();
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var id = (await _api.PostAsync("/api/clientes", NuevoCliente(), recepcion.Token)).Data.GetProperty("id").GetInt32();

        (await _api.DeleteAsync($"/api/clientes/{id}", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.DeleteAsync($"/api/clientes/{id}", admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.GetAsync($"/api/clientes/{id}", admin.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await fabrica.EscalarAsync<bool>("SELECT eliminado FROM clientes WHERE id = @id", ("@id", id))).ShouldBeTrue();
    }
}
