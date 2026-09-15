using RiverVet.Aplicacion.Citas;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Vacunas;

namespace RiverVet.Pruebas.Unitarias;

[Trait("Categoria", "Unitaria")]
public class HorariosPruebas
{
    // Martes 1 de enero de 2030, 09:00 en Lima.
    private static readonly DateTime _ahora = new(2030, 1, 1, 9, 0, 0);

    [Theory]
    [InlineData("2030-01-02T10:30")] // miércoles, bloque válido
    [InlineData("2030-01-02T08:00")] // primer bloque del día
    [InlineData("2030-01-02T19:30")] // último bloque (termina 20:00)
    [InlineData("2030-01-05T12:00")] // sábado
    public void Acepta_horarios_validos(string fechaHora) =>
        ReglasHorario.Validar(fechaHora, _ahora).ShouldBeNull();

    [Theory]
    [InlineData("2030-01-02T10:15", "(:00 o :30)")]
    [InlineData("2030-01-06T10:00", "horario de atención")] // domingo
    [InlineData("2030-01-02T07:30", "horario de atención")]
    [InlineData("2030-01-02T20:00", "horario de atención")]
    [InlineData("2029-12-31T10:00", "pasado")]
    [InlineData("2030-01-01T08:30", "pasado")] // hoy, pero ya pasó
    [InlineData("2030-12-31T10:00", "anticipación")]
    [InlineData("02/01/2030 10:30", "formato")]
    [InlineData(null, "formato")]
    public void Rechaza_horarios_invalidos(string? fechaHora, string mensaje) =>
        ReglasHorario.Validar(fechaHora, _ahora)!.ShouldContain(mensaje);

    [Fact]
    public void Lima_es_UTC_menos_5_en_ambos_sentidos()
    {
        var lima = new DateTime(2030, 1, 2, 10, 30, 0);
        HoraLima.AUtc(lima).ShouldBe(new DateTime(2030, 1, 2, 15, 30, 0));
        HoraLima.DeUtc(new DateTime(2030, 1, 2, 15, 30, 0)).ShouldBe(lima);
        HoraLima.InicioDelDiaUtc(new DateOnly(2030, 1, 2)).ShouldBe(new DateTime(2030, 1, 2, 5, 0, 0));
    }

    [Fact]
    public void Una_cita_de_noche_en_Lima_cae_al_dia_siguiente_en_UTC() =>
        HoraLima.AUtc(new DateTime(2030, 1, 2, 19, 30, 0)).ShouldBe(new DateTime(2030, 1, 3, 0, 30, 0));

    [Fact]
    public void Vacuna_proxima_dosis_debe_ser_posterior_a_la_aplicacion()
    {
        var hoy = HoraLima.Hoy().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var solicitud = new RegistrarVacunaSolicitud { TipoVacuna = "Rabia", Lote = "L-1", FechaAplicacion = hoy, ProximaDosis = hoy };
        new RegistrarVacunaValidador().Validate(solicitud).Errors.ShouldContain(e => e.PropertyName == "ProximaDosis");
    }

    [Fact]
    public void Vacuna_no_puede_aplicarse_en_el_futuro()
    {
        var manana = HoraLima.Hoy().AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        new RegistrarVacunaValidador()
            .Validate(new RegistrarVacunaSolicitud { TipoVacuna = "Rabia", Lote = "L-1", FechaAplicacion = manana })
            .Errors.ShouldContain(e => e.PropertyName == "FechaAplicacion");
    }

    [Fact]
    public void Rango_del_calendario_limitado_a_93_dias() =>
        new ConsultaCitasValidador()
            .Validate(new ConsultaCitas { Desde = "2030-01-01", Hasta = "2030-06-01" })
            .Errors.ShouldContain(e => e.PropertyName == "hasta");
}
