using FluentValidation;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Vacunas;

public sealed class RegistroVacuna
{
    public int Id { get; init; }

    public int MascotaId { get; init; }

    public int VeterinarioId { get; init; }

    public string VeterinarioNombre { get; init; } = string.Empty;

    public string TipoVacuna { get; init; } = string.Empty;

    /// <summary>'AAAA-MM-DD' (columna DATE: sin zona horaria).</summary>
    public string FechaAplicacion { get; init; } = string.Empty;

    public string Lote { get; init; } = string.Empty;

    public string? ProximaDosis { get; init; }

    public string? Observaciones { get; init; }

    public DateTime CreadoEn { get; init; }
}

/// <summary>HU-011: registrar una vacuna aplicada.</summary>
public sealed class RegistrarVacunaSolicitud : INormalizable
{
    public string? TipoVacuna { get; set; }

    public string? FechaAplicacion { get; set; }

    public string? Lote { get; set; }

    public string? ProximaDosis { get; set; }

    public string? Observaciones { get; set; }

    public void Normalizar()
    {
        TipoVacuna = Texto.Libre(TipoVacuna);
        FechaAplicacion = Texto.Recortar(FechaAplicacion);
        Lote = Texto.Recortar(Lote)?.ToUpperInvariant();
        ProximaDosis = Texto.Recortar(ProximaDosis);
        Observaciones = Texto.Libre(Observaciones);
    }
}

public sealed class RegistrarVacunaValidador : AbstractValidator<RegistrarVacunaSolicitud>
{
    public RegistrarVacunaValidador()
    {
        RuleFor(x => x.TipoVacuna).NotEmpty().WithMessage("Indica la vacuna").MaximumLength(100).WithMessage("Máximo 100 caracteres");
        RuleFor(x => x.Lote)
            .NotEmpty().WithMessage("El lote es obligatorio (trazabilidad)")
            .MaximumLength(50).WithMessage("Máximo 50 caracteres")
            .Matches("^[A-Z0-9][A-Z0-9-/.]*$").WithMessage("Solo letras, números, guion, barra y punto");
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Máximo 500 caracteres");
        RuleFor(x => x.FechaAplicacion)
            .Must(f => HoraLima.LeerFecha(f) is DateOnly d && d.Year >= 1990).WithMessage("Fecha inválida (AAAA-MM-DD)")
            .Must(f => HoraLima.LeerFecha(f) is not DateOnly d || d <= HoraLima.Hoy()).WithMessage("No puede ser una fecha futura");
        RuleFor(x => x.ProximaDosis)
            .Must(f => HoraLima.LeerFecha(f) is not null).WithMessage("Fecha inválida (AAAA-MM-DD)")
            .Must((s, f) => HoraLima.LeerFecha(f) is not DateOnly proxima || HoraLima.LeerFecha(s.FechaAplicacion) is not DateOnly aplicacion || proxima > aplicacion)
                .WithMessage("Debe ser posterior a la fecha de aplicación")
            .Must(f => HoraLima.LeerFecha(f) is not DateOnly d || d <= HoraLima.Hoy().AddYears(5)).WithMessage("Máximo 5 años hacia adelante")
            .When(x => x.ProximaDosis is not null);
    }
}

public interface IVacunasRepositorio
{
    Task<IReadOnlyList<RegistroVacuna>> ListarAsync(int mascotaId, CancellationToken ct = default);

    Task<RegistroVacuna?> ObtenerAsync(int id, CancellationToken ct = default);

    Task<int> InsertarAsync(int mascotaId, int veterinarioId, RegistrarVacunaSolicitud datos, CancellationToken ct = default);
}
