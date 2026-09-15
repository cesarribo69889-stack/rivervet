using System.Globalization;
using System.Text.Json.Serialization;
using FluentValidation;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Mascotas;

public sealed class MascotaRespuesta : IConTotal
{
    public int Id { get; init; }

    public int ClienteId { get; init; }

    public string ClienteNombre { get; init; } = string.Empty;

    public string Nombre { get; init; } = string.Empty;

    public string Especie { get; init; } = string.Empty;

    public string? Raza { get; init; }

    public string? Sexo { get; init; }

    /// <summary>
    /// 'AAAA-MM-DD' como texto (la consulta usa CONVERT estilo 23). Si viajara
    /// como fecha con hora, "2019-03-15" (medianoche UTC) se mostraría en Lima
    /// como 14 de marzo.
    /// </summary>
    public string? FechaNacimiento { get; init; }

    public decimal? PesoKg { get; init; }

    public string? Color { get; init; }

    public string? SenasParticulares { get; init; }

    public bool? Esterilizado { get; init; }

    public DateTime CreadoEn { get; init; }

    public DateTime ActualizadoEn { get; init; }

    [JsonIgnore]
    public int Total { get; init; }
}

/// <summary>Campos de una mascota (alta y edición).</summary>
public class DatosMascotaSolicitud : INormalizable
{
    public string? Nombre { get; set; }

    public string? Especie { get; set; }

    public string? Raza { get; set; }

    public string? Sexo { get; set; }

    public string? FechaNacimiento { get; set; }

    public decimal? PesoKg { get; set; }

    public string? Color { get; set; }

    public string? SenasParticulares { get; set; }

    public bool? Esterilizado { get; set; }

    public void Normalizar()
    {
        Nombre = Texto.Recortar(Nombre);
        Especie = Texto.Recortar(Especie);
        Sexo = Texto.Recortar(Sexo);
        FechaNacimiento = Texto.Recortar(FechaNacimiento);
        Raza = Texto.Libre(Raza);
        Color = Texto.Libre(Color);
        SenasParticulares = Texto.Libre(SenasParticulares);
    }
}

/// <summary>HU-004: alta de mascota asociada a un cliente.</summary>
public sealed class CrearMascotaSolicitud : DatosMascotaSolicitud
{
    public int? ClienteId { get; set; }
}

/// <summary>
/// Edición: el dueño (clienteId) NO se acepta. Transferir una mascota a otro
/// cliente sería una operación distinta, con su propia auditoría.
/// </summary>
public sealed class ActualizarMascotaSolicitud : DatosMascotaSolicitud;

public abstract class DatosMascotaValidador<T> : AbstractValidator<T>
    where T : DatosMascotaSolicitud
{
    protected DatosMascotaValidador()
    {
        RuleFor(x => x.Nombre).NombrePropio(60);
        RuleFor(x => x.Especie)
            .Must(e => e is not null && Catalogos.Especies.Contains(e))
            .WithMessage("Especie inválida: " + string.Join(", ", Catalogos.Especies));
        RuleFor(x => x.Raza).MaximumLength(60).WithMessage("Máximo 60 caracteres");
        RuleFor(x => x.Sexo).Must(s => s is null || Catalogos.Sexos.Contains(s)).WithMessage("Sexo inválido: macho o hembra");
        RuleFor(x => x.Color).MaximumLength(40).WithMessage("Máximo 40 caracteres");
        RuleFor(x => x.SenasParticulares).MaximumLength(250).WithMessage("Máximo 250 caracteres");
        RuleFor(x => x.PesoKg)
            .GreaterThan(0).WithMessage("El peso debe ser mayor a 0")
            .LessThanOrEqualTo(999.99m).WithMessage("Máximo 999.99 kg")
            .PrecisionScale(5, 2, true).WithMessage("Máximo dos decimales")
            .When(x => x.PesoKg is not null);
        RuleFor(x => x.FechaNacimiento)
            .Must(EsFechaValida).WithMessage("Fecha inválida (formato AAAA-MM-DD)")
            .Must(NoEsFutura).WithMessage("La fecha de nacimiento no puede ser futura")
            .When(x => x.FechaNacimiento is not null);
    }

    private static bool EsFechaValida(string? texto) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
        && fecha.Year >= 1980;

    // Margen de un día por la diferencia horaria entre Lima y UTC.
    private static bool NoEsFutura(string? texto) =>
        !DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
        || fecha <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
}

public sealed class CrearMascotaValidador : DatosMascotaValidador<CrearMascotaSolicitud>
{
    public CrearMascotaValidador() => RuleFor(x => x.ClienteId).IdValido();
}

public sealed class ActualizarMascotaValidador : DatosMascotaValidador<ActualizarMascotaSolicitud>;

/// <summary>
/// ★ CONTROL DE PROPIEDAD (IDOR) ★
/// Todas las lecturas reciben <c>propietarioId</c>:
///   null  → quien consulta es personal de la clínica: ve todas.
///   un id → quien consulta es un cliente: el SQL agrega
///           "c.usuario_id = @propietarioId" y solo aparecen SUS mascotas.
/// El filtro está en el SQL, no en un if del controlador: aunque alguien
/// olvidara el if en una ruta nueva, la consulta seguiría sin devolver datos
/// ajenos.
/// </summary>
public interface IMascotasRepositorio
{
    Task<IReadOnlyList<MascotaRespuesta>> ListarAsync(
        PaginacionConsulta consulta, string? texto, int? clienteId, int? propietarioId, CancellationToken ct = default);

    Task<MascotaRespuesta?> ObtenerPorIdAsync(int id, int? propietarioId, CancellationToken ct = default);

    /// <summary>Inserta solo si el cliente existe y no está eliminado. Devuelve null si no existe.</summary>
    Task<int?> CrearAsync(CrearMascotaSolicitud datos, int? creadoPor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(int id, DatosMascotaSolicitud datos, CancellationToken ct = default);

    Task<bool> EliminarAsync(int id, CancellationToken ct = default);

    /// <summary>Actualiza el peso con el registrado en la última atención.</summary>
    Task ActualizarPesoAsync(int id, decimal pesoKg, CancellationToken ct = default);
}
