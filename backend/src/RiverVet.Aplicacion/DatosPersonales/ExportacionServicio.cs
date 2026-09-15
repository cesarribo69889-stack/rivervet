using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.DatosPersonales;

public sealed class CuentaExportada
{
    public string Nombre { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Rol { get; init; } = string.Empty;

    public DateTime CreadoEn { get; init; }

    public DateTime? UltimoLoginEn { get; init; }
}

public sealed class TitularExportado
{
    public int Id { get; init; }

    public string Dni { get; init; } = string.Empty;

    public string Nombres { get; init; } = string.Empty;

    public string Apellidos { get; init; } = string.Empty;

    public string? Email { get; init; }

    public string Telefono { get; init; } = string.Empty;

    public string? Direccion { get; init; }

    public bool ConsentimientoDatos { get; init; }

    public DateTime? ConsentimientoEn { get; init; }

    public DateTime CreadoEn { get; init; }
}

public sealed class MascotaExportada
{
    public int Id { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public string Especie { get; init; } = string.Empty;

    public string? Raza { get; init; }

    public string? Sexo { get; init; }

    public string? FechaNacimiento { get; init; }

    public decimal? PesoKg { get; init; }

    public string? Color { get; init; }

    public string? SenasParticulares { get; init; }

    public bool? Esterilizado { get; init; }
}

public sealed class CitaExportada
{
    public string Mascota { get; init; } = string.Empty;

    public string Veterinario { get; init; } = string.Empty;

    public DateTime FechaHora { get; init; }

    public string Motivo { get; init; } = string.Empty;

    public string Estado { get; init; } = string.Empty;

    public string? MotivoCancelacion { get; init; }
}

public sealed class AtencionExportada
{
    public string Mascota { get; init; } = string.Empty;

    public string Veterinario { get; init; } = string.Empty;

    public DateTime FechaAtencion { get; init; }

    public string Motivo { get; init; } = string.Empty;

    public string Diagnostico { get; init; } = string.Empty;

    public string Tratamiento { get; init; } = string.Empty;

    public string? Observaciones { get; init; }

    public decimal? PesoKg { get; init; }

    public decimal? TemperaturaC { get; init; }
}

public sealed class VacunaExportada
{
    public string Mascota { get; init; } = string.Empty;

    public string TipoVacuna { get; init; } = string.Empty;

    public string FechaAplicacion { get; init; } = string.Empty;

    public string Lote { get; init; } = string.Empty;

    public string? ProximaDosis { get; init; }
}

public sealed class PedidoExportado
{
    public string Codigo { get; init; } = string.Empty;

    public string Canal { get; init; } = string.Empty;

    public string Estado { get; init; } = string.Empty;

    public decimal Total { get; init; }

    public DateTime CreadoEn { get; init; }

    /// <summary>"2 x Croquetas perro adulto 15 kg (S/ 189.90)".</summary>
    public string Detalle { get; init; } = string.Empty;
}

public sealed class PagoExportado
{
    public string Pedido { get; init; } = string.Empty;

    public string Metodo { get; init; } = string.Empty;

    public string Estado { get; init; } = string.Empty;

    public decimal Monto { get; init; }

    public string? CodigoOperacion { get; init; }

    public DateTime CreadoEn { get; init; }
}

public sealed class EnvioExportado
{
    public string Pedido { get; init; } = string.Empty;

    public string Telefono { get; init; } = string.Empty;

    public string Estado { get; init; } = string.Empty;

    public DateTime CreadoEn { get; init; }
}

/// <summary>Inicios de sesión y otras acciones registradas de la cuenta (incluye la IP, que también es un dato personal).</summary>
public sealed class ActividadExportada
{
    public string Accion { get; init; } = string.Empty;

    public string Recurso { get; init; } = string.Empty;

    public string? Ip { get; init; }

    public DateTime CreadoEn { get; init; }
}

public sealed record ExportacionDatosPersonales(
    DateTime GeneradoEn,
    string Responsable,
    string Finalidad,
    string Derechos,
    TitularExportado Titular,
    CuentaExportada? Cuenta,
    IReadOnlyList<MascotaExportada> Mascotas,
    IReadOnlyList<CitaExportada> Citas,
    IReadOnlyList<AtencionExportada> HistorialClinico,
    IReadOnlyList<VacunaExportada> Vacunas,
    IReadOnlyList<PedidoExportado> Pedidos,
    IReadOnlyList<PagoExportado> Pagos,
    IReadOnlyList<EnvioExportado> EnviosComprobante,
    IReadOnlyList<ActividadExportada> Actividad);

/// <summary>
/// Todas las consultas filtran por el cliente de la SESIÓN (usuarioId del
/// token), nunca por un id que venga en la petición: no hay forma de pedir
/// la exportación de otra persona.
/// </summary>
public interface IExportacionRepositorio
{
    Task<TitularExportado?> TitularAsync(int usuarioId, CancellationToken ct = default);

    Task<CuentaExportada?> CuentaAsync(int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<MascotaExportada>> MascotasAsync(int clienteId, CancellationToken ct = default);

    Task<IReadOnlyList<CitaExportada>> CitasAsync(int clienteId, CancellationToken ct = default);

    Task<IReadOnlyList<AtencionExportada>> HistorialAsync(int clienteId, CancellationToken ct = default);

    Task<IReadOnlyList<VacunaExportada>> VacunasAsync(int clienteId, CancellationToken ct = default);

    Task<IReadOnlyList<PedidoExportado>> PedidosAsync(int clienteId, CancellationToken ct = default);

    Task<IReadOnlyList<PagoExportado>> PagosAsync(int clienteId, CancellationToken ct = default);

    Task<IReadOnlyList<EnvioExportado>> EnviosAsync(int clienteId, CancellationToken ct = default);

    Task<IReadOnlyList<ActividadExportada>> ActividadAsync(int usuarioId, CancellationToken ct = default);
}

/// <summary>
/// Ley N° 29733 — DERECHO DE ACCESO: el cliente descarga TODOS los datos
/// personales que la clínica tiene sobre él y sus mascotas, en un formato
/// legible por máquinas (JSON). La exportación queda auditada.
/// </summary>
public sealed class ExportacionServicio(
    IExportacionRepositorio repositorio,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto)
{
    public const string Finalidad =
        "Tus datos se usan para: registrar a tus mascotas y su historia clínica, agendar citas, " +
        "enviarte recordatorios, y registrar tus compras y pagos. No se ceden a terceros con fines comerciales.";

    public const string Derechos =
        "Según la Ley N° 29733 puedes acceder a tus datos (esta descarga), y pedir que se rectifiquen, " +
        "actualicen o supriman, o revocar tu consentimiento, solicitándolo en la clínica. Algunos registros " +
        "(ventas e historia clínica) deben conservarse el tiempo que exigen otras normas.";

    public async Task<ExportacionDatosPersonales> ExportarAsync(CancellationToken ct = default)
    {
        var usuarioId = contexto.UsuarioId ?? throw new NoAutenticadoException();
        var titular = await repositorio.TitularAsync(usuarioId, ct)
            ?? throw new NoEncontradoException("No hay una ficha de cliente asociada a tu cuenta");
        var clienteId = titular.Id;

        var exportacion = new ExportacionDatosPersonales(
            HoraLima.AhoraUtc(),
            "RiverVet — Clínica veterinaria",
            Finalidad,
            Derechos,
            titular,
            await repositorio.CuentaAsync(usuarioId, ct),
            await repositorio.MascotasAsync(clienteId, ct),
            await repositorio.CitasAsync(clienteId, ct),
            await repositorio.HistorialAsync(clienteId, ct),
            await repositorio.VacunasAsync(clienteId, ct),
            await repositorio.PedidosAsync(clienteId, ct),
            await repositorio.PagosAsync(clienteId, ct),
            await repositorio.EnviosAsync(clienteId, ct),
            await repositorio.ActividadAsync(usuarioId, ct));

        await auditoria.RegistrarAsync(AccionesAuditoria.ExportarDatos, RecursosAuditoria.DatosPersonales, clienteId, ct: ct);
        return exportacion;
    }
}
