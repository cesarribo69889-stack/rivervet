using System.Globalization;
using RiverVet.Aplicacion.Citas;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Tablero;

/// <summary>
/// Estadísticas del tablero del personal (Sprint 2). A diferencia de
/// /api/indicadores (ventas, solo admin), esto es lo que necesita a diario
/// recepción y el veterinario: la agenda y las vacunas por vencer.
/// </summary>
public sealed class EstadisticasTablero
{
    /// <summary>Día de Lima al que se refiere "hoy" ('AAAA-MM-DD').</summary>
    public string Fecha { get; init; } = string.Empty;

    /// <summary>Citas de hoy que no se cancelaron.</summary>
    public int CitasHoy { get; init; }

    public int CitasHoyAtendidas { get; init; }

    /// <summary>Programadas desde ahora en adelante.</summary>
    public int CitasPendientes { get; init; }

    /// <summary>Programadas cuya hora ya pasó: falta registrar la atención o la inasistencia.</summary>
    public int CitasSinCerrar { get; init; }

    public int CitasAtendidasMes { get; init; }

    public int ClientesNuevosMes { get; init; }

    /// <summary>Próxima dosis en los próximos 30 días (solo la última aplicación de cada tipo).</summary>
    public int VacunasPorVencer { get; init; }

    /// <summary>Las anteriores que vencen en 7 días o menos.</summary>
    public int VacunasUrgentes { get; init; }
}

public interface ITableroRepositorio
{
    Task<EstadisticasTablero> EstadisticasAsync(
        DateOnly hoy, DateTime inicioHoyUtc, DateTime finHoyUtc, DateTime inicioMesUtc, CancellationToken ct = default);
}

public sealed class TableroServicio(ITableroRepositorio repositorio, CitasServicio citas)
{
    public Task<EstadisticasTablero> EstadisticasAsync(CancellationToken ct = default)
    {
        var hoy = HoraLima.Hoy();
        return repositorio.EstadisticasAsync(
            hoy,
            HoraLima.InicioDelDiaUtc(hoy),
            HoraLima.InicioDelDiaUtc(hoy.AddDays(1)),
            HoraLima.InicioDelDiaUtc(new DateOnly(hoy.Year, hoy.Month, 1)),
            ct);
    }

    public Task<IReadOnlyList<CitaRespuesta>> CitasDeHoyAsync(CancellationToken ct = default)
    {
        var hoy = Texto(HoraLima.Hoy());
        return citas.ListarAsync(new ConsultaCitas { Desde = hoy, Hasta = hoy }, ct);
    }

    /// <summary>De lunes a domingo de la semana en curso (para el calendario).</summary>
    public Task<IReadOnlyList<CitaRespuesta>> CitasDeLaSemanaAsync(CancellationToken ct = default)
    {
        var hoy = HoraLima.Hoy();
        var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        return citas.ListarAsync(new ConsultaCitas { Desde = Texto(lunes), Hasta = Texto(lunes.AddDays(6)) }, ct);
    }

    private static string Texto(DateOnly fecha) => fecha.ToString(HoraLima.FormatoFecha, CultureInfo.InvariantCulture);
}
