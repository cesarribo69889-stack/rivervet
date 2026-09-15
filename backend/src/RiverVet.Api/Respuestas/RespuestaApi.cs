using System.Text.Json;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Api.Respuestas;

/// <summary>Forma única de las respuestas exitosas: { "success": true, "data": ... }.</summary>
public sealed record RespuestaApi<T>(T Data)
{
    public bool Success => true;
}

/// <summary>
/// Forma única de TODAS las respuestas de error:
/// { "success": false, "message": "...", "errors": [...], "traceId": "..." }
/// </summary>
public sealed record RespuestaError(string Message, IReadOnlyList<ErrorCampo> Errors, string TraceId)
{
    public bool Success => false;

    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    /// <summary>Escribe un error directamente en la respuesta (lo usan middlewares y eventos de JWT).</summary>
    public static Task EscribirAsync(HttpContext contexto, int estado, string mensaje, IReadOnlyList<ErrorCampo>? errores = null)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Response.StatusCode = estado;
        contexto.Response.ContentType = "application/json; charset=utf-8";
        var cuerpo = new RespuestaError(mensaje, errores ?? [], contexto.TraceIdentifier);
        return contexto.Response.WriteAsync(JsonSerializer.Serialize(cuerpo, _opciones), contexto.RequestAborted);
    }
}
