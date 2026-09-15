using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Autenticacion;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Usuarios;

/// <summary>HU-001: alta y gestión del personal por el admin.</summary>
public sealed class UsuariosServicio(
    IUsuariosRepositorio usuarios,
    IRefreshTokensRepositorio refreshTokens,
    IUnidadTrabajo unidadTrabajo,
    IHasheadorPassword hasheador,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    private static readonly Dictionary<string, string> _mensajesUnicos = new()
    {
        ["UX_usuarios_email"] = "Ya existe un usuario con ese email",
    };

    public async Task<ResultadoPaginado<UsuarioRespuesta>> ListarAsync(ListarUsuariosConsulta consulta, CancellationToken ct = default) =>
        ResultadoPaginado.Crear(await usuarios.ListarAsync(consulta, ct), consulta);

    public Task<IReadOnlyList<VeterinarioResumen>> ListarVeterinariosAsync(CancellationToken ct = default) =>
        usuarios.ListarVeterinariosAsync(ct);

    public async Task<UsuarioRespuesta> ObtenerAsync(int id, CancellationToken ct = default) =>
        await usuarios.ObtenerPorIdAsync(id, ct) ?? throw new NoEncontradoException("Usuario no encontrado");

    public async Task<UsuarioRespuesta> CrearAsync(CrearUsuarioSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var hash = hasheador.Hashear(solicitud.Password!);
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            var usuario = await usuarios.CrearAsync(solicitud.Nombre!, solicitud.Email!, hash, solicitud.Rol!, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Crear, RecursosAuditoria.Usuario, usuario.Id, new { rol = usuario.Rol }, ct: ct);
            await tx.ConfirmarAsync(ct);
            return usuario;
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
    }

    public async Task<UsuarioRespuesta> ActualizarAsync(int id, ActualizarUsuarioSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var activo = solicitud.Activo!.Value;

        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        var actual = await usuarios.ObtenerPorIdAsync(id, ct) ?? throw new NoEncontradoException("Usuario no encontrado");

        // Un admin no puede quitarse su propio rol ni desactivarse: dejaría el
        // sistema sin nadie capaz de administrarlo.
        if (id == contexto.UsuarioId && (solicitud.Rol != actual.Rol || !activo))
        {
            throw new ConflictoException("No puedes cambiar tu propio rol ni desactivar tu cuenta");
        }
        // El rol 'cliente' no se asigna ni se quita desde aquí: esas cuentas
        // están atadas a una ficha de cliente (base del control de propiedad).
        if (solicitud.Rol != actual.Rol && (solicitud.Rol == Roles.Cliente || actual.Rol == Roles.Cliente))
        {
            throw new ValidacionException("rol", "El rol cliente no se puede asignar ni quitar desde usuarios");
        }

        var usuario = await usuarios.ActualizarAsync(id, solicitud.Nombre!, solicitud.Rol!, activo, ct)
            ?? throw new NoEncontradoException("Usuario no encontrado");

        if (actual.Activo && !activo)
        {
            // Al desactivar se cierran todas sus sesiones de inmediato.
            await refreshTokens.RevocarTodosDelUsuarioAsync(id, MotivosRevocacion.UsuarioDesactivado, ct);
        }

        await auditoria.RegistrarAsync(
            AccionesAuditoria.Actualizar,
            RecursosAuditoria.Usuario,
            id,
            new
            {
                antes = new { nombre = actual.Nombre, rol = actual.Rol, activo = actual.Activo },
                despues = new { nombre = usuario.Nombre, rol = usuario.Rol, activo = usuario.Activo },
            },
            ct: ct);
        await tx.ConfirmarAsync(ct);
        return usuario;
    }
}
