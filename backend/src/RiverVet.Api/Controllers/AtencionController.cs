using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Historial;
using RiverVet.Aplicacion.Recordatorios;
using RiverVet.Aplicacion.Vacunas;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Historial clínico y vacunas de una mascota (HU-006, HU-007, HU-011)
///
///  Método  Ruta                                   Roles
///  GET     /api/mascotas/{id}/historial           los 4 roles (cliente: solo sus mascotas)
///  GET     /api/mascotas/{id}/historial/{aid}     los 4 roles (detalle)
///  POST    /api/mascotas/{id}/historial           veterinario
///  PUT     /api/mascotas/{id}/historial/{aid}     el veterinario que la registró
///  DELETE  /api/mascotas/{id}/historial/{aid}     el veterinario que la registró (borrado lógico)
///  GET     /api/mascotas/{id}/vacunas             los 4 roles (cliente: solo sus mascotas)
///  GET     /api/mascotas/{id}/vacunas/{vid}       los 4 roles (detalle)
///  POST    /api/mascotas/{id}/vacunas             veterinario
///  PUT     /api/mascotas/{id}/vacunas/{vid}       el veterinario que la registró
///  DELETE  /api/mascotas/{id}/vacunas/{vid}       el veterinario que la registró (borrado lógico)
///
/// "El que la registró" se comprueba en el servicio (403 si es otro). Toda
/// corrección o eliminación queda en la auditoría con el contenido anterior.
/// </summary>
[Route("api/mascotas/{mascotaId}")]
public sealed class AtencionController(AtencionServicio servicio) : ApiControllerBase
{
    [HttpGet("historial")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Historial(int mascotaId, [FromQuery] ConsultaHistorial consulta, CancellationToken ct) =>
        Exito(await servicio.ListarHistorialAsync(mascotaId, consulta, ct));

    [HttpPost("historial")]
    [Authorize(Roles = Roles.Veterinario)]
    public async Task<IActionResult> RegistrarAtencion(int mascotaId, [FromBody] RegistrarAtencionSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.RegistrarAtencionAsync(mascotaId, solicitud, ct));

    [HttpGet("historial/{id:int}")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> DetalleAtencion(int mascotaId, int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerAtencionAsync(mascotaId, id, ct));

    [HttpPut("historial/{id:int}")]
    [Authorize(Roles = Roles.Veterinario)]
    public async Task<IActionResult> ActualizarAtencion(int mascotaId, int id, [FromBody] ActualizarAtencionSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ActualizarAtencionAsync(mascotaId, id, solicitud, ct));

    [HttpDelete("historial/{id:int}")]
    [Authorize(Roles = Roles.Veterinario)]
    public async Task<IActionResult> EliminarAtencion(int mascotaId, int id, CancellationToken ct)
    {
        await servicio.EliminarAtencionAsync(mascotaId, id, ct);
        return Mensaje("Atención eliminada");
    }

    [HttpGet("vacunas")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Vacunas(int mascotaId, CancellationToken ct) =>
        Exito(await servicio.ListarVacunasAsync(mascotaId, ct));

    [HttpPost("vacunas")]
    [Authorize(Roles = Roles.Veterinario)]
    public async Task<IActionResult> RegistrarVacuna(int mascotaId, [FromBody] RegistrarVacunaSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.RegistrarVacunaAsync(mascotaId, solicitud, ct));

    [HttpGet("vacunas/{id:int}")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> DetalleVacuna(int mascotaId, int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerVacunaAsync(mascotaId, id, ct));

    [HttpPut("vacunas/{id:int}")]
    [Authorize(Roles = Roles.Veterinario)]
    public async Task<IActionResult> ActualizarVacuna(int mascotaId, int id, [FromBody] RegistrarVacunaSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ActualizarVacunaAsync(mascotaId, id, solicitud, ct));

    [HttpDelete("vacunas/{id:int}")]
    [Authorize(Roles = Roles.Veterinario)]
    public async Task<IActionResult> EliminarVacuna(int mascotaId, int id, CancellationToken ct)
    {
        await servicio.EliminarVacunaAsync(mascotaId, id, ct);
        return Mensaje("Vacuna eliminada");
    }
}

/// <summary>
/// GET /api/recordatorios?dias=7 — HU-012. Los 4 roles: el personal ve todos
/// (para llamar a los clientes); un cliente, solo los de sus mascotas.
/// </summary>
[Route("api/recordatorios")]
[Authorize(Roles = Roles.Todos)]
public sealed class RecordatoriosController(RecordatoriosServicio servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener([FromQuery] ConsultaRecordatorios consulta, CancellationToken ct) =>
        Exito(await servicio.ObtenerAsync(consulta, ct));
}
