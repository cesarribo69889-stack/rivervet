using System.Globalization;
using System.Net;
using RiverVet.Aplicacion.Comun;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>HU-006, HU-007 (historial), HU-011 (vacunas) y HU-012 (recordatorios).</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class AtencionPruebas(FabricaApi fabrica)
{
    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private Task<int> MascotaAsync(string nombre) =>
        fabrica.EscalarAsync<int>("SELECT TOP 1 id FROM mascotas WHERE nombre = @n", ("@n", nombre))!;

    private static string Dia(int desplazamiento) =>
        HoraLima.Hoy().AddDays(desplazamiento).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static object Atencion(int? citaId = null, string observaciones = "Sin novedades", decimal? peso = null, decimal? temperatura = 38.5m) => new
    {
        citaId,
        motivo = "Control anual",
        diagnostico = "Paciente sano",
        tratamiento = "Desparasitación",
        observaciones,
        pesoKg = peso,
        temperaturaC = temperatura,
    };

    [Fact]
    public async Task El_veterinario_registra_una_atencion_se_limpia_el_HTML_y_se_actualiza_el_peso()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var luna = await MascotaAsync("Luna");
        var r = await _api.PostAsync($"/api/mascotas/{luna}/historial",
            Atencion(observaciones: "Come bien <script>alert('xss')</script><img src=x onerror=alert(1)>", peso: 6.75m), vet.Token);

        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("observaciones").GetString().ShouldBe("Come bien alert('xss')");
        r.Data.GetProperty("veterinarioNombre").GetString().ShouldBe("Dra. Carla López");
        (await fabrica.EscalarAsync<decimal>("SELECT peso_kg FROM mascotas WHERE id = @id", ("@id", luna))).ShouldBe(6.75m);
    }

    [Fact]
    public async Task Atender_una_cita_la_marca_como_atendida_y_no_se_atiende_dos_veces()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var pepe = await MascotaAsync("Pepe");
        var cita = await _api.PostAsync("/api/citas",
            new { mascotaId = pepe, veterinarioId = vet.UsuarioId, fechaHora = Horarios.Siguiente(), motivo = "Revisión de plumas" }, recepcion.Token);
        var citaId = cita.Data.GetProperty("id").GetInt32();

        (await _api.PostAsync($"/api/mascotas/{pepe}/historial", Atencion(citaId), vet.Token)).Estado.ShouldBe(HttpStatusCode.Created);
        (await fabrica.EscalarAsync<string>("SELECT estado FROM citas WHERE id = @id", ("@id", citaId))).ShouldBe("atendida");

        var otra = await _api.PostAsync($"/api/mascotas/{pepe}/historial", Atencion(citaId), vet.Token);
        otra.Estado.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task La_cita_debe_ser_de_la_misma_mascota()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var cita = await _api.PostAsync("/api/citas",
            new { mascotaId = await MascotaAsync("Rocky"), veterinarioId = vet.UsuarioId, fechaHora = Horarios.Siguiente(), motivo = "Vacunas" }, recepcion.Token);
        var r = await _api.PostAsync($"/api/mascotas/{await MascotaAsync("Nala")}/historial", Atencion(cita.Data.GetProperty("id").GetInt32()), vet.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldBe(["citaId"]);
    }

    [Fact]
    public async Task RBAC_registran_atenciones_y_vacunas_el_veterinario_y_el_admin()
    {
        var mishi = await MascotaAsync("Mishi");
        foreach (var email in new[] { Usuarios.Recepcionista, Usuarios.Ana })
        {
            var sesion = await _api.IniciarSesionAsync(email);
            (await _api.PostAsync($"/api/mascotas/{mishi}/historial", Atencion(), sesion.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
            (await _api.PostAsync($"/api/mascotas/{mishi}/vacunas", new { tipoVacuna = "Triple felina", fechaAplicacion = Dia(0), lote = "L1" }, sesion.Token))
                .Estado.ShouldBe(HttpStatusCode.Forbidden);
        }

        // El admin es el dueño de la clínica: también registra (HU-006 y HU-011),
        // y queda como autor. Se eliminan al final para no tocar otras pruebas.
        var admin = await _api.IniciarSesionAsync(Usuarios.Admin);
        var atencion = await _api.PostAsync($"/api/mascotas/{mishi}/historial", Atencion(), admin.Token);
        atencion.Estado.ShouldBe(HttpStatusCode.Created);
        atencion.Data.GetProperty("veterinarioId").GetInt32().ShouldBe(admin.UsuarioId);
        var vacuna = await _api.PostAsync($"/api/mascotas/{mishi}/vacunas", new { tipoVacuna = "Vacuna de prueba del admin", fechaAplicacion = Dia(0), lote = "L1" }, admin.Token);
        vacuna.Estado.ShouldBe(HttpStatusCode.Created);
        (await _api.DeleteAsync($"/api/mascotas/{mishi}/historial/{atencion.Data.GetProperty("id").GetInt32()}", admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.DeleteAsync($"/api/mascotas/{mishi}/vacunas/{vacuna.Data.GetProperty("id").GetInt32()}", admin.Token)).Estado.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Historial_en_orden_cronologico_y_al_reves_con_orden_desc()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var coco = await MascotaAsync("Coco");
        await _api.PostAsync($"/api/mascotas/{coco}/historial", Atencion(observaciones: "primera"), vet.Token);
        await _api.PostAsync($"/api/mascotas/{coco}/historial", Atencion(observaciones: "segunda"), vet.Token);

        var asc = (await _api.GetAsync($"/api/mascotas/{coco}/historial", vet.Token)).Data.EnumerateArray()
            .Select(h => h.GetProperty("id").GetInt32()).ToList();
        asc.ShouldBe(asc.Order().ToList());
        var desc = (await _api.GetAsync($"/api/mascotas/{coco}/historial?orden=desc", vet.Token)).Data.EnumerateArray()
            .Select(h => h.GetProperty("id").GetInt32()).ToList();
        desc.ShouldBe(asc.AsEnumerable().Reverse().ToList());
        (await _api.GetAsync($"/api/mascotas/{coco}/historial?orden=raro", vet.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task IDOR_el_cliente_ve_el_historial_y_vacunas_de_su_mascota_pero_no_de_otra()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var firulais = await MascotaAsync("Firulais");
        var rocky = await MascotaAsync("Rocky");
        await _api.PostAsync($"/api/mascotas/{firulais}/historial", Atencion(), vet.Token);

        (await _api.GetAsync($"/api/mascotas/{firulais}/historial", ana.Token)).Data.GetArrayLength().ShouldBeGreaterThan(0);
        (await _api.GetAsync($"/api/mascotas/{rocky}/historial", ana.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.GetAsync($"/api/mascotas/{rocky}/vacunas", ana.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.GetAsync($"/api/mascotas/{firulais}/vacunas", ana.Token)).Estado.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Validaciones_de_la_atencion()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var r = await _api.PostAsync($"/api/mascotas/{await MascotaAsync("Max")}/historial",
            new { motivo = "Control", diagnostico = "", tratamiento = "Reposo", temperaturaC = 50.0 }, vet.Token);
        r.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        r.CamposConError.ShouldContain("diagnostico");
        r.CamposConError.ShouldContain("temperaturaC");
        (await _api.PostAsync("/api/mascotas/999999/historial", Atencion(), vet.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Registra_una_vacuna_con_lote_en_mayusculas_y_valida_fechas()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var max = await MascotaAsync("Max");
        var r = await _api.PostAsync($"/api/mascotas/{max}/vacunas",
            new { tipoVacuna = "Antirrábica", fechaAplicacion = Dia(0), lote = "ab-123", proximaDosis = Dia(365) }, vet.Token);
        r.Estado.ShouldBe(HttpStatusCode.Created);
        r.Data.GetProperty("lote").GetString().ShouldBe("AB-123");
        r.Data.GetProperty("proximaDosis").GetString().ShouldBe(Dia(365));

        var mala = await _api.PostAsync($"/api/mascotas/{max}/vacunas",
            new { tipoVacuna = "Antirrábica", fechaAplicacion = Dia(0), lote = "L1", proximaDosis = Dia(-1) }, vet.Token);
        mala.Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
        mala.CamposConError.ShouldBe(["proximaDosis"]);
    }

    [Fact]
    public async Task Recordatorios_de_vacunas_por_vencer_solo_la_ultima_y_filtrados_por_propietario()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        var mishi = await MascotaAsync("Mishi");
        var tipo = $"Triple felina {Guid.NewGuid():N}"[..24];

        // Una aplicación vieja cuya próxima dosis vence en 3 días...
        await _api.PostAsync($"/api/mascotas/{mishi}/vacunas", new { tipoVacuna = tipo, fechaAplicacion = Dia(-360), lote = "V1", proximaDosis = Dia(3) }, vet.Token);
        var personal = await _api.GetAsync("/api/recordatorios?dias=7", recepcion.Token);
        var vacunas = personal.Data.GetProperty("vacunas").EnumerateArray().Where(v => v.GetProperty("tipoVacuna").GetString() == tipo).ToList();
        vacunas.Count.ShouldBe(1);
        vacunas[0].GetProperty("diasRestantes").GetInt32().ShouldBe(3);
        vacunas[0].GetProperty("clienteTelefono").GetString().ShouldBe("987654321");

        (await _api.GetAsync("/api/recordatorios?dias=7", ana.Token)).Data.GetProperty("vacunas").EnumerateArray()
            .ShouldContain(v => v.GetProperty("tipoVacuna").GetString() == tipo);
        (await _api.GetAsync("/api/recordatorios?dias=7", luis.Token)).Data.GetProperty("vacunas").EnumerateArray()
            .ShouldNotContain(v => v.GetProperty("tipoVacuna").GetString() == tipo);

        // ...si hoy se aplica la dosis nueva, el recordatorio viejo desaparece.
        await _api.PostAsync($"/api/mascotas/{mishi}/vacunas", new { tipoVacuna = tipo, fechaAplicacion = Dia(0), lote = "V2", proximaDosis = Dia(360) }, vet.Token);
        (await _api.GetAsync("/api/recordatorios?dias=7", recepcion.Token)).Data.GetProperty("vacunas").EnumerateArray()
            .ShouldNotContain(v => v.GetProperty("tipoVacuna").GetString() == tipo);

        (await _api.GetAsync("/api/recordatorios?dias=100", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Recordatorios_incluyen_las_citas_programadas_proximas()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var cita = await _api.PostAsync("/api/citas",
            new { mascotaId = await MascotaAsync("Nala"), veterinarioId = vet.UsuarioId, fechaHora = Horarios.Siguiente(), motivo = "Control" }, recepcion.Token);
        var id = cita.Data.GetProperty("id").GetInt32();
        var r = await _api.GetAsync("/api/recordatorios?dias=60", recepcion.Token);
        r.Data.GetProperty("citas").EnumerateArray().ShouldContain(c => c.GetProperty("citaId").GetInt32() == id);
    }
}
