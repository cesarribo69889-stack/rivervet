namespace RiverVet.Aplicacion.Comun;

/// <summary>
/// Solicitudes que limpian sus datos ANTES de validarse: recortar espacios,
/// pasar el email a minúsculas, eliminar etiquetas HTML del texto libre.
/// El filtro de validación de la API llama a Normalizar() y luego valida.
/// </summary>
public interface INormalizable
{
    void Normalizar();
}
