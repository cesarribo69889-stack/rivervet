using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RiverVet.Aplicacion.Abstracciones;

namespace RiverVet.Infraestructura.Seguridad;

/// <summary>Sección "Jwt" de la configuración.</summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>
    /// Clave HMAC. Mínimo 64 caracteres (256 bits en hexadecimal). Nunca en el
    /// repositorio: User Secrets en desarrollo, variable de entorno en producción.
    /// </summary>
    public string Secreto { get; set; } = string.Empty;

    public string Emisor { get; set; } = "rivervet-api";

    public string Audiencia { get; set; } = "rivervet-web";

    public int MinutosAcceso { get; set; } = 15;
}

/// <summary>
/// ACCESS TOKEN (JWT, 15 min): viaja en la cabecera Authorization: Bearer.
///   El JWT NO está cifrado, solo firmado: cualquiera puede leer su contenido
///   en jwt.io. Por eso solo lleva el id (sub), el rol y un id único (jti).
///   Nunca email, nombre, DNI ni nada sensible.
///
/// REFRESH TOKEN (aleatorio, 7 días): viaja SOLO en una cookie httpOnly. No es
///   un JWT: son 32 bytes aleatorios. En la base se guarda su hash SHA-256.
/// </summary>
public sealed class ServicioTokens(IOptions<OpcionesJwt> opciones) : IServicioTokens
{
    private readonly OpcionesJwt _opciones = opciones.Value;
    private readonly JsonWebTokenHandler _manejador = new();

    public int SegundosValidezAcceso => _opciones.MinutosAcceso * 60;

    public string GenerarAccessToken(int usuarioId, string rol)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim("rol", rol),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            Issuer = _opciones.Emisor,
            Audience = _opciones.Audiencia,
            Expires = DateTime.UtcNow.AddMinutes(_opciones.MinutosAcceso),
            SigningCredentials = new SigningCredentials(Clave(_opciones.Secreto), SecurityAlgorithms.HmacSha256),
        };
        return _manejador.CreateToken(descriptor);
    }

    /// <summary>
    /// Parámetros con los que la API VERIFICA cada access token.
    /// <c>ValidAlgorithms = HS256</c> es OBLIGATORIO: sin esa lista, un atacante
    /// podría mandar un token con "alg": "none" (sin firma) o intentar
    /// confundir el algoritmo. Solo se acepta exactamente el que usamos.
    /// </summary>
    public static TokenValidationParameters ParametrosValidacion(OpcionesJwt opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = opciones.Emisor,
            ValidateAudience = true,
            ValidAudience = opciones.Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = Clave(opciones.Secreto),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "rol",
        };
    }

    public string GenerarRefreshToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public string GenerarTokenCsrf() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static SymmetricSecurityKey Clave(string secreto) => new(Encoding.UTF8.GetBytes(secreto));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
