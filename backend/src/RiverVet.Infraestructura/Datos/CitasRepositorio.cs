using RiverVet.Aplicacion.Citas;

namespace RiverVet.Infraestructura.Datos;

public sealed class CitasRepositorio(SesionBd sesion) : RepositorioBase(sesion), ICitasRepositorio
{
    private const string Consulta = """
        SELECT ci.id, ci.mascota_id AS mascotaId, m.nombre AS mascotaNombre, m.especie AS mascotaEspecie,
               c.id AS clienteId, CONCAT(c.nombres, N' ', c.apellidos) AS clienteNombre, c.telefono AS clienteTelefono,
               ci.veterinario_id AS veterinarioId, v.nombre AS veterinarioNombre,
               ci.fecha_hora AS fechaHora, ci.motivo, ci.estado, ci.motivo_cancelacion AS motivoCancelacion,
               ci.creado_en AS creadoEn
        FROM dbo.citas ci
        JOIN dbo.mascotas m ON m.id = ci.mascota_id AND m.eliminado = 0
        JOIN dbo.clientes c ON c.id = m.cliente_id AND c.eliminado = 0
        JOIN dbo.usuarios v ON v.id = ci.veterinario_id
        """;

    public Task<IReadOnlyList<CitaRespuesta>> ListarAsync(
        DateTime desdeUtc, DateTime hastaUtc, int? veterinarioId, int? mascotaId, string? estado, int? propietarioId, CancellationToken ct = default) =>
        ConsultarAsync<CitaRespuesta>(
            $"""
            {Consulta}
            WHERE ci.eliminado = 0
              AND ci.fecha_hora >= @desde AND ci.fecha_hora < @hasta
              AND (@veterinarioId IS NULL OR ci.veterinario_id = @veterinarioId)
              AND (@mascotaId IS NULL OR ci.mascota_id = @mascotaId)
              AND (@estado IS NULL OR ci.estado = @estado)
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            ORDER BY ci.fecha_hora, v.nombre
            """,
            new { desde = desdeUtc, hasta = hastaUtc, veterinarioId, mascotaId, estado = Ascii(estado, 15), propietarioId },
            ct);

    public Task<CitaRespuesta?> ObtenerAsync(int id, int? propietarioId, CancellationToken ct = default) =>
        ConsultarUnoAsync<CitaRespuesta>(
            $"""
            {Consulta}
            WHERE ci.id = @id AND ci.eliminado = 0
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            """,
            new { id, propietarioId },
            ct);

    public async Task<bool> VeterinarioActivoAsync(int veterinarioId, CancellationToken ct = default) =>
        await EscalarOpcionalAsync<int?>(
            "SELECT 1 FROM dbo.usuarios WHERE id = @id AND rol = N'veterinario' AND activo = 1 AND eliminado = 0",
            new { id = veterinarioId },
            ct) == 1;

    // Si otra petición ya reservó ese horario (aunque haya sido un milisegundo
    // antes), el índice único UX_citas_veterinario_horario rechaza este INSERT
    // con el error 2601 → RegistroDuplicadoException → 409.
    public Task<int> CrearAsync(int mascotaId, int veterinarioId, DateTime fechaHoraUtc, string motivo, int? creadoPor, CancellationToken ct = default) =>
        EscalarAsync<int>(
            """
            INSERT INTO dbo.citas (mascota_id, veterinario_id, fecha_hora, motivo, creado_por)
            OUTPUT INSERTED.id
            VALUES (@mascotaId, @veterinarioId, @fechaHora, @motivo, @creadoPor)
            """,
            new { mascotaId, veterinarioId, fechaHora = fechaHoraUtc, motivo = Unicode(motivo, 250), creadoPor },
            ct);

    public async Task<bool> ReprogramarAsync(int id, int veterinarioId, DateTime fechaHoraUtc, string motivo, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.citas
            SET veterinario_id = @veterinarioId, fecha_hora = @fechaHora, motivo = @motivo, actualizado_en = SYSUTCDATETIME()
            WHERE id = @id AND estado = 'programada' AND eliminado = 0
            """,
            new { id, veterinarioId, fechaHora = fechaHoraUtc, motivo = Unicode(motivo, 250) },
            ct) > 0;

    // El filtro de propiedad va DENTRO del UPDATE: aunque un cliente mande el
    // id de una cita ajena, no se modifica ninguna fila.
    public async Task<bool> CancelarAsync(int id, string motivo, int? canceladaPor, int? propietarioId, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE ci
            SET estado = 'cancelada', motivo_cancelacion = @motivo, cancelada_en = SYSUTCDATETIME(),
                cancelada_por = @canceladaPor, actualizado_en = SYSUTCDATETIME()
            FROM dbo.citas ci
            JOIN dbo.mascotas m ON m.id = ci.mascota_id
            JOIN dbo.clientes c ON c.id = m.cliente_id
            WHERE ci.id = @id AND ci.estado = 'programada' AND ci.eliminado = 0
              AND ci.fecha_hora > SYSUTCDATETIME()
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            """,
            new { id, motivo = Unicode(motivo, 250), canceladaPor, propietarioId },
            ct) > 0;

    public Task<IReadOnlyList<DateTime>> HorariosOcupadosAsync(int veterinarioId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
        ConsultarAsync<DateTime>(
            """
            SELECT fecha_hora FROM dbo.citas
            WHERE veterinario_id = @veterinarioId AND fecha_hora >= @desde AND fecha_hora < @hasta
              AND estado IN ('programada', 'atendida', 'no_asistio') AND eliminado = 0
            """,
            new { veterinarioId, desde = desdeUtc, hasta = hastaUtc },
            ct);

    // UPDLOCK: si dos veterinarios registran la atención de la misma cita a la
    // vez, el segundo espera y luego ve la cita ya atendida.
    public Task<CitaParaAtencion?> ObtenerParaAtencionAsync(int citaId, int mascotaId, CancellationToken ct = default) =>
        ConsultarUnoAsync<CitaParaAtencion>(
            """
            SELECT id, estado FROM dbo.citas WITH (UPDLOCK, ROWLOCK)
            WHERE id = @citaId AND mascota_id = @mascotaId AND eliminado = 0
            """,
            new { citaId, mascotaId },
            ct);

    public Task MarcarAtendidaAsync(int citaId, CancellationToken ct = default) =>
        EjecutarAsync(
            "UPDATE dbo.citas SET estado = 'atendida', actualizado_en = SYSUTCDATETIME() WHERE id = @citaId",
            new { citaId },
            ct);

    // Las condiciones van en el WHERE (no solo en el servicio): si dos personas
    // la cierran a la vez, una sola actualización gana.
    public async Task<bool> MarcarNoAsistioAsync(int citaId, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.citas SET estado = 'no_asistio', actualizado_en = SYSUTCDATETIME()
            WHERE id = @citaId AND estado = 'programada' AND eliminado = 0 AND fecha_hora <= SYSUTCDATETIME()
            """,
            new { citaId },
            ct) > 0;
}
