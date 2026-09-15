using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RiverVet.Aplicacion.Comun;

/// <summary>
/// Redacta datos sensibles antes de escribirlos en logs o en la auditoría.
///
/// Los logs se copian, se envían a otros servicios y los lee mucha gente. Una
/// contraseña o un token que llega a un log es una credencial filtrada. Por
/// eso todo lo que se registra pasa antes por aquí.
/// </summary>
public static partial class RedactorDatos
{
    public const string Redactado = "[REDACTADO]";

    private static readonly JsonSerializerOptions _opcionesJson = new(JsonSerializerDefaults.Web);

    /// <summary>¿El nombre del campo indica un dato que nunca debe registrarse?</summary>
    public static bool EsClaveSensible(string clave) => ClaveSensible().IsMatch(clave);

    /// <summary>Reemplaza JWT, "Bearer xxx" y números de tarjeta dentro de un texto.</summary>
    public static string RedactarTexto(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var resultado = Jwt().Replace(texto, "[JWT REDACTADO]");
        resultado = Bearer().Replace(resultado, "$1[REDACTADO]");
        // 13 a 19 dígitos seguidos (con espacios o guiones): posible tarjeta.
        // DNI (8) y celular (9) no se ven afectados.
        return NumeroTarjeta().Replace(resultado, "[NUMERO REDACTADO]");
    }

    /// <summary>
    /// Serializa un objeto a JSON (camelCase) con los campos sensibles
    /// redactados. Devuelve null si el objeto es null.
    /// </summary>
    public static string? SerializarRedactado(object? valor, int longitudMaxima = 2000)
    {
        if (valor is null)
        {
            return null;
        }
        var nodo = JsonSerializer.SerializeToNode(valor, valor.GetType(), _opcionesJson);
        var json = Redactar(nodo)?.ToJsonString() ?? "null";
        return json.Length <= longitudMaxima ? json : """{"truncado":true}""";
    }

    /// <summary>Devuelve una copia del nodo JSON con los datos sensibles redactados.</summary>
    public static JsonNode? Redactar(JsonNode? nodo)
    {
        switch (nodo)
        {
            case JsonObject objeto:
                var copia = new JsonObject();
                foreach (var (clave, valor) in objeto)
                {
                    copia[clave] = EsClaveSensible(clave) ? JsonValue.Create(Redactado) : Redactar(valor);
                }
                return copia;
            case JsonArray arreglo:
                return new JsonArray([.. arreglo.Select(Redactar)]);
            case JsonValue valorJson when valorJson.TryGetValue<string>(out var texto):
                return JsonValue.Create(RedactarTexto(texto));
            default:
                return nodo?.DeepClone();
        }
    }

    [GeneratedRegex(
        "(pass(word)?|contrase|clave|token|secret|authori[sz]ation|cookie|csrf|jwt|tarjeta|card|cvv|cvc|vencimiento|expir(y|ation)_?date)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClaveSensible();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(bearer\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\b(?:\d[ -]?){12,18}\d\b")]
    private static partial Regex NumeroTarjeta();
}
