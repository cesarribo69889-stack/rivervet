using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Usuarios;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Usuarios del personal (HU-001, alta por admin) — /api/usuarios
///
///  Método  Ruta    Roles
///  GET     /       admin
///  POST    /       admin
///  GET     /{id}   admin
///  PUT     /{id}   admin
/// </summary>
[Route("api/usuarios")]
[Authorize(Roles = Roles.Admin)]
public sealed class UsuariosController(UsuariosServicio servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] ListarUsuariosConsulta consulta, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(consulta, ct));

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearUsuarioSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.CrearAsync(solicitud, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerAsync(id, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarUsuarioSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ActualizarAsync(id, solicitud, ct));
}
