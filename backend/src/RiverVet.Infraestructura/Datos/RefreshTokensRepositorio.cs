using RiverVet.Aplicacion.Autenticacion;

namespace RiverVet.Infraestructura.Datos;

public sealed class RefreshTokensRepositorio(SesionBd sesion) : RepositorioBase(sesion), IRefreshTokensRepositorio
{
    public Task<long> InsertarAsync(
        int usuarioId, string tokenHash, Guid familiaId, int dias, string? ip, string? userAgent, CancellationToken ct = default) =>
        EscalarAsync<long>(
            """
            INSERT INTO dbo.refresh_tokens (usuario_id, token_hash, familia_id, expira_en, creado_ip, user_agent)
            OUTPUT INSERTED.id
            VALUES (@usuarioId, @hash, @familia, DATEADD(DAY, @dias, SYSUTCDATETIME()), @ip, @userAgent)
            """,
            new
            {
                usuarioId,
                hash = Ascii(tokenHash, 64, fija: true),
                familia = familiaId,
                dias,
                ip = Ascii(ip, 45),
                userAgent = Unicode(Recortar(userAgent, 300), 300),
            },
            ct);

    // UPDLOCK: si llegan dos refresh simultáneos con el mismo token, el
    // segundo ESPERA a que el primero termine y luego ve el token ya rotado.
    // Sin el bloqueo, ambos podrían leerlo "vigente" a la vez y emitir dos
    // sesiones.
    public Task<RefreshTokenRegistro?> BuscarParaRotarAsync(string tokenHash, CancellationToken ct = default) =>
        ConsultarUnoAsync<RefreshTokenRegistro>(
            """
            SELECT rt.id, rt.usuario_id AS usuarioId, rt.familia_id AS familiaId,
                   rt.revocado_en AS revocadoEn, rt.motivo_revocacion AS motivoRevocacion,
                   DATEDIFF(SECOND, rt.revocado_en, SYSUTCDATETIME()) AS segundosDesdeRevocacion,
                   CAST(CASE WHEN rt.expira_en <= SYSUTCDATETIME() THEN 1 ELSE 0 END AS BIT) AS expirado,
                   CAST(CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.refresh_tokens f
                          WHERE f.familia_id = rt.familia_id AND f.revocado_en IS NULL
                            AND f.expira_en > SYSUTCDATETIME())
                        THEN 1 ELSE 0 END AS BIT) AS familiaActiva,
                   u.nombre, u.email, u.rol,
                   CAST(CASE WHEN u.activo = 1 AND u.eliminado = 0 THEN 1 ELSE 0 END AS BIT) AS usuarioActivo
            FROM dbo.refresh_tokens rt WITH (UPDLOCK, ROWLOCK)
            JOIN dbo.usuarios u ON u.id = rt.usuario_id
            WHERE rt.token_hash = @hash
            """,
            new { hash = Ascii(tokenHash, 64, fija: true) },
            ct);

    public Task MarcarRotadoAsync(long id, long reemplazoId, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.refresh_tokens
            SET revocado_en = SYSUTCDATETIME(), motivo_revocacion = 'rotado', reemplazado_por_id = @reemplazo
            WHERE id = @id AND revocado_en IS NULL
            """,
            new { id, reemplazo = reemplazoId },
            ct);

    public Task RevocarFamiliaAsync(Guid familiaId, string motivo, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.refresh_tokens SET revocado_en = SYSUTCDATETIME(), motivo_revocacion = @motivo
            WHERE familia_id = @familia AND revocado_en IS NULL
            """,
            new { familia = familiaId, motivo = Ascii(motivo, 30) },
            ct);

    public Task RevocarTodosDelUsuarioAsync(int usuarioId, string motivo, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.refresh_tokens SET revocado_en = SYSUTCDATETIME(), motivo_revocacion = @motivo
            WHERE usuario_id = @usuarioId AND revocado_en IS NULL
            """,
            new { usuarioId, motivo = Ascii(motivo, 30) },
            ct);

    public Task<RefreshTokenBasico?> BuscarPorHashAsync(string tokenHash, CancellationToken ct = default) =>
        ConsultarUnoAsync<RefreshTokenBasico>(
            """
            SELECT id, usuario_id AS usuarioId, familia_id AS familiaId
            FROM dbo.refresh_tokens WHERE token_hash = @hash
            """,
            new { hash = Ascii(tokenHash, 64, fija: true) },
            ct);

    private static string? Recortar(string? texto, int maximo) =>
        texto is null || texto.Length <= maximo ? texto : texto[..maximo];
}
