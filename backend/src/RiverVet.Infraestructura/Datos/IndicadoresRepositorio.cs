using RiverVet.Aplicacion.Indicadores;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// HU-018: consultas de agregación del dashboard. Los rangos llegan en UTC
/// (inicio y fin de los días de Lima) y se comparan con columnas UTC.
/// </summary>
public sealed class IndicadoresRepositorio(SesionBd sesion) : RepositorioBase(sesion), IIndicadoresRepositorio
{
    // Venta "cobrada" = pedido pagado o entregado.
    private const string Cobrado = "pe.estado IN ('pagado', 'entregado')";

    public async Task<ResumenVentas> VentasAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
        await ConsultarUnoAsync<ResumenVentas>(
            $"""
            SELECT CAST(COALESCE(SUM(CASE WHEN {Cobrado} THEN pe.total END), 0) AS DECIMAL(12,2)) AS total,
                   COUNT(CASE WHEN {Cobrado} THEN 1 END) AS pedidos,
                   CAST(COALESCE(AVG(CASE WHEN {Cobrado} THEN pe.total END), 0) AS DECIMAL(12,2)) AS ticketPromedio,
                   CAST(COALESCE(SUM(CASE WHEN {Cobrado} AND pe.canal = 'web' THEN pe.total END), 0) AS DECIMAL(12,2)) AS totalWeb,
                   CAST(COALESCE(SUM(CASE WHEN {Cobrado} AND pe.canal = 'presencial' THEN pe.total END), 0) AS DECIMAL(12,2)) AS totalPresencial,
                   COUNT(CASE WHEN pe.estado IN ('pendiente_pago', 'pendiente_verificacion') THEN 1 END) AS pendientesDeCobro
            FROM dbo.pedidos pe
            WHERE pe.eliminado = 0 AND pe.creado_en >= @desde AND pe.creado_en < @hasta
            """,
            new { desde = desdeUtc, hasta = hastaUtc },
            ct) ?? new ResumenVentas();

    // Día de Lima = hora UTC - 5 (el Perú no usa horario de verano desde 1994).
    public Task<IReadOnlyList<VentaDiaria>> VentasPorDiaAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
        ConsultarAsync<VentaDiaria>(
            $"""
            SELECT CONVERT(CHAR(10), CAST(DATEADD(HOUR, -5, pe.creado_en) AS DATE), 23) AS fecha,
                   CAST(SUM(pe.total) AS DECIMAL(12,2)) AS total,
                   COUNT(*) AS pedidos
            FROM dbo.pedidos pe
            WHERE pe.eliminado = 0 AND {Cobrado} AND pe.creado_en >= @desde AND pe.creado_en < @hasta
            GROUP BY CAST(DATEADD(HOUR, -5, pe.creado_en) AS DATE)
            ORDER BY fecha
            """,
            new { desde = desdeUtc, hasta = hastaUtc },
            ct);

    public async Task<ResumenCitas> CitasAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
        await ConsultarUnoAsync<ResumenCitas>(
            """
            SELECT COUNT(CASE WHEN estado = 'atendida' THEN 1 END) AS atendidas,
                   COUNT(CASE WHEN estado = 'programada' THEN 1 END) AS programadas,
                   COUNT(CASE WHEN estado = 'cancelada' THEN 1 END) AS canceladas,
                   COUNT(CASE WHEN estado = 'no_asistio' THEN 1 END) AS noAsistio
            FROM dbo.citas
            WHERE eliminado = 0 AND fecha_hora >= @desde AND fecha_hora < @hasta
            """,
            new { desde = desdeUtc, hasta = hastaUtc },
            ct) ?? new ResumenCitas();

    public Task<int> ClientesNuevosAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
        EscalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.clientes WHERE eliminado = 0 AND creado_en >= @desde AND creado_en < @hasta",
            new { desde = desdeUtc, hasta = hastaUtc },
            ct);

    public Task<IReadOnlyList<ProductoStockBajo>> StockBajoAsync(CancellationToken ct = default) =>
        ConsultarAsync<ProductoStockBajo>(
            """
            SELECT TOP (20) id, sku, nombre, stock, stock_minimo AS stockMinimo
            FROM dbo.productos
            WHERE eliminado = 0 AND activo = 1 AND stock <= stock_minimo
            ORDER BY stock, nombre
            """,
            null,
            ct);

    public Task<IReadOnlyList<ProductoMasVendido>> MasVendidosAsync(DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
        ConsultarAsync<ProductoMasVendido>(
            $"""
            SELECT TOP (5) pi.producto_id AS productoId, p.nombre,
                   SUM(pi.cantidad) AS cantidad, CAST(SUM(pi.subtotal) AS DECIMAL(12,2)) AS monto
            FROM dbo.pedido_items pi
            JOIN dbo.pedidos pe ON pe.id = pi.pedido_id
            JOIN dbo.productos p ON p.id = pi.producto_id
            WHERE pe.eliminado = 0 AND {Cobrado} AND pe.creado_en >= @desde AND pe.creado_en < @hasta
            GROUP BY pi.producto_id, p.nombre
            ORDER BY cantidad DESC, monto DESC
            """,
            new { desde = desdeUtc, hasta = hastaUtc },
            ct);
}
