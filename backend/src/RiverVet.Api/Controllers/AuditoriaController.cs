using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Auditoria;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Auditoría — /api/auditoria  (GET, solo admin)
/// Solo lectura: la API no ofrece forma de modificarla ni borrarla (y el
/// usuario de base de datos de la app tampoco tiene permiso para hacerlo).
/// </summary>
[Route("api/auditoria")]
[Authorize(Roles = Roles.Admin)]
public sealed class AuditoriaController(AuditoriaServicio servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] ConsultaAuditoria consulta, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(consulta, ct));
}
