using RiverVet.Aplicacion.Historial;
using RiverVet.Aplicacion.Recordatorios;
using RiverVet.Aplicacion.Vacunas;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// Historial clínico. Se corrige con UPDATE (queda actualizado_en y la auditoría
/// guarda el antes y el después) y se elimina con borrado LÓGICO, nunca DELETE.
/// </summary>
public sealed class HistorialRepositorio(SesionBd sesion) : RepositorioBase(sesion), IHistorialRepositorio
{
    private const string Consulta = """
        SELECT h.id, h.mascota_id AS mascotaId, h.veterinario_id AS veterinarioId, v.nombre AS veterinarioNombre,
               h.cita_id AS citaId, h.fecha_atencion AS fechaAtencion, h.motivo, h.diagnostico, h.tratamiento,
               h.observaciones, h.peso_kg AS pesoKg, h.temperatura_c AS temperaturaC,
               h.creado_en AS creadoEn, h.actualizado_en AS actualizadoEn
        FROM dbo.historial_clinico h
        JOIN dbo.usuarios v ON v.id = h.veterinario_id
        """;

    // El sentido del orden también va parametrizado (CASE), sin concatenar SQL.
    public Task<IReadOnlyList<RegistroHistorial>> ListarAsync(int mascotaId, bool descendente, CancellationToken ct = default) =>
        ConsultarAsync<RegistroHistorial>(
            $"""
            {Consulta}
            WHERE h.mascota_id = @mascotaId AND h.eliminado = 0
            ORDER BY CASE WHEN @descendente = 1 THEN h.fecha_atencion END DESC,
                     CASE WHEN @descendente = 1 THEN h.id END DESC,
                     CASE WHEN @descendente = 0 THEN h.fecha_atencion END ASC,
                     CASE WHEN @descendente = 0 THEN h.id END ASC
            """,
            new { mascotaId, descendente },
            ct);

    public Task<RegistroHistorial?> ObtenerAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<RegistroHistorial>($"{Consulta} WHERE h.id = @id AND h.eliminado = 0", new { id }, ct);

    public Task<int> InsertarAsync(int mascotaId, int veterinarioId, RegistrarAtencionSolicitud datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        return EscalarAsync<int>(
            """
            INSERT INTO dbo.historial_clinico (mascota_id, veterinario_id, cita_id, motivo, diagnostico, tratamiento,
                                               observaciones, peso_kg, temperatura_c)
            OUTPUT INSERTED.id
            VALUES (@mascotaId, @veterinarioId, @citaId, @motivo, @diagnostico, @tratamiento,
                    @observaciones, @pesoKg, @temperaturaC)
            """,
            new
            {
                mascotaId,
                veterinarioId,
                citaId = datos.CitaId,
                motivo = Unicode(datos.Motivo, 250),
                diagnostico = Unicode(datos.Diagnostico, 1000),
                tratamiento = Unicode(datos.Tratamiento, 1000),
                observaciones = Unicode(datos.Observaciones, 2000),
                pesoKg = datos.PesoKg,
                temperaturaC = datos.TemperaturaC,
            },
            ct);
    }

    public async Task<bool> ActualizarAsync(int id, ActualizarAtencionSolicitud datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        return await EjecutarAsync(
            """
            UPDATE dbo.historial_clinico
            SET motivo = @motivo, diagnostico = @diagnostico, tratamiento = @tratamiento,
                observaciones = @observaciones, peso_kg = @pesoKg, temperatura_c = @temperaturaC,
                actualizado_en = SYSUTCDATETIME()
            WHERE id = @id AND eliminado = 0
            """,
            new
            {
                id,
                motivo = Unicode(datos.Motivo, 250),
                diagnostico = Unicode(datos.Diagnostico, 1000),
                tratamiento = Unicode(datos.Tratamiento, 1000),
                observaciones = Unicode(datos.Observaciones, 2000),
                pesoKg = datos.PesoKg,
                temperaturaC = datos.TemperaturaC,
            },
            ct) > 0;
    }

    public async Task<bool> EliminarAsync(int id, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.historial_clinico SET eliminado = 1, eliminado_en = SYSUTCDATETIME(), actualizado_en = SYSUTCDATETIME()
            WHERE id = @id AND eliminado = 0
            """,
            new { id },
            ct) > 0;
}

public sealed class VacunasRepositorio(SesionBd sesion) : RepositorioBase(sesion), IVacunasRepositorio
{
    private const string Consulta = """
        SELECT va.id, va.mascota_id AS mascotaId, va.veterinario_id AS veterinarioId, v.nombre AS veterinarioNombre,
               va.tipo_vacuna AS tipoVacuna, CONVERT(CHAR(10), va.fecha_aplicacion, 23) AS fechaAplicacion,
               va.lote, CONVERT(CHAR(10), va.proxima_dosis, 23) AS proximaDosis, va.observaciones,
               va.creado_en AS creadoEn
        FROM dbo.vacunas va
        JOIN dbo.usuarios v ON v.id = va.veterinario_id
        """;

    public Task<IReadOnlyList<RegistroVacuna>> ListarAsync(int mascotaId, CancellationToken ct = default) =>
        ConsultarAsync<RegistroVacuna>(
            $"{Consulta} WHERE va.mascota_id = @mascotaId AND va.eliminado = 0 ORDER BY va.fecha_aplicacion DESC, va.id DESC",
            new { mascotaId },
            ct);

    public Task<RegistroVacuna?> ObtenerAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<RegistroVacuna>($"{Consulta} WHERE va.id = @id AND va.eliminado = 0", new { id }, ct);

    // Las fechas viajan como texto 'AAAA-MM-DD' (ya validado) y SQL las
    // convierte con CAST: sin corrimientos de zona horaria.
    public Task<int> InsertarAsync(int mascotaId, int veterinarioId, RegistrarVacunaSolicitud datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        return EscalarAsync<int>(
            """
            INSERT INTO dbo.vacunas (mascota_id, veterinario_id, tipo_vacuna, fecha_aplicacion, lote, proxima_dosis, observaciones)
            OUTPUT INSERTED.id
            VALUES (@mascotaId, @veterinarioId, @tipo, CAST(@fechaAplicacion AS DATE), @lote,
                    CAST(@proximaDosis AS DATE), @observaciones)
            """,
            new
            {
                mascotaId,
                veterinarioId,
                tipo = Unicode(datos.TipoVacuna, 100),
                fechaAplicacion = Ascii(datos.FechaAplicacion, 10),
                lote = Unicode(datos.Lote, 50),
                proximaDosis = Ascii(datos.ProximaDosis, 10),
                observaciones = Unicode(datos.Observaciones, 500),
            },
            ct);
    }

    public async Task<bool> ActualizarAsync(int id, RegistrarVacunaSolicitud datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        return await EjecutarAsync(
            """
            UPDATE dbo.vacunas
            SET tipo_vacuna = @tipo, fecha_aplicacion = CAST(@fechaAplicacion AS DATE), lote = @lote,
                proxima_dosis = CAST(@proximaDosis AS DATE), observaciones = @observaciones
            WHERE id = @id AND eliminado = 0
            """,
            new
            {
                id,
                tipo = Unicode(datos.TipoVacuna, 100),
                fechaAplicacion = Ascii(datos.FechaAplicacion, 10),
                lote = Unicode(datos.Lote, 50),
                proximaDosis = Ascii(datos.ProximaDosis, 10),
                observaciones = Unicode(datos.Observaciones, 500),
            },
            ct) > 0;
    }

    public async Task<bool> EliminarAsync(int id, CancellationToken ct = default) =>
        await EjecutarAsync(
            "UPDATE dbo.vacunas SET eliminado = 1, eliminado_en = SYSUTCDATETIME() WHERE id = @id AND eliminado = 0",
            new { id },
            ct) > 0;
}

public sealed class RecordatoriosRepositorio(SesionBd sesion) : RepositorioBase(sesion), IRecordatoriosRepositorio
{
    public Task<IReadOnlyList<RecordatorioVacuna>> VacunasPorVencerAsync(DateOnly hoy, DateOnly limite, int? propietarioId, CancellationToken ct = default) =>
        ConsultarAsync<RecordatorioVacuna>(
            """
            SELECT va.id AS vacunaId, m.id AS mascotaId, m.nombre AS mascotaNombre, c.id AS clienteId,
                   CONCAT(c.nombres, N' ', c.apellidos) AS clienteNombre, c.telefono AS clienteTelefono,
                   va.tipo_vacuna AS tipoVacuna, CONVERT(CHAR(10), va.proxima_dosis, 23) AS proximaDosis,
                   DATEDIFF(DAY, CAST(@hoy AS DATE), va.proxima_dosis) AS diasRestantes
            FROM dbo.vacunas va
            JOIN dbo.mascotas m ON m.id = va.mascota_id AND m.eliminado = 0
            JOIN dbo.clientes c ON c.id = m.cliente_id AND c.eliminado = 0
            WHERE va.eliminado = 0
              AND va.proxima_dosis BETWEEN CAST(@hoy AS DATE) AND CAST(@limite AS DATE)
              -- Solo la última aplicación de ese tipo: si ya se puso una más
              -- nueva, el recordatorio de la anterior ya no aplica.
              AND NOT EXISTS (
                    SELECT 1 FROM dbo.vacunas posterior
                    WHERE posterior.mascota_id = va.mascota_id AND posterior.tipo_vacuna = va.tipo_vacuna
                      AND posterior.eliminado = 0 AND posterior.fecha_aplicacion > va.fecha_aplicacion)
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            ORDER BY va.proxima_dosis, m.nombre
            """,
            new { hoy = Ascii(Fecha(hoy), 10), limite = Ascii(Fecha(limite), 10), propietarioId },
            ct);

    public Task<IReadOnlyList<RecordatorioCita>> CitasProximasAsync(DateTime desdeUtc, DateTime hastaUtc, int? propietarioId, CancellationToken ct = default) =>
        ConsultarAsync<RecordatorioCita>(
            """
            SELECT ci.id AS citaId, m.id AS mascotaId, m.nombre AS mascotaNombre,
                   CONCAT(c.nombres, N' ', c.apellidos) AS clienteNombre, c.telefono AS clienteTelefono,
                   v.nombre AS veterinarioNombre, ci.fecha_hora AS fechaHora
            FROM dbo.citas ci
            JOIN dbo.mascotas m ON m.id = ci.mascota_id AND m.eliminado = 0
            JOIN dbo.clientes c ON c.id = m.cliente_id AND c.eliminado = 0
            JOIN dbo.usuarios v ON v.id = ci.veterinario_id
            WHERE ci.eliminado = 0 AND ci.estado = 'programada'
              AND ci.fecha_hora >= @desde AND ci.fecha_hora < @hasta
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            ORDER BY ci.fecha_hora
            """,
            new { desde = desdeUtc, hasta = hastaUtc, propietarioId },
            ct);

    private static string Fecha(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
