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
///  Método  Ruta                              Roles
///  GET     /api/mascotas/{id}/historial      los 4 roles (cliente: solo sus mascotas)
///  POST    /api/mascotas/{id}/historial      veterinario
///  GET     /api/mascotas/{id}/vacunas        los 4 roles (cliente: solo sus mascotas)
///  POST    /api/mascotas/{id}/vacunas        veterinario
///
/// No hay PUT ni DELETE: el historial médico es de solo-agregar.
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

    [HttpGet("vacunas")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Vacunas(int mascotaId, CancellationToken ct) =>
        Exito(await servicio.ListarVacunasAsync(mascotaId, ct));

    [HttpPost("vacunas")]
    [Authorize(Roles = Roles.Veterinario)]
    public async Task<IActionResult> RegistrarVacuna(int mascotaId, [FromBody] RegistrarVacunaSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.RegistrarVacunaAsync(mascotaId, solicitud, ct));
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
