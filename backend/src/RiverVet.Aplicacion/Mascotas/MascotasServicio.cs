using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Clientes;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Mascotas;

/// <summary>HU-004 (registro) y HU-005 (consulta) de mascotas.</summary>
public sealed class MascotasServicio(
    IMascotasRepositorio mascotas,
    IClientesRepositorio clientes,
    IUnidadTrabajo unidadTrabajo,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    // 404 y no 403 cuando un cliente pide una mascota ajena: responder "no
    // tienes permiso" confirmaría que ese id existe.
    private const string NoEncontrada = "Mascota no encontrada";

    public async Task<ResultadoPaginado<MascotaRespuesta>> ListarAsync(
        PaginacionConsulta consulta, string? texto = null, CancellationToken ct = default) =>
        ResultadoPaginado.Crear(
            await mascotas.ListarAsync(consulta, texto, null, contexto.PropietarioId, ct), consulta);

    /// <summary>HU-005: mascotas de un cliente. Un cliente solo puede consultar SU propio id.</summary>
    public async Task<ResultadoPaginado<MascotaRespuesta>> ListarDeClienteAsync(
        int clienteId, PaginacionConsulta consulta, CancellationToken ct = default)
    {
        if (!await clientes.ExisteParaUsuarioAsync(clienteId, contexto.PropietarioId, ct))
        {
            throw new NoEncontradoException("Cliente no encontrado");
        }
        return ResultadoPaginado.Crear(
            await mascotas.ListarAsync(consulta, null, clienteId, contexto.PropietarioId, ct), consulta);
    }

    public async Task<MascotaRespuesta> ObtenerAsync(int id, CancellationToken ct = default) =>
        await mascotas.ObtenerPorIdAsync(id, contexto.PropietarioId, ct) ?? throw new NoEncontradoException(NoEncontrada);

    public async Task<MascotaRespuesta> CrearAsync(CrearMascotaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        int id;
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            id = await mascotas.CrearAsync(solicitud, contexto.UsuarioId, ct)
                ?? throw new ValidacionException("clienteId", "El cliente no existe");
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Crear, RecursosAuditoria.Mascota, id, new { clienteId = solicitud.ClienteId }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await mascotas.ObtenerPorIdAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
    }

    public async Task<MascotaRespuesta> ActualizarAsync(int id, ActualizarMascotaSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            if (!await mascotas.ActualizarAsync(id, solicitud, ct))
            {
                throw new NoEncontradoException(NoEncontrada);
            }
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar,
                RecursosAuditoria.Mascota,
                id,
                new { campos = new[] { "nombre", "especie", "raza", "sexo", "fechaNacimiento", "pesoKg", "color", "senasParticulares", "esterilizado" } },
                ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await mascotas.ObtenerPorIdAsync(id, null, ct) ?? throw new NoEncontradoException(NoEncontrada);
    }

    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        if (!await mascotas.EliminarAsync(id, ct))
        {
            throw new NoEncontradoException(NoEncontrada);
        }
        await auditoria.RegistrarAsync(AccionesAuditoria.Eliminar, RecursosAuditoria.Mascota, id, ct: ct);
        await tx.ConfirmarAsync(ct);
    }
}
