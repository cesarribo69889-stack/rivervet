using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RiverVet.Migrador;

namespace RiverVet.Pruebas.Integracion;

/// <summary>
/// Levanta la API COMPLETA en memoria contra un SQL Server real.
///
/// Antes de las pruebas recrea la base rivervet_test desde cero, aplica TODAS
/// las migraciones con el migrador real y carga el seed. Así cada corrida parte
/// de un estado conocido, igual en tu máquina que en la CI.
///
/// Servidor: variable de entorno RIVERVET_PRUEBAS_SQL (en la CI apunta al
/// contenedor de SQL Server). Por defecto, tu .\SQLEXPRESS con autenticación
/// de Windows.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Coleccion = "Api";

    /// <summary>Secreto de webhooks SOLO para pruebas (las pruebas firman los avisos de la pasarela simulada).</summary>
    public const string SecretoWebhook = "secreto-de-pruebas-para-webhooks-0123456789"; // gitleaks:allow (solo pruebas)

    private const string ServidorPorDefecto = @"Server=.\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

    public string CadenaConexion { get; } = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("RIVERVET_PRUEBAS_SQL") ?? ServidorPorDefecto)
    { InitialCatalog = "rivervet_test" }.ConnectionString;

    /// <summary>Configuración extra (p. ej. límites bajos para probar el rate limit).</summary>
    public Dictionary<string, string?> ConfiguracionExtra { get; } = [];

    public async Task InitializeAsync()
    {
        await HerramientasBd.CrearBaseDatosAsync(CadenaConexion, recrear: true);
        await using var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        var database = Rutas.CarpetaDatabase();
        await Migraciones.AplicarAsync(conexion, Path.Combine(database, "migraciones"), soloVerificar: false, _ => { });
        await HerramientasBd.AplicarSeedAsync(conexion, Path.Combine(database, "seed", "datos_desarrollo.sql"), _ => { });
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Pruebas");
        builder.ConfigureAppConfiguration(configuracion =>
        {
            var valores = new Dictionary<string, string?>
            {
                ["ConnectionStrings:RiverVet"] = CadenaConexion,
                // Secreto SOLO para pruebas (66 caracteres).
                ["Jwt:Secreto"] = string.Concat(Enumerable.Repeat("prueba", 11)),
                // Límites altos: las pruebas no deben bloquearse entre sí. Las de
                // rate limit crean su propia fábrica con límites bajos.
                ["Seguridad:GlobalMax"] = "100000",
                ["Seguridad:LoginIntentosMax"] = "100000",
                // Sin ventana de gracia: permite probar la reutilización de
                // refresh tokens sin esperar.
                ["Seguridad:GraciaRefreshSegundos"] = "0",
                ["Seguridad:CookiesSeguras"] = "false",
                ["Seguridad:OrigenesCors:0"] = "http://localhost:5173",
                ["Serilog:MinimumLevel:Default"] = "Warning",
                // Pagos de prueba: Yape del negocio y pasarela SIMULADA con
                // un secreto de webhooks solo para pruebas.
                ["Pagos:Yape:Celular"] = "987000111",
                ["Pagos:Yape:Titular"] = "RiverVet SAC",
                ["Pagos:Pasarela:Proveedor"] = "simulada",
                ["Pagos:Pasarela:SecretoWebhook"] = SecretoWebhook,
                ["WhatsApp:Modo"] = "enlace",
            };
            foreach (var (clave, valor) in ConfiguracionExtra)
            {
                valores[clave] = valor;
            }
            configuracion.AddInMemoryCollection(valores);
        });
    }

    /// <summary>Cliente HTTP que NO maneja cookies solo: las pruebas las envían a mano para controlarlas.</summary>
    public ClienteApi CrearClienteApi() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false }));

    /// <summary>Otra instancia de la API con configuración distinta (misma base de datos).</summary>
    public static FabricaApi ConConfiguracion(Dictionary<string, string?> extra)
    {
        var fabrica = new FabricaApi();
        foreach (var (clave, valor) in extra)
        {
            fabrica.ConfiguracionExtra[clave] = valor;
        }
        return fabrica;
    }

    /// <summary>Ejecuta una consulta directa a la base de pruebas (para verificar lo que quedó guardado).</summary>
    public async Task<T?> EscalarAsync<T>(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync();
#pragma warning disable CA2100 // Consultas fijas de las pruebas, con parámetros.
        await using var comando = new SqlCommand(sql, conexion);
#pragma warning restore CA2100
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nombre, valor);
        }
        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? default : (T)resultado;
    }
}

[CollectionDefinition(FabricaApi.Coleccion)]
public sealed class ColeccionApi : ICollectionFixture<FabricaApi>;
