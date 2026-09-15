using RiverVet.Aplicacion.Comun;

namespace RiverVet.Api.Seguridad;

/// <summary>
/// Cookies de la sesión.
///
/// rv_refresh (refresh token):
///  - HttpOnly: JavaScript NO puede leerla. Si hubiera un XSS, el atacante no
///    podría robar el refresh token.
///  - Secure: solo viaja por HTTPS (en producción).
///  - SameSite=Strict: el navegador no la envía en peticiones iniciadas desde
///    otro sitio (primera defensa contra CSRF).
///  - Path=/api/auth: solo se envía a las rutas de autenticación.
///
/// rv_csrf (token anti-CSRF, patrón double-submit): SIN HttpOnly, a propósito:
/// el frontend la lee y la devuelve en la cabecera X-CSRF-Token.
/// </summary>
public static class CookiesSesion
{
    public const string Refresh = "rv_refresh";
    public const string Csrf = "rv_csrf";
    public const string CabeceraCsrf = "X-CSRF-Token";
    private const string RutaRefresh = "/api/auth";

    public static void Establecer(HttpResponse respuesta, string refreshToken, string tokenCsrf, OpcionesSeguridad opciones)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentNullException.ThrowIfNull(opciones);
        var duracion = TimeSpan.FromDays(opciones.DiasRefresh);
        respuesta.Cookies.Append(Refresh, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = opciones.CookiesSeguras,
            SameSite = SameSiteMode.Strict,
            Path = RutaRefresh,
            MaxAge = duracion,
            IsEssential = true,
        });
        respuesta.Cookies.Append(Csrf, tokenCsrf, new CookieOptions
        {
            HttpOnly = false,
            Secure = opciones.CookiesSeguras,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = duracion,
            IsEssential = true,
        });
    }

    public static void Limpiar(HttpResponse respuesta, OpcionesSeguridad opciones)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentNullException.ThrowIfNull(opciones);
        respuesta.Cookies.Delete(Refresh, new CookieOptions
        {
            HttpOnly = true,
            Secure = opciones.CookiesSeguras,
            SameSite = SameSiteMode.Strict,
            Path = RutaRefresh,
        });
        respuesta.Cookies.Delete(Csrf, new CookieOptions
        {
            Secure = opciones.CookiesSeguras,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }
}
