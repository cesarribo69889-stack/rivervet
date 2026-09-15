using FluentValidation;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Historial;

/// <summary>Una atención del historial clínico.</summary>
public sealed class RegistroHistorial
{
    public int Id { get; init; }

    public int MascotaId { get; init; }

    public int VeterinarioId { get; init; }

    public string VeterinarioNombre { get; init; } = string.Empty;

    public int? CitaId { get; init; }

    public DateTime FechaAtencion { get; init; }

    public string Motivo { get; init; } = string.Empty;

    public string Diagnostico { get; init; } = string.Empty;

    public string Tratamiento { get; init; } = string.Empty;

    public string? Observaciones { get; init; }

    public decimal? PesoKg { get; init; }

    public decimal? TemperaturaC { get; init; }
}

/// <summary>
/// HU-006: registrar una atención. Todo el texto libre pasa por
/// Texto.Libre (se eliminan etiquetas HTML: XSS almacenado). Si viene
/// citaId, la cita queda marcada como atendida.
/// </summary>
public sealed class RegistrarAtencionSolicitud : INormalizable
{
    public int? CitaId { get; set; }

    public string? Motivo { get; set; }

    public string? Diagnostico { get; set; }

    public string? Tratamiento { get; set; }

    public string? Observaciones { get; set; }

    public decimal? PesoKg { get; set; }

    public decimal? TemperaturaC { get; set; }

    public void Normalizar()
    {
        Motivo = Texto.Libre(Motivo);
        Diagnostico = Texto.Libre(Diagnostico);
        Tratamiento = Texto.Libre(Tratamiento);
        Observaciones = Texto.Libre(Observaciones);
    }
}

public sealed class ConsultaHistorial
{
    /// <summary>asc = cronológico (más antigua primero, por defecto); desc = más reciente primero.</summary>
    public string? Orden { get; set; }
}

public sealed class RegistrarAtencionValidador : AbstractValidator<RegistrarAtencionSolicitud>
{
    public RegistrarAtencionValidador()
    {
        RuleFor(x => x.CitaId).GreaterThan(0).When(x => x.CitaId is not null).WithMessage("Id inválido");
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo es obligatorio").MaximumLength(250).WithMessage("Máximo 250 caracteres");
        RuleFor(x => x.Diagnostico).NotEmpty().WithMessage("El diagnóstico es obligatorio").MaximumLength(1000).WithMessage("Máximo 1000 caracteres");
        RuleFor(x => x.Tratamiento).NotEmpty().WithMessage("El tratamiento es obligatorio").MaximumLength(1000).WithMessage("Máximo 1000 caracteres");
        RuleFor(x => x.Observaciones).MaximumLength(2000).WithMessage("Máximo 2000 caracteres");
        RuleFor(x => x.PesoKg)
            .GreaterThan(0).WithMessage("El peso debe ser mayor a 0")
            .LessThanOrEqualTo(999.99m).WithMessage("Máximo 999.99 kg")
            .PrecisionScale(5, 2, true).WithMessage("Máximo dos decimales")
            .When(x => x.PesoKg is not null);
        RuleFor(x => x.TemperaturaC)
            .InclusiveBetween(30m, 45m).WithMessage("Temperatura fuera de rango (30 a 45 °C)")
            .PrecisionScale(4, 1, true).WithMessage("Máximo un decimal")
            .When(x => x.TemperaturaC is not null);
    }
}

public sealed class ConsultaHistorialValidador : AbstractValidator<ConsultaHistorial>
{
    public ConsultaHistorialValidador() =>
        RuleFor(x => x.Orden).Must(o => o is null or "asc" or "desc").WithMessage("Orden inválido: asc o desc");
}

public interface IHistorialRepositorio
{
    Task<IReadOnlyList<RegistroHistorial>> ListarAsync(int mascotaId, bool descendente, CancellationToken ct = default);

    Task<RegistroHistorial?> ObtenerAsync(int id, CancellationToken ct = default);

    Task<int> InsertarAsync(int mascotaId, int veterinarioId, RegistrarAtencionSolicitud datos, CancellationToken ct = default);
}
