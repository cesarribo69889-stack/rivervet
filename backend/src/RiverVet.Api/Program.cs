// ═══════════════════════════════════════════════════════════════════════
//  RiverVet API — punto de entrada.
//  En Visual Studio: elige el perfil "IIS Express" y pulsa el botón verde.
//  Se abre el navegador en /swagger con todos los endpoints.
// ═══════════════════════════════════════════════════════════════════════
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using RiverVet.Api.Configuracion;
using RiverVet.Api.Filtros;
using RiverVet.Api.Json;
using RiverVet.Api.Middlewares;
using RiverVet.Api.Registro;
using RiverVet.Api.Respuestas;
using RiverVet.Api.Seguridad;
using RiverVet.Aplicacion;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comprobantes;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Pagos;
using RiverVet.Infraestructura;
using RiverVet.Infraestructura.Seguridad;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Opciones (se leen de appsettings*.json, User Secrets y variables de entorno) ──
builder.Services.Configure<OpcionesSeguridad>(builder.Configuration.GetSection(OpcionesSeguridad.Seccion));
builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection(OpcionesJwt.Seccion));
builder.Services.Configure<OpcionesPagos>(builder.Configuration.GetSection(OpcionesPagos.Seccion));
builder.Services.Configure<OpcionesWhatsApp>(builder.Configuration.GetSection(OpcionesWhatsApp.Seccion));

// ── Logs: consola + archivo diario, todo pasa por el redactor de datos sensibles ──
builder.Services.AddSerilog((servicios, log) =>
{
    log.ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .Enrich.With<RedactorLogEnricher>()
        .WriteTo.Console();
    if (!builder.Environment.IsEnvironment("Pruebas"))
    {
        // Un archivo por día; se conservan 14 días (no guardamos IPs e ids
        // más tiempo del necesario).
        var directorio = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Registro:Directorio"] ?? "logs");
        log.WriteTo.File(
            new Serilog.Formatting.Json.JsonFormatter(),
            Path.Combine(directorio, "rivervet-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 20 * 1024 * 1024,
            rollOnFileSizeLimit: true);
    }
});

// ── Capas de la aplicación ──
builder.Services.AgregarAplicacion().AgregarInfraestructura();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoPeticion, ContextoPeticionHttp>();

// ── Controladores + JSON ──
builder.Services
    .AddControllers(opciones =>
    {
        // Toda acción valida sus datos antes de ejecutarse (FluentValidation).
        opciones.Filters.Add<FiltroValidacion>();
        opciones.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(opciones =>
    {
        // LISTA BLANCA: si el JSON trae un campo que el DTO no declara (p. ej.
        // "rol": "admin"), se RECHAZA con 422. Evita el "mass assignment".
        opciones.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        opciones.JsonSerializerOptions.Converters.Add(new ConvertidorFechaUtc());
        opciones.AllowInputFormatterExceptionMessages = true;
    })
    .ConfigureApiBehaviorOptions(opciones => opciones.InvalidModelStateResponseFactory = FabricaErroresModelo.Crear);

// ── Autenticación JWT ──
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<OpcionesJwt>>((opciones, jwt) =>
    {
        opciones.MapInboundClaims = false;
        opciones.TokenValidationParameters = ServicioTokens.ParametrosValidacion(jwt.Value);
        opciones.Events = EventosJwt.Crear();
    });

// Seguro por defecto: todo endpoint exige sesión salvo que diga [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ── CORS con lista blanca (nunca "*") ──
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<OpcionesSeguridad>>((cors, seguridad) => cors.AddPolicy("Frontend", politica => politica
        .WithOrigins([.. seguridad.Value.OrigenesCors])
        .AllowCredentials()
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .WithHeaders("Content-Type", "Authorization", CookiesSesion.CabeceraCsrf)
        .WithExposedHeaders("X-Trace-Id")
        .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

// ── Rate limiting global por IP (100 solicitudes / 15 min por defecto) ──
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.OnRejected = (contexto, _) => new ValueTask(RespuestaError.EscribirAsync(
        contexto.HttpContext,
        StatusCodes.Status429TooManyRequests,
        "Demasiadas solicitudes. Espera unos minutos e inténtalo de nuevo."));
    opciones.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
    {
        // El health check y Swagger no se limitan: el monitoreo no debe bloquearse a sí mismo.
        if (contexto.Request.Path.StartsWithSegments("/api/health") || contexto.Request.Path.StartsWithSegments("/swagger"))
        {
            return RateLimitPartition.GetNoLimiter("sin-limite");
        }
        var seguridad = contexto.RequestServices.GetRequiredService<IOptions<OpcionesSeguridad>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            ClaveIp.Normalizar(contexto.Connection.RemoteIpAddress?.ToString()),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = seguridad.GlobalMax,
                Window = TimeSpan.FromMinutes(seguridad.VentanaMinutos),
                QueueLimit = 0,
            });
    });
});

// ── Proxies de confianza (IP real del cliente detrás de Vercel/Azure) ──
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<OpcionesSeguridad>>((reenvio, seguridad) =>
    {
        reenvio.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        reenvio.ForwardLimit = seguridad.Value.ProxiesConfiables;
        reenvio.KnownIPNetworks.Clear();
        reenvio.KnownProxies.Clear();
    });

// ── Límite de 1 MB en el body (Kestrel e IIS) y sin cabecera "Server" ──
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = ManejadorErroresMiddleware.TamanoMaximoBody;
    kestrel.AddServerHeader = false;
});
builder.Services.Configure<IISServerOptions>(iis => iis.MaxRequestBodySize = ManejadorErroresMiddleware.TamanoMaximoBody);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AgregarSwagger();
}

var app = builder.Build();

// Fail-fast: con configuración inválida la API no arranca.
ValidadorConfiguracion.ValidarOFallar(app.Configuration, app.Environment);

var opcionesSeguridad = app.Services.GetRequiredService<IOptions<OpcionesSeguridad>>().Value;
if (opcionesSeguridad.ProxiesConfiables > 0)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<TrazaMiddleware>();
app.UseMiddleware<ManejadorErroresMiddleware>();
app.UseMiddleware<CabecerasSeguridadMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(ui =>
    {
        ui.SwaggerEndpoint("/swagger/v1/swagger.json", "RiverVet API v1");
        ui.DocumentTitle = "RiverVet API";
    });
}
else
{
    // HSTS: el navegador recordará 1 año que este dominio SOLO se usa por HTTPS.
    app.UseHsts();
}

app.UseRouting();
app.UseCors("Frontend");
// El límite va ANTES de la autenticación: así también cuentan las peticiones
// sin sesión (si fuera después, un 401 cortaría la petición antes de contarla
// y cualquiera podría inundar la API sin iniciar sesión).
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers().RequireCors("Frontend");
app.MapFallback(contexto => RespuestaError.EscribirAsync(
        contexto, StatusCodes.Status404NotFound, "La ruta solicitada no existe"))
    .AllowAnonymous();

await app.RunAsync();

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
