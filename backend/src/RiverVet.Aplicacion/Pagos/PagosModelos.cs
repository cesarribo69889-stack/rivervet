using System.Text.RegularExpressions;
using FluentValidation;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Aplicacion.Pagos;

/// <summary>
/// Configuración de pagos (sección "Pagos"). Ningún valor de aquí es un dato
/// de tarjeta: el sistema no los tiene. El único secreto es SecretoWebhook,
/// que va en User Secrets / variables de entorno, nunca en appsettings.json.
/// </summary>
public sealed class OpcionesPagos
{
    public const string Seccion = "Pagos";

    public OpcionesYape Yape { get; set; } = new();

    public OpcionesPasarela Pasarela { get; set; } = new();
}

public sealed class OpcionesYape
{
    /// <summary>Celular del negocio asociado a Yape (se muestra al cliente para que pague).</summary>
    public string? Celular { get; set; }

    public string? Titular { get; set; }

    public bool Habilitado => !string.IsNullOrWhiteSpace(Celular);
}

public sealed class OpcionesPasarela
{
    /// <summary>Proveedor: vacío = pasarela deshabilitada. "simulada" = solo desarrollo y pruebas.</summary>
    public string? Proveedor { get; set; }

    /// <summary>Página de pago hospedada por la pasarela (checkout).</summary>
    public string? UrlCheckout { get; set; }

    /// <summary>Secreto compartido con la pasarela para firmar los webhooks (HMAC-SHA256).</summary>
    public string? SecretoWebhook { get; set; }

    /// <summary>Antigüedad máxima de un webhook: uno más viejo se rechaza aunque la firma sea válida (repetición).</summary>
    public int ToleranciaSegundos { get; set; } = 300;

    public bool Habilitada => !string.IsNullOrWhiteSpace(Proveedor);
}

/// <summary>HU-017: el cliente pagó con Yape y registra el código de operación que le mostró la app.</summary>
public sealed partial class RegistrarYapeSolicitud : INormalizable
{
    public string? CodigoOperacion { get; set; }

    public void Normalizar() => CodigoOperacion = Texto.Recortar(CodigoOperacion)?.ToUpperInvariant();

    [GeneratedRegex("^[A-Z0-9]{6,30}$")]
    internal static partial Regex FormatoCodigo();
}

/// <summary>Recepción confirma (o rechaza) que el Yape llegó a la cuenta del negocio.</summary>
public sealed class VerificarPagoSolicitud : INormalizable
{
    public bool? Aprobado { get; set; }

    public string? Observacion { get; set; }

    public void Normalizar() => Observacion = Texto.Libre(Observacion);
}

public sealed class RegistrarYapeValidador : AbstractValidator<RegistrarYapeSolicitud>
{
    public RegistrarYapeValidador() =>
        RuleFor(x => x.CodigoOperacion)
            .NotEmpty().WithMessage("Ingresa el número de operación que aparece en tu Yape")
            .Matches(RegistrarYapeSolicitud.FormatoCodigo()).WithMessage("Entre 6 y 30 letras o números, sin espacios");
}

public sealed class VerificarPagoValidador : AbstractValidator<VerificarPagoSolicitud>
{
    public VerificarPagoValidador()
    {
        RuleFor(x => x.Aprobado).NotNull().WithMessage("Indica si el pago llegó (true) o no (false)");
        RuleFor(x => x.Observacion).MaximumLength(250).WithMessage("Máximo 250 caracteres");
        RuleFor(x => x.Observacion).NotEmpty().When(x => x.Aprobado == false)
            .WithMessage("Indica por qué se rechaza el pago");
    }
}

/// <summary>
/// Datos para pagar con Yape. Si hay QR, la web lo pide a GET /api/pagos/yape/qr
/// con ?v=<paramref name="QrVersion"/>: la versión cambia al subir otro y evita
/// que el navegador muestre el anterior guardado en caché.
/// </summary>
public sealed record DatosYapeRespuesta(string Celular, string? Titular, bool TieneQr, string? QrVersion, IReadOnlyList<string> Instrucciones);

public sealed record CheckoutRespuesta(int PagoId, string Referencia, Uri UrlCheckout);

public sealed class PagoPendienteVerificacion
{
    public int PagoId { get; init; }

    public int PedidoId { get; init; }

    public string PedidoCodigo { get; init; } = string.Empty;

    public string ClienteNombre { get; init; } = string.Empty;

    public decimal Monto { get; init; }

    public string CodigoOperacion { get; init; } = string.Empty;

    public DateTime CreadoEn { get; init; }
}

public sealed record ResultadoWebhook(string Resultado);

/// <summary>Pago leído (con bloqueo, dentro de una transacción) para cambiarle el estado.</summary>
public sealed record PagoRegistrado(int Id, int PedidoId, string Metodo, string Estado, decimal Monto, string Moneda);

/// <summary>Cuerpo del webhook de la pasarela, leído SOLO después de verificar la firma.</summary>
public sealed class EventoPasarela
{
    public string? Id { get; set; }

    public string? Tipo { get; set; }

    public string? Referencia { get; set; }

    public decimal? Monto { get; set; }

    public string? Moneda { get; set; }
}

public sealed class EventoPasarelaValidador : AbstractValidator<EventoPasarela>
{
    public EventoPasarelaValidador()
    {
        RuleFor(x => x.Id).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Tipo).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Referencia).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Monto).NotNull().GreaterThan(0);
        RuleFor(x => x.Moneda).NotEmpty().Length(3);
    }
}

/// <summary>Sesión de pago creada en la pasarela: una referencia opaca y la URL de su página.</summary>
public sealed record SesionCheckout(string Referencia, Uri UrlCheckout);

/// <summary>
/// Puerto hacia la pasarela de pagos (checkout HOSPEDADO).
///
/// ★ POR QUÉ ESTO REDUCE EL ALCANCE DE PCI DSS ★
/// El cliente escribe su tarjeta en la página de la PASARELA, no en la
/// nuestra: el número, el CVV y el vencimiento viajan de su navegador a la
/// pasarela y nunca pasan por este servidor ni por esta base de datos. Aquí
/// solo guardamos la referencia que la pasarela nos da y el resultado que
/// nos avisa por webhook. PCI DSS se aplica a los sistemas que almacenan,
/// procesan o transmiten datos de tarjeta: como este no hace ninguna de las
/// tres cosas, queda fuera de casi todos sus requisitos (típicamente basta el
/// cuestionario de autoevaluación más simple), y un robo de nuestra base no
/// expondría ninguna tarjeta.
/// </summary>
public interface IPasarelaPagos
{
    string Proveedor { get; }

    Task<SesionCheckout> CrearCheckoutAsync(string codigoPedido, decimal monto, CancellationToken ct = default);
}

public interface IPagosRepositorio
{
    Task<int> CrearAsync(
        int pedidoId, string metodo, string estado, decimal monto, string? codigoOperacion,
        string? proveedor, string? referencia, int? verificadoPor, CancellationToken ct = default);

    Task<PagoRegistrado?> ObtenerAsync(int id, CancellationToken ct = default);

    Task<PagoRegistrado?> BuscarPorReferenciaAsync(string proveedor, string referencia, CancellationToken ct = default);

    /// <summary>Relee el pago con UPDLOCK (llamar DESPUÉS de bloquear su pedido: siempre pedido → pago).</summary>
    Task<PagoRegistrado?> BloquearAsync(int id, CancellationToken ct = default);

    Task<bool> CambiarEstadoAsync(int id, string estadoEsperado, string estadoNuevo, int? verificadoPor, CancellationToken ct = default);

    Task<IReadOnlyList<PagoPendienteVerificacion>> PendientesDeVerificacionAsync(CancellationToken ct = default);

    /// <summary>Registra el evento del webhook. Devuelve null si ese evento ya se había recibido (idempotencia).</summary>
    Task<long?> RegistrarEventoAsync(string proveedor, string eventoId, string tipo, bool firmaValida, CancellationToken ct = default);

    Task MarcarEventoProcesadoAsync(long id, CancellationToken ct = default);
}
