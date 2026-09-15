using System.Globalization;

namespace RiverVet.Aplicacion.Comun;

/// <summary>
/// Zona horaria de la clínica: America/Lima (UTC-5, sin horario de verano).
///
/// Regla del sistema: en la base todo se guarda en UTC; lo que el usuario
/// escribe o ve está en hora de Lima. La conversión vive SOLO aquí. Si la zona
/// no se encuentra, el sistema falla al arrancar en lugar de usar UTC en
/// silencio (una zona equivocada correría todas las citas 5 horas).
/// </summary>
public static class HoraLima
{
    public const string FormatoFechaHora = "yyyy-MM-dd'T'HH:mm";
    public const string FormatoFecha = "yyyy-MM-dd";

    private static readonly TimeZoneInfo _zona = BuscarZona();

    private static TimeZoneInfo BuscarZona()
    {
        // IANA ("America/Lima") funciona en Linux y en Windows 10+ con ICU;
        // "SA Pacific Standard Time" es el identificador clásico de Windows.
        foreach (var id in new[] { "America/Lima", "SA Pacific Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zona))
            {
                return zona;
            }
        }
        throw new InvalidOperationException("No se encontró la zona horaria America/Lima en el servidor.");
    }

    public static DateTime AhoraUtc() => DateTime.UtcNow;

    public static DateTime Ahora() => DeUtc(DateTime.UtcNow);

    public static DateOnly Hoy() => DateOnly.FromDateTime(Ahora());

    /// <summary>Hora de Lima (sin zona) → UTC.</summary>
    public static DateTime AUtc(DateTime horaLima) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(horaLima, DateTimeKind.Unspecified), _zona);

    /// <summary>UTC → hora de Lima (sin zona).</summary>
    public static DateTime DeUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zona);

    /// <summary>Inicio del día (00:00 de Lima) expresado en UTC.</summary>
    public static DateTime InicioDelDiaUtc(DateOnly fecha) => AUtc(fecha.ToDateTime(TimeOnly.MinValue));

    /// <summary>"2026-09-20T10:30" (hora de Lima) → DateTime sin zona; null si el formato no es válido.</summary>
    public static DateTime? LeerFechaHora(string? texto) =>
        DateTime.TryParseExact(texto, FormatoFechaHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor)
            ? valor
            : null;

    public static DateOnly? LeerFecha(string? texto) =>
        DateOnly.TryParseExact(texto, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor)
            ? valor
            : null;

    public static string Formatear(DateTime horaLima) => horaLima.ToString(FormatoFechaHora, CultureInfo.InvariantCulture);
}
