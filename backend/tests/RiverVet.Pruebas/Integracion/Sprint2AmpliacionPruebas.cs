using System.Net;
using RiverVet.Aplicacion.Comun;
using static RiverVet.Pruebas.Integracion.ClienteApi;

namespace RiverVet.Pruebas.Integracion;

/// <summary>
/// Ampliación del Sprint 2: citas por mascota y por veterinario, inasistencia,
/// detalle/corrección/eliminación de atenciones y vacunas (solo su autor) y
/// el tablero del personal (/api/dashboard).
/// </summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class Sprint2AmpliacionPruebas(FabricaApi fabrica)
{
    private const string OtroVeterinario = "vet.quispe@rivervet.pe";

    private readonly ClienteApi _api = fabrica.CrearClienteApi();

    private Task<int> MascotaAsync(string nombre) =>
        fabrica.EscalarAsync<int>("SELECT TOP 1 id FROM mascotas WHERE nombre = @n", ("@n", nombre))!;

    private static object Atencion(string diagnostico = "Paciente sano") => new
    {
        motivo = "Control",
        diagnostico,
        tratamiento = "Ninguno",
        receta = "Amoxicilina 250 mg cada 12 h por 7 días <b>con comida</b>",
        observaciones = "Sin novedades",
        pesoKg = 5.5m,
        temperaturaC = 38.4m,
    };

    private static object Vacuna(string lote = "LT-100") => new
    {
        tipoVacuna = "Vacuna de prueba Sprint 2", // unica: otra aplicacion del mismo tipo ocultaria el recordatorio
        fechaAplicacion = HoraLima.Hoy().AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        lote,
        proximaDosis = HoraLima.Hoy().AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        observaciones = "Sin reacción",
    };

    private static List<int> Ids(Respuesta r) => [.. r.Data.EnumerateArray().Select(c => c.GetProperty("id").GetInt32())];

    // ── Citas ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Citas_por_mascota_y_por_veterinario_respetando_al_dueno()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var rocky = await MascotaAsync("Rocky");
        var cita = await _api.PostAsync("/api/citas",
            new { mascotaId = rocky, veterinarioId = vet.UsuarioId, fechaHora = Horarios.Siguiente(), motivo = "Control de rutina" }, recepcion.Token);
        cita.Estado.ShouldBe(HttpStatusCode.Created);
        var citaId = cita.Data.GetProperty("id").GetInt32();

        Ids(await _api.GetAsync($"/api/citas/mascota/{rocky}", recepcion.Token)).ShouldContain(citaId);
        Ids(await _api.GetAsync($"/api/citas?mascotaId={rocky}", recepcion.Token)).ShouldContain(citaId);
        Ids(await _api.GetAsync($"/api/citas/veterinario/{vet.UsuarioId}", recepcion.Token)).ShouldContain(citaId);

        // Rocky es de Luis: él la ve; Ana (otra clienta) recibe una lista sin ella.
        var luis = await _api.IniciarSesionAsync(Usuarios.Luis);
        Ids(await _api.GetAsync($"/api/citas/mascota/{rocky}", luis.Token)).ShouldContain(citaId);
        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        Ids(await _api.GetAsync($"/api/citas/mascota/{rocky}", ana.Token)).ShouldNotContain(citaId);
    }

    [Fact]
    public async Task La_inasistencia_solo_se_registra_cuando_la_cita_ya_paso()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var cita = await _api.PostAsync("/api/citas",
            new { mascotaId = await MascotaAsync("Nala"), veterinarioId = vet.UsuarioId, fechaHora = Horarios.Siguiente(), motivo = "Baño medicado" }, recepcion.Token);
        var citaId = cita.Data.GetProperty("id").GetInt32();

        (await _api.PostAsync($"/api/citas/{citaId}/no-asistio", null, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Conflict);

        // Se lleva la cita al pasado (la API no deja agendar en el pasado), a un
        // bloque de :00 o :30 exactos: CK_citas_bloque rechaza cualquier otra hora.
        var ahora = DateTime.UtcNow;
        var bloquePasado = new DateTime(ahora.Year, ahora.Month, ahora.Day, ahora.Hour, ahora.Minute >= 30 ? 30 : 0, 0, DateTimeKind.Utc).AddHours(-2);
        await fabrica.EscalarAsync<int>(
            "UPDATE citas SET fecha_hora = @f WHERE id = @id; SELECT 1", ("@f", bloquePasado), ("@id", citaId));

        (await _api.PostAsync($"/api/citas/{citaId}/no-asistio", null, vet.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        var r = await _api.PostAsync($"/api/citas/{citaId}/no-asistio", null, recepcion.Token);
        r.Estado.ShouldBe(HttpStatusCode.OK);
        r.Data.GetProperty("estado").GetString().ShouldBe("no_asistio");
        (await _api.PostAsync($"/api/citas/{citaId}/no-asistio", null, recepcion.Token)).Estado.ShouldBe(HttpStatusCode.Conflict);
    }

    // ── Historial ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Solo_el_veterinario_autor_corrige_o_elimina_una_atencion_y_queda_auditado()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var otro = await _api.IniciarSesionAsync(OtroVeterinario);
        var luna = await MascotaAsync("Luna");
        var creada = await _api.PostAsync($"/api/mascotas/{luna}/historial", Atencion(), vet.Token);
        creada.Estado.ShouldBe(HttpStatusCode.Created);
        var id = creada.Data.GetProperty("id").GetInt32();
        creada.Data.GetProperty("editado").GetBoolean().ShouldBeFalse();
        // La receta se guarda sin HTML (XSS almacenado), igual que el resto del texto libre.
        creada.Data.GetProperty("receta").GetString().ShouldBe("Amoxicilina 250 mg cada 12 h por 7 días con comida");

        var detalle = await _api.GetAsync($"/api/mascotas/{luna}/historial/{id}", vet.Token);
        detalle.Estado.ShouldBe(HttpStatusCode.OK);
        detalle.Data.GetProperty("diagnostico").GetString().ShouldBe("Paciente sano");

        // Registrada "hace 5 minutos", para que la corrección se note en actualizado_en.
        await fabrica.EscalarAsync<int>(
            "UPDATE historial_clinico SET creado_en = DATEADD(MINUTE, -5, creado_en), actualizado_en = DATEADD(MINUTE, -5, actualizado_en) WHERE id = @id; SELECT 1",
            ("@id", id));

        (await _api.PutAsync($"/api/mascotas/{luna}/historial/{id}", Atencion("Otitis leve"), otro.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        var editada = await _api.PutAsync($"/api/mascotas/{luna}/historial/{id}", Atencion("Otitis leve"), vet.Token);
        editada.Estado.ShouldBe(HttpStatusCode.OK);
        editada.Data.GetProperty("diagnostico").GetString().ShouldBe("Otitis leve");
        editada.Data.GetProperty("editado").GetBoolean().ShouldBeTrue();
        (await fabrica.EscalarAsync<int>(
            "SELECT COUNT(*) FROM auditoria WHERE accion = 'ACTUALIZAR' AND recurso = 'historial' AND recurso_id = @id AND detalle LIKE '%Paciente sano%'",
            ("@id", id))).ShouldBe(1);

        (await _api.DeleteAsync($"/api/mascotas/{luna}/historial/{id}", otro.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.DeleteAsync($"/api/mascotas/{luna}/historial/{id}", vet.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.GetAsync($"/api/mascotas/{luna}/historial/{id}", vet.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        Ids(await _api.GetAsync($"/api/mascotas/{luna}/historial", vet.Token)).ShouldNotContain(id);
        (await fabrica.EscalarAsync<bool>("SELECT eliminado FROM historial_clinico WHERE id = @id", ("@id", id))).ShouldBeTrue(); // BIT
    }

    [Fact]
    public async Task La_atencion_de_otra_mascota_responde_404()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var creada = await _api.PostAsync($"/api/mascotas/{await MascotaAsync("Firulais")}/historial", Atencion(), vet.Token);
        var id = creada.Data.GetProperty("id").GetInt32();

        var ajena = await MascotaAsync("Rocky");
        (await _api.GetAsync($"/api/mascotas/{ajena}/historial/{id}", vet.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
        (await _api.PutAsync($"/api/mascotas/{ajena}/historial/{id}", Atencion("x"), vet.Token)).Estado.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Vacunas ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Solo_el_veterinario_autor_corrige_o_elimina_una_vacuna_y_deja_de_recordarse()
    {
        var vet = await _api.IniciarSesionAsync(Usuarios.Veterinario);
        var otro = await _api.IniciarSesionAsync(OtroVeterinario);
        var mishi = await MascotaAsync("Mishi");
        var creada = await _api.PostAsync($"/api/mascotas/{mishi}/vacunas", Vacuna(), vet.Token);
        creada.Estado.ShouldBe(HttpStatusCode.Created);
        var id = creada.Data.GetProperty("id").GetInt32();

        (await _api.GetAsync($"/api/mascotas/{mishi}/vacunas/{id}", vet.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.PutAsync($"/api/mascotas/{mishi}/vacunas/{id}", Vacuna("LT-200"), otro.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
        var editada = await _api.PutAsync($"/api/mascotas/{mishi}/vacunas/{id}", Vacuna("lt-200"), vet.Token);
        editada.Estado.ShouldBe(HttpStatusCode.OK);
        editada.Data.GetProperty("lote").GetString().ShouldBe("LT-200");

        var recordatorios = await _api.GetAsync("/api/recordatorios?dias=7", vet.Token);
        recordatorios.Data.GetProperty("vacunas").EnumerateArray().Select(v => v.GetProperty("vacunaId").GetInt32()).ShouldContain(id);

        (await _api.DeleteAsync($"/api/mascotas/{mishi}/vacunas/{id}", vet.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        Ids(await _api.GetAsync($"/api/mascotas/{mishi}/vacunas", vet.Token)).ShouldNotContain(id);
        recordatorios = await _api.GetAsync("/api/recordatorios?dias=7", vet.Token);
        recordatorios.Data.GetProperty("vacunas").EnumerateArray().Select(v => v.GetProperty("vacunaId").GetInt32()).ShouldNotContain(id);
    }

    // ── Tablero ───────────────────────────────────────────────────────────

    [Fact]
    public async Task El_tablero_es_del_personal_y_cuenta_las_citas_de_hoy()
    {
        var recepcion = await _api.IniciarSesionAsync(Usuarios.Recepcionista);
        var stats = await _api.GetAsync("/api/dashboard/stats", recepcion.Token);
        stats.Estado.ShouldBe(HttpStatusCode.OK);
        foreach (var campo in new[] { "citasHoy", "citasPendientes", "citasSinCerrar", "citasAtendidasMes", "clientesNuevosMes", "vacunasPorVencer", "vacunasUrgentes" })
        {
            stats.Data.GetProperty(campo).GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        }
        stats.Data.GetProperty("fecha").GetString().ShouldBe(HoraLima.Hoy().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        (await _api.GetAsync("/api/dashboard/citas-hoy", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.OK);
        (await _api.GetAsync("/api/dashboard/citas-semana", recepcion.Token)).Estado.ShouldBe(HttpStatusCode.OK);

        var ana = await _api.IniciarSesionAsync(Usuarios.Ana);
        (await _api.GetAsync("/api/dashboard/stats", ana.Token)).Estado.ShouldBe(HttpStatusCode.Forbidden);
    }
}
