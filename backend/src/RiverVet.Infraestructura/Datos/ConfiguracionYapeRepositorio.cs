using RiverVet.Aplicacion.Pagos;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// Configuración de Yape editable desde el panel: una sola fila (id = 1) que
/// se crea la primera vez que el administrador guarda algo (MERGE).
/// </summary>
public sealed class ConfiguracionYapeRepositorio(SesionBd sesion) : RepositorioBase(sesion), IConfiguracionYapeRepositorio
{
    public Task<ConfiguracionYapeGuardada?> ObtenerAsync(CancellationToken ct = default) =>
        ConsultarUnoAsync<ConfiguracionYapeGuardada>(
            """
            SELECT celular, titular, CAST(CASE WHEN qr IS NULL THEN 0 ELSE 1 END AS BIT) AS tieneQr,
                   qr_actualizado_en AS qrActualizadoEn
            FROM dbo.configuracion_yape
            WHERE id = 1
            """,
            null,
            ct);

    public Task<ImagenQr?> ObtenerQrAsync(CancellationToken ct = default) =>
        ConsultarUnoAsync<ImagenQr>(
            "SELECT qr AS contenido, qr_tipo AS tipo FROM dbo.configuracion_yape WHERE id = 1 AND qr IS NOT NULL",
            null,
            ct);

    public Task GuardarDatosAsync(string celular, string titular, int? usuarioId, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            MERGE dbo.configuracion_yape WITH (HOLDLOCK) AS destino
            USING (SELECT 1 AS id) AS origen ON destino.id = origen.id
            WHEN MATCHED THEN
                UPDATE SET celular = @celular, titular = @titular,
                           actualizado_por = @usuarioId, actualizado_en = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (id, celular, titular, actualizado_por) VALUES (1, @celular, @titular, @usuarioId);
            """,
            new { celular = Ascii(celular, 9, fija: true), titular = Unicode(titular, 100), usuarioId },
            ct);

    public Task GuardarQrAsync(ImagenQr imagen, int? usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        return EjecutarAsync(
            """
            MERGE dbo.configuracion_yape WITH (HOLDLOCK) AS destino
            USING (SELECT 1 AS id) AS origen ON destino.id = origen.id
            WHEN MATCHED THEN
                UPDATE SET qr = @qr, qr_tipo = @tipo, qr_actualizado_en = SYSUTCDATETIME(),
                           actualizado_por = @usuarioId, actualizado_en = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (id, qr, qr_tipo, qr_actualizado_en, actualizado_por)
                VALUES (1, @qr, @tipo, SYSUTCDATETIME(), @usuarioId);
            """,
            new { qr = imagen.Contenido, tipo = Ascii(imagen.Tipo, 20), usuarioId },
            ct);
    }
}
