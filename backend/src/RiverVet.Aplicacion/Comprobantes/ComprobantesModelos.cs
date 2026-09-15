using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Comprobantes;

/// <summary>
/// Configuración de WhatsApp (sección "WhatsApp").
///   Modo "enlace"   (por defecto): se arma un enlace https://wa.me/... que la
///                   recepcionista abre y envía desde el WhatsApp del negocio.
///                   No requiere cuentas ni credenciales.
///   Modo "cloudapi": se envía por la API de WhatsApp Cloud (Meta) con una
///                   plantilla aprobada. Token y NumeroId son secretos.
/// </summary>
public sealed class OpcionesWhatsApp
{
    public const string Seccion = "WhatsApp";
    public const string ModoEnlace = "enlace";
    public const string ModoCloudApi = "cloudapi";

    public string Modo { get; set; } = ModoEnlace;

    /// <summary>Versión de la Graph API (p. ej. "v21.0"): se toma de la consola de Meta, no se inventa.</summary>
    public string? VersionApi { get; set; }

    public string? NumeroId { get; set; }

    public string? Token { get; set; }

    /// <summary>Nombre de la plantilla aprobada, con 3 variables: código, total y fecha.</summary>
    public string? Plantilla { get; set; }

    public string CodigoIdioma { get; set; } = "es";
}

/// <summary>
/// HU-020. Lo ÚNICO que sale en el mensaje es el código del pedido, el total
/// y la fecha. Nada de DNI, dirección, nombre de la mascota ni productos:
/// WhatsApp es un canal externo, el mensaje queda en el teléfono (que otros
/// pueden ver) y la lista de productos puede revelar la salud de la mascota
/// o del hogar (medicamentos). Minimización de datos, Ley N° 29733.
/// </summary>
public sealed record MensajeComprobante(string CodigoPedido, decimal Total, string Fecha)
{
    public string Texto =>
        $"RiverVet: comprobante de tu pedido {CodigoPedido}. " +
        $"Total pagado: S/ {Total.ToString("0.00", CultureInfo.InvariantCulture)}. Fecha: {Fecha}. ¡Gracias por tu compra!";
}

public sealed record ResultadoEnvio(bool Enviado, Uri? Enlace, string? Error);

public sealed class EnviarComprobanteSolicitud : INormalizable
{
    /// <summary>Opcional: si no viene, se usa el celular de la ficha del cliente.</summary>
    public string? Telefono { get; set; }

    public void Normalizar() => Telefono = Comun.Texto.Recortar(Telefono);
}

public sealed class EnviarComprobanteValidador : AbstractValidator<EnviarComprobanteSolicitud>
{
    public EnviarComprobanteValidador() =>
        RuleFor(x => x.Telefono).Celular().When(x => x.Telefono is not null);
}

public sealed record EnvioComprobanteRespuesta(int EnvioId, string Estado, string Telefono, string Mensaje, Uri? Enlace);

public sealed record DatosComprobante(int PedidoId, string Codigo, string Estado, decimal Total, DateTime CreadoEn, string ClienteTelefono);

/// <summary>Puerto hacia WhatsApp (enlace wa.me o API Cloud).</summary>
public interface IMensajeriaWhatsApp
{
    string Modo { get; }

    Task<ResultadoEnvio> EnviarComprobanteAsync(string celular, MensajeComprobante mensaje, CancellationToken ct = default);
}

public interface IComprobantesRepositorio
{
    Task<DatosComprobante?> ObtenerAsync(int pedidoId, CancellationToken ct = default);

    Task<int> EnviosRecientesAsync(int pedidoId, DateTime desdeUtc, CancellationToken ct = default);

    Task<int> RegistrarEnvioAsync(int pedidoId, string telefono, string estado, int? enviadoPor, CancellationToken ct = default);
}

/// <summary>HU-020: envío del comprobante de un pedido pagado por WhatsApp.</summary>
public sealed class ComprobantesServicio(
    IComprobantesRepositorio comprobantes,
    IMensajeriaWhatsApp whatsApp,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto,
    IOptions<OpcionesWhatsApp> opciones)
{
    /// <summary>Máximo de envíos por pedido en 24 horas: evita usar la clínica para hacer spam.</summary>
    public const int EnviosMaximosDiarios = 3;

    public async Task<EnvioComprobanteRespuesta> EnviarAsync(int pedidoId, EnviarComprobanteSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var datos = await comprobantes.ObtenerAsync(pedidoId, ct) ?? throw new NoEncontradoException("Pedido no encontrado");
        if (!EstadosPedido.Cobrados.Contains(datos.Estado))
        {
            throw new ConflictoException("El comprobante se envía cuando el pedido está pagado");
        }
        if (await comprobantes.EnviosRecientesAsync(pedidoId, HoraLima.AhoraUtc().AddDays(-1), ct) >= EnviosMaximosDiarios)
        {
            throw new DemasiadosIntentosException($"Ya se enviaron {EnviosMaximosDiarios} comprobantes de este pedido hoy");
        }

        var telefono = solicitud.Telefono ?? datos.ClienteTelefono;
        var mensaje = new MensajeComprobante(
            datos.Codigo, datos.Total, HoraLima.DeUtc(datos.CreadoEn).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        var resultado = await whatsApp.EnviarComprobanteAsync(telefono, mensaje, ct);

        // En modo enlace el mensaje queda "pendiente": lo envía una persona desde WhatsApp.
        var estado = opciones.Value.Modo == OpcionesWhatsApp.ModoEnlace ? "pendiente"
            : resultado.Enviado ? "enviado" : "fallido";
        var envioId = await comprobantes.RegistrarEnvioAsync(pedidoId, telefono, estado, contexto.UsuarioId, ct);
        await auditoria.RegistrarAsync(
            AccionesAuditoria.EnviarComprobante, RecursosAuditoria.Comprobante, envioId,
            new { datos.Codigo, estado, telefono = Enmascarar(telefono), error = resultado.Error }, ct: ct);

        return new EnvioComprobanteRespuesta(envioId, estado, telefono, mensaje.Texto, resultado.Enlace);
    }

    /// <summary>"987654321" -> "98*****21": la auditoría no necesita el número completo.</summary>
    internal static string Enmascarar(string telefono) =>
        telefono.Length == 9 ? string.Concat(telefono.AsSpan(0, 2), "*****", telefono.AsSpan(7)) : "*********";
}
