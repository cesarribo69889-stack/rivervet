using FluentValidation;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Autenticacion;

public sealed class LoginSolicitud : INormalizable
{
    public string? Email { get; set; }

    public string? Password { get; set; }

    public void Normalizar() => Email = Texto.Recortar(Email)?.ToLowerInvariant();
}

public sealed class CambiarPasswordSolicitud
{
    public string? PasswordActual { get; set; }

    public string? PasswordNueva { get; set; }
}

/// <summary>Datos públicos del usuario que viajan en la respuesta del login.</summary>
public sealed record UsuarioPublico(int Id, string Nombre, string Email, string Rol);

/// <summary>
/// Resultado de un login o refresh. El RefreshToken NO va en el body de la
/// respuesta: el controlador lo pone en una cookie httpOnly.
/// </summary>
public sealed record SesionEmitida(string AccessToken, int ExpiraEn, UsuarioPublico Usuario, string RefreshToken);

public sealed class LoginValidador : AbstractValidator<LoginSolicitud>
{
    public LoginValidador()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("El email es obligatorio").EmailValido();
        // En el login NO se aplica la política de contraseñas: si la política
        // cambia, los usuarios con contraseñas antiguas igual deben poder
        // entrar. Solo se limita el tamaño (bcrypt con textos enormes consume
        // CPU innecesaria).
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria")
            .MaximumLength(200).WithMessage("Máximo 200 caracteres");
    }
}

public sealed class CambiarPasswordValidador : AbstractValidator<CambiarPasswordSolicitud>
{
    public CambiarPasswordValidador()
    {
        RuleFor(x => x.PasswordActual)
            .NotEmpty().WithMessage("Es obligatorio")
            .MaximumLength(200).WithMessage("Máximo 200 caracteres");
        RuleFor(x => x.PasswordNueva).PasswordSegura();
    }
}

/// <summary>Registro de un refresh token leído (y bloqueado) para rotarlo.</summary>
public sealed class RefreshTokenRegistro
{
    public long Id { get; init; }

    public int UsuarioId { get; init; }

    public Guid FamiliaId { get; init; }

    public DateTime? RevocadoEn { get; init; }

    public string? MotivoRevocacion { get; init; }

    public int? SegundosDesdeRevocacion { get; init; }

    public bool Expirado { get; init; }

    public bool FamiliaActiva { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Rol { get; init; } = string.Empty;

    public bool UsuarioActivo { get; init; }
}

public sealed record RefreshTokenBasico(long Id, int UsuarioId, Guid FamiliaId);

public interface IRefreshTokensRepositorio
{
    Task<long> InsertarAsync(int usuarioId, string tokenHash, Guid familiaId, int dias, string? ip, string? userAgent, CancellationToken ct = default);

    /// <summary>Lee el token BLOQUEÁNDOLO (UPDLOCK) hasta el fin de la transacción.</summary>
    Task<RefreshTokenRegistro?> BuscarParaRotarAsync(string tokenHash, CancellationToken ct = default);

    Task MarcarRotadoAsync(long id, long reemplazoId, CancellationToken ct = default);

    Task RevocarFamiliaAsync(Guid familiaId, string motivo, CancellationToken ct = default);

    Task RevocarTodosDelUsuarioAsync(int usuarioId, string motivo, CancellationToken ct = default);

    Task<RefreshTokenBasico?> BuscarPorHashAsync(string tokenHash, CancellationToken ct = default);
}
