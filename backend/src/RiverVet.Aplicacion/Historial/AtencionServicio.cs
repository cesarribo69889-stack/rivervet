using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Citas;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Mascotas;
using RiverVet.Aplicacion.Vacunas;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Historial;

/// <summary>
/// Atención médica: historial clínico (HU-006, HU-007) y vacunas (HU-011).
///
/// Registran el veterinario y el administrador (el dueño de la clínica; así lo
/// piden HU-006 y HU-011). Corregir o eliminar un registro clínico lo puede
/// hacer el veterinario que lo firmó (otro veterinario no) o el administrador.
/// Y nunca se pierde el rastro: la corrección deja actualizado_en (la ficha
/// muestra "editado"), la eliminación es lógica, y la auditoría guarda el
/// contenido anterior y quién lo cambió en ambos casos.
/// </summary>
public sealed class AtencionServicio(
    IHistorialRepositorio historial,
    IVacunasRepositorio vacunas,
    IMascotasRepositorio mascotas,
    ICitasRepositorio citas,
    IUnidadTrabajo unidadTrabajo,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    private static readonly Dictionary<string, string> _mensajesUnicos = new()
    {
        ["UX_historial_cita"] = "Esta cita ya tiene una atención registrada",
    };

    /// <summary>
    /// Verifica que la mascota exista y, si quien consulta es un cliente, que
    /// sea SUYA (filtro de propiedad en el SQL). Ajena = 404.
    /// </summary>
    private async Task VerificarMascotaAsync(int mascotaId, CancellationToken ct)
    {
        if (await mascotas.ObtenerPorIdAsync(mascotaId, contexto.PropietarioId, ct) is null)
        {
            throw new NoEncontradoException("Mascota no encontrada");
        }
    }

    public async Task<IReadOnlyList<RegistroHistorial>> ListarHistorialAsync(int mascotaId, ConsultaHistorial consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        await VerificarMascotaAsync(mascotaId, ct);
        return await historial.ListarAsync(mascotaId, consulta.Orden == "desc", ct);
    }

    /// <summary>
    /// HU-006. Con citaId, en UNA transacción: se bloquea la cita, se
    /// verifica que sea de esta mascota y esté programada, se registra la
    /// atención y la cita pasa a "atendida". El índice único UX_historial_cita
    /// impide registrar dos atenciones para la misma cita.
    /// </summary>
    public async Task<RegistroHistorial> RegistrarAtencionAsync(int mascotaId, RegistrarAtencionSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await VerificarMascotaAsync(mascotaId, ct);
        var veterinarioId = contexto.UsuarioId ?? throw new NoAutenticadoException();

        int id;
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            if (solicitud.CitaId is int citaId)
            {
                var cita = await citas.ObtenerParaAtencionAsync(citaId, mascotaId, ct)
                    ?? throw new ValidacionException("citaId", "La cita no existe o no corresponde a esta mascota");
                if (cita.Estado != EstadosCita.Programada)
                {
                    throw new ConflictoException("La cita ya fue atendida o cancelada");
                }
            }

            id = await historial.InsertarAsync(mascotaId, veterinarioId, solicitud, ct);
            if (solicitud.CitaId is int atendida)
            {
                await citas.MarcarAtendidaAsync(atendida, ct);
            }
            if (solicitud.PesoKg is decimal peso)
            {
                // El peso de la última atención es el peso actual de la mascota.
                await mascotas.ActualizarPesoAsync(mascotaId, peso, ct);
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Crear, RecursosAuditoria.Historial, id,
                new { mascotaId, citaId = solicitud.CitaId }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await historial.ObtenerAsync(id, ct) ?? throw new NoEncontradoException("Atención no encontrada");
    }

    public async Task<RegistroHistorial> ObtenerAtencionAsync(int mascotaId, int id, CancellationToken ct = default)
    {
        await VerificarMascotaAsync(mascotaId, ct);
        return await AtencionDeLaMascotaAsync(mascotaId, id, ct);
    }

    public async Task<RegistroHistorial> ActualizarAtencionAsync(int mascotaId, int id, ActualizarAtencionSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await VerificarMascotaAsync(mascotaId, ct);
        var antes = await AtencionDeLaMascotaAsync(mascotaId, id, ct);
        VerificarAutor(antes.VeterinarioId, "la atención");

        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            if (!await historial.ActualizarAsync(id, solicitud, ct))
            {
                throw new NoEncontradoException("Atención no encontrada");
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar, RecursosAuditoria.Historial, id,
                new { mascotaId, antes = ContenidoClinico(antes), despues = ContenidoClinico(solicitud) }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await historial.ObtenerAsync(id, ct) ?? throw new NoEncontradoException("Atención no encontrada");
    }

    public async Task EliminarAtencionAsync(int mascotaId, int id, CancellationToken ct = default)
    {
        await VerificarMascotaAsync(mascotaId, ct);
        var registro = await AtencionDeLaMascotaAsync(mascotaId, id, ct);
        VerificarAutor(registro.VeterinarioId, "la atención");

        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        if (!await historial.EliminarAsync(id, ct))
        {
            throw new NoEncontradoException("Atención no encontrada");
        }
        await auditoria.RegistrarAsync(
            AccionesAuditoria.Eliminar, RecursosAuditoria.Historial, id,
            new { mascotaId, registro.CitaId, contenido = ContenidoClinico(registro) }, ct: ct);
        await tx.ConfirmarAsync(ct);
    }

    /// <summary>Una atención que exista y sea de ESTA mascota (la de otra mascota responde 404).</summary>
    private async Task<RegistroHistorial> AtencionDeLaMascotaAsync(int mascotaId, int id, CancellationToken ct)
    {
        var registro = await historial.ObtenerAsync(id, ct);
        return registro is not null && registro.MascotaId == mascotaId
            ? registro
            : throw new NoEncontradoException("Atención no encontrada");
    }

    private void VerificarAutor(int veterinarioId, string queCosa)
    {
        if (contexto.UsuarioId != veterinarioId && contexto.Rol != Roles.Admin)
        {
            throw new AccesoDenegadoException($"Solo quien registró {queCosa} o el administrador puede corregirla o eliminarla");
        }
    }

    private static object ContenidoClinico(RegistroHistorial r) =>
        new { r.Motivo, r.Diagnostico, r.Tratamiento, r.Receta, r.Observaciones, r.PesoKg, r.TemperaturaC };

    private static object ContenidoClinico(DatosAtencionSolicitud s) =>
        new { s.Motivo, s.Diagnostico, s.Tratamiento, s.Receta, s.Observaciones, s.PesoKg, s.TemperaturaC };

    public async Task<IReadOnlyList<RegistroVacuna>> ListarVacunasAsync(int mascotaId, CancellationToken ct = default)
    {
        await VerificarMascotaAsync(mascotaId, ct);
        return await vacunas.ListarAsync(mascotaId, ct);
    }

    /// <summary>HU-011: registrar una vacuna aplicada (con lote, por trazabilidad).</summary>
    public async Task<RegistroVacuna> RegistrarVacunaAsync(int mascotaId, RegistrarVacunaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await VerificarMascotaAsync(mascotaId, ct);
        var veterinarioId = contexto.UsuarioId ?? throw new NoAutenticadoException();

        int id;
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            id = await vacunas.InsertarAsync(mascotaId, veterinarioId, solicitud, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Crear, RecursosAuditoria.Vacuna, id,
                new { mascotaId, tipo = solicitud.TipoVacuna, lote = solicitud.Lote }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await vacunas.ObtenerAsync(id, ct) ?? throw new NoEncontradoException("Vacuna no encontrada");
    }

    public async Task<RegistroVacuna> ObtenerVacunaAsync(int mascotaId, int id, CancellationToken ct = default)
    {
        await VerificarMascotaAsync(mascotaId, ct);
        return await VacunaDeLaMascotaAsync(mascotaId, id, ct);
    }

    public async Task<RegistroVacuna> ActualizarVacunaAsync(int mascotaId, int id, RegistrarVacunaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await VerificarMascotaAsync(mascotaId, ct);
        var antes = await VacunaDeLaMascotaAsync(mascotaId, id, ct);
        VerificarAutor(antes.VeterinarioId, "la vacuna");

        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            if (!await vacunas.ActualizarAsync(id, solicitud, ct))
            {
                throw new NoEncontradoException("Vacuna no encontrada");
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar, RecursosAuditoria.Vacuna, id,
                new
                {
                    mascotaId,
                    antes = new { antes.TipoVacuna, antes.FechaAplicacion, antes.Lote, antes.ProximaDosis },
                    despues = new { solicitud.TipoVacuna, solicitud.FechaAplicacion, solicitud.Lote, solicitud.ProximaDosis },
                },
                ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await vacunas.ObtenerAsync(id, ct) ?? throw new NoEncontradoException("Vacuna no encontrada");
    }

    public async Task EliminarVacunaAsync(int mascotaId, int id, CancellationToken ct = default)
    {
        await VerificarMascotaAsync(mascotaId, ct);
        var registro = await VacunaDeLaMascotaAsync(mascotaId, id, ct);
        VerificarAutor(registro.VeterinarioId, "la vacuna");

        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        if (!await vacunas.EliminarAsync(id, ct))
        {
            throw new NoEncontradoException("Vacuna no encontrada");
        }
        await auditoria.RegistrarAsync(
            AccionesAuditoria.Eliminar, RecursosAuditoria.Vacuna, id,
            new { mascotaId, registro.TipoVacuna, registro.FechaAplicacion, registro.Lote }, ct: ct);
        await tx.ConfirmarAsync(ct);
    }

    private async Task<RegistroVacuna> VacunaDeLaMascotaAsync(int mascotaId, int id, CancellationToken ct)
    {
        var registro = await vacunas.ObtenerAsync(id, ct);
        return registro is not null && registro.MascotaId == mascotaId
            ? registro
            : throw new NoEncontradoException("Vacuna no encontrada");
    }
}
