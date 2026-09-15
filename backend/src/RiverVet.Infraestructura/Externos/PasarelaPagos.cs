using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Pagos;

namespace RiverVet.Infraestructura.Externos;

/// <summary>
/// Pasarela SIMULADA, para desarrollo y pruebas: no llama a ningún servicio
/// real ni mueve dinero. Genera una referencia aleatoria como haría una
/// pasarela real; el "aviso de pago" se simula enviando un webhook firmado
/// con el secreto configurado (ver docs/API.md §13).
///
/// ValidadorConfiguracion impide usarla en producción.
///
/// Para conectar una pasarela real (Culqi, Niubiz, Izipay, Mercado Pago...)
/// se escribe otra clase que implemente IPasarelaPagos con el SDK o la API
/// OFICIAL del proveedor, usando su checkout hospedado, y se adapta el
/// webhook a su esquema de firma. El resto del sistema no cambia.
/// </summary>
public sealed class PasarelaSimulada(IOptions<OpcionesPagos> opciones) : IPasarelaPagos
{
    public const string Nombre = "simulada";

    /// <summary>Dominio .invalid (RFC 2606): reservado, nunca resuelve a un sitio real.</summary>
    public const string UrlPorDefecto = "https://pasarela-simulada.invalid/checkout";

    public string Proveedor => Nombre;

    public Task<SesionCheckout> CrearCheckoutAsync(string codigoPedido, decimal monto, CancellationToken ct = default)
    {
        var referencia = "sim_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        // IsNullOrWhiteSpace y no "??": en appsettings.json el valor vacío es "" (no null).
        var configurada = opciones.Value.Pasarela.UrlCheckout;
        var baseUrl = string.IsNullOrWhiteSpace(configurada) ? UrlPorDefecto : configurada;
        var url = new Uri(
            $"{baseUrl}?referencia={referencia}&pedido={Uri.EscapeDataString(codigoPedido)}" +
            $"&monto={monto.ToString("0.00", CultureInfo.InvariantCulture)}");
        return Task.FromResult(new SesionCheckout(referencia, url));
    }
}

/// <summary>Cuando no hay pasarela configurada: toda operación responde 503.</summary>
public sealed class PasarelaNoConfigurada : IPasarelaPagos
{
    public string Proveedor => "ninguna";

    public Task<SesionCheckout> CrearCheckoutAsync(string codigoPedido, decimal monto, CancellationToken ct = default) =>
        throw new ServicioNoDisponibleException("El pago con tarjeta no está habilitado");
}
