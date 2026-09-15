using Microsoft.Data.SqlClient;

namespace RiverVet.Api.Configuracion;

/// <summary>
/// Validación de la configuración al ARRANCAR (fail-fast).
///
/// Si falta un valor o es inseguro, la API no arranca. Es mejor fallar
/// ruidosamente al iniciar que descubrir en producción, con usuarios reales,
/// que el JWT se estaba firmando con una clave vacía. El mensaje lista los
/// NOMBRES de los valores con problemas, nunca sus valores.
/// </summary>
public static class ValidadorConfiguracion
{
    private static readonly string[] _usuariosAdministradores = ["sa", "admin", "administrator"];

    public static IReadOnlyList<string> Validar(IConfiguration configuracion, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var problemas = new List<string>();

        var cadena = configuracion.GetConnectionString("RiverVet");
        if (string.IsNullOrWhiteSpace(cadena))
        {
            problemas.Add("ConnectionStrings:RiverVet: falta la cadena de conexión (ejecuta configurar-desarrollo.ps1)");
        }
        else if (esProduccion)
        {
            ValidarConexionProduccion(cadena, problemas);
        }

        // 64 caracteres hexadecimales = 32 bytes = 256 bits: lo que pide
        // HS256. Una clave corta se rompe por fuerza bruta offline: basta un
        // token válido capturado para probar claves sin límite.
        var secreto = configuracion["Jwt:Secreto"];
        if (string.IsNullOrWhiteSpace(secreto) || secreto.Length < 64)
        {
            problemas.Add("Jwt:Secreto: debe tener al menos 64 caracteres (256 bits)");
        }

        var origenes = configuracion.GetSection("Seguridad:OrigenesCors").Get<string[]>() ?? [];
        if (origenes.Contains("*"))
        {
            problemas.Add("Seguridad:OrigenesCors: no se permite \"*\"");
        }
        if (esProduccion && origenes.Any(o => o.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            problemas.Add("Seguridad:OrigenesCors: solo orígenes https en producción");
        }
        if (esProduccion && !configuracion.GetValue("Seguridad:CookiesSeguras", true))
        {
            problemas.Add("Seguridad:CookiesSeguras: debe ser true en producción");
        }
        ValidarPagos(configuracion, esProduccion, problemas);
        ValidarWhatsApp(configuracion, problemas);
        return problemas;
    }

    /// <summary>
    /// Pagos: la pasarela simulada nunca en producción (aprobaría pagos que
    /// nadie cobró), y si hay pasarela, el secreto de los webhooks es obligatorio
    /// y largo: sin él, cualquiera podría "avisar" que un pedido está pagado.
    /// </summary>
    private static void ValidarPagos(IConfiguration configuracion, bool esProduccion, List<string> problemas)
    {
        var proveedor = configuracion["Pagos:Pasarela:Proveedor"];
        if (!string.IsNullOrWhiteSpace(proveedor))
        {
            if (proveedor != "simulada")
            {
                problemas.Add("Pagos:Pasarela:Proveedor: proveedor no implementado (vacío = deshabilitada; \"simulada\" = desarrollo)");
            }
            else if (esProduccion)
            {
                problemas.Add("Pagos:Pasarela:Proveedor: la pasarela simulada no se puede usar en producción");
            }
            if ((configuracion["Pagos:Pasarela:SecretoWebhook"] ?? string.Empty).Length < 32)
            {
                problemas.Add("Pagos:Pasarela:SecretoWebhook: debe tener al menos 32 caracteres");
            }
            var url = configuracion["Pagos:Pasarela:UrlCheckout"];
            if (!string.IsNullOrWhiteSpace(url) && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            {
                problemas.Add("Pagos:Pasarela:UrlCheckout: debe ser una URL https:// completa");
            }
        }

        var yape = configuracion["Pagos:Yape:Celular"];
        if (!string.IsNullOrWhiteSpace(yape) && !(yape.Length == 9 && yape[0] == '9' && yape.All(char.IsAsciiDigit)))
        {
            problemas.Add("Pagos:Yape:Celular: debe ser un celular de 9 dígitos que empiece con 9");
        }
    }

    private static void ValidarWhatsApp(IConfiguration configuracion, List<string> problemas)
    {
        var modo = configuracion["WhatsApp:Modo"] ?? "enlace";
        if (modo is not ("enlace" or "cloudapi"))
        {
            problemas.Add("WhatsApp:Modo: debe ser \"enlace\" o \"cloudapi\"");
            return;
        }
        if (modo == "cloudapi")
        {
            foreach (var clave in new[] { "VersionApi", "NumeroId", "Token", "Plantilla" })
            {
                if (string.IsNullOrWhiteSpace(configuracion[$"WhatsApp:{clave}"]))
                {
                    problemas.Add($"WhatsApp:{clave}: obligatorio en modo cloudapi");
                }
            }
        }
    }

    /// <summary>
    /// En producción la conexión va cifrada y verificando el certificado del
    /// servidor (si no, alguien en la red podría leer consultas con datos
    /// personales o suplantar al servidor), y nunca con un administrador.
    /// </summary>
    private static void ValidarConexionProduccion(string cadena, List<string> problemas)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(cadena);
        }
        catch (ArgumentException)
        {
            problemas.Add("ConnectionStrings:RiverVet: formato inválido");
            return;
        }
        if (builder.Encrypt == SqlConnectionEncryptOption.Optional)
        {
            problemas.Add("ConnectionStrings:RiverVet: Encrypt debe ser True en producción");
        }
        if (builder.TrustServerCertificate)
        {
            problemas.Add("ConnectionStrings:RiverVet: TrustServerCertificate debe ser False en producción");
        }
        if (_usuariosAdministradores.Contains(builder.UserID, StringComparer.OrdinalIgnoreCase) || builder.IntegratedSecurity)
        {
            problemas.Add("ConnectionStrings:RiverVet: la app no puede conectarse con un usuario administrador");
        }
    }

    public static void ValidarOFallar(IConfiguration configuracion, IHostEnvironment entorno)
    {
        ArgumentNullException.ThrowIfNull(entorno);
        var problemas = Validar(configuracion, entorno.IsProduction());
        if (problemas.Count > 0)
        {
            throw new InvalidOperationException(
                "Configuración inválida:" + Environment.NewLine + string.Join(Environment.NewLine, problemas.Select(p => "  - " + p)));
        }
    }
}
