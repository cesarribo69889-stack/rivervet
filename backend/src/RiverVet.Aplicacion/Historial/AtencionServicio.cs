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
/// El historial es de SOLO-AGREGAR: no hay endpoint para editar ni borrar una
/// atención. Un registro médico corregido se agrega como una nueva atención;
/// así queda el rastro completo de lo que se diagnosticó y cuándo.
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
}
