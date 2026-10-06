using System.Globalization;
using RiverVet.Aplicacion.Tablero;

namespace RiverVet.Infraestructura.Datos;

public sealed class TableroRepositorio(SesionBd sesion) : RepositorioBase(sesion), ITableroRepositorio
{
    // Una sola ida a la base. Cada conteo usa índices existentes:
    // IX_citas_fecha (fecha_hora) e IX_vacunas_proxima_dosis.
    public async Task<EstadisticasTablero> EstadisticasAsync(
        DateOnly hoy, DateTime inicioHoyUtc, DateTime finHoyUtc, DateTime inicioMesUtc, CancellationToken ct = default) =>
        await ConsultarUnoAsync<EstadisticasTablero>(
            """
            SELECT
              @hoy AS fecha,
              (SELECT COUNT(*) FROM dbo.citas
                 WHERE eliminado = 0 AND estado <> 'cancelada' AND fecha_hora >= @inicioHoy AND fecha_hora < @finHoy) AS citasHoy,
              (SELECT COUNT(*) FROM dbo.citas
                 WHERE eliminado = 0 AND estado = 'atendida' AND fecha_hora >= @inicioHoy AND fecha_hora < @finHoy) AS citasHoyAtendidas,
              (SELECT COUNT(*) FROM dbo.citas
                 WHERE eliminado = 0 AND estado = 'programada' AND fecha_hora >= SYSUTCDATETIME()) AS citasPendientes,
              (SELECT COUNT(*) FROM dbo.citas
                 WHERE eliminado = 0 AND estado = 'programada' AND fecha_hora < SYSUTCDATETIME()) AS citasSinCerrar,
              (SELECT COUNT(*) FROM dbo.citas
                 WHERE eliminado = 0 AND estado = 'atendida' AND fecha_hora >= @inicioMes AND fecha_hora < @finHoy) AS citasAtendidasMes,
              (SELECT COUNT(*) FROM dbo.clientes WHERE eliminado = 0 AND creado_en >= @inicioMes) AS clientesNuevosMes,
              (SELECT COUNT(*) FROM dbo.vacunas va
                 JOIN dbo.mascotas m ON m.id = va.mascota_id AND m.eliminado = 0
                 WHERE va.eliminado = 0
                   AND va.proxima_dosis BETWEEN CAST(@hoy AS DATE) AND DATEADD(DAY, 30, CAST(@hoy AS DATE))
                   -- Igual que los recordatorios: solo la última aplicación de cada tipo.
                   AND NOT EXISTS (SELECT 1 FROM dbo.vacunas p
                                   WHERE p.mascota_id = va.mascota_id AND p.tipo_vacuna = va.tipo_vacuna
                                     AND p.eliminado = 0 AND p.fecha_aplicacion > va.fecha_aplicacion)) AS vacunasPorVencer,
              (SELECT COUNT(*) FROM dbo.vacunas va
                 JOIN dbo.mascotas m ON m.id = va.mascota_id AND m.eliminado = 0
                 WHERE va.eliminado = 0
                   AND va.proxima_dosis BETWEEN CAST(@hoy AS DATE) AND DATEADD(DAY, 7, CAST(@hoy AS DATE))
                   AND NOT EXISTS (SELECT 1 FROM dbo.vacunas p
                                   WHERE p.mascota_id = va.mascota_id AND p.tipo_vacuna = va.tipo_vacuna
                                     AND p.eliminado = 0 AND p.fecha_aplicacion > va.fecha_aplicacion)) AS vacunasUrgentes
            """,
            new
            {
                hoy = Ascii(hoy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 10),
                inicioHoy = inicioHoyUtc,
                finHoy = finHoyUtc,
                inicioMes = inicioMesUtc,
            },
            ct) ?? new EstadisticasTablero();
}
