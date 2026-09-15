using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Indicadores;
using RiverVet.Aplicacion.Ventas;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Carrito del cliente (HU-015) — /api/carrito. Solo rol cliente.
///
///  GET     /                     ver el carrito (precios actuales)
///  POST    /items                agregar { productoId, cantidad }
///  PUT     /items/{productoId}   cambiar cantidad { cantidad }
///  DELETE  /items/{productoId}   quitar
///  DELETE  /                     vaciar
/// </summary>
[Route("api/carrito")]
[Authorize(Roles = Roles.Cliente)]
public sealed class CarritoController(CarritoServicio servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener(CancellationToken ct) => Exito(await servicio.ObtenerAsync(ct));

    [HttpPost("items")]
    public async Task<IActionResult> Agregar([FromBody] AgregarItemSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.AgregarAsync(solicitud, ct));

    [HttpPut("items/{productoId}")]
    public async Task<IActionResult> CambiarCantidad(int productoId, [FromBody] CambiarCantidadSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.CambiarCantidadAsync(productoId, solicitud, ct));

    [HttpDelete("items/{productoId}")]
    public async Task<IActionResult> Quitar(int productoId, CancellationToken ct) => Exito(await servicio.QuitarAsync(productoId, ct));

    [HttpDelete]
    public async Task<IActionResult> Vaciar(CancellationToken ct) => Exito(await servicio.VaciarAsync(ct));
}

/// <summary>
/// Pedidos y ventas (HU-016) — /api/pedidos
///
///  Método  Ruta              Roles
///  GET     /                 admin, recepcionista, cliente (cliente: solo los suyos)
///  GET     /{id}             admin, recepcionista, cliente (ajeno = 404)
///  POST    /                 cliente          confirma SU carrito (sin body)
///  POST    /presencial       admin, recepcionista
///  POST    /{id}/cancelar    admin, recepcionista, cliente (cliente: los suyos, sin pagar)
///  POST    /{id}/entregar    admin, recepcionista
/// </summary>
[Route("api/pedidos")]
public sealed class PedidosController(PedidosServicio servicio) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Roles = Roles.VentasYCliente)]
    public async Task<IActionResult> Listar([FromQuery] ConsultaPedidos consulta, CancellationToken ct) =>
        Exito(await servicio.ListarAsync(consulta, ct));

    [HttpGet("{id}")]
    [Authorize(Roles = Roles.VentasYCliente)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct) => Exito(await servicio.ObtenerAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> ConfirmarCarrito(CancellationToken ct) => Creado(await servicio.ConfirmarCarritoAsync(ct));

    [HttpPost("presencial")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Presencial([FromBody] VentaPresencialSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.RegistrarPresencialAsync(solicitud, ct));

    [HttpPost("{id}/cancelar")]
    [Authorize(Roles = Roles.VentasYCliente)]
    public async Task<IActionResult> Cancelar(int id, [FromBody] CancelarPedidoSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.CancelarAsync(id, solicitud, ct));

    [HttpPost("{id}/entregar")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Entregar(int id, CancellationToken ct) => Exito(await servicio.EntregarAsync(id, ct));
}

/// <summary>GET /api/indicadores?desde=AAAA-MM-DD&amp;hasta=AAAA-MM-DD — HU-018. Solo admin.</summary>
[Route("api/indicadores")]
[Authorize(Roles = Roles.Admin)]
public sealed class IndicadoresController(IndicadoresServicio servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener([FromQuery] ConsultaIndicadores consulta, CancellationToken ct) =>
        Exito(await servicio.ObtenerAsync(consulta, ct));
}
