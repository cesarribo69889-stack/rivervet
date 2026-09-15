using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Autenticacion;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Usuarios;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Clientes;

/// <summary>HU-002 (registro) y HU-003 (consulta y actualización) de clientes.</summary>
public sealed class ClientesServicio(
    IClientesRepositorio clientes,
    IUsuariosRepositorio usuarios,
    IRefreshTokensRepositorio refreshTokens,
    IUnidadTrabajo unidadTrabajo,
    IHasheadorPassword hasheador,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    private static readonly Dictionary<string, string> _mensajesUnicos = new()
    {
        ["UX_clientes_dni"] = "Ya existe un cliente con ese DNI",
        ["UX_clientes_email"] = "Ya existe un cliente con ese email",
        ["UX_usuarios_email"] = "Ya existe una cuenta de usuario con ese email",
    };

    public async Task<ResultadoPaginado<ClienteListado>> ListarAsync(
        PaginacionConsulta consulta, string? texto = null, CancellationToken ct = default) =>
        ResultadoPaginado.Crear(await clientes.ListarAsync(consulta, texto, ct), consulta);

    public async Task<ClienteRespuesta> ObtenerAsync(int id, CancellationToken ct = default) =>
        await clientes.ObtenerPorIdAsync(id, ct) ?? throw new NoEncontradoException("Cliente no encontrado");

    public async Task<ClienteRespuesta> ObtenerPropioAsync(int usuarioId, CancellationToken ct = default) =>
        await clientes.ObtenerPorUsuarioAsync(usuarioId, ct)
        ?? throw new NoEncontradoException("No hay una ficha de cliente asociada a tu cuenta");

    /// <summary>
    /// Crea el cliente y, si se pide, su cuenta web, en UNA transacción: nunca
    /// queda una cuenta sin ficha ni una ficha a medio crear.
    /// </summary>
    public async Task<ClienteRespuesta> CrearAsync(CrearClienteSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var crearCuenta = solicitud.CrearCuenta == true;
        var hash = crearCuenta ? hasheador.Hashear(solicitud.Password!) : null;

        int id;
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            int? usuarioId = null;
            if (crearCuenta)
            {
                var nombre = Recortar100($"{solicitud.Nombres} {solicitud.Apellidos}");
                var usuario = await usuarios.CrearAsync(nombre, solicitud.Email!, hash!, Roles.Cliente, ct);
                usuarioId = usuario.Id;
            }
            id = await clientes.CrearAsync(solicitud, usuarioId, contexto.UsuarioId, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Crear, RecursosAuditoria.Cliente, id, new { conCuenta = crearCuenta }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await ObtenerAsync(id, ct);
    }

    public async Task<ClienteRespuesta> ActualizarAsync(int id, ActualizarClienteSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            var actual = await clientes.ObtenerPorIdAsync(id, ct) ?? throw new NoEncontradoException("Cliente no encontrado");

            if (actual.UsuarioId is not null && solicitud.Email is null)
            {
                throw new ValidacionException("email", "El cliente tiene cuenta web: el email es obligatorio");
            }

            await clientes.ActualizarAsync(id, solicitud, ct);
            // Si tiene cuenta web, su email de inicio de sesión se mantiene
            // igual al de su ficha.
            if (actual.UsuarioId is int usuarioId)
            {
                await usuarios.ActualizarEmailYNombreAsync(
                    usuarioId, solicitud.Email!, Recortar100($"{solicitud.Nombres} {solicitud.Apellidos}"), ct);
            }

            // Se audita QUÉ campos cambiaron, sin copiar los datos personales.
            await auditoria.RegistrarAsync(
                AccionesAuditoria.Actualizar,
                RecursosAuditoria.Cliente,
                id,
                new { campos = CamposCambiados(actual, solicitud) },
                ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await ObtenerAsync(id, ct);
    }

    /// <summary>Baja lógica: la ficha, sus mascotas y su cuenta web (con sus sesiones).</summary>
    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        var actual = await clientes.ObtenerPorIdAsync(id, ct) ?? throw new NoEncontradoException("Cliente no encontrado");

        await clientes.EliminarAsync(id, ct);
        if (actual.UsuarioId is int usuarioId)
        {
            await usuarios.DesactivarYEliminarAsync(usuarioId, ct);
            await refreshTokens.RevocarTodosDelUsuarioAsync(usuarioId, MotivosRevocacion.UsuarioDesactivado, ct);
        }
        await auditoria.RegistrarAsync(AccionesAuditoria.Eliminar, RecursosAuditoria.Cliente, id, ct: ct);
        await tx.ConfirmarAsync(ct);
    }

    private static string Recortar100(string texto) => texto.Length <= 100 ? texto : texto[..100];

    private static List<string> CamposCambiados(ClienteRespuesta antes, DatosClienteSolicitud despues)
    {
        var campos = new List<string>();
        void Comparar(string nombre, string? a, string? b)
        {
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                campos.Add(nombre);
            }
        }
        Comparar("dni", antes.Dni, despues.Dni);
        Comparar("nombres", antes.Nombres, despues.Nombres);
        Comparar("apellidos", antes.Apellidos, despues.Apellidos);
        Comparar("email", antes.Email, despues.Email);
        Comparar("telefono", antes.Telefono, despues.Telefono);
        Comparar("direccion", antes.Direccion, despues.Direccion);
        return campos;
    }
}
