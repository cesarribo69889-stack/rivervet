using System.Net;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-001 (alta de usuario por el admin), RBAC y auditoría.</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class UsuariosYAuditoriaPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    [Fact]
    public async Task El_admin_crea_un_veterinario_sin_devolver_el_hash()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var r = await _api.PostAsync("/api/usuarios", new { nombre = "Dra. Nueva", email = EmailUnico("vet"), password = PasswordSeed, rol = "veterinario" }, admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("rol").GetString().ShouldBe("veterinario");
        r.Data.GetProperty("activo").GetBoolean().ShouldBeTrue();
        r.Json.GetRawText().ShouldNotMatch("(?i)password|hash");
    }

    [Fact]
    public async Task RBAC_una_recepcionista_no_puede_crear_usuarios()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        (await _api.PostAsync("/api/usuarios", new { nombre = "Intruso", email = EmailUnico(), password = PasswordSeed, rol = "admin" }, recepcion.Token))
            .Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RBAC_un_cliente_no_puede_listar_usuarios()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        (await _api.GetAsync("/api/usuarios", ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Email_duplicado_devuelve_409_con_mensaje_claro()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var r = await _api.PostAsync("/api/usuarios", new { nombre = "Duplicado", email = Usuarios.Veterinario, password = PasswordSeed, rol = "veterinario" }, admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldBe("Ya existe un usuario con ese email");
    }

    [Fact]
    public async Task Contrasena_debil_devuelve_422_con_el_campo()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var r = await _api.PostAsync("/api/usuarios", new { nombre = "Débil", email = EmailUnico(), password = "12345678", rol = "veterinario" }, admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("password");
    }

    [Fact]
    public async Task Listado_paginado_filtrado_por_rol()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var r = await _api.GetAsync("/api/usuarios?rol=veterinario&limite=5", admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("paginacion").GetProperty("limite").GetInt32().ShouldBe(5);
        r.Data.GetProperty("items").EnumerateArray().ShouldAllBe(u => u.GetProperty("rol").GetString() == "veterinario");
    }

    [Fact]
    public async Task El_admin_no_puede_desactivarse_a_si_mismo()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        (await _api.PutAsync($"/api/usuarios/{admin.UsuarioId}", new { nombre = "Administración RiverVet", rol = "admin", activo = false }, admin.Token))
            .Estado.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task No_se_asigna_el_rol_cliente_desde_usuarios()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var creado = await _api.PostAsync("/api/usuarios", new { nombre = "Vet Temporal", email = EmailUnico(), password = PasswordSeed, rol = "veterinario" }, admin.Token);
        var r = await _api.PutAsync($"/api/usuarios/{creado.Data.GetProperty("id").GetInt32()}", new { nombre = "Vet Temporal", rol = "cliente", activo = true }, admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _api.GetAsync("/api/usuarios/999999", admin.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task El_admin_consulta_la_auditoria_filtrada_y_nadie_mas_puede()
    {
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var r = await _api.GetAsync("/api/auditoria?accion=LOGIN&limite=5", admin.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        var items = r.Data.GetProperty("items").EnumerateArray().ToList();
        items.ShouldNotBeEmpty();
        items.ShouldAllBe(a => a.GetProperty("accion").GetString() == "LOGIN");
        items[0].TryGetProperty("ip", out _).ShouldBeTrue();

        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        (await _api.GetAsync("/api/auditoria", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.GetAsync("/api/auditoria?accion=INVENTADA", admin.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
