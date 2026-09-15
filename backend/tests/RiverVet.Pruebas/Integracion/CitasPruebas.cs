using System.Net;
using System.Text.Json;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-008, HU-009, HU-010 — incluida la prueba de CONCURRENCIA del doble agendamiento.</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class CitasPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private Task<int> IdAsync(string sql, params (string, object)[] p) => fabrica.EscalarAsync<int>(sql, p)!;

    private Task<int> VetLopezAsync() => IdAsync("SELECT id FROM usuarios WHERE email = @e", ("@e", Usuarios.Veterinario));

    private Task<int> VetQuispeAsync() => IdAsync("SELECT id FROM usuarios WHERE email = 'vet.quispe@rivervet.pe'");

    private Task<int> MascotaAsync(string nombre) => IdAsync("SELECT TOP 1 id FROM mascotas WHERE nombre = @n", ("@n", nombre));

    private async Task<Respuesta> AgendarAsync(string token, int mascotaId, int vetId, string fechaHora, string motivo = "Control general") =>
        await _api.PostAsync("/api/citas", new { mascotaId, veterinarioId = vetId, fechaHora, motivo }, token);

    [Fact]
    public async Task Recepcion_agenda_una_cita_y_se_guarda_en_UTC()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var horario = Horarios.Siguiente();
        var r = await AgendarAsync(recepcion.Token, await MascotaAsync("Firulais"), await VetLopezAsync(), horario);

        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("estado").GetString().ShouldBe("programada");
        r.Data.GetProperty("fechaHoraLima").GetString().ShouldBe(horario);
        // Lima = UTC-5: la hora guardada es 5 horas más.
        var utc = DateTime.Parse(r.Data.GetProperty("fechaHora").GetString()!, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal);
        utc.ShouldBe(DateTime.ParseExact(horario, "yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture).AddHours(5));
        r.Data.GetProperty("veterinarioNombre").GetString().ShouldBe("Dra. Carla López");
    }

    /// <summary>
    /// ★ HU-008 CRÍTICO: 8 peticiones SIMULTÁNEAS reservan el mismo horario
    /// del mismo veterinario (8 mascotas distintas). Exactamente UNA gana; las
    /// otras 7 reciben 409. Lo garantiza el índice único de la base, no un
    /// SELECT previo.
    /// </summary>
    [Fact]
    public async Task Concurrencia_dos_o_mas_reservas_simultaneas_del_mismo_horario_solo_una_gana()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await VetQuispeAsync();
        var horario = Horarios.Siguiente();
        string[] nombres = ["Firulais", "Mishi", "Rocky", "Luna", "Pepe", "Max", "Nala", "Coco"];
        var mascotas = (await Task.WhenAll(nombres.Select(MascotaAsync))).ToList();

        var respuestas = await Task.WhenAll(mascotas.Select(m => AgendarAsync(recepcion.Token, m, vet, horario)));

        respuestas.Count(r => r.Estado == HttpStatusCode.Created).ShouldBe(1);
        respuestas.Count(r => r.Estado == HttpStatusCode.Conflict).ShouldBe(7);
        respuestas.Where(r => r.Estado == HttpStatusCode.Conflict)
            .ShouldAllBe(r => r.Mensaje == "El veterinario ya tiene una cita en ese horario. Elige otro.");

        var fechaUtc = DateTime.ParseExact(horario, "yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture).AddHours(5);
        (await fabrica.EscalarAsync<int>(
            "SELECT COUNT(*) FROM citas WHERE veterinario_id = @v AND fecha_hora = @f AND estado <> 'cancelada'",
            ("@v", vet), ("@f", fechaUtc))).ShouldBe(1);
    }

    [Fact]
    public async Task La_misma_mascota_no_puede_tener_dos_citas_a_la_misma_hora()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var horario = Horarios.Siguiente();
        var rocky = await MascotaAsync("Rocky");
        (await AgendarAsync(recepcion.Token, rocky, await VetLopezAsync(), horario)).Estado.ShouldBe(HttpStatusCode.Created);
        var r = await AgendarAsync(recepcion.Token, rocky, await VetQuispeAsync(), horario);
        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldBe("La mascota ya tiene otra cita en ese horario.");
    }

    [Theory]
    [InlineData("2020-01-06T10:00")] // pasado
    [InlineData("formato-invalido")]
    public async Task Horarios_invalidos_devuelven_422(string fechaHora)
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var r = await AgendarAsync(recepcion.Token, await MascotaAsync("Luna"), await VetLopezAsync(), fechaHora);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("fechaHora");
    }

    [Fact]
    public async Task Fuera_de_bloque_domingo_y_fuera_de_horario_devuelven_422()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var basico = Horarios.Siguiente();
        foreach (var fechaHora in new[] { basico[..11] + "10:15", Horarios.ProximoDomingo() + "T10:00", basico[..11] + "21:00" })
        {
            (await AgendarAsync(recepcion.Token, await MascotaAsync("Luna"), await VetLopezAsync(), fechaHora))
                .Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
    }

    [Fact]
    public async Task Veterinario_que_no_es_veterinario_y_mascota_inexistente_devuelven_422()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var r = await AgendarAsync(recepcion.Token, await MascotaAsync("Luna"), recepcion.UsuarioId, Horarios.Siguiente());
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldBe(["veterinarioId"]);

        var m = await AgendarAsync(recepcion.Token, 999999, await VetLopezAsync(), Horarios.Siguiente());
        m.CamposConError.ShouldBe(["mascotaId"]);
    }

    [Fact]
    public async Task RBAC_ni_el_cliente_ni_el_veterinario_agendan()
    {
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        (await AgendarAsync(ana.Token, await MascotaAsync("Firulais"), await VetLopezAsync(), Horarios.Siguiente())).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await AgendarAsync(vet.Token, await MascotaAsync("Firulais"), await VetLopezAsync(), Horarios.Siguiente())).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Calendario_por_rango_y_veterinario()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await VetLopezAsync();
        var horario = Horarios.Siguiente();
        var id = (await AgendarAsync(recepcion.Token, await MascotaAsync("Max"), vet, horario)).Data.GetProperty("id").GetInt32();
        var dia = Horarios.Fecha(horario);

        var r = await _api.GetAsync($"/api/citas?desde={dia}&hasta={dia}&veterinarioId={vet}", recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        var citas = r.Data.EnumerateArray().ToList();
        citas.ShouldContain(c => c.GetProperty("id").GetInt32() == id);
        citas.ShouldAllBe(c => c.GetProperty("veterinarioId").GetInt32() == vet);
        citas.ShouldAllBe(c => c.GetProperty("fechaHoraLima").GetString()!.StartsWith(dia, StringComparison.Ordinal));

        (await _api.GetAsync("/api/citas?desde=2030-01-01&hasta=2030-12-31", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _api.GetAsync("/api/citas?desde=2030-02-01&hasta=2030-01-01", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task IDOR_un_cliente_solo_ve_y_cancela_las_citas_de_sus_mascotas()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var horarioAna = Horarios.Siguiente();
        var horarioLuis = Horarios.Siguiente();
        var citaAna = (await AgendarAsync(recepcion.Token, await MascotaAsync("Mishi"), await VetLopezAsync(), horarioAna)).Data.GetProperty("id").GetInt32();
        var citaLuis = (await AgendarAsync(recepcion.Token, await MascotaAsync("Rocky"), await VetQuispeAsync(), horarioLuis)).Data.GetProperty("id").GetInt32();

        var desde = Horarios.Fecha(horarioAna).CompareTo(Horarios.Fecha(horarioLuis)) <= 0 ? Horarios.Fecha(horarioAna) : Horarios.Fecha(horarioLuis);
        var lista = await _api.GetAsync($"/api/citas?desde={desde}", ana.Token);
        var ids = lista.Data.EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToList();
        ids.ShouldContain(citaAna);
        ids.ShouldNotContain(citaLuis);

        (await _api.GetAsync($"/api/citas/{citaLuis}", ana.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.PostAsync($"/api/citas/{citaLuis}/cancelar", new { motivo = "no es mía" }, ana.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await fabrica.EscalarAsync<string>("SELECT estado FROM citas WHERE id = @id", ("@id", citaLuis))).ShouldBe("programada");

        var propia = await _api.PostAsync($"/api/citas/{citaAna}/cancelar", new { motivo = "Viaje" }, ana.Token);
        propia.Estado.ShouldBe(HttpStatusCode.OK);
        propia.Data.GetProperty("estado").GetString().ShouldBe("cancelada");
    }

    [Fact]
    public async Task Cancelar_libera_el_horario()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await VetLopezAsync();
        var horario = Horarios.Siguiente();
        var id = (await AgendarAsync(recepcion.Token, await MascotaAsync("Nala"), vet, horario)).Data.GetProperty("id").GetInt32();
        (await _api.PostAsync($"/api/citas/{id}/cancelar", new { motivo = "Reprogramará por teléfono" }, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await AgendarAsync(recepcion.Token, await MascotaAsync("Coco"), vet, horario)).Estado.ShouldBe(HttpStatusCode.Created);
        (await _api.PostAsync($"/api/citas/{id}/cancelar", new { motivo = "otra vez" }, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reprogramar_a_un_horario_libre_y_no_a_uno_ocupado()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await VetQuispeAsync();
        var ocupado = Horarios.Siguiente();
        await AgendarAsync(recepcion.Token, await MascotaAsync("Max"), vet, ocupado);
        var id = (await AgendarAsync(recepcion.Token, await MascotaAsync("Pepe"), vet, Horarios.Siguiente())).Data.GetProperty("id").GetInt32();

        var nuevo = Horarios.Siguiente();
        var r = await _api.PutAsync($"/api/citas/{id}", new { veterinarioId = vet, fechaHora = nuevo, motivo = "Control reprogramado" }, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("fechaHoraLima").GetString().ShouldBe(nuevo);

        (await _api.PutAsync($"/api/citas/{id}", new { veterinarioId = vet, fechaHora = ocupado, motivo = "x" }, recepcion.Token))
            .Estado.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Disponibilidad_marca_el_horario_ocupado_y_el_domingo_no_atiende()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await VetLopezAsync();
        var horario = Horarios.Siguiente();
        await AgendarAsync(recepcion.Token, await MascotaAsync("Luna"), vet, horario);

        var r = await _api.GetAsync($"/api/citas/disponibilidad?veterinarioId={vet}&fecha={Horarios.Fecha(horario)}", recepcion.Token);
        r.Data.GetProperty("atiende").GetBoolean().ShouldBeTrue();
        var horarios = r.Data.GetProperty("horarios").EnumerateArray().ToList();
        horarios.Count.ShouldBe(24);
        horarios.Single(h => h.GetProperty("fechaHoraLima").GetString() == horario).GetProperty("libre").GetBoolean().ShouldBeFalse();

        var domingo = await _api.GetAsync($"/api/citas/disponibilidad?veterinarioId={vet}&fecha={Horarios.ProximoDomingo()}", recepcion.Token);
        domingo.Data.GetProperty("atiende").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Veterinarios_activos_para_el_personal_y_no_para_clientes()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var r = await _api.GetAsync("/api/veterinarios", recepcion.Token);
        r.Data.EnumerateArray().Select(v => v.GetProperty("nombre").GetString()).ShouldContain("Dra. Carla López");
        (await _api.GetAsync("/api/veterinarios", (await _api.IniciarSesionAsync(Usuarios.Ana)).Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task No_se_cancela_una_cita_ya_atendida()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var max = await MascotaAsync("Max");
        var id = (await AgendarAsync(recepcion.Token, max, vet.UsuarioId, Horarios.Siguiente())).Data.GetProperty("id").GetInt32();
        (await _api.PostAsync($"/api/mascotas/{max}/historial",
            new { citaId = id, motivo = "Control", diagnostico = "Sano", tratamiento = "Ninguno" }, vet.Token)).Estado.ShouldBe(HttpStatusCode.Created);

        var r = await _api.PostAsync($"/api/citas/{id}/cancelar", new { motivo = "tarde" }, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.Conflict);
        r.Mensaje.ShouldBe("No se puede cancelar una cita ya atendida");
        (await _api.PutAsync($"/api/citas/{id}", new { veterinarioId = vet.UsuarioId, fechaHora = Horarios.Siguiente(), motivo = "x" }, recepcion.Token))
            .Estado.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task La_cancelacion_queda_auditada()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var id = (await AgendarAsync(recepcion.Token, await MascotaAsync("Coco"), await VetQuispeAsync(), Horarios.Siguiente())).Data.GetProperty("id").GetInt32();
        await _api.PostAsync($"/api/citas/{id}/cancelar", new { motivo = "Enfermedad del dueño" }, recepcion.Token);
        var detalle = await fabrica.EscalarAsync<string>(
            "SELECT detalle FROM auditoria WHERE recurso = 'cita' AND accion = 'CANCELAR' AND recurso_id = @id",
            ("@id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        JsonDocument.Parse(detalle!).RootElement.GetProperty("motivo").GetString().ShouldBe("Enfermedad del dueño");
    }
}
