using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Mascotas;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Mascotas (HU-004, HU-005) — /api/mascotas
///
///  Método  Ruta      Roles
///  GET     /         los 4 roles (cliente: solo las suyas)
///  POST    /buscar   los 4 roles (cliente: solo las suyas)
///  POST    /         admin, recepcionista
///  GET     /{id}     los 4 roles (cliente: solo las suyas; ajena = 404)
///  PUT     /{id}     admin, recepcionista, veterinario
///  DELETE  /{id}     admin, recepcionista
/// </summary>
[Route("api/mascotas")]
public sealed class MascotasController(MascotasServicio servicio) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Listar([FromQuery] PaginacionConsulta consulta, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(consulta, null, ct));

    [HttpPost("buscar")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Buscar([FromBody] BusquedaSolicitud busqueda, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(busqueda, busqueda.Texto, ct));

    [HttpPost]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Crear([FromBody] CrearMascotaSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.CrearAsync(solicitud, ct));

    [HttpGet("{id}")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerAsync(id, ct));

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Personal)]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarMascotaSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ActualizarAsync(id, solicitud, ct));

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await servicio.EliminarAsync(id, ct);
        return Mensaje("Mascota eliminada");
    }
}
