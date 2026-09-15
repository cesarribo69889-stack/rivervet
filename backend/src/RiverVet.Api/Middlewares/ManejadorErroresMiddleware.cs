using RiverVet.Api.Respuestas;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Api.Middlewares;

/// <summary>
/// Manejo centralizado de errores.
///
/// En producción NUNCA se envía el stack trace ni el mensaje interno de un
/// error inesperado: revelarían rutas de archivos, versiones de librerías y
/// nombres de tablas, información valiosa para un atacante. El usuario recibe
/// un mensaje genérico y el traceId; el detalle completo queda en el log con
/// ese mismo traceId.
/// </summary>
public sealed class ManejadorErroresMiddleware(
    RequestDelegate siguiente,
    ILogger<ManejadorErroresMiddleware> logger,
    IHostEnvironment entorno)
{
    /// <summary>Límite del body: 1 MB. Evita que alguien agote la memoria con JSON gigantes.</summary>
    public const long TamanoMaximoBody = 1024 * 1024;

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        try
        {
            // Se rechaza antes de leer el body. Si el cliente no envía
            // Content-Length, el propio servidor (Kestrel/IIS) corta al
            // superar el límite (configurado en Program.cs).
            if (contexto.Request.ContentLength > TamanoMaximoBody)
            {
                throw new BadHttpRequestException("Body demasiado grande", StatusCodes.Status413PayloadTooLarge);
            }
            await siguiente(contexto);
        }
        catch (Exception ex) when (!contexto.Response.HasStarted)
        {
            await ResponderAsync(contexto, ex);
        }
    }

    private Task ResponderAsync(HttpContext contexto, Exception ex)
    {
        switch (ex)
        {
            case ValidacionException v:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status422UnprocessableEntity, v.Message, v.Errores);
            case NoAutenticadoException:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status401Unauthorized, ex.Message);
            case AccesoDenegadoException:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status403Forbidden, ex.Message);
            case NoEncontradoException:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status404NotFound, ex.Message);
            case ConflictoException:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status409Conflict, ex.Message);
            case DemasiadosIntentosException:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status429TooManyRequests, ex.Message);
            case ServicioNoDisponibleException:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status503ServiceUnavailable, ex.Message);
            case BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status413PayloadTooLarge, "La solicitud es demasiado grande");
            case BadHttpRequestException:
                return RespuestaError.EscribirAsync(contexto, StatusCodes.Status400BadRequest, "Solicitud inválida");
            case OperationCanceledException when contexto.RequestAborted.IsCancellationRequested:
                // El cliente cerró la conexión: no hay a quién responder.
                return Task.CompletedTask;
        }

        // Error inesperado: TODO al log del servidor...
        logger.LogError(ex, "Error no controlado en {Metodo} {Ruta} (traceId {TraceId})",
            contexto.Request.Method, contexto.Request.Path.Value, contexto.TraceIdentifier);

        // ...y al cliente solo un mensaje genérico (con el detalle solo en desarrollo).
        var errores = entorno.IsDevelopment() ? new[] { new ErrorCampo("(desarrollo)", ex.Message) } : [];
        return RespuestaError.EscribirAsync(
            contexto,
            StatusCodes.Status500InternalServerError,
            "Ocurrió un error inesperado. Si el problema continúa, comunica este código a soporte.",
            errores);
    }
}
