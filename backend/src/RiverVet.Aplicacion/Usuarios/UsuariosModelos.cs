using System.Text.Json.Serialization;
using FluentValidation;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Usuarios;

/// <summary>Usuario tal como lo devuelve la API. Nunca incluye password_hash.</summary>
public sealed class UsuarioRespuesta : IConTotal
{
    public int Id { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Rol { get; init; } = string.Empty;

    public bool Activo { get; init; }

    public DateTime? UltimoLoginEn { get; init; }

    public DateTime CreadoEn { get; init; }

    [JsonIgnore]
    public int Total { get; init; }
}

/// <summary>Datos mínimos de la sesión, releídos de la base en cada petición autenticada.</summary>
public sealed class UsuarioSesion
{
    public int Id { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Rol { get; init; } = string.Empty;

    public int? ClienteId { get; init; }
}

/// <summary>Único modelo que lleva el hash de la contraseña. Solo se usa en el login.</summary>
public sealed class UsuarioLogin
{
    public int Id { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Rol { get; init; } = string.Empty;

    public bool Activo { get; init; }

    public string PasswordHash { get; init; } = string.Empty;

    public bool Bloqueado { get; init; }
}

public sealed class ListarUsuariosConsulta : PaginacionConsulta
{
    public string? Rol { get; set; }
}

/// <summary>El admin solo da de alta PERSONAL; las cuentas de cliente se crean desde clientes.</summary>
public sealed class CrearUsuarioSolicitud : INormalizable
{
    public string? Nombre { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? Rol { get; set; }

    public void Normalizar()
    {
        Nombre = Texto.Recortar(Nombre);
        Email = Texto.Recortar(Email)?.ToLowerInvariant();
    }
}

public sealed class ActualizarUsuarioSolicitud : INormalizable
{
    public string? Nombre { get; set; }

    public string? Rol { get; set; }

    public bool? Activo { get; set; }

    public void Normalizar() => Nombre = Texto.Recortar(Nombre);
}

public sealed class ListarUsuariosValidador : AbstractValidator<ListarUsuariosConsulta>
{
    public ListarUsuariosValidador()
    {
        Include(new PaginacionValidador());
        RuleFor(x => x.Rol).Must(r => r is null || Roles.Validos.Contains(r)).WithMessage("Rol inválido");
    }
}

public sealed class CrearUsuarioValidador : AbstractValidator<CrearUsuarioSolicitud>
{
    public CrearUsuarioValidador()
    {
        RuleFor(x => x.Nombre).NombrePropio(100);
        RuleFor(x => x.Email).NotEmpty().WithMessage("El email es obligatorio").EmailValido();
        RuleFor(x => x.Password).PasswordSegura();
        RuleFor(x => x.Rol)
            .Must(r => r is not null && Roles.DePersonal.Contains(r))
            .WithMessage("Rol inválido: admin, veterinario o recepcionista");
    }
}

public sealed class ActualizarUsuarioValidador : AbstractValidator<ActualizarUsuarioSolicitud>
{
    public ActualizarUsuarioValidador()
    {
        RuleFor(x => x.Nombre).NombrePropio(100);
        RuleFor(x => x.Rol).Must(r => r is not null && Roles.Validos.Contains(r)).WithMessage("Rol inválido");
        RuleFor(x => x.Activo).NotNull().WithMessage("Es obligatorio");
    }
}

/// <summary>Veterinario activo, para elegirlo al agendar una cita.</summary>
public sealed record VeterinarioResumen(int Id, string Nombre);

public interface IUsuariosRepositorio
{
    Task<IReadOnlyList<VeterinarioResumen>> ListarVeterinariosAsync(CancellationToken ct = default);

    Task<UsuarioSesion?> ObtenerSesionAsync(int id, CancellationToken ct = default);

    Task<UsuarioLogin?> BuscarParaLoginAsync(string email, CancellationToken ct = default);

    /// <summary>Suma un intento fallido de forma atómica. Devuelve true si este intento bloqueó la cuenta.</summary>
    Task<bool> RegistrarIntentoFallidoAsync(int id, int maxIntentos, int minutosBloqueo, CancellationToken ct = default);

    Task RegistrarLoginExitosoAsync(int id, CancellationToken ct = default);

    Task<UsuarioRespuesta> CrearAsync(string nombre, string email, string passwordHash, string rol, CancellationToken ct = default);

    Task<IReadOnlyList<UsuarioRespuesta>> ListarAsync(ListarUsuariosConsulta consulta, CancellationToken ct = default);

    Task<UsuarioRespuesta?> ObtenerPorIdAsync(int id, CancellationToken ct = default);

    Task<UsuarioRespuesta?> ActualizarAsync(int id, string nombre, string rol, bool activo, CancellationToken ct = default);

    Task ActualizarEmailYNombreAsync(int id, string email, string nombre, CancellationToken ct = default);

    Task<string?> ObtenerHashAsync(int id, CancellationToken ct = default);

    Task ActualizarPasswordAsync(int id, string passwordHash, CancellationToken ct = default);

    Task DesactivarYEliminarAsync(int id, CancellationToken ct = default);
}
