using System.Net;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-004, HU-005 y ★ CONTROL DE PROPIEDAD (IDOR) ★.</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class MascotasPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private Task<int> ClienteIdAsync(string dni) => fabrica.EscalarAsync<int>("SELECT id FROM clientes WHERE dni = @d", ("@d", dni))!;

    private Task<int> MascotaIdAsync(string nombre) => fabrica.EscalarAsync<int>("SELECT TOP 1 id FROM mascotas WHERE nombre = @n", ("@n", nombre))!;

    [Fact]
    public async Task Recepcion_registra_una_mascota_y_se_limpia_el_HTML()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var luis = await ClienteIdAsync("71234567");
        var r = await _api.PostAsync("/api/mascotas", new
        {
            clienteId = luis,
            nombre = "Canela",
            especie = "perro",
            raza = "Chihuahua",
            sexo = "hembra",
            fechaNacimiento = "2021-06-15",
            pesoKg = 2.35,
            senasParticulares = "Mancha blanca <b onmouseover=alert(1)>en el pecho</b>",
        }, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("clienteId").GetInt32().ShouldBe(luis);
        r.Data.GetProperty("fechaNacimiento").GetString().ShouldBe("2021-06-15");
        r.Data.GetProperty("pesoKg").GetDecimal().ShouldBe(2.35m);
        r.Data.GetProperty("senasParticulares").GetString().ShouldBe("Mancha blanca en el pecho");
    }

    [Fact]
    public async Task Cliente_inexistente_devuelve_422_en_clienteId()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var r = await _api.PostAsync("/api/mascotas", new { clienteId = 999999, nombre = "Nadie", especie = "gato" }, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldBe(["clienteId"]);
    }

    [Fact]
    public async Task Validaciones_de_especie_fecha_y_peso()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var r = await _api.PostAsync("/api/mascotas",
            new { clienteId = await ClienteIdAsync("70123456"), nombre = "X", especie = "dragón", fechaNacimiento = "2999-01-01", pesoKg = -1 },
            recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("especie");
        r.CamposConError.ShouldContain("fechaNacimiento");
        r.CamposConError.ShouldContain("pesoKg");
    }

    [Fact]
    public async Task RBAC_un_cliente_no_registra_modifica_ni_elimina_mascotas()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var propia = await MascotaIdAsync("Firulais");
        (await _api.PostAsync("/api/mascotas", new { clienteId = await ClienteIdAsync("70123456"), nombre = "Mía", especie = "gato" }, ana.Token))
            .Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.PutAsync($"/api/mascotas/{propia}", new { nombre = "X", especie = "perro" }, ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.DeleteAsync($"/api/mascotas/{await MascotaIdAsync("Rocky")}", ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task El_personal_ve_las_mascotas_de_cualquier_cliente()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var r = await _api.GetAsync($"/api/clientes/{await ClienteIdAsync("70123456")}/mascotas", vet.Token);
        r.Data.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("nombre").GetString()).ShouldBe(["Firulais", "Mishi"], ignoreOrder: true);
    }

    [Fact]
    public async Task Un_cliente_ve_SOLO_sus_mascotas()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var r = await _api.GetAsync("/api/mascotas?limite=100", ana.Token);
        var nombres = r.Data.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("nombre").GetString()).ToList();
        nombres.ShouldBe(["Firulais", "Mishi"], ignoreOrder: true);
        nombres.ShouldNotContain("Rocky");
    }

    [Fact]
    public async Task Las_fechas_DATE_viajan_sin_corrimiento_de_zona_horaria()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var r = await _api.GetAsync($"/api/mascotas/{await MascotaIdAsync("Firulais")}", ana.Token);
        r.Data.GetProperty("fechaNacimiento").GetString().ShouldBe("2019-03-15");
        r.Data.GetProperty("creadoEn").GetString()!.ShouldEndWith("Z");
    }

    [Fact]
    public async Task IDOR_mascota_ajena_devuelve_404_y_no_403()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var r = await _api.GetAsync($"/api/mascotas/{await MascotaIdAsync("Rocky")}", ana.Token);
        r.Estado.ShouldBe(HttpStatusCode.NotFound);
        r.Mensaje.ShouldBe("Mascota no encontrada");
    }

    [Fact]
    public async Task IDOR_mascotas_de_otro_cliente_por_la_ruta_del_cliente_devuelve_404()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        var idLuis = await ClienteIdAsync("71234567");
        (await _api.GetAsync($"/api/clientes/{idLuis}/mascotas", ana.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.GetAsync($"/api/clientes/{idLuis}/mascotas", luis.Token)).Estado.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IDOR_la_busqueda_tampoco_filtra_datos_ajenos()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        (await _api.PostAsync("/api/mascotas/buscar", new { texto = "Rocky" }, ana.Token)).Data.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task El_veterinario_actualiza_pero_el_duenio_no_se_cambia_por_el_body()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var max = await MascotaIdAsync("Max");
        (await _api.PutAsync($"/api/mascotas/{max}", new { nombre = "Max", especie = "perro", pesoKg = 30.1, clienteId = 1 }, vet.Token))
            .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var r = await _api.PutAsync($"/api/mascotas/{max}", new { nombre = "Max", especie = "perro", raza = "Labrador", pesoKg = 30.1 }, vet.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("pesoKg").GetDecimal().ShouldBe(30.1m);
    }

    [Fact]
    public async Task Baja_logica_por_recepcion_y_el_veterinario_no_puede_eliminar()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var creada = await _api.PostAsync("/api/mascotas", new { clienteId = await ClienteIdAsync("70123456"), nombre = "Temporal", especie = "roedor" }, recepcion.Token);
        var id = creada.Data.GetProperty("id").GetInt32();
        (await _api.DeleteAsync($"/api/mascotas/{id}", vet.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.DeleteAsync($"/api/mascotas/{id}", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.GetAsync($"/api/mascotas/{id}", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.DeleteAsync($"/api/mascotas/{id}", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
    }
}
