using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RiverVet.Aplicacion.Pagos;

/// <summary>
/// Firma de los webhooks de la pasarela: HMAC-SHA256 con un secreto compartido.
///
///   firma = hex( HMAC-SHA256( secreto, "{tiempo}.{cuerpo}" ) )
///
/// - Cualquiera puede enviar un POST a /api/webhooks/pasarela diciendo "el
///   pedido RV-000123 está pagado". Sin verificar la firma, un atacante se
///   regalaría productos. Solo quien conoce el secreto (la pasarela) puede
///   calcular una firma válida para ESE cuerpo exacto: si cambia un solo
///   byte (el monto, la referencia), la firma deja de coincidir.
/// - El tiempo va DENTRO de lo firmado y se exige que sea reciente: así no
///   sirve capturar un webhook legítimo y reenviarlo días después.
/// - La comparación es en tiempo constante (FixedTimeEquals): comparar byte a
///   byte y cortar en la primera diferencia deja medir cuánto tarda la
///   respuesta y adivinar la firma de a poco.
/// </summary>
public static class FirmaWebhook
{
    public const string CabeceraFirma = "X-Firma";
    public const string CabeceraTiempo = "X-Firma-Tiempo";

    public static string Calcular(string secreto, long tiempoUnix, byte[] cuerpo)
    {
        ArgumentNullException.ThrowIfNull(secreto);
        ArgumentNullException.ThrowIfNull(cuerpo);
        var prefijo = Encoding.UTF8.GetBytes(tiempoUnix.ToString(CultureInfo.InvariantCulture) + ".");
        var mensaje = new byte[prefijo.Length + cuerpo.Length];
        prefijo.CopyTo(mensaje, 0);
        cuerpo.CopyTo(mensaje, prefijo.Length);
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), mensaje));
    }

    public static bool Verificar(
        string secreto, string? cabeceraTiempo, string? cabeceraFirma, byte[] cuerpo, DateTimeOffset ahora, int toleranciaSegundos)
    {
        if (string.IsNullOrEmpty(secreto) || string.IsNullOrEmpty(cabeceraFirma)
            || !long.TryParse(cabeceraTiempo, NumberStyles.None, CultureInfo.InvariantCulture, out var tiempo))
        {
            return false;
        }
        // Webhook demasiado viejo (o del futuro): posible repetición.
        if (Math.Abs(ahora.ToUnixTimeSeconds() - tiempo) > toleranciaSegundos)
        {
            return false;
        }

        byte[] recibida;
        try
        {
            recibida = Convert.FromHexString(cabeceraFirma);
        }
        catch (FormatException)
        {
            return false;
        }
        var esperada = Convert.FromHexString(Calcular(secreto, tiempo, cuerpo));
        return CryptographicOperations.FixedTimeEquals(esperada, recibida);
    }
}
