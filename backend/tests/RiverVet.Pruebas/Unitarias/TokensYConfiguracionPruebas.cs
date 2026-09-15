using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RiverVet.Api.Configuracion;
using RiverVet.Infraestructura.Seguridad;

namespace RiverVet.Pruebas.Unitarias;

[Trait("Categoria", "Unitaria")]
public class ServicioTokensPruebas
{
    private static readonly OpcionesJwt _opciones = new() { Secreto = string.Concat(Enumerable.Repeat("prueba", 11)) };
    private readonly ServicioTokens _tokens = new(Options.Create(_opciones));

    private static async Task<TokenValidationResult> ValidarAsync(string token) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, ServicioTokens.ParametrosValidacion(_opciones));

    private static string Firmar(string algoritmo, string secreto, string audiencia = "rivervet-web", DateTime? expira = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", "1"), new Claim("rol", "admin")]),
            Issuer = "rivervet-api",
            Audience = audiencia,
            Expires = expira ?? DateTime.UtcNow.AddMinutes(5),
            NotBefore = expira?.AddMinutes(-10),
            IssuedAt = expira?.AddMinutes(-10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secreto)), algoritmo),
        });

    [Fact]
    public async Task El_access_token_solo_lleva_sub_rol_y_jti()
    {
        var resultado = await ValidarAsync(_tokens.GenerarAccessToken(7, "admin"));
        resultado.IsValid.ShouldBeTrue();
        resultado.Claims["sub"].ShouldBe("7");
        resultado.Claims["rol"].ShouldBe("admin");
        resultado.Claims.ShouldContainKey("jti");
        resultado.Claims.Keys.Except(["sub", "rol", "jti", "exp", "iat", "nbf", "iss", "aud"]).ShouldBeEmpty();
    }

    [Fact]
    public async Task Rechaza_un_token_con_alg_none_sin_firma()
    {
        var cabecera = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var cuerpo = Base64Url("""{"sub":"1","rol":"admin","iss":"rivervet-api","aud":"rivervet-web","exp":9999999999}""");
        (await ValidarAsync($"{cabecera}.{cuerpo}.")).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Rechaza_un_token_firmado_con_otro_secreto() =>
        (await ValidarAsync(Firmar(SecurityAlgorithms.HmacSha256, new string('z', 64)))).IsValid.ShouldBeFalse();

    [Fact]
    public async Task Rechaza_otro_algoritmo_aunque_la_firma_sea_valida_HS512() =>
        (await ValidarAsync(Firmar(SecurityAlgorithms.HmacSha512, _opciones.Secreto))).IsValid.ShouldBeFalse();

    [Fact]
    public async Task Rechaza_audiencia_incorrecta() =>
        (await ValidarAsync(Firmar(SecurityAlgorithms.HmacSha256, _opciones.Secreto, "otra-app"))).IsValid.ShouldBeFalse();

    [Fact]
    public async Task Rechaza_un_token_expirado() =>
        (await ValidarAsync(Firmar(SecurityAlgorithms.HmacSha256, _opciones.Secreto, expira: DateTime.UtcNow.AddMinutes(-5))))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void El_refresh_token_es_aleatorio_de_256_bits_y_se_guarda_hasheado()
    {
        var a = _tokens.GenerarRefreshToken();
        var b = _tokens.GenerarRefreshToken();
        a.ShouldNotBe(b);
        Convert.FromBase64String(a.Replace('-', '+').Replace('_', '/') + "=").Length.ShouldBe(32);
        _tokens.HashToken("abc").ShouldMatch("^[0-9a-f]{64}$");
        _tokens.HashToken("abc").ShouldBe(_tokens.HashToken("abc"));
    }

    [Fact]
    public void Bcrypt_verifica_el_hash_del_seed_y_rechaza_otra_contrasena()
    {
        var hasheador = new HasheadorBcrypt();
        const string HashSeed = "$2b$12$DQWUuV44oPYNgu4J4G/bBuZ599kKJCcfUfGxfbkaeoG.dA0XfnG96";
        hasheador.Verificar("RiverVet#2026", HashSeed).ShouldBeTrue();
        hasheador.Verificar("otra", HashSeed).ShouldBeFalse();
        hasheador.Verificar("x", "hash-corrupto").ShouldBeFalse();
    }

    private static string Base64Url(string texto) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(texto)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

[Trait("Categoria", "Unitaria")]
public class ValidadorConfiguracionPruebas
{
    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    private static Dictionary<string, string?> Base() => new()
    {
        ["ConnectionStrings:RiverVet"] = "Server=.;Database=rivervet_db;User Id=rivervet_app;Password=x;TrustServerCertificate=True",
        ["Jwt:Secreto"] = new string('a', 64),
        ["Seguridad:OrigenesCors:0"] = "http://localhost:5173",
    };

    private static Dictionary<string, string?> Produccion() => new()
    {
        ["ConnectionStrings:RiverVet"] = "Server=tcp:rivervet.database.windows.net;Database=rivervet_db;User Id=rivervet_app;Password=x;Encrypt=True;TrustServerCertificate=False",
        ["Jwt:Secreto"] = new string('a', 64),
        ["Seguridad:OrigenesCors:0"] = "https://rivervet.vercel.app",
        ["Seguridad:CookiesSeguras"] = "true",
    };

    [Fact]
    public void Acepta_una_configuracion_valida_de_desarrollo() =>
        ValidadorConfiguracion.Validar(Config(Base()), esProduccion: false).ShouldBeEmpty();

    [Fact]
    public void Falla_sin_cadena_de_conexion_y_sin_mostrar_valores()
    {
        var valores = Base();
        valores.Remove("ConnectionStrings:RiverVet");
        ValidadorConfiguracion.Validar(Config(valores), false).ShouldContain(p => p.StartsWith("ConnectionStrings:RiverVet", StringComparison.Ordinal));
    }

    [Fact]
    public void Exige_un_secreto_JWT_de_256_bits()
    {
        var valores = Base();
        valores["Jwt:Secreto"] = "corto";
        ValidadorConfiguracion.Validar(Config(valores), false).ShouldContain(p => p.StartsWith("Jwt:Secreto", StringComparison.Ordinal));
    }

    [Fact]
    public void Prohibe_asterisco_en_CORS()
    {
        var valores = Base();
        valores["Seguridad:OrigenesCors:0"] = "*";
        ValidadorConfiguracion.Validar(Config(valores), false).ShouldContain(p => p.Contains("OrigenesCors", StringComparison.Ordinal));
    }

    [Fact]
    public void Acepta_una_configuracion_segura_de_produccion() =>
        ValidadorConfiguracion.Validar(Config(Produccion()), esProduccion: true).ShouldBeEmpty();

    [Theory]
    [InlineData("TrustServerCertificate=True")]
    [InlineData("Encrypt=False")]
    [InlineData("User Id=sa")]
    public void En_produccion_exige_conexion_cifrada_verificada_y_sin_administrador(string cambio)
    {
        var valores = Produccion();
        var clave = cambio.Split('=')[0];
        var cadena = string.Join(';', valores["ConnectionStrings:RiverVet"]!.Split(';').Where(p => !p.StartsWith(clave, StringComparison.OrdinalIgnoreCase)));
        valores["ConnectionStrings:RiverVet"] = $"{cadena};{cambio}";
        ValidadorConfiguracion.Validar(Config(valores), true).ShouldNotBeEmpty();
    }

    [Fact]
    public void En_produccion_prohibe_origenes_http_y_cookies_inseguras()
    {
        var valores = Produccion();
        valores["Seguridad:OrigenesCors:0"] = "http://rivervet.pe";
        valores["Seguridad:CookiesSeguras"] = "false";
        var problemas = ValidadorConfiguracion.Validar(Config(valores), true);
        problemas.ShouldContain(p => p.Contains("https", StringComparison.Ordinal));
        problemas.ShouldContain(p => p.Contains("CookiesSeguras", StringComparison.Ordinal));
    }
}
