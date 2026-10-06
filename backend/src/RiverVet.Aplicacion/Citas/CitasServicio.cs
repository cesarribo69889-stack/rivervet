using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Mascotas;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Citas;

/// <summary>HU-008 (agendar), HU-009 (reprogramar/cancelar) y HU-010 (calendario).</summary>
public sealed class CitasServicio(
    ICitasRepositorio citas,
    IMascotasRepositorio mascotas,
    IUnidadTrabajo unidadTrabajo,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    private static readonly Dictionary<string, string> _mensajesUnicos = new()
    {
        ["UX_citas_veterinario_horario"] = "El veterinario ya tiene una cita en ese horario. Elige otro.",
        ["UX_citas_mascota_horario"] = "La mascota ya tiene otra cita en ese horario.",
    };

    private const string NoEncontrada = "Cita no encontrada";

    /// <summary>
    /// Calendario. Sin fechas: desde hoy y 30 días hacia adelante. Un cliente
    /// solo recibe las citas de SUS mascotas (filtro en el SQL).
    ///
    /// Con mascotaId y sin fechas se devuelve su historia: el último año y
    /// todo lo agendado (hasta el máximo de anticipación).
    /// </summary>
    public Task<IReadOnlyList<CitaRespuesta>> ListarAsync(ConsultaCitas consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var hoy = HoraLima.Hoy();
        var porMascota = consulta.MascotaId is not null && consulta.Desde is null && consulta.Hasta is null;
        var desde = HoraLima.LeerFecha(consulta.Desde) ?? (porMascota ? hoy.AddDays(-365) : hoy);
        var hasta = HoraLima.LeerFecha(consulta.Hasta) ?? (porMascota ? hoy.AddDays(HorarioAtencion.DiasMaximosAnticipacion) : desde.AddDays(30));
        return citas.ListarAsync(
            HoraLima.InicioDelDiaUtc(desde),
            HoraLima.InicioDelDiaUtc(hasta.AddDays(1)),
            consulta.VeterinarioId,
            consulta.MascotaId,
            consulta.Estado,
            contexto.PropietarioId,
            ct);
    }

    /// <summary>
    /// Cierra como inasistencia una cita programada cuya hora ya pasó. Junto
    /// con "atendida" (que se marca al registrar la atención), es la otra
    /// forma de cerrar una cita: así el calendario no arrastra citas abiertas.
    /// </summary>
    public async Task<CitaRespuesta> MarcarNoAsistioAsync(int id, CancellationToken ct = default)
    {
        var actual = await citas.ObtenerAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
        if (actual.Estado != EstadosCita.Programada)
        {
            throw new ConflictoException("Solo se registra la inasistencia de una cita programada");
        }
        if (actual.FechaHora > HoraLima.AhoraUtc())
        {
            throw new ConflictoException("La cita aún no llega: la inasistencia se registra después de su hora");
        }

        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            if (!await citas.MarcarNoAsistioAsync(id, ct))
            {
                throw new ConflictoException("La cita cambió de estado. Vuelve a cargarla.");
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar, RecursosAuditoria.Cita, id, new { estado = EstadosCita.NoAsistio }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await citas.ObtenerAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
    }

    public async Task<CitaRespuesta> ObtenerAsync(int id, CancellationToken ct = default) =>
        await citas.ObtenerAsync(id, contexto.PropietarioId, ct) ?? throw new NoEncontradoException(NoEncontrada);

    /// <summary>Bloques de 30 minutos de un día para un veterinario, marcando los ocupados.</summary>
    public async Task<DisponibilidadRespuesta> DisponibilidadAsync(ConsultaDisponibilidad consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var fecha = HoraLima.LeerFecha(consulta.Fecha)!.Value;
        var textoFecha = fecha.ToString(HoraLima.FormatoFecha, System.Globalization.CultureInfo.InvariantCulture);
        if (!HorarioAtencion.AtiendeEl(fecha.DayOfWeek))
        {
            return new DisponibilidadRespuesta(textoFecha, false, []);
        }

        var ocupados = (await citas.HorariosOcupadosAsync(
                consulta.VeterinarioId!.Value, HoraLima.InicioDelDiaUtc(fecha), HoraLima.InicioDelDiaUtc(fecha.AddDays(1)), ct))
            .Select(HoraLima.DeUtc)
            .ToHashSet();
        var ahora = HoraLima.Ahora();

        var horarios = new List<HorarioDisponible>();
        for (var hora = HorarioAtencion.Apertura; hora < HorarioAtencion.Cierre; hora = hora.AddMinutes(HorarioAtencion.MinutosBloque))
        {
            var inicio = fecha.ToDateTime(hora);
            horarios.Add(new HorarioDisponible(
                hora.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                HoraLima.Formatear(inicio),
                Libre: inicio > ahora && !ocupados.Contains(inicio)));
        }
        return new DisponibilidadRespuesta(textoFecha, true, horarios);
    }

    /// <summary>
    /// HU-008. ★ El doble agendamiento lo impide la BASE DE DATOS (índice
    /// único UX_citas_veterinario_horario), no un "SELECT para ver si está
    /// libre": entre ese SELECT y el INSERT otra petición podría colarse. Si
    /// dos recepcionistas reservan el mismo horario a la vez, una gana y la
    /// otra recibe 409.
    /// </summary>
    public async Task<CitaRespuesta> AgendarAsync(AgendarCitaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await ValidarVeterinarioAsync(solicitud.VeterinarioId!.Value, ct);
        if (await mascotas.ObtenerPorIdAsync(solicitud.MascotaId!.Value, null, ct) is null)
        {
            throw new ValidacionException("mascotaId", "La mascota no existe");
        }

        var fechaUtc = HoraLima.AUtc(HoraLima.LeerFechaHora(solicitud.FechaHora)!.Value);
        int id;
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            id = await citas.CrearAsync(solicitud.MascotaId.Value, solicitud.VeterinarioId.Value, fechaUtc, solicitud.Motivo!, contexto.UsuarioId, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Crear, RecursosAuditoria.Cita, id,
                new { mascotaId = solicitud.MascotaId, veterinarioId = solicitud.VeterinarioId, fechaHora = solicitud.FechaHora },
                ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await citas.ObtenerAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
    }

    /// <summary>HU-009: solo se reprograman citas PROGRAMADAS, nunca al pasado.</summary>
    public async Task<CitaRespuesta> ReprogramarAsync(int id, ReprogramarCitaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var actual = await citas.ObtenerAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
        if (actual.Estado != EstadosCita.Programada)
        {
            throw new ConflictoException("Solo se pueden modificar citas programadas");
        }
        await ValidarVeterinarioAsync(solicitud.VeterinarioId!.Value, ct);

        var fechaUtc = HoraLima.AUtc(HoraLima.LeerFechaHora(solicitud.FechaHora)!.Value);
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            if (!await citas.ReprogramarAsync(id, solicitud.VeterinarioId.Value, fechaUtc, solicitud.Motivo!, ct))
            {
                throw new ConflictoException("La cita cambió de estado mientras la editabas. Vuelve a cargarla.");
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar, RecursosAuditoria.Cita, id,
                new { antes = new { actual.VeterinarioId, fechaHora = actual.FechaHoraLima }, despues = new { solicitud.VeterinarioId, solicitud.FechaHora } },
                ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await citas.ObtenerAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
    }

    /// <summary>
    /// HU-009: cancelar. No se cancela una cita atendida ni una que ya pasó.
    /// Un cliente puede cancelar SOLO las citas de sus mascotas: el UPDATE
    /// lleva el filtro de propiedad, y una ajena responde 404.
    /// </summary>
    public async Task<CitaRespuesta> CancelarAsync(int id, CancelarCitaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var actual = await citas.ObtenerAsync(id, contexto.PropietarioId, ct) ?? throw new NoEncontradoException(NoEncontrada);
        switch (actual.Estado)
        {
            case EstadosCita.Atendida:
                throw new ConflictoException("No se puede cancelar una cita ya atendida");
            case EstadosCita.Cancelada:
                throw new ConflictoException("La cita ya está cancelada");
            case EstadosCita.NoAsistio:
                throw new ConflictoException("La cita ya se cerró como inasistencia");
        }
        if (actual.FechaHora <= HoraLima.AhoraUtc())
        {
            throw new ConflictoException("La cita ya pasó: no se puede cancelar");
        }

        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            if (!await citas.CancelarAsync(id, solicitud.Motivo!, contexto.UsuarioId, contexto.PropietarioId, ct))
            {
                throw new ConflictoException("La cita cambió de estado. Vuelve a cargarla.");
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Cancelar, RecursosAuditoria.Cita, id, new { motivo = solicitud.Motivo }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await citas.ObtenerAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
    }

    private async Task ValidarVeterinarioAsync(int veterinarioId, CancellationToken ct)
    {
        if (!await citas.VeterinarioActivoAsync(veterinarioId, ct))
        {
            throw new ValidacionException("veterinarioId", "El veterinario no existe o no está activo");
        }
    }
}
