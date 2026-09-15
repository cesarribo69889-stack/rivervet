namespace RiverVet.Aplicacion.Comun;

/// <summary>Error de un campo concreto, para mostrarlo junto al input del formulario.</summary>
public sealed record ErrorCampo(string Campo, string Mensaje);

/// <summary>
/// Error "esperado" de la aplicación: su mensaje es apto para mostrarse al
/// usuario. Cualquier excepción que NO herede de aquí se trata como
/// inesperada (500) y su detalle nunca sale del servidor.
/// El manejador global (RiverVet.Api) traduce cada tipo a su código HTTP.
/// </summary>
public abstract class RiverVetException : Exception
{
    protected RiverVetException(string mensaje)
        : base(mensaje) { }

    protected RiverVetException() { }

    protected RiverVetException(string mensaje, Exception interna)
        : base(mensaje, interna) { }
}

/// <summary>422 — los datos no cumplen las reglas. Trae el detalle por campo.</summary>
public sealed class ValidacionException : RiverVetException
{
    public ValidacionException(IReadOnlyList<ErrorCampo> errores, string mensaje = "Los datos enviados no son válidos")
        : base(mensaje) => Errores = errores;

    public ValidacionException(string campo, string mensaje)
        : this([new ErrorCampo(campo, mensaje)]) { }

    public ValidacionException()
        : this([]) { }

    public ValidacionException(string mensaje, Exception interna)
        : base(mensaje, interna) => Errores = [];

    public IReadOnlyList<ErrorCampo> Errores { get; }
}

/// <summary>401 — no hay sesión válida (o las credenciales son incorrectas).</summary>
public sealed class NoAutenticadoException : RiverVetException
{
    public NoAutenticadoException(string mensaje = "Debes iniciar sesión")
        : base(mensaje) { }

    public NoAutenticadoException(string mensaje, Exception interna)
        : base(mensaje, interna) { }

    public NoAutenticadoException()
        : this("Debes iniciar sesión") { }
}

/// <summary>403 — hay sesión, pero no se permite la acción.</summary>
public sealed class AccesoDenegadoException : RiverVetException
{
    public AccesoDenegadoException(string mensaje = "No tienes permiso para realizar esta acción")
        : base(mensaje) { }

    public AccesoDenegadoException(string mensaje, Exception interna)
        : base(mensaje, interna) { }

    public AccesoDenegadoException()
        : this("No tienes permiso para realizar esta acción") { }
}

/// <summary>
/// 404 — no existe, o NO ES TUYO. Pedir un recurso ajeno responde 404 y no
/// 403: responder "no tienes permiso" confirmaría que ese id existe.
/// </summary>
public sealed class NoEncontradoException : RiverVetException
{
    public NoEncontradoException(string mensaje = "Recurso no encontrado")
        : base(mensaje) { }

    public NoEncontradoException(string mensaje, Exception interna)
        : base(mensaje, interna) { }

    public NoEncontradoException()
        : this("Recurso no encontrado") { }
}

/// <summary>409 — duplicado (DNI, email) o regla de negocio que impide la operación.</summary>
public sealed class ConflictoException : RiverVetException
{
    public ConflictoException(string mensaje)
        : base(mensaje) { }

    public ConflictoException(string mensaje, Exception interna)
        : base(mensaje, interna) { }

    public ConflictoException()
        : this("El registro ya existe") { }
}

/// <summary>
/// 503 — la función depende de un servicio externo que no está configurado
/// en este entorno (p. ej. la pasarela de pagos o el número de Yape).
/// </summary>
public sealed class ServicioNoDisponibleException : RiverVetException
{
    public ServicioNoDisponibleException(string mensaje)
        : base(mensaje) { }

    public ServicioNoDisponibleException(string mensaje, Exception interna)
        : base(mensaje, interna) { }

    public ServicioNoDisponibleException()
        : this("Este servicio no está disponible en este momento") { }
}

/// <summary>429 — demasiados intentos.</summary>
public sealed class DemasiadosIntentosException : RiverVetException
{
    public DemasiadosIntentosException(string mensaje)
        : base(mensaje) { }

    public DemasiadosIntentosException(string mensaje, Exception interna)
        : base(mensaje, interna) { }

    public DemasiadosIntentosException()
        : this("Demasiados intentos. Espera unos minutos.") { }
}
