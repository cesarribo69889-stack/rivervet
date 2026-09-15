using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Api.Filtros;

/// <summary>
/// Antes de ejecutar CUALQUIER acción de un controlador:
///   1) limpia cada argumento que lo permita (INormalizable: recortar
///      espacios, email en minúsculas, quitar HTML del texto libre);
///   2) lo valida con su validador de FluentValidation (si tiene uno).
/// Si hay errores, responde 422 con el detalle por campo y la acción NO se
/// ejecuta. Así ningún controlador puede "olvidarse" de validar.
/// </summary>
public sealed class FiltroValidacion(IServiceProvider servicios) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var errores = new List<ErrorCampo>();

        foreach (var argumento in context.ActionArguments.Values)
        {
            if (argumento is null)
            {
                continue;
            }
            if (argumento is INormalizable normalizable)
            {
                normalizable.Normalizar();
            }

            var tipoValidador = typeof(IValidator<>).MakeGenericType(argumento.GetType());
            if (servicios.GetService(tipoValidador) is not IValidator validador)
            {
                continue;
            }
            var resultado = await validador.ValidateAsync(
                new ValidationContext<object>(argumento), context.HttpContext.RequestAborted);
            errores.AddRange(resultado.Errors.Select(e => new ErrorCampo(CamelCase(e.PropertyName), e.ErrorMessage)));
        }

        if (errores.Count > 0)
        {
            throw new ValidacionException(errores);
        }
        await next();
    }

    // "Items[0].Cantidad" -> "items[0].cantidad": cada tramo en camelCase,
    // igual que los nombres del JSON.
    private static string CamelCase(string nombre) =>
        string.IsNullOrEmpty(nombre)
            ? "(general)"
            : string.Join('.', nombre.Split('.').Select(t => t.Length == 0 ? t : char.ToLowerInvariant(t[0]) + t[1..]));
}
