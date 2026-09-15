namespace RiverVet.Aplicacion.Comun;

/// <summary>
/// La base de datos rechazó un INSERT/UPDATE por una restricción de unicidad
/// (errores 2627 y 2601 de SQL Server). La lanza la infraestructura con el
/// NOMBRE de la restricción (UX_clientes_dni, ...) y el servicio la traduce a
/// un 409 con un mensaje claro. Así la capa de aplicación no depende de
/// SqlException.
/// </summary>
public sealed class RegistroDuplicadoException : Exception
{
    public RegistroDuplicadoException(string? restriccion, Exception interna)
        : base($"Violación de unicidad: {restriccion}", interna) => Restriccion = restriccion;

    public RegistroDuplicadoException() { }

    public RegistroDuplicadoException(string mensaje)
        : base(mensaje) { }

    public string? Restriccion { get; }

    /// <summary>Convierte a 409 usando el mensaje asociado a la restricción.</summary>
    public ConflictoException ComoConflicto(IReadOnlyDictionary<string, string> mensajes)
    {
        ArgumentNullException.ThrowIfNull(mensajes);
        var mensaje = Restriccion is not null && mensajes.TryGetValue(Restriccion, out var m) ? m : "El registro ya existe";
        return new ConflictoException(mensaje, this);
    }
}
