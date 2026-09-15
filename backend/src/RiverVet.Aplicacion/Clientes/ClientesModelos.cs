using System.Text.Json.Serialization;
using FluentValidation;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Clientes;

public class ClienteRespuesta
{
    public int Id { get; init; }

    public string Dni { get; init; } = string.Empty;

    public string Nombres { get; init; } = string.Empty;

    public string Apellidos { get; init; } = string.Empty;

    public string? Email { get; init; }

    public string Telefono { get; init; } = string.Empty;

    public string? Direccion { get; init; }

    public bool ConsentimientoDatos { get; init; }

    public DateTime? ConsentimientoEn { get; init; }

    public bool TieneCuenta { get; init; }

    public DateTime CreadoEn { get; init; }

    public DateTime ActualizadoEn { get; init; }
}

/// <summary>Fila del listado: además trae el número de mascotas.</summary>
public sealed class ClienteListado : ClienteRespuesta, IConTotal
{
    public int TotalMascotas { get; init; }

    [JsonIgnore]
    public int Total { get; init; }
}

/// <summary>Uso interno: incluye el usuario_id de su cuenta web. NUNCA se devuelve tal cual.</summary>
public sealed class ClienteInterno : ClienteRespuesta
{
    [JsonIgnore]
    public int? UsuarioId { get; init; }
}

/// <summary>Campos comunes de alta y edición.</summary>
public abstract class DatosClienteSolicitud : INormalizable
{
    public string? Dni { get; set; }

    public string? Nombres { get; set; }

    public string? Apellidos { get; set; }

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    public string? Direccion { get; set; }

    public virtual void Normalizar()
    {
        Dni = Texto.Recortar(Dni);
        Nombres = Texto.Recortar(Nombres);
        Apellidos = Texto.Recortar(Apellidos);
        Email = Texto.Recortar(Email)?.ToLowerInvariant();
        Telefono = Texto.Recortar(Telefono);
        Direccion = Texto.Libre(Direccion);
    }
}

/// <summary>HU-002: alta de cliente, opcionalmente con cuenta web.</summary>
public sealed class CrearClienteSolicitud : DatosClienteSolicitud
{
    /// <summary>Ley N° 29733: sin consentimiento expreso no se registran datos personales.</summary>
    public bool? ConsentimientoDatos { get; set; }

    public bool? CrearCuenta { get; set; }

    public string? Password { get; set; }
}

public sealed class ActualizarClienteSolicitud : DatosClienteSolicitud;

public abstract class DatosClienteValidador<T> : AbstractValidator<T>
    where T : DatosClienteSolicitud
{
    protected DatosClienteValidador()
    {
        RuleFor(x => x.Dni).Dni();
        RuleFor(x => x.Nombres).NombrePropio(100);
        RuleFor(x => x.Apellidos).NombrePropio(100);
        RuleFor(x => x.Telefono).Celular();
        RuleFor(x => x.Email).EmailValido().When(x => x.Email is not null);
        RuleFor(x => x.Direccion).MaximumLength(250).WithMessage("Máximo 250 caracteres");
    }
}

public sealed class CrearClienteValidador : DatosClienteValidador<CrearClienteSolicitud>
{
    public CrearClienteValidador()
    {
        RuleFor(x => x.ConsentimientoDatos)
            .Equal(true)
            .WithMessage("Se requiere el consentimiento del cliente para el tratamiento de sus datos");

        When(x => x.CrearCuenta == true, () =>
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("El email es obligatorio para crear la cuenta");
            RuleFor(x => x.Password).PasswordSegura();
        });
    }
}

public sealed class ActualizarClienteValidador : DatosClienteValidador<ActualizarClienteSolicitud>;

public interface IClientesRepositorio
{
    Task<IReadOnlyList<ClienteListado>> ListarAsync(PaginacionConsulta consulta, string? texto, CancellationToken ct = default);

    Task<ClienteInterno?> ObtenerPorIdAsync(int id, CancellationToken ct = default);

    Task<ClienteRespuesta?> ObtenerPorUsuarioAsync(int usuarioId, CancellationToken ct = default);

    /// <summary>¿Existe el cliente y (si se indica) pertenece a ese usuario? Control de propiedad en SQL.</summary>
    Task<bool> ExisteParaUsuarioAsync(int id, int? propietarioId, CancellationToken ct = default);

    Task<int> CrearAsync(DatosClienteSolicitud datos, int? usuarioId, int? creadoPor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(int id, DatosClienteSolicitud datos, CancellationToken ct = default);

    /// <summary>Baja LÓGICA del cliente y, en cascada, de sus mascotas.</summary>
    Task EliminarAsync(int id, CancellationToken ct = default);
}
