using RiverVet.Aplicacion.Comprobantes;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Pagos;

namespace RiverVet.Infraestructura.Datos;

/// <summary>Pagos (HU-017, HU-019) y eventos de webhook. Sin columnas de tarjeta: no existen.</summary>
public sealed class PagosRepositorio(SesionBd sesion) : RepositorioBase(sesion), IPagosRepositorio
{
    private const string Columnas = "id, pedido_id AS pedidoId, metodo, estado, monto, moneda";

    public Task<int> CrearAsync(
        int pedidoId, string metodo, string estado, decimal monto, string? codigoOperacion,
        string? proveedor, string? referencia, int? verificadoPor, CancellationToken ct = default) =>
        EscalarAsync<int>(
            """
            INSERT INTO dbo.pagos (pedido_id, metodo, estado, monto, codigo_operacion, proveedor, referencia_externa,
                                   verificado_por, verificado_en)
            OUTPUT INSERTED.id
            VALUES (@pedidoId, @metodo, @estado, @monto, @codigoOperacion, @proveedor, @referencia,
                    @verificadoPor, CASE WHEN @verificadoPor IS NULL THEN NULL ELSE SYSUTCDATETIME() END)
            """,
            new
            {
                pedidoId,
                metodo = Ascii(metodo, 15),
                estado = Ascii(estado, 25),
                monto,
                codigoOperacion = Ascii(codigoOperacion, 30),
                proveedor = Ascii(proveedor, 30),
                referencia = Ascii(referencia, 100),
                verificadoPor,
            },
            ct);

    public Task<PagoRegistrado?> ObtenerAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<PagoRegistrado>($"SELECT {Columnas} FROM dbo.pagos WHERE id = @id", new { id }, ct);

    public Task<PagoRegistrado?> BuscarPorReferenciaAsync(string proveedor, string referencia, CancellationToken ct = default) =>
        ConsultarUnoAsync<PagoRegistrado>(
            $"SELECT {Columnas} FROM dbo.pagos WHERE proveedor = @proveedor AND referencia_externa = @referencia",
            new { proveedor = Ascii(proveedor, 30), referencia = Ascii(referencia, 100) },
            ct);

    public Task<PagoRegistrado?> BloquearAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<PagoRegistrado>($"SELECT {Columnas} FROM dbo.pagos WITH (UPDLOCK, ROWLOCK) WHERE id = @id", new { id }, ct);

    public async Task<bool> CambiarEstadoAsync(int id, string estadoEsperado, string estadoNuevo, int? verificadoPor, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.pagos
            SET estado = @estadoNuevo, actualizado_en = SYSUTCDATETIME(),
                verificado_por = COALESCE(@verificadoPor, verificado_por),
                verificado_en = CASE WHEN @verificadoPor IS NULL THEN verificado_en ELSE SYSUTCDATETIME() END
            WHERE id = @id AND estado = @estadoEsperado
            """,
            new { id, estadoEsperado = Ascii(estadoEsperado, 25), estadoNuevo = Ascii(estadoNuevo, 25), verificadoPor },
            ct) > 0;

    public Task<IReadOnlyList<PagoPendienteVerificacion>> PendientesDeVerificacionAsync(CancellationToken ct = default) =>
        ConsultarAsync<PagoPendienteVerificacion>(
            """
            SELECT pa.id AS pagoId, pe.id AS pedidoId, pe.codigo AS pedidoCodigo,
                   CONCAT(c.nombres, N' ', c.apellidos) AS clienteNombre, pa.monto,
                   pa.codigo_operacion AS codigoOperacion, pa.creado_en AS creadoEn
            FROM dbo.pagos pa
            JOIN dbo.pedidos pe ON pe.id = pa.pedido_id
            JOIN dbo.clientes c ON c.id = pe.cliente_id
            WHERE pa.metodo = 'yape' AND pa.estado = 'pendiente_verificacion'
            ORDER BY pa.creado_en
            """,
            null,
            ct);

    // El índice único (proveedor, evento_id) hace imposible registrar dos
    // veces el mismo aviso, incluso si llegan dos copias al mismo tiempo.
    public async Task<long?> RegistrarEventoAsync(string proveedor, string eventoId, string tipo, bool firmaValida, CancellationToken ct = default)
    {
        try
        {
            return await EscalarAsync<long>(
                """
                INSERT INTO dbo.webhook_eventos (proveedor, evento_id, tipo, firma_valida)
                OUTPUT INSERTED.id
                VALUES (@proveedor, @eventoId, @tipo, @firmaValida)
                """,
                new { proveedor = Ascii(proveedor, 30), eventoId = Ascii(eventoId, 100), tipo = Ascii(tipo, 60), firmaValida },
                ct);
        }
        catch (RegistroDuplicadoException)
        {
            return null;
        }
    }

    public Task MarcarEventoProcesadoAsync(long id, CancellationToken ct = default) =>
        EjecutarAsync("UPDATE dbo.webhook_eventos SET procesado = 1 WHERE id = @id", new { id }, ct);
}

public sealed class ComprobantesRepositorio(SesionBd sesion) : RepositorioBase(sesion), IComprobantesRepositorio
{
    public Task<DatosComprobante?> ObtenerAsync(int pedidoId, CancellationToken ct = default) =>
        ConsultarUnoAsync<DatosComprobante>(
            """
            SELECT pe.id AS pedidoId, pe.codigo, pe.estado, pe.total, pe.creado_en AS creadoEn, c.telefono AS clienteTelefono
            FROM dbo.pedidos pe
            JOIN dbo.clientes c ON c.id = pe.cliente_id
            WHERE pe.id = @pedidoId AND pe.eliminado = 0
            """,
            new { pedidoId },
            ct);

    public Task<int> EnviosRecientesAsync(int pedidoId, DateTime desdeUtc, CancellationToken ct = default) =>
        EscalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.envios_comprobante WHERE pedido_id = @pedidoId AND creado_en >= @desde",
            new { pedidoId, desde = desdeUtc },
            ct);

    public Task<int> RegistrarEnvioAsync(int pedidoId, string telefono, string estado, int? enviadoPor, CancellationToken ct = default) =>
        EscalarAsync<int>(
            """
            INSERT INTO dbo.envios_comprobante (pedido_id, telefono, estado, enviado_por)
            OUTPUT INSERTED.id
            VALUES (@pedidoId, @telefono, @estado, @enviadoPor)
            """,
            new { pedidoId, telefono = Ascii(telefono, 9, fija: true), estado = Ascii(estado, 15), enviadoPor },
            ct);
}
