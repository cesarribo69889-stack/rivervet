using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RiverVet.Api.Respuestas;
using RiverVet.Infraestructura.Datos;

namespace RiverVet.Api.Controllers;

/// <summary>
/// GET /api/health — público. Lo usan el monitoreo del servidor y el pipeline
/// de despliegue. Comprueba la base de datos. No revela versiones, nombres de
/// servidor ni mensajes de error. Queda FUERA del rate limit.
/// </summary>
[Route("api/health")]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class SaludController(SesionBd sesion, ILogger<SaludController> logger) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener(CancellationToken ct)
    {
        try
        {
            var conexion = await sesion.ObtenerConexionAsync(ct);
            await conexion.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: ct));
            return Exito(new { estado = "ok", baseDatos = "ok", fecha = DateTime.UtcNow });
        }
#pragma warning disable CA1031 // Cualquier fallo de conexión se informa como 503, sin detalles.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Health check: base de datos no disponible");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new RespuestaError("Servicio no disponible", [], HttpContext.TraceIdentifier));
        }
    }
}
