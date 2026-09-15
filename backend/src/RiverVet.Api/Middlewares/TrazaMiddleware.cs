using System.Diagnostics;
using System.Security.Claims;

namespace RiverVet.Api.Middlewares;

/// <summary>
/// 1) Asigna un traceId único a cada petición (cabecera X-Trace-Id y campo
///    traceId de los errores). Si una recepcionista reporta "me salió un error
///    con código 3f2a...", buscamos ese código en el log del servidor.
/// 2) Registra cada petición: método, ruta SIN query string (ahí podrían venir
///    datos personales), estado, duración, usuario e IP.
/// </summary>
public sealed class TrazaMiddleware(RequestDelegate siguiente, ILogger<TrazaMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        // Siempre uno nuevo: no aceptamos el que mande el cliente (podría
        // usarlo para ensuciar o falsificar los logs).
        contexto.TraceIdentifier = Guid.NewGuid().ToString();
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers["X-Trace-Id"] = contexto.TraceIdentifier;
            return Task.CompletedTask;
        });

        var cronometro = Stopwatch.StartNew();
        try
        {
            await siguiente(contexto);
        }
        finally
        {
            logger.LogInformation(
                "HTTP {Metodo} {Ruta} -> {Estado} en {Ms} ms (usuario {UsuarioId}, ip {Ip}, traceId {TraceId})",
                contexto.Request.Method,
                contexto.Request.Path.Value,
                contexto.Response.StatusCode,
                cronometro.ElapsedMilliseconds,
                contexto.User.FindFirstValue("sub"),
                contexto.Connection.RemoteIpAddress?.ToString(),
                contexto.TraceIdentifier);
        }
    }
}
