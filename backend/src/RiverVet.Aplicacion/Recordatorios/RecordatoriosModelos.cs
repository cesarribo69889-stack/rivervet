using FluentValidation;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Recordatorios;

public sealed class RecordatorioVacuna
{
    public int VacunaId { get; init; }

    public int MascotaId { get; init; }

    public string MascotaNombre { get; init; } = string.Empty;

    public int ClienteId { get; init; }

    public string ClienteNombre { get; init; } = string.Empty;

    public string ClienteTelefono { get; init; } = string.Empty;

    public string TipoVacuna { get; init; } = string.Empty;

    /// <summary>'AAAA-MM-DD'.</summary>
    public string ProximaDosis { get; init; } = string.Empty;

    public int DiasRestantes { get; init; }
}

public sealed class RecordatorioCita
{
    public int CitaId { get; init; }

    public int MascotaId { get; init; }

    public string MascotaNombre { get; init; } = string.Empty;

    public string ClienteNombre { get; init; } = string.Empty;

    public string ClienteTelefono { get; init; } = string.Empty;

    public string VeterinarioNombre { get; init; } = string.Empty;

    public DateTime FechaHora { get; init; }

    public string FechaHoraLima => HoraLima.Formatear(HoraLima.DeUtc(FechaHora));
}

public sealed record RecordatoriosRespuesta(int Dias, IReadOnlyList<RecordatorioVacuna> Vacunas, IReadOnlyList<RecordatorioCita> Citas);

public sealed class ConsultaRecordatorios
{
    public int Dias { get; set; } = 7;
}

public sealed class ConsultaRecordatoriosValidador : AbstractValidator<ConsultaRecordatorios>
{
    public ConsultaRecordatoriosValidador() =>
        RuleFor(x => x.Dias).InclusiveBetween(1, 60).WithMessage("Debe estar entre 1 y 60 días");
}

public interface IRecordatoriosRepositorio
{
    /// <summary>
    /// Vacunas cuya próxima dosis vence entre hoy y el límite, solo la ÚLTIMA
    /// de cada tipo por mascota (si ya se aplicó una más nueva, no se avisa).
    /// </summary>
    Task<IReadOnlyList<RecordatorioVacuna>> VacunasPorVencerAsync(DateOnly hoy, DateOnly limite, int? propietarioId, CancellationToken ct = default);

    Task<IReadOnlyList<RecordatorioCita>> CitasProximasAsync(DateTime desdeUtc, DateTime hastaUtc, int? propietarioId, CancellationToken ct = default);
}

/// <summary>
/// HU-012: recordatorios de próximas vacunas y citas en los próximos N días.
/// El personal ve todos (para llamar a los clientes); un cliente, solo los de
/// sus mascotas.
/// </summary>
public sealed class RecordatoriosServicio(IRecordatoriosRepositorio repositorio, IContextoPeticion contexto)
{
    public async Task<RecordatoriosRespuesta> ObtenerAsync(ConsultaRecordatorios consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var hoy = HoraLima.Hoy();
        var limite = hoy.AddDays(consulta.Dias);
        var vacunas = await repositorio.VacunasPorVencerAsync(hoy, limite, contexto.PropietarioId, ct);
        var citas = await repositorio.CitasProximasAsync(
            HoraLima.AhoraUtc(), HoraLima.InicioDelDiaUtc(limite.AddDays(1)), contexto.PropietarioId, ct);
        return new RecordatoriosRespuesta(consulta.Dias, vacunas, citas);
    }
}
