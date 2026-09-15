using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Comprobantes;

namespace RiverVet.Infraestructura.Externos;

/// <summary>
/// Modo "enlace": arma un enlace de "click to chat" de WhatsApp
/// (https://wa.me/51XXXXXXXXX?text=...). La recepcionista lo abre y pulsa
/// enviar desde el WhatsApp de la clínica. No necesita cuentas, tokens ni
/// pagos a Meta, y el mensaje sale del número que el cliente ya conoce.
/// </summary>
public sealed class WhatsAppEnlace : IMensajeriaWhatsApp
{
    public string Modo => OpcionesWhatsApp.ModoEnlace;

    public Task<ResultadoEnvio> EnviarComprobanteAsync(string celular, MensajeComprobante mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        // 51 = código de país del Perú. El celular ya fue validado (9 dígitos, empieza con 9).
        var enlace = new Uri($"https://wa.me/51{celular}?text={Uri.EscapeDataString(mensaje.Texto)}");
        return Task.FromResult(new ResultadoEnvio(false, enlace, null));
    }
}

/// <summary>
/// Modo "cloudapi": envío automático por la API de WhatsApp Cloud de Meta,
/// con una PLANTILLA aprobada por Meta (obligatorio para escribir primero a
/// un cliente). La plantilla tiene 3 variables: código, total y fecha.
///
/// El token viaja SOLO en la cabecera Authorization; nunca se escribe en el
/// log (si el envío falla, se registra el código HTTP, no la petición).
/// </summary>
public sealed class WhatsAppCloudApi(HttpClient http, IOptions<OpcionesWhatsApp> opciones, ILogger<WhatsAppCloudApi> logger)
    : IMensajeriaWhatsApp
{
    public const string ClienteHttp = "whatsapp";

    public string Modo => OpcionesWhatsApp.ModoCloudApi;

    public async Task<ResultadoEnvio> EnviarComprobanteAsync(string celular, MensajeComprobante mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        var config = opciones.Value;
        using var peticion = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"https://graph.facebook.com/{config.VersionApi}/{config.NumeroId}/messages"));
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
        peticion.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to = "51" + celular,
            type = "template",
            template = new
            {
                name = config.Plantilla,
                language = new { code = config.CodigoIdioma },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = new[]
                        {
                            new { type = "text", text = mensaje.CodigoPedido },
                            new { type = "text", text = mensaje.Total.ToString("0.00", CultureInfo.InvariantCulture) },
                            new { type = "text", text = mensaje.Fecha },
                        },
                    },
                },
            },
        });

        try
        {
            using var respuesta = await http.SendAsync(peticion, ct);
            if (respuesta.IsSuccessStatusCode)
            {
                return new ResultadoEnvio(true, null, null);
            }
            logger.LogWarning("WhatsApp Cloud API respondió {Codigo} al enviar un comprobante", (int)respuesta.StatusCode);
            return new ResultadoEnvio(false, null, $"WhatsApp respondió {(int)respuesta.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "No se pudo contactar a WhatsApp Cloud API");
            return new ResultadoEnvio(false, null, "No se pudo contactar a WhatsApp");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResultadoEnvio(false, null, "WhatsApp no respondió a tiempo");
        }
    }
}
