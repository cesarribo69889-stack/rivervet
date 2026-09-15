using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RiverVet.Api.Seguridad;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Autenticacion;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Autenticación (HU-001) — /api/auth
///
///  Método  Ruta        Roles               Protección extra
///  POST    /login      público             límite de intentos (IP + email), bloqueo de cuenta
///  POST    /refresh    público (cookie)    CSRF
///  POST    /logout     público (cookie)    CSRF
///  GET     /yo         los 4 roles
///  PUT     /password   los 4 roles
/// </summary>
[Route("api/auth")]
public sealed class AuthController(
    AutenticacionServicio servicio,
    IServicioTokens tokens,
    IOptions<OpcionesSeguridad> opciones) : ApiControllerBase
{
    private readonly OpcionesSeguridad _opciones = opciones.Value;

    /// <summary>
    /// El access token va en el BODY (el frontend lo guarda en memoria); el
    /// refresh token va en la cookie httpOnly, NUNCA en el body.
    /// </summary>
    public sealed record SesionRespuesta(string AccessToken, int ExpiraEn, UsuarioPublico Usuario);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginSolicitud solicitud, CancellationToken ct)
    {
        var sesion = await servicio.LoginAsync(solicitud, ct);
        CookiesSesion.Establecer(Response, sesion.RefreshToken, tokens.GenerarTokenCsrf(), _opciones);
        return Exito(new SesionRespuesta(sesion.AccessToken, sesion.ExpiraEn, sesion.Usuario));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ValidarCsrf]
    public async Task<IActionResult> Refrescar(CancellationToken ct)
    {
        try
        {
            var sesion = await servicio.RefrescarAsync(Request.Cookies[CookiesSesion.Refresh], ct);
            CookiesSesion.Establecer(Response, sesion.RefreshToken, tokens.GenerarTokenCsrf(), _opciones);
            return Exito(new SesionRespuesta(sesion.AccessToken, sesion.ExpiraEn, sesion.Usuario));
        }
        catch (NoAutenticadoException)
        {
            // Sesión inválida: se borran las cookies para no seguir reintentando.
            CookiesSesion.Limpiar(Response, _opciones);
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ValidarCsrf]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await servicio.LogoutAsync(Request.Cookies[CookiesSesion.Refresh], ct);
        CookiesSesion.Limpiar(Response, _opciones);
        return Mensaje("Sesión cerrada");
    }

    [HttpGet("yo")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Yo(CancellationToken ct) =>
        Exito(await servicio.ObtenerSesionAsync(UsuarioActual(), ct));

    [HttpPut("password")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordSolicitud solicitud, CancellationToken ct)
    {
        await servicio.CambiarPasswordAsync(UsuarioActual(), solicitud, ct);
        CookiesSesion.Limpiar(Response, _opciones);
        return Mensaje("Contraseña actualizada. Vuelve a iniciar sesión.");
    }

    private int UsuarioActual() =>
        int.Parse(User.FindFirst(ClaimsSesion.Id)!.Value, CultureInfo.InvariantCulture);
}
