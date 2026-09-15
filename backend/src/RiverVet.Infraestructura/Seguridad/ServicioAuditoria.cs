using System.Globalization;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Auditoria;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Infraestructura.Seguridad;

/// <summary>
/// Registra quién hizo qué, sobre qué recurso, cuándo y desde qué IP.
/// Usa la sesión de base de datos de la petición: si hay una transacción
/// abierta, el registro se escribe dentro de ella.
/// El detalle pasa por RedactorDatos: nunca contraseñas ni tokens.
/// </summary>
public sealed class ServicioAuditoria(IAuditoriaRepositorio repositorio, IContextoPeticion contexto) : IServicioAuditoria
{
    public Task RegistrarAsync(
        string accion,
        string recurso,
        object? recursoId = null,
        object? detalle = null,
        int? usuarioId = null,
        CancellationToken ct = default)
    {
        var agente = contexto.UserAgent;
        var registro = new NuevoRegistroAuditoria(
            usuarioId ?? contexto.UsuarioId,
            accion,
            recurso,
            recursoId is null ? null : Convert.ToString(recursoId, CultureInfo.InvariantCulture),
            RedactorDatos.SerializarRedactado(detalle),
            contexto.Ip,
            agente is { Length: > 300 } ? agente[..300] : agente);
        return repositorio.InsertarAsync(registro, ct);
    }
}
