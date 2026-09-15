using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.DatosPersonales;
using RiverVet.Aplicacion.Triage;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Api.Controllers;

/// <summary>
/// Triage orientativo (HU-021) — /api/triage. PÚBLICO: sirve a quien aún no
/// es cliente. No se guarda nada de lo consultado.
///
///  GET   /sintomas   catálogo de síntomas
///  POST  /           { especie, sintomas: [...], edadMeses?, horasConSintomas? }
/// </summary>
[Route("api/triage")]
[AllowAnonymous]
public sealed class TriageController : ApiControllerBase
{
    [HttpGet("sintomas")]
    public IActionResult Sintomas() => Exito(new { sintomas = CatalogoSintomas.Todos, descargo = TriageServicio.Descargo });

    [HttpPost]
    public IActionResult Evaluar([FromBody] TriageSolicitud solicitud) => Exito(TriageServicio.Evaluar(solicitud));
}

/// <summary>
/// GET /api/clientes/yo/exportar — Ley N° 29733, derecho de acceso: el
/// cliente descarga todos sus datos personales. Solo rol cliente, y solo los
/// PROPIOS (se resuelven desde la sesión; la ruta no lleva ningún id).
/// </summary>
[Route("api/clientes/yo/exportar")]
[Authorize(Roles = Roles.Cliente)]
public sealed class DatosPersonalesController(ExportacionServicio servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Exportar(CancellationToken ct)
    {
        var datos = await servicio.ExportarAsync(ct);
        // El navegador lo ofrece como descarga.
        Response.Headers.ContentDisposition = "attachment; filename=\"mis-datos-rivervet.json\"";
        return Exito(datos);
    }
}
