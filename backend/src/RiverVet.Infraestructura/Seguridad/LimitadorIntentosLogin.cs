using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Infraestructura.Seguridad;

/// <summary>
/// Cuenta los logins FALLIDOS por IP + email dentro de una ventana (5 cada 15
/// minutos por defecto). Por IP + email y no solo por IP: en una clínica todo
/// el personal sale a internet por la misma IP pública, y un error de una
/// persona no debe bloquear a las demás.
///
/// Se guarda en memoria del servidor: suficiente con una sola instancia. Con
/// varias instancias haría falta un almacén compartido (Redis).
/// </summary>
public sealed class LimitadorIntentosLogin(
    IMemoryCache cache,
    IContextoPeticion contexto,
    IOptions<OpcionesSeguridad> opciones) : ILimitadorIntentosLogin
{
    private readonly OpcionesSeguridad _opciones = opciones.Value;

    public bool EstaBloqueado(string email) =>
        cache.TryGetValue<Contador>(Clave(email), out var contador) && contador!.Valor >= _opciones.LoginIntentosMax;

    public void RegistrarFallo(string email)
    {
        var contador = cache.GetOrCreate(Clave(email), entrada =>
        {
            // La ventana empieza con el PRIMER fallo y no se extiende.
            entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_opciones.VentanaMinutos);
            return new Contador();
        })!;
        Interlocked.Increment(ref contador.Valor);
    }

    private string Clave(string email) => $"login:{ClaveIp.Normalizar(contexto.Ip)}|{email}";

    private sealed class Contador
    {
        public int Valor;
    }
}

/// <summary>Clave de IP para límites de solicitudes.</summary>
public static class ClaveIp
{
    /// <summary>
    /// IPv4 tal cual. IPv6 agrupada por su red /56: un atacante con IPv6 suele
    /// tener millones de direcciones de su misma red; sin agruparlas, se
    /// saltaría el límite cambiando de dirección en cada intento.
    /// </summary>
    public static string Normalizar(string? ip)
    {
        if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out var direccion))
        {
            return "desconocida";
        }
        if (direccion.IsIPv4MappedToIPv6)
        {
            direccion = direccion.MapToIPv4();
        }
        if (direccion.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return direccion.ToString();
        }
        var bytes = direccion.GetAddressBytes();
        Array.Clear(bytes, 7, bytes.Length - 7); // conserva los primeros 56 bits
        return $"{new IPAddress(bytes)}/56";
    }
}
