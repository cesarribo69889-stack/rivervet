using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RiverVet.Api.Respuestas;
using RiverVet.Aplicacion.Usuarios;

namespace RiverVet.Api.Seguridad;

/// <summary>Qué pasa en cada etapa de la verificación del access token.</summary>
public static class EventosJwt
{
    public static JwtBearerEvents Crear() => new()
    {
        // Firma, expiración, emisor y audiencia ya son válidos. Ahora se
        // vuelve a leer el usuario de la BASE DE DATOS: si el admin lo
        // desactivó o le cambió el rol, el cambio aplica en la SIGUIENTE
        // petición y no recién cuando el token venza. El rol que se usa para
        // autorizar es el de la base, no el que viene en el token.
        OnTokenValidated = async contexto =>
        {
            var sub = contexto.Principal?.FindFirstValue(ClaimsSesion.Id);
            if (!int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                contexto.Fail("Token sin identificador válido");
                return;
            }
            var repositorio = contexto.HttpContext.RequestServices.GetRequiredService<IUsuariosRepositorio>();
            var usuario = await repositorio.ObtenerSesionAsync(id, contexto.HttpContext.RequestAborted);
            if (usuario is null)
            {
                contexto.Fail("Usuario inactivo o eliminado");
                return;
            }

            var claims = new List<Claim>
            {
                new(ClaimsSesion.Id, usuario.Id.ToString(CultureInfo.InvariantCulture)),
                new(ClaimsSesion.Rol, usuario.Rol),
                new(ClaimsSesion.Nombre, usuario.Nombre),
                new(ClaimsSesion.Email, usuario.Email),
            };
            if (usuario.ClienteId is int clienteId)
            {
                claims.Add(new Claim(ClaimsSesion.ClienteId, clienteId.ToString(CultureInfo.InvariantCulture)));
            }
            contexto.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims, JwtBearerDefaults.AuthenticationScheme, ClaimsSesion.Id, ClaimsSesion.Rol));
        },

        // 401 con nuestra forma de error. No se distingue "expirado" de "firma
        // inválida": el frontend hace lo mismo en ambos casos (refrescar).
        OnChallenge = async contexto =>
        {
            contexto.HandleResponse();
            var mensaje = contexto.AuthenticateFailure is null
                ? "Debes iniciar sesión"
                : "Tu sesión expiró. Vuelve a iniciar sesión.";
            await RespuestaError.EscribirAsync(contexto.HttpContext, StatusCodes.Status401Unauthorized, mensaje);
        },

        // 403: hay sesión, pero el rol no está entre los permitidos.
        OnForbidden = contexto => RespuestaError.EscribirAsync(
            contexto.HttpContext,
            StatusCodes.Status403Forbidden,
            "No tienes permiso para realizar esta acción"),
    };
}
