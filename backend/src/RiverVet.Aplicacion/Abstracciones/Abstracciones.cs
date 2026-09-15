using System.Data;

namespace RiverVet.Aplicacion.Abstracciones;

/// <summary>
/// Unidad de trabajo: agrupa varias operaciones en UNA transacción de SQL
/// Server. O se guardan todas, o ninguna (p. ej. crear cliente + su cuenta +
/// su registro de auditoría). Mientras hay una transacción abierta, todos los
/// repositorios de la petición la usan automáticamente.
/// </summary>
public interface IUnidadTrabajo
{
    Task<ITransaccion> IniciarTransaccionAsync(
        IsolationLevel nivel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);
}

/// <summary>Transacción abierta. Si se descarta sin ConfirmarAsync(), se hace ROLLBACK.</summary>
public interface ITransaccion : IAsyncDisposable
{
    Task ConfirmarAsync(CancellationToken ct = default);
}

/// <summary>Quién hace la petición y desde dónde (para autorización y auditoría).</summary>
public interface IContextoPeticion
{
    int? UsuarioId { get; }

    string? Rol { get; }

    string? Ip { get; }

    string? UserAgent { get; }

    /// <summary>
    /// Control de propiedad (IDOR): el id de usuario que las consultas SQL usan
    /// como filtro "solo lo mío", o null si quien consulta es personal de la
    /// clínica y puede ver todo.
    /// </summary>
    int? PropietarioId { get; }
}

public interface IHasheadorPassword
{
    string Hashear(string password);

    bool Verificar(string password, string hash);
}

/// <summary>Emisión de tokens de sesión y de CSRF.</summary>
public interface IServicioTokens
{
    /// <summary>JWT de acceso: solo lleva id (sub), rol y jti.</summary>
    string GenerarAccessToken(int usuarioId, string rol);

    int SegundosValidezAcceso { get; }

    /// <summary>32 bytes aleatorios (256 bits) en base64url.</summary>
    string GenerarRefreshToken();

    /// <summary>SHA-256 en hexadecimal: lo que se guarda en la base de datos.</summary>
    string HashToken(string token);

    string GenerarTokenCsrf();
}

public interface IServicioAuditoria
{
    /// <summary>
    /// Registra la acción. Si hay una transacción abierta, se escribe en ELLA:
    /// si el cambio se revierte, su registro también, y si la auditoría falla,
    /// el cambio no se guarda. No existen cambios sin rastro.
    /// </summary>
    Task RegistrarAsync(
        string accion,
        string recurso,
        object? recursoId = null,
        object? detalle = null,
        int? usuarioId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Límite de intentos fallidos de login por IP + email (5 cada 15 minutos).
/// Complementa al bloqueo de cuenta: este frena a un atacante concreto; el
/// bloqueo protege la cuenta aunque el ataque venga de muchas IPs.
/// </summary>
public interface ILimitadorIntentosLogin
{
    bool EstaBloqueado(string email);

    void RegistrarFallo(string email);
}
