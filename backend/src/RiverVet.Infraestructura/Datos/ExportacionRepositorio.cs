using RiverVet.Aplicacion.DatosPersonales;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// Ley N° 29733 (derecho de acceso). Cada consulta recibe el cliente o el
/// usuario de la SESIÓN, resuelto por el servicio a partir del token.
/// Incluye también lo dado de baja (eliminado = 1): el titular tiene derecho
/// a saber qué datos suyos se CONSERVAN, no solo los que se muestran.
/// </summary>
public sealed class ExportacionRepositorio(SesionBd sesion) : RepositorioBase(sesion), IExportacionRepositorio
{
    public Task<TitularExportado?> TitularAsync(int usuarioId, CancellationToken ct = default) =>
        ConsultarUnoAsync<TitularExportado>(
            """
            SELECT id, dni, nombres, apellidos, email, telefono, direccion,
                   consentimiento_datos AS consentimientoDatos, consentimiento_en AS consentimientoEn, creado_en AS creadoEn
            FROM dbo.clientes
            WHERE usuario_id = @usuarioId AND eliminado = 0
            """,
            new { usuarioId },
            ct);

    public Task<CuentaExportada?> CuentaAsync(int usuarioId, CancellationToken ct = default) =>
        ConsultarUnoAsync<CuentaExportada>(
            "SELECT nombre, email, rol, creado_en AS creadoEn, ultimo_login_en AS ultimoLoginEn FROM dbo.usuarios WHERE id = @usuarioId",
            new { usuarioId },
            ct);

    public Task<IReadOnlyList<MascotaExportada>> MascotasAsync(int clienteId, CancellationToken ct = default) =>
        ConsultarAsync<MascotaExportada>(
            """
            SELECT id, nombre, especie, raza, sexo, CONVERT(CHAR(10), fecha_nacimiento, 23) AS fechaNacimiento,
                   peso_kg AS pesoKg, color, senas_particulares AS senasParticulares, esterilizado
            FROM dbo.mascotas
            WHERE cliente_id = @clienteId
            ORDER BY nombre
            """,
            new { clienteId },
            ct);

    public Task<IReadOnlyList<CitaExportada>> CitasAsync(int clienteId, CancellationToken ct = default) =>
        ConsultarAsync<CitaExportada>(
            """
            SELECT m.nombre AS mascota, v.nombre AS veterinario, ci.fecha_hora AS fechaHora, ci.motivo, ci.estado,
                   ci.motivo_cancelacion AS motivoCancelacion
            FROM dbo.citas ci
            JOIN dbo.mascotas m ON m.id = ci.mascota_id
            JOIN dbo.usuarios v ON v.id = ci.veterinario_id
            WHERE m.cliente_id = @clienteId
            ORDER BY ci.fecha_hora DESC
            """,
            new { clienteId },
            ct);

    public Task<IReadOnlyList<AtencionExportada>> HistorialAsync(int clienteId, CancellationToken ct = default) =>
        ConsultarAsync<AtencionExportada>(
            """
            SELECT m.nombre AS mascota, v.nombre AS veterinario, h.fecha_atencion AS fechaAtencion, h.motivo,
                   h.diagnostico, h.tratamiento, h.observaciones, h.peso_kg AS pesoKg, h.temperatura_c AS temperaturaC
            FROM dbo.historial_clinico h
            JOIN dbo.mascotas m ON m.id = h.mascota_id
            JOIN dbo.usuarios v ON v.id = h.veterinario_id
            WHERE m.cliente_id = @clienteId
            ORDER BY h.fecha_atencion DESC
            """,
            new { clienteId },
            ct);

    public Task<IReadOnlyList<VacunaExportada>> VacunasAsync(int clienteId, CancellationToken ct = default) =>
        ConsultarAsync<VacunaExportada>(
            """
            SELECT m.nombre AS mascota, va.tipo_vacuna AS tipoVacuna, CONVERT(CHAR(10), va.fecha_aplicacion, 23) AS fechaAplicacion,
                   va.lote, CONVERT(CHAR(10), va.proxima_dosis, 23) AS proximaDosis
            FROM dbo.vacunas va
            JOIN dbo.mascotas m ON m.id = va.mascota_id
            WHERE m.cliente_id = @clienteId
            ORDER BY va.fecha_aplicacion DESC
            """,
            new { clienteId },
            ct);

    public Task<IReadOnlyList<PedidoExportado>> PedidosAsync(int clienteId, CancellationToken ct = default) =>
        ConsultarAsync<PedidoExportado>(
            """
            SELECT pe.codigo, pe.canal, pe.estado, pe.total, pe.creado_en AS creadoEn,
                   -- NVARCHAR(MAX): sin el CAST, STRING_AGG corta con error al pasar de 4000 caracteres.
                   COALESCE((SELECT STRING_AGG(CAST(CONCAT(pi.cantidad, N' x ', p.nombre, N' (S/ ', pi.precio_unitario, N')') AS NVARCHAR(MAX)), N'; ')
                             WITHIN GROUP (ORDER BY pi.id)
                             FROM dbo.pedido_items pi JOIN dbo.productos p ON p.id = pi.producto_id
                             WHERE pi.pedido_id = pe.id), N'') AS detalle
            FROM dbo.pedidos pe
            WHERE pe.cliente_id = @clienteId
            ORDER BY pe.creado_en DESC
            """,
            new { clienteId },
            ct);

    public Task<IReadOnlyList<PagoExportado>> PagosAsync(int clienteId, CancellationToken ct = default) =>
        ConsultarAsync<PagoExportado>(
            """
            SELECT pe.codigo AS pedido, pa.metodo, pa.estado, pa.monto, pa.codigo_operacion AS codigoOperacion, pa.creado_en AS creadoEn
            FROM dbo.pagos pa
            JOIN dbo.pedidos pe ON pe.id = pa.pedido_id
            WHERE pe.cliente_id = @clienteId
            ORDER BY pa.creado_en DESC
            """,
            new { clienteId },
            ct);

    public Task<IReadOnlyList<EnvioExportado>> EnviosAsync(int clienteId, CancellationToken ct = default) =>
        ConsultarAsync<EnvioExportado>(
            """
            SELECT pe.codigo AS pedido, en.telefono, en.estado, en.creado_en AS creadoEn
            FROM dbo.envios_comprobante en
            JOIN dbo.pedidos pe ON pe.id = en.pedido_id
            WHERE pe.cliente_id = @clienteId
            ORDER BY en.creado_en DESC
            """,
            new { clienteId },
            ct);

    // Las últimas 500 acciones de la cuenta (inicios de sesión, cambios de
    // contraseña...). La IP también es un dato personal.
    public Task<IReadOnlyList<ActividadExportada>> ActividadAsync(int usuarioId, CancellationToken ct = default) =>
        ConsultarAsync<ActividadExportada>(
            """
            SELECT TOP (500) accion, recurso, ip, creado_en AS creadoEn
            FROM dbo.auditoria
            WHERE usuario_id = @usuarioId
            ORDER BY creado_en DESC
            """,
            new { usuarioId },
            ct);
}
