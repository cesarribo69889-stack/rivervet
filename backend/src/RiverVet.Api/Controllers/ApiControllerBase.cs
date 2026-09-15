using Microsoft.AspNetCore.Mvc;
using RiverVet.Api.Respuestas;

namespace RiverVet.Api.Controllers;

/// <summary>Base de los controladores: respuestas con la forma { success, data }.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected OkObjectResult Exito<T>(T datos) => Ok(new RespuestaApi<T>(datos));

    protected ObjectResult Creado<T>(T datos) => StatusCode(StatusCodes.Status201Created, new RespuestaApi<T>(datos));

    protected OkObjectResult Mensaje(string mensaje) => Ok(new RespuestaApi<object>(new { mensaje }));
}
