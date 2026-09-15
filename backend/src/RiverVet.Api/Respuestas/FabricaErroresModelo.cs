using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Api.Respuestas;

/// <summary>
/// Traduce los errores de "model binding" (lectura del JSON, de la URL y de
/// la query) a nuestra forma de error:
///   - JSON mal formado                         → 400
///   - campo NO permitido (lista blanca)        → 422 "Campo no permitido"
///   - tipo de dato incorrecto / valor inválido → 422 con el campo
/// </summary>
public static partial class FabricaErroresModelo
{
    public static IActionResult Crear(ActionContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var errores = new List<ErrorCampo>();
        var jsonMalFormado = false;

        foreach (var (clave, entrada) in contexto.ModelState)
        {
            foreach (var error in entrada.Errors)
            {
                var mensaje = error.ErrorMessage;
                var noMapeado = CampoNoMapeado().Match(mensaje);
                if (noMapeado.Success)
                {
                    // JsonUnmappedMemberHandling.Disallow: llegó un campo que el
                    // DTO no declara (p. ej. "rol": "admin" en un formulario).
                    errores.Add(new ErrorCampo(NombreCampo(noMapeado.Groups[1].Value), "Campo no permitido"));
                }
                else if (mensaje.Contains("could not be converted", StringComparison.Ordinal))
                {
                    errores.Add(new ErrorCampo(NombreCampo(clave), "Tipo de dato inválido"));
                }
                else if (clave.StartsWith('$') || clave.Length == 0 || mensaje.Contains("request body", StringComparison.OrdinalIgnoreCase))
                {
                    jsonMalFormado = true;
                }
                else
                {
                    errores.Add(new ErrorCampo(NombreCampo(clave), "Valor inválido"));
                }
            }
        }

        var traceId = contexto.HttpContext.TraceIdentifier;
        if (jsonMalFormado && errores.Count == 0)
        {
            return new BadRequestObjectResult(
                new RespuestaError("El cuerpo de la solicitud no es un JSON válido", [], traceId));
        }
        return new UnprocessableEntityObjectResult(
            new RespuestaError("Los datos enviados no son válidos", errores, traceId));
    }

    /// <summary>"$.cliente.nombreCompleto" o "Pagina" → "cliente.nombreCompleto" / "pagina".</summary>
    private static string NombreCampo(string clave)
    {
        var limpio = clave.TrimStart('$', '.');
        if (limpio.Length == 0)
        {
            return "(general)";
        }
        return string.Join('.', limpio.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
    }

    [GeneratedRegex("JSON property '([^']+)' could not be mapped")]
    private static partial Regex CampoNoMapeado();
}
