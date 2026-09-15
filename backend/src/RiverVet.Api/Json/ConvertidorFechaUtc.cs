using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiverVet.Api.Json;

/// <summary>
/// Todas las fechas con hora se guardan en UTC (SYSUTCDATETIME()), pero SQL
/// Server las entrega sin zona. Sin este convertidor el JSON diría
/// "2026-09-12T15:30:00" y el navegador lo interpretaría como hora LOCAL
/// (5 horas de error en Lima). Con él: "2026-09-12T15:30:00.000Z".
/// </summary>
public sealed class ConvertidorFechaUtc : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
