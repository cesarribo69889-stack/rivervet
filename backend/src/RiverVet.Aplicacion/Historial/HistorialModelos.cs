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

    /// <summary>Medicamentos indicados (dosis y duración). Opcional.</summary>
    public string? Receta { get; init; }

    public string? Observaciones { get; init; }

    public decimal? PesoKg { get; init; }

    public decimal? TemperaturaC { get; init; }

    public DateTime CreadoEn { get; init; }

    public DateTime ActualizadoEn { get; init; }

    /// <summary>
    /// true si se corrigió después de registrarla. Se muestra en la ficha: un
    /// registro clínico editado no debe pasar por original. Al insertar, ambas
    /// columnas reciben el mismo SYSUTCDATETIME() (DATETIME2(0)), así que
    /// cualquier diferencia es una corrección.
    /// </summary>
    public bool Editado => ActualizadoEn > CreadoEn;
}

/// <summary>
/// Datos clínicos de una atención, comunes a registrar y a corregir. Todo el
/// texto libre pasa por Texto.Libre (se eliminan etiquetas HTML: XSS almacenado).
/// </summary>
public abstract class DatosAtencionSolicitud : INormalizable
{
    public string? Motivo { get; set; }

    public string? Diagnostico { get; set; }

    public string? Tratamiento { get; set; }

    public string? Receta { get; set; }

    public string? Observaciones { get; set; }

    public decimal? PesoKg { get; set; }

    public decimal? TemperaturaC { get; set; }

    public void Normalizar()
    {
        Motivo = Texto.Libre(Motivo);
        Diagnostico = Texto.Libre(Diagnostico);
        Tratamiento = Texto.Libre(Tratamiento);
        Receta = Texto.Libre(Receta);
        Observaciones = Texto.Libre(Observaciones);
    }
}

/// <summary>HU-006: registrar una atención. Si viene citaId, la cita queda marcada como atendida.</summary>
public sealed class RegistrarAtencionSolicitud : DatosAtencionSolicitud
{
    public int? CitaId { get; set; }
}

/// <summary>
/// Corregir una atención. No lleva citaId: la cita que se atendió no cambia
/// al corregir el diagnóstico.
/// </summary>
public sealed class ActualizarAtencionSolicitud : DatosAtencionSolicitud;

public sealed class ConsultaHistorial
{
    /// <summary>asc = cronológico (más antigua primero, por defecto); desc = más reciente primero.</summary>
    public string? Orden { get; set; }
}

public abstract class DatosAtencionValidador<T> : AbstractValidator<T>
    where T : DatosAtencionSolicitud
{
    protected DatosAtencionValidador()
    {
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo es obligatorio").MaximumLength(250).WithMessage("Máximo 250 caracteres");
        RuleFor(x => x.Diagnostico).NotEmpty().WithMessage("El diagnóstico es obligatorio").MaximumLength(1000).WithMessage("Máximo 1000 caracteres");
        RuleFor(x => x.Tratamiento).NotEmpty().WithMessage("El tratamiento es obligatorio").MaximumLength(1000).WithMessage("Máximo 1000 caracteres");
        RuleFor(x => x.Receta).MaximumLength(2000).WithMessage("Máximo 2000 caracteres");
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

public sealed class RegistrarAtencionValidador : DatosAtencionValidador<RegistrarAtencionSolicitud>
{
    public RegistrarAtencionValidador() =>
        RuleFor(x => x.CitaId).GreaterThan(0).When(x => x.CitaId is not null).WithMessage("Id inválido");
}

public sealed class ActualizarAtencionValidador : DatosAtencionValidador<ActualizarAtencionSolicitud>;

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

    /// <summary>false = ya no existe (otra persona la eliminó mientras tanto).</summary>
    Task<bool> ActualizarAsync(int id, ActualizarAtencionSolicitud datos, CancellationToken ct = default);

    /// <summary>Borrado LÓGICO: la fila queda (eliminado = 1) y la auditoría guarda el contenido.</summary>
    Task<bool> EliminarAsync(int id, CancellationToken ct = default);
}
