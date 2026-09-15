using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Usuarios;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Autenticacion;

/// <summary>HU-001: login, refresh con rotación, logout y cambio de contraseña.</summary>
public sealed class AutenticacionServicio(
    IUsuariosRepositorio usuarios,
    IRefreshTokensRepositorio refreshTokens,
    IUnidadTrabajo unidadTrabajo,
    IHasheadorPassword hasheador,
    IServicioTokens tokens,
    IServicioAuditoria auditoria,
    ILimitadorIntentosLogin limitador,
    IContextoPeticion contexto,
    IOptions<OpcionesSeguridad> opciones,
    ILogger<AutenticacionServicio> logger)
{
    /// <summary>
    /// ANTI-ENUMERACIÓN: el mismo mensaje para email inexistente, contraseña
    /// incorrecta, cuenta bloqueada o desactivada. Nunca "usuario no encontrado".
    /// </summary>
    public const string MensajeCredenciales = "Email o contraseña incorrectos";

    public const string MensajeSesionExpirada = "Tu sesión expiró. Vuelve a iniciar sesión.";

    /// <summary>
    /// Hash bcrypt (cost 12) de un valor aleatorio que nadie conoce. Se compara
    /// contra él cuando el email NO existe, para que el login tarde lo mismo
    /// exista o no el usuario. Si respondiéramos más rápido para emails
    /// inexistentes, un atacante podría averiguar qué emails están registrados
    /// midiendo el tiempo.
    /// </summary>
    private const string HashFalso = "$2b$12$DvoAd2MKw0aBYzHhC/Emf.OLeT1W8lIFUvrmc.YYvaMzFgbfcMh/O";

    private readonly OpcionesSeguridad _opciones = opciones.Value;

    public async Task<SesionEmitida> LoginAsync(LoginSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var email = solicitud.Email!;

        if (limitador.EstaBloqueado(email))
        {
            throw new DemasiadosIntentosException("Demasiados intentos de inicio de sesión. Espera 15 minutos.");
        }

        var usuario = await usuarios.BuscarParaLoginAsync(email, ct);

        // bcrypt se ejecuta SIEMPRE, exista o no el usuario (ver HashFalso).
        var coincide = hasheador.Verificar(solicitud.Password!, usuario?.PasswordHash ?? HashFalso);

        if (usuario is null || !usuario.Activo || usuario.Bloqueado || !coincide)
        {
            await RegistrarFalloAsync(usuario, email, ct);
            // Mismo código y mismo mensaje en todos los casos.
            throw new NoAutenticadoException(MensajeCredenciales);
        }

        // Login correcto: reiniciar contador, crear la sesión y auditar, todo junto.
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        await usuarios.RegistrarLoginExitosoAsync(usuario.Id, ct);
        var publico = new UsuarioPublico(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol);
        var (sesion, _) = await EmitirSesionAsync(publico, Guid.NewGuid(), ct);
        await auditoria.RegistrarAsync(AccionesAuditoria.Login, RecursosAuditoria.Sesion, usuarioId: usuario.Id, ct: ct);
        await tx.ConfirmarAsync(ct);
        return sesion;
    }

    private async Task RegistrarFalloAsync(UsuarioLogin? usuario, string email, CancellationToken ct)
    {
        limitador.RegistrarFallo(email);

        var motivo = usuario switch
        {
            null => "email_inexistente",
            { Activo: false } => "cuenta_inactiva",
            { Bloqueado: true } => "cuenta_bloqueada",
            _ => "credenciales",
        };

        var seBloqueo = false;
        if (motivo == "credenciales")
        {
            seBloqueo = await usuarios.RegistrarIntentoFallidoAsync(
                usuario!.Id, _opciones.BloqueoIntentosMax, _opciones.BloqueoMinutos, ct);
        }

        await AuditarSinFallarAsync(
            AccionesAuditoria.LoginFallido,
            RecursosAuditoria.Sesion,
            null,
            new { email = Texto.EnmascararEmail(email), motivo },
            usuario?.Id,
            ct);

        if (seBloqueo)
        {
            await AuditarSinFallarAsync(
                AccionesAuditoria.CuentaBloqueada,
                RecursosAuditoria.Usuario,
                usuario!.Id,
                new { minutos = _opciones.BloqueoMinutos },
                usuario.Id,
                ct);
        }
    }

    /// <summary>
    /// La auditoría de un intento FALLIDO no debe cambiar la respuesta: si
    /// falla, se registra en el log y se sigue.
    /// </summary>
    private async Task AuditarSinFallarAsync(
        string accion, string recurso, object? recursoId, object? detalle, int? usuarioId, CancellationToken ct)
    {
        try
        {
            await auditoria.RegistrarAsync(accion, recurso, recursoId, detalle, usuarioId, ct);
        }
#pragma warning disable CA1031 // Se captura todo a propósito: ver comentario del método.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "No se pudo registrar la auditoría de {Accion}", accion);
        }
    }

    /// <summary>Crea un refresh token nuevo (en la familia indicada) y el access token.</summary>
    private async Task<(SesionEmitida Sesion, long RefreshTokenId)> EmitirSesionAsync(
        UsuarioPublico usuario, Guid familiaId, CancellationToken ct)
    {
        var refreshToken = tokens.GenerarRefreshToken();
        var id = await refreshTokens.InsertarAsync(
            usuario.Id,
            tokens.HashToken(refreshToken),
            familiaId,
            _opciones.DiasRefresh,
            contexto.Ip,
            contexto.UserAgent,
            ct);
        var sesion = new SesionEmitida(
            tokens.GenerarAccessToken(usuario.Id, usuario.Rol),
            tokens.SegundosValidezAcceso,
            usuario,
            refreshToken);
        return (sesion, id);
    }

    /// <summary>
    /// Refresh con ROTACIÓN: cada refresh token sirve UNA sola vez. Al usarlo
    /// se revoca y se emite otro de la misma familia.
    ///
    /// Si llega un token YA ROTADO, alguien lo está reutilizando: o lo robaron,
    /// o el ladrón ya lo usó. No sabemos quién es quién, así que se revoca la
    /// familia entera (se cierra esa sesión en todos lados).
    ///
    /// Excepción (gracia, 15 s por defecto): con dos pestañas abiertas que
    /// refrescan a la vez con el mismo token, la segunda llegaría con un token
    /// recién rotado. Sin la gracia, eso cerraría la sesión del usuario
    /// legítimo. Dentro de esa ventana, y solo si la familia sigue activa, se
    /// emite un token hermano en lugar de revocar.
    /// </summary>
    public async Task<SesionEmitida> RefrescarAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new NoAutenticadoException(MensajeSesionExpirada);
        }

        string? error = null;
        SesionEmitida? sesion = null;

        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            var registro = await refreshTokens.BuscarParaRotarAsync(tokens.HashToken(refreshToken), ct);
            if (registro is null)
            {
                error = "no_encontrado";
            }
            else if (registro.RevocadoEn is not null && !EstaEnGracia(registro))
            {
                if (registro.FamiliaActiva)
                {
                    await refreshTokens.RevocarFamiliaAsync(registro.FamiliaId, MotivosRevocacion.ReusoDetectado, ct);
                    await auditoria.RegistrarAsync(
                        AccionesAuditoria.ReusoRefreshToken,
                        RecursosAuditoria.Sesion,
                        detalle: new { familiaId = registro.FamiliaId },
                        usuarioId: registro.UsuarioId,
                        ct: ct);
                }
                error = "reuso";
            }
            else if (registro.Expirado || !registro.UsuarioActivo)
            {
                error = "invalido";
            }
            else
            {
                var usuario = new UsuarioPublico(registro.UsuarioId, registro.Nombre, registro.Email, registro.Rol);
                (sesion, var nuevoId) = await EmitirSesionAsync(usuario, registro.FamiliaId, ct);
                if (registro.RevocadoEn is null)
                {
                    await refreshTokens.MarcarRotadoAsync(registro.Id, nuevoId, ct);
                }
            }

            // Se confirma también en el caso de reúso: la revocación de la
            // familia DEBE quedar guardada aunque luego respondamos 401.
            await tx.ConfirmarAsync(ct);
        }

        if (error is not null || sesion is null)
        {
            if (error == "reuso")
            {
                logger.LogWarning("Reutilización de refresh token detectada desde {Ip}", contexto.Ip);
            }
            throw new NoAutenticadoException(MensajeSesionExpirada);
        }
        return sesion;
    }

    private bool EstaEnGracia(RefreshTokenRegistro registro)
    {
        // DATETIME2(0) REDONDEA al segundo más cercano al guardar: un token
        // revocado a las 10:00:00.6 queda como 10:00:01 y la diferencia con
        // "ahora" puede salir -1. Se lleva a 0 para que un valor negativo no
        // caiga dentro de la ventana (con gracia 0, -1 < 0 dejaría pasar la
        // reutilización).
        var segundos = Math.Max(0, registro.SegundosDesdeRevocacion ?? 0);
        return _opciones.GraciaRefreshSegundos > 0
            && registro.MotivoRevocacion == MotivosRevocacion.Rotado
            && segundos < _opciones.GraciaRefreshSegundos
            && registro.FamiliaActiva;
    }

    /// <summary>
    /// Logout: revoca en la base de datos la sesión (familia) del refresh
    /// token. Aunque alguien hubiera copiado la cookie, ya no le sirve.
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        var registro = await refreshTokens.BuscarPorHashAsync(tokens.HashToken(refreshToken), ct);
        if (registro is null)
        {
            return;
        }
        await refreshTokens.RevocarFamiliaAsync(registro.FamiliaId, MotivosRevocacion.Logout, ct);
        await auditoria.RegistrarAsync(
            AccionesAuditoria.Logout, RecursosAuditoria.Sesion, usuarioId: registro.UsuarioId, ct: ct);
        await tx.ConfirmarAsync(ct);
    }

    public async Task<UsuarioSesion> ObtenerSesionAsync(int usuarioId, CancellationToken ct = default) =>
        await usuarios.ObtenerSesionAsync(usuarioId, ct) ?? throw new NoAutenticadoException();

    /// <summary>
    /// Cambio de contraseña: exige la actual y cierra TODAS las sesiones del
    /// usuario (si alguien más tenía la contraseña vieja, queda fuera).
    /// </summary>
    public async Task CambiarPasswordAsync(int usuarioId, CambiarPasswordSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var hashActual = await usuarios.ObtenerHashAsync(usuarioId, ct) ?? throw new NoAutenticadoException();

        if (!hasheador.Verificar(solicitud.PasswordActual!, hashActual))
        {
            throw new ValidacionException("passwordActual", "La contraseña actual no es correcta");
        }
        if (hasheador.Verificar(solicitud.PasswordNueva!, hashActual))
        {
            throw new ValidacionException("passwordNueva", "La nueva contraseña debe ser distinta de la actual");
        }

        var nuevoHash = hasheador.Hashear(solicitud.PasswordNueva!);
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        await usuarios.ActualizarPasswordAsync(usuarioId, nuevoHash, ct);
        await refreshTokens.RevocarTodosDelUsuarioAsync(usuarioId, MotivosRevocacion.CambioPassword, ct);
        await auditoria.RegistrarAsync(
            AccionesAuditoria.CambioPassword, RecursosAuditoria.Usuario, usuarioId, usuarioId: usuarioId, ct: ct);
        await tx.ConfirmarAsync(ct);
    }
}
