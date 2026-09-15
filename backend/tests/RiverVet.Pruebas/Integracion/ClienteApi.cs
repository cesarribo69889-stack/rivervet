using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace RiverVet.Pruebas.Integracion;

/// <summary>Respuesta ya leída: código, cabeceras, cookies y JSON.</summary>
public sealed class Respuesta
{
    public required HttpStatusCode Estado { get; init; }

    public required JsonElement Json { get; init; }

    public required HttpResponseMessage Http { get; init; }

    public JsonElement Data => Json.GetProperty("data");

    public string Mensaje => Json.GetProperty("message").GetString()!;

    public IReadOnlyList<string> CamposConError =>
        [.. Json.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("campo").GetString()!)];

    public IEnumerable<string> SetCookies =>
        Http.Headers.TryGetValues("Set-Cookie", out var valores) ? valores : [];

    public string? Cookie(string nombre)
    {
        var cruda = SetCookies.FirstOrDefault(c => c.StartsWith($"{nombre}=", StringComparison.Ordinal));
        return cruda?.Split(';')[0][(nombre.Length + 1)..];
    }

    public string? Cabecera(string nombre) =>
        Http.Headers.TryGetValues(nombre, out var v) ? v.FirstOrDefault()
        : Http.Content.Headers.TryGetValues(nombre, out var c) ? c.FirstOrDefault() : null;
}

/// <summary>Sesión iniciada: token de acceso + cookies.</summary>
public sealed record Sesion(string Token, JsonElement Usuario, string Refresh, string Csrf, Respuesta Login)
{
    public int UsuarioId => Usuario.GetProperty("id").GetInt32();

    public string CabeceraCookies => $"rv_refresh={Refresh}; rv_csrf={Csrf}";
}

/// <summary>Cliente HTTP de pruebas con atajos para la API de RiverVet.</summary>
public sealed class ClienteApi(HttpClient http)
{
    public const string PasswordSeed = "RiverVet#2026";

    public static class Usuarios
    {
        public const string Admin = "admin@rivervet.pe";
        public const string Veterinario = "vet.lopez@rivervet.pe";
        public const string Recepcionista = "recepcion@rivervet.pe";
        public const string Ana = "ana.torres@correo.pe"; // cliente con Firulais y Mishi
        public const string Luis = "luis.ramirez@correo.pe"; // cliente con Rocky
    }

    public HttpClient Http => http;

    public async Task<Respuesta> EnviarAsync(
        HttpMethod metodo, string url, object? cuerpo = null, string? token = null, Action<HttpRequestMessage>? ajustar = null)
    {
        using var peticion = new HttpRequestMessage(metodo, url);
        if (cuerpo is not null)
        {
            peticion.Content = cuerpo is HttpContent contenido ? contenido : JsonContent.Create(cuerpo);
        }
        if (token is not null)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        ajustar?.Invoke(peticion);
        var respuesta = await http.SendAsync(peticion);
        var texto = await respuesta.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
        return new Respuesta { Estado = respuesta.StatusCode, Json = json, Http = respuesta };
    }

    public Task<Respuesta> GetAsync(string url, string? token = null) => EnviarAsync(HttpMethod.Get, url, null, token);

    public Task<Respuesta> PostAsync(string url, object? cuerpo, string? token = null) => EnviarAsync(HttpMethod.Post, url, cuerpo, token);

    public Task<Respuesta> PutAsync(string url, object? cuerpo, string? token = null) => EnviarAsync(HttpMethod.Put, url, cuerpo, token);

    public Task<Respuesta> DeleteAsync(string url, string? token = null) => EnviarAsync(HttpMethod.Delete, url, null, token);

    /// <summary>POST con cookies y cabecera CSRF (refresh y logout).</summary>
    public Task<Respuesta> PostConCookiesAsync(string url, string cookies, string? csrf) =>
        EnviarAsync(HttpMethod.Post, url, ajustar: p =>
        {
            p.Headers.Add("Cookie", cookies);
            if (csrf is not null)
            {
                p.Headers.Add("X-CSRF-Token", csrf);
            }
        });

    public async Task<Sesion> IniciarSesionAsync(string email, string password = PasswordSeed)
    {
        var r = await PostAsync("/api/auth/login", new { email, password });
        if (r.Estado != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Login falló para {email}: {(int)r.Estado} {r.Json}");
        }
        return new Sesion(
            r.Data.GetProperty("accessToken").GetString()!,
            r.Data.GetProperty("usuario"),
            r.Cookie("rv_refresh")!,
            r.Cookie("rv_csrf")!,
            r);
    }

    // Datos únicos por prueba: todas comparten la base, así que cada una crea
    // sus propios registros sin chocar con las demás.
    public static string DniUnico() => RandomNumberGenerator.GetInt32(10000000, 99999999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string CelularUnico() => "9" + RandomNumberGenerator.GetInt32(10000000, 99999999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string EmailUnico(string prefijo = "prueba") => $"{prefijo}.{Guid.NewGuid().ToString("N")[..8]}@correo.pe";
}
