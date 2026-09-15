using System.Globalization;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Pruebas.Integracion;

/// <summary>
/// Entrega horarios de cita FUTUROS, válidos y DISTINTOS para cada prueba (todas
/// comparten la base: dos pruebas con el mismo horario chocarían entre sí).
/// </summary>
public static class Horarios
{
    private static int _siguiente = -1;

    /// <summary>Próximo bloque libre, en hora de Lima: "AAAA-MM-DDTHH:mm".</summary>
    public static string Siguiente()
    {
        var indice = Interlocked.Increment(ref _siguiente);
        // 24 bloques por día hábil; cuando se acaban, se pasa al siguiente día
        // hábil (la clínica no atiende los domingos).
        var fecha = SaltarDomingo(HoraLima.Hoy().AddDays(2));
        for (var d = 0; d < indice / 24; d++)
        {
            fecha = SaltarDomingo(fecha.AddDays(1));
        }
        var hora = new TimeOnly(8, 0).AddMinutes(30 * (indice % 24));
        return fecha.ToDateTime(hora).ToString(HoraLima.FormatoFechaHora, CultureInfo.InvariantCulture);
    }

    private static DateOnly SaltarDomingo(DateOnly fecha) =>
        fecha.DayOfWeek == DayOfWeek.Sunday ? fecha.AddDays(1) : fecha;

    public static string Fecha(string fechaHora) => fechaHora[..10];

    /// <summary>El próximo domingo (la clínica no atiende).</summary>
    public static string ProximoDomingo()
    {
        var fecha = HoraLima.Hoy().AddDays(1);
        while (fecha.DayOfWeek != DayOfWeek.Sunday)
        {
            fecha = fecha.AddDays(1);
        }
        return fecha.ToString(HoraLima.FormatoFecha, CultureInfo.InvariantCulture);
    }
}
