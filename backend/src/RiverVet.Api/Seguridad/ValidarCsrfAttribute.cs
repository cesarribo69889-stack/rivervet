using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Filters;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Api.Seguridad;

/// <summary>
/// Protección CSRF con el patrón "double-submit cookie".
///
/// ¿Por qué hace falta? /api/auth/refresh y /api/auth/logout se autentican con
/// una COOKIE, y el navegador adjunta cookies automáticamente. Una página
/// maliciosa podría hacer que tu navegador envíe una petición a nuestra API
/// con tu cookie (Cross-Site Request Forgery).
///
/// ¿Cómo funciona? Al iniciar sesión enviamos la cookie rv_csrf con un valor
/// aleatorio y legible por JavaScript. Nuestro frontend la lee y la reenvía en
/// la cabecera X-CSRF-Token. Una página de otro dominio NO puede leer nuestras
/// cookies, así que no puede poner esa cabecera: si cookie y cabecera no
/// coinciden, se rechaza (403). Se suma a SameSite=Strict: dos defensas.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ValidarCsrfAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var peticion = context.HttpContext.Request;
        var cookie = peticion.Cookies[CookiesSesion.Csrf];
        var cabecera = peticion.Headers[CookiesSesion.CabeceraCsrf].ToString();

        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(cabecera) || !SonIguales(cookie, cabecera))
        {
            throw new AccesoDenegadoException("Solicitud no válida. Recarga la página e inténtalo de nuevo.");
        }
    }

    /// <summary>
    /// Comparación en tiempo constante. Una comparación normal se detiene en el
    /// primer carácter distinto, y midiendo ese tiempo un atacante podría
    /// adivinar el valor carácter por carácter.
    /// </summary>
    internal static bool SonIguales(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
