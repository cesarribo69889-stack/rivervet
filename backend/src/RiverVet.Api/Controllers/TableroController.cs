using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Tablero;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Tablero del personal (Sprint 2) — /api/dashboard. Admin, recepción y veterinario.
///
///  GET  /stats          citas de hoy, pendientes, sin cerrar, atendidas del mes,
///                       clientes nuevos del mes y vacunas por vencer (30 y 7 días)
///  GET  /citas-hoy      citas de hoy (igual que GET /api/citas?desde=hoy&amp;hasta=hoy)
///  GET  /citas-semana   citas de lunes a domingo de esta semana
///
/// Las ventas no están aquí: siguen en /api/indicadores (solo admin).
/// </summary>
[Route("api/dashboard")]
[Authorize(Roles = Roles.Personal)]
public sealed class TableroController(TableroServicio servicio) : ApiControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Estadisticas(CancellationToken ct) => Exito(await servicio.EstadisticasAsync(ct));

    [HttpGet("citas-hoy")]
    public async Task<IActionResult> CitasHoy(CancellationToken ct) => Exito(await servicio.CitasDeHoyAsync(ct));

    [HttpGet("citas-semana")]
    public async Task<IActionResult> CitasSemana(CancellationToken ct) => Exito(await servicio.CitasDeLaSemanaAsync(ct));
}
