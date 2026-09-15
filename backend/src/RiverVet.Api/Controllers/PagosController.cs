using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Comprobantes;
using RiverVet.Aplicacion.Pagos;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Pagos y comprobante de un pedido (HU-017, HU-019, HU-020) — /api/pedidos/{pedidoId}
///
///  Método  Ruta                    Roles
///  POST    /pagos/yape             admin, recepcionista, cliente (cliente: sus pedidos)
///  POST    /pagos/pasarela         cliente (sus pedidos) → URL del checkout de la pasarela
///  POST    /pagos/efectivo         admin, recepcionista (cobro en caja)
///  POST    /comprobante/whatsapp   admin, recepcionista
///
/// Ninguna petición lleva el monto: sale del pedido guardado.
/// </summary>
[Route("api/pedidos/{pedidoId}")]
public sealed class PagosPedidoController(PagosServicio pagos, ComprobantesServicio comprobantes) : ApiControllerBase
{
    [HttpPost("pagos/yape")]
    [Authorize(Roles = Roles.VentasYCliente)]
    public async Task<IActionResult> Yape(int pedidoId, [FromBody] RegistrarYapeSolicitud solicitud, CancellationToken ct) =>
        Creado(await pagos.RegistrarYapeAsync(pedidoId, solicitud, ct));

    [HttpPost("pagos/pasarela")]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> Pasarela(int pedidoId, CancellationToken ct) =>
        Creado(await pagos.IniciarCheckoutAsync(pedidoId, ct));

    [HttpPost("pagos/efectivo")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Efectivo(int pedidoId, CancellationToken ct) =>
        Creado(await pagos.RegistrarEfectivoAsync(pedidoId, ct));

    [HttpPost("comprobante/whatsapp")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> ComprobanteWhatsApp(int pedidoId, [FromBody] EnviarComprobanteSolicitud solicitud, CancellationToken ct) =>
        Creado(await comprobantes.EnviarAsync(pedidoId, solicitud, ct));
}

/// <summary>
/// Pagos — /api/pagos
///
///  GET   /yape             admin, recepcionista, cliente   celular y titular del Yape del negocio
///  GET   /pendientes       admin, recepcionista            Yapes por verificar
///  POST  /{id}/verificar   admin, recepcionista            { aprobado, observacion }
/// </summary>
[Route("api/pagos")]
public sealed class PagosController(PagosServicio servicio) : ApiControllerBase
{
    [HttpGet("yape")]
    [Authorize(Roles = Roles.VentasYCliente)]
    public IActionResult DatosYape() => Exito(servicio.DatosYape());

    [HttpGet("pendientes")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Pendientes(CancellationToken ct) => Exito(await servicio.PendientesDeVerificacionAsync(ct));

    [HttpPost("{id}/verificar")]
    [Authorize(Roles = Roles.AdminORecepcion)]
    public async Task<IActionResult> Verificar(int id, [FromBody] VerificarPagoSolicitud solicitud, CancellationToken ct) =>
        Exito(await servicio.VerificarAsync(id, solicitud, ct));
}

/// <summary>
/// POST /api/webhooks/pasarela — lo llama la PASARELA (servidor a servidor),
/// no un navegador: no lleva sesión. Su "autenticación" es la firma HMAC de
/// las cabeceras X-Firma / X-Firma-Tiempo, que se verifica sobre los bytes
/// exactos del cuerpo; por eso se lee el body crudo en lugar de [FromBody].
/// </summary>
[Route("api/webhooks")]
public sealed class WebhooksController(PagosServicio servicio) : ApiControllerBase
{
    [HttpPost("pasarela")]
    [AllowAnonymous]
    public async Task<IActionResult> Pasarela(CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        await Request.Body.CopyToAsync(memoria, ct);
        var resultado = await servicio.ProcesarWebhookAsync(
            memoria.ToArray(),
            Request.Headers[FirmaWebhook.CabeceraTiempo].ToString(),
            Request.Headers[FirmaWebhook.CabeceraFirma].ToString(),
            ct);
        return Exito(resultado);
    }
}
