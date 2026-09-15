namespace RiverVet.Api.Middlewares;

/// <summary>
/// Cabeceras HTTP de seguridad en TODA respuesta.
///
/// La API solo devuelve JSON, así que su CSP puede ser la más estricta: no se
/// permite cargar NADA ni ser embebida en un iframe. La única excepción es
/// /swagger (solo existe en desarrollo), que necesita cargar sus scripts.
/// HSTS se agrega en Program.cs con UseHsts() fuera de desarrollo.
/// </summary>
public sealed class CabecerasSeguridadMiddleware(RequestDelegate siguiente)
{
    public Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var cabeceras = contexto.Response.Headers;

        // No "adivinar" tipos de contenido.
        cabeceras.XContentTypeOptions = "nosniff";
        // Prohibido mostrarse dentro de un iframe (clickjacking).
        cabeceras.XFrameOptions = "DENY";
        cabeceras["Referrer-Policy"] = "no-referrer";
        cabeceras["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        cabeceras["Cross-Origin-Opener-Policy"] = "same-origin";
        cabeceras["Cross-Origin-Resource-Policy"] = "same-site";

        if (!contexto.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            cabeceras.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            // Las respuestas traen datos personales (fichas, historial,
            // exportación Ley 29733): ni el navegador ni un proxy intermedio
            // deben guardarlas en caché.
            cabeceras.CacheControl = "no-store";
        }

        // IIS agrega "X-Powered-By: ASP.NET"; también se quita en web.config.
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers.Remove("X-Powered-By");
            contexto.Response.Headers.Remove("Server");
            return Task.CompletedTask;
        });

        return siguiente(contexto);
    }
}
