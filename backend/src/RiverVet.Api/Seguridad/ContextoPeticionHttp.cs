using System.Globalization;
using System.Security.Claims;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Seguridad;

/// <summary>
/// Datos de quien hace la petición, leídos de la sesión autenticada (claims
/// armados en EventosJwt con los datos RELEÍDOS de la base) y de la conexión.
/// </summary>
public sealed class ContextoPeticionHttp(IHttpContextAccessor accesor) : IContextoPeticion
{
    private HttpContext? Contexto => accesor.HttpContext;

    public int? UsuarioId =>
        int.TryParse(Contexto?.User.FindFirstValue(ClaimsSesion.Id), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public string? Rol => Contexto?.User.FindFirstValue(ClaimsSesion.Rol);

    public string? Ip => Contexto?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Contexto?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agente ? agente : null;

    public int? PropietarioId => Rol == Roles.Cliente ? UsuarioId : null;
}

/// <summary>Nombres de los claims de la sesión.</summary>
public static class ClaimsSesion
{
    public const string Id = "sub";
    public const string Rol = "rol";
    public const string Nombre = "nombre";
    public const string Email = "email";
    public const string ClienteId = "clienteId";
}
