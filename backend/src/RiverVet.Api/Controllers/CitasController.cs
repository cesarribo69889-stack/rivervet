using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Citas;
using RiverVet.Aplicacion.Usuarios;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Citas (HU-008, HU-009, HU-010) — /api/citas
///
///  Método  Ruta                Roles
///  GET     /                   los 4 roles (cliente: solo las de sus mascotas)
///  GET     /disponibilidad     admin, recepcionista, veterinario
///  GET     /{id}               los 4 roles (cliente: solo las suyas; ajena = 404)
///  POST    /                   admin, recepcionista
///  PUT     /{id}               admin, recepcionista            (reprogramar)
///  POST    /{id}/cancelar      admin, recepcionista, cliente   (cliente: solo las suyas)
/// </summary>
[Route("api/citas")]
public sealed class CitasController(CitasServicio servicio) : ApiControllerBase
{
    private const string PuedenCancelar = Roles.AdminORecepcion + "," + Roles.Cliente;

    [HttpGet]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Listar([FromQuery] ConsultaCitas consulta, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(consulta, ct));

    [HttpGet("disponibilidad")]
    [Authorize(Roles = Roles.Personal)]
    public async Task<IActionResult> Disponibilidad([FromQuery] ConsultaDisponibilidad consulta, CancellationToken ct) =>
        Exito(await servicio.DisponibilidadAsync(consulta, ct));

    [HttpGet("{id}")]
    [Authorize(Roles = Roles.Todos)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Agendar([FromBody] AgendarCitaSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.AgendarAsync(solicitud, ct));

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Reprogramar(int id, [FromBody] ReprogramarCitaSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ReprogramarAsync(id, solicitud, ct));

    [HttpPost("{id}/cancelar")]
    [Authorize(Roles = PuedenCancelar)]
    public async Task<IActionResult> Cancelar(int id, [FromBody] CancelarCitaSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.CancelarAsync(id, solicitud, ct));
}

/// <summary>GET /api/veterinarios — veterinarios activos (para agendar). Personal.</summary>
[Route("api/veterinarios")]
[Authorize(Roles = Roles.Personal)]
public sealed class VeterinariosController(UsuariosServicio servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) => Exito(await servicio.ListarVeterinariosAsync(ct));
}
