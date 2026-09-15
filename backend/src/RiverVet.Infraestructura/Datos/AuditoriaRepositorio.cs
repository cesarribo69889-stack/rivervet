using RiverVet.Aplicacion.Auditoria;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// Auditoría. El usuario de base de datos de la app tiene DENY UPDATE/DELETE
/// sobre esta tabla: solo puede agregar filas, nunca borrar su rastro.
/// </summary>
public sealed class AuditoriaRepositorio(SesionBd sesion) : RepositorioBase(sesion), IAuditoriaRepositorio
{
    public Task InsertarAsync(NuevoRegistroAuditoria registro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return EjecutarAsync(
            """
            INSERT INTO dbo.auditoria (usuario_id, accion, recurso, recurso_id, detalle, ip, user_agent)
            VALUES (@usuarioId, @accion, @recurso, @recursoId, @detalle, @ip, @userAgent)
            """,
            new
            {
                usuarioId = registro.UsuarioId,
                accion = Ascii(registro.Accion, 40),
                recurso = Ascii(registro.Recurso, 40),
                recursoId = Ascii(registro.RecursoId, 40),
                detalle = Unicode(registro.DetalleJson, 2000),
                ip = Ascii(registro.Ip, 45),
                userAgent = Unicode(registro.UserAgent, 300),
            },
            ct);
    }

    public Task<IReadOnlyList<RegistroAuditoria>> ListarAsync(ConsultaAuditoria consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultarAsync<RegistroAuditoria>(
            """
            SELECT a.id, a.usuario_id AS usuarioId, u.nombre AS usuarioNombre, u.rol AS usuarioRol,
                   a.accion, a.recurso, a.recurso_id AS recursoId, a.detalle AS detalleJson, a.ip,
                   a.creado_en AS creadoEn, COUNT(*) OVER() AS total
            FROM dbo.auditoria a
            LEFT JOIN dbo.usuarios u ON u.id = a.usuario_id
            WHERE (@accion IS NULL OR a.accion = @accion)
              AND (@recurso IS NULL OR a.recurso = @recurso)
              AND (@usuarioId IS NULL OR a.usuario_id = @usuarioId)
            ORDER BY a.creado_en DESC, a.id DESC
            OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY
            """,
            new
            {
                accion = Ascii(consulta.Accion, 40),
                recurso = Ascii(consulta.Recurso, 40),
                usuarioId = consulta.UsuarioId,
                offset = consulta.Offset,
                limite = consulta.Limite,
            },
            ct);
    }
}
