using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Productos;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Productos (HU-013, HU-014) — /api/productos
///
///  Método  Ruta                 Roles
///  GET     /                    PÚBLICO   catálogo paginado con filtros
///  GET     /{id}                PÚBLICO   solo si está a la venta
///  GET     /inventario          admin, recepcionista (con stock)
///  GET     /{id}/inventario     admin, recepcionista
///  POST    /                    admin
///  PUT     /{id}                admin     (no toca el stock)
///  POST    /{id}/stock          admin     ajuste relativo: +5 / -2
///  DELETE  /{id}                admin     baja lógica
/// </summary>
[Route("api/productos")]
public sealed class ProductosController(ProductosServicio servicio) : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Catalogo([FromQuery] ConsultaCatalogo consulta, CancellationToken ct) =>
        Exito(await servicio.CatalogoAsync(consulta, ct));

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerDelCatalogoAsync(id, ct));

    [HttpGet("inventario")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Inventario([FromQuery] ConsultaInventario consulta, CancellationToken ct) =>
        Exito(await servicio.InventarioAsync(consulta, ct));

    [HttpGet("{id}/inventario")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> ObtenerInventario(int id, CancellationToken ct) =>
        Exito(await servicio.ObtenerDelInventarioAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Crear([FromBody] CrearProductoSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.CrearAsync(solicitud, ct));

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarProductoSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ActualizarAsync(id, solicitud, ct));

    [HttpPost("{id}/stock")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AjustarStock(int id, [FromBody] AjusteStockSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.AjustarStockAsync(id, solicitud, ct));

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await servicio.EliminarAsync(id, ct);
        return Mensaje("Producto eliminado");
    }
}

/// <summary>
/// Categorías — /api/categorias
///
///  GET  /        PÚBLICO (solo activas)
///  GET  /todas   admin
///  POST /        admin
///  PUT  /{id}    admin
/// </summary>
[Route("api/categorias")]
public sealed class CategoriasController(ProductosServicio servicio) : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Listar(CancellationToken ct) => Exito(await servicio.ListarCategoriasAsync(false, ct));

    [HttpGet("todas")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ListarTodas(CancellationToken ct) => Exito(await servicio.ListarCategoriasAsync(true, ct));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Crear([FromBody] DatosCategoriaSolicitud solicitud, CancellationToken ct) =>
        Creado(await servicio.CrearCategoriaAsync(solicitud, ct));

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Actualizar(int id, [FromBody] DatosCategoriaSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.ActualizarCategoriaAsync(id, solicitud, ct));
}
