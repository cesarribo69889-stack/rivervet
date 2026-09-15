using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Api.Seguridad;
using RiverVet.Aplicacion.Clientes;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Mascotas;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Clientes (HU-002, HU-003, HU-005) — /api/clientes
///
///  Método  Ruta              Roles
///  GET     /                 admin, recepcionista, veterinario
///  POST    /buscar           admin, recepcionista, veterinario
///  GET     /yo               cliente
///  POST    /                 admin, recepcionista
///  GET     /{id}             admin, recepcionista, veterinario
///  PUT     /{id}             admin, recepcionista
///  DELETE  /{id}             admin
///  GET     /{id}/mascotas    los 4 roles (cliente: solo su propio id; ajeno = 404)
/// </summary>
[Route("api/clientes")]
public sealed class ClientesController(ClientesServicio servicio, MascotasServicio mascotas) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Roles = Roles.Personal)]
    public async Task<IActionResult> Listar([FromQuery] PaginacionConsulta consulta, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(consulta, null, ct));

    [HttpPost("buscar")]
    [Authorize(Roles = Roles.Personal)]
    public async Task<IActionResult> Buscar([FromBody] BusquedaSolicitud busqueda, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(busqueda, busqueda.Texto, ct));

    // "yo" es un segmento literal: tiene prioridad sobre {id}.
    [HttpGet("yo")]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> ObtenerPropio(CancellationToken ct) =>
        Exito(await servicio.ObtenerPropioAsync(
            int.Parse(User.FindFirst(ClaimsSesion.Id)!.Value, CultureInfo.InvariantCulture), ct));

    [HttpPost]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Crear([FromBody] CrearClienteSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.CrearAsync(solicitud, ct));

    [HttpGet("{id}")]
    [Authorize(Roles = Roles.Personal)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerAsync(id, ct));

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarClienteSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ActualizarAsync(id, solicitud, ct));

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await servicio.EliminarAsync(id, ct);
        return Mensaje("Cliente eliminado");
    }

    /// <summary>HU-005: mascotas de un cliente. El control de propiedad está en el SQL.</summary>
    [HttpGet("{id}/mascotas")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> ListarMascotas(int id, [FromQuery] PaginacionConsulta consulta, CancellationToken ct) =>
        Exito(await mascotas.ListarDeClienteAsync(id, consulta, ct));
}
