using System.Globalization;
using FluentValidation;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Indicadores;

/// <summary>HU-018: periodo del dashboard en días de Lima. Sin fechas: del día 1 del mes a hoy.</summary>
public sealed class ConsultaIndicadores : INormalizable
{
    public const int DiasMaximos = 366;

    public string? Desde { get; set; }

    public string? Hasta { get; set; }

    public void Normalizar()
    {
        Desde = Texto.Recortar(Desde);
        Hasta = Texto.Recortar(Hasta);
    }
}

public sealed class ConsultaIndicadoresValidador : AbstractValidator<ConsultaIndicadores>
{
    public ConsultaIndicadoresValidador()
    {
        RuleFor(x => x.Desde).Must(d => HoraLima.LeerFecha(d) is not null).When(x => x.Desde is not null).WithMessage("Fecha inválida (AAAA-MM-DD)");
        RuleFor(x => x.Hasta).Must(d => HoraLima.LeerFecha(d) is not null).When(x => x.Hasta is not null).WithMessage("Fecha inválida (AAAA-MM-DD)");
        RuleFor(x => x).Custom((c, ctx) =>
        {
            if (HoraLima.LeerFecha(c.Desde) is DateOnly desde && HoraLima.LeerFecha(c.Hasta) is DateOnly hasta)
            {
                if (hasta < desde)
                {
                    ctx.AddFailure("hasta", "Debe ser igual o posterior a 'desde'");
                }
                else if (hasta.DayNumber - desde.DayNumber > ConsultaIndicadores.DiasMaximos)
                {
                    ctx.AddFailure("hasta", $"El periodo no puede superar {ConsultaIndicadores.DiasMaximos} días");
                }
            }
        });
    }
}

public sealed class ResumenVentas
{
    /// <summary>Soles cobrados (pedidos pagados o entregados) en el periodo.</summary>
    public decimal Total { get; init; }

    public int Pedidos { get; init; }

    public decimal TicketPromedio { get; init; }

    public decimal TotalWeb { get; init; }

    public decimal TotalPresencial { get; init; }

    /// <summary>Pedidos del periodo que aún esperan pago o verificación.</summary>
    public int PendientesDeCobro { get; init; }
}

public sealed class VentaDiaria
{
    /// <summary>Día de Lima 'AAAA-MM-DD'.</summary>
    public string Fecha { get; init; } = string.Empty;

    public decimal Total { get; init; }

    public int Pedidos { get; init; }
}

public sealed class ResumenCitas
{
    public int Atendidas { get; init; }

    public int Programadas { get; init; }

    public int Canceladas { get; init; }

    public int NoAsistio { get; init; }
}

public sealed class ProductoStockBajo
{
    public int Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Nombre { get; init; } = string.Empty;

    public int Stock { get; init; }

    public int StockMinimo { get; init; }
}

public sealed class ProductoMasVendido
{
    public int ProductoId { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public int Cantidad { get; init; }

    public decimal Monto { get; init; }
}

public sealed record IndicadoresRespuesta(
    string Desde,
    string Hasta,
    ResumenVentas Ventas,
    IReadOnlyList<VentaDiaria> VentasPorDia,
    ResumenCitas Citas,
    int ClientesNuevos,
    IReadOnlyList<ProductoStockBajo> ProductosStockBajo,
    IReadOnlyList<ProductoMasVendido> ProductosMasVendidos);

public interface IIndicadoresRepositorio
{
    Task<ResumenVentas> VentasAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);

    Task<IReadOnlyList<VentaDiaria>> VentasPorDiaAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);

    Task<ResumenCitas> CitasAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);

    Task<int> ClientesNuevosAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ProductoStockBajo>> StockBajoAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ProductoMasVendido>> MasVendidosAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);
}

/// <summary>HU-018: indicadores del negocio. Solo admin.</summary>
public sealed class IndicadoresServicio(IIndicadoresRepositorio repositorio)
{
    public async Task<IndicadoresRespuesta> ObtenerAsync(ConsultaIndicadores consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var hoy = HoraLima.Hoy();
        var hasta = HoraLima.LeerFecha(consulta.Hasta) ?? hoy;
        var desde = HoraLima.LeerFecha(consulta.Desde) ?? new DateOnly(hasta.Year, hasta.Month, 1);
        var desdeUtc = HoraLima.InicioDelDiaUtc(desde);
        var hastaUtc = HoraLima.InicioDelDiaUtc(hasta.AddDays(1));

        return new IndicadoresRespuesta(
            desde.ToString(HoraLima.FormatoFecha, CultureInfo.InvariantCulture),
            hasta.ToString(HoraLima.FormatoFecha, CultureInfo.InvariantCulture),
            await repositorio.VentasAsync(desdeUtc, hastaUtc, ct),
            await repositorio.VentasPorDiaAsync(desdeUtc, hastaUtc, ct),
            await repositorio.CitasAsync(desdeUtc, hastaUtc, ct),
            await repositorio.ClientesNuevosAsync(desdeUtc, hastaUtc, ct),
            await repositorio.StockBajoAsync(ct),
            await repositorio.MasVendidosAsync(desdeUtc, hastaUtc, ct));
    }
}
