using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace RiverVet.Migrador;

/// <summary>
/// Herramienta de base de datos de RiverVet.
///
///   dotnet run --project src/RiverVet.Migrador -- migrar      aplica las migraciones pendientes
///   dotnet run --project src/RiverVet.Migrador -- verificar   solo verifica (no cambia nada)
///   dotnet run --project src/RiverVet.Migrador -- seed        datos de prueba (bloqueado en producción)
///   dotnet run --project src/RiverVet.Migrador -- esquema     verifica tablas y restricciones críticas
///   dotnet run --project src/RiverVet.Migrador -- crear-bd    crea la base si no existe (CI)
///   dotnet run --project src/RiverVet.Migrador -- esperar     espera a que SQL Server responda (CI)
///   dotnet run --project src/RiverVet.Migrador -- crear-admin email "Nombre"
///                                                             primer administrador (producción);
///                                                             contraseña en RIVERVET_ADMIN_PASSWORD
///
/// Usa la cadena ConnectionStrings:Migrador (usuario con permisos de DDL),
/// leída de User Secrets (desarrollo) o de la variable de entorno
/// ConnectionStrings__Migrador (CI y producción).
/// </summary>
internal static class Programa
{
    private static async Task<int> Main(string[] args)
    {
        var comando = args.FirstOrDefault() ?? "ayuda";
        try
        {
            var configuracion = new ConfigurationBuilder()
                .AddUserSecrets(typeof(Programa).Assembly, optional: true)
                .AddEnvironmentVariables()
                .Build();
            var cadena = configuracion.GetConnectionString("Migrador")
                ?? throw new InvalidOperationException(
                    "Falta ConnectionStrings:Migrador. Ejecuta configurar-desarrollo.ps1 o define la variable ConnectionStrings__Migrador.");
            var database = Rutas.CarpetaDatabase(args);
            var migraciones = Path.Combine(database, "migraciones");

            switch (comando)
            {
                case "migrar":
                case "verificar":
                    {
                        await using var conexion = await AbrirAsync(cadena);
                        var aplicadas = await Migraciones.AplicarAsync(conexion, migraciones, comando == "verificar", Console.WriteLine);
                        if (comando == "migrar" && aplicadas.Count > 0)
                        {
                            Console.WriteLine($"Listo: {aplicadas.Count} migración(es) aplicada(s).");
                        }
                        return 0;
                    }
                case "seed":
                    {
                        // Los usuarios de prueba tienen una contraseña conocida:
                        // en producción serían una puerta abierta.
                        if (EsProduccion())
                        {
                            Console.Error.WriteLine("ERROR: el seed de desarrollo no se puede ejecutar en producción.");
                            return 1;
                        }
                        await using var conexion = await AbrirAsync(cadena);
                        await HerramientasBd.AplicarSeedAsync(conexion, Path.Combine(database, "seed", "datos_desarrollo.sql"), Console.WriteLine);
                        return 0;
                    }
                case "esquema":
                    {
                        await using var conexion = await AbrirAsync(cadena);
                        var problemas = await HerramientasBd.VerificarEsquemaAsync(conexion, migraciones);
                        if (problemas.Count > 0)
                        {
                            Console.Error.WriteLine("Esquema INCONSISTENTE:");
                            foreach (var p in problemas)
                            {
                                Console.Error.WriteLine($"  - {p}");
                            }
                            return 1;
                        }
                        Console.WriteLine("Esquema consistente: migraciones, tablas y restricciones críticas OK.");
                        return 0;
                    }
                case "crear-bd":
                    await HerramientasBd.CrearBaseDatosAsync(cadena);
                    Console.WriteLine($"Base {new SqlConnectionStringBuilder(cadena).InitialCatalog} lista.");
                    return 0;
                case "esperar":
                    await HerramientasBd.EsperarAsync(cadena, TimeSpan.FromMinutes(2), Console.WriteLine);
                    return 0;
                case "crear-admin":
                    {
                        if (args.Length < 3)
                        {
                            Console.Error.WriteLine("Uso: crear-admin <email> \"<nombre>\"   (contraseña en la variable RIVERVET_ADMIN_PASSWORD)");
                            return 1;
                        }
                        var password = Environment.GetEnvironmentVariable("RIVERVET_ADMIN_PASSWORD");
                        var errores = PrimerAdministrador.ValidarPassword(password);
                        if (errores.Count > 0)
                        {
                            Console.Error.WriteLine($"ERROR: la contraseña de RIVERVET_ADMIN_PASSWORD no cumple: {string.Join(", ", errores)}.");
                            return 1;
                        }
                        await using var conexion = await AbrirAsync(cadena);
                        var creado = await PrimerAdministrador.CrearAsync(conexion, args[1], args[2], password!);
                        Console.WriteLine(creado ? $"Administrador {args[1]} creado." : $"Ya existe un usuario con el email {args[1]}: no se cambió nada.");
                        return 0;
                    }
                default:
                    Console.WriteLine("Comandos: migrar | verificar | seed | esquema | crear-bd | esperar | crear-admin  [--directorio <ruta a database>]");
                    return comando == "ayuda" ? 0 : 1;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqlException or IOException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static async Task<SqlConnection> AbrirAsync(string cadena)
    {
        var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        return conexion;
    }

    private static bool EsProduccion() =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Ubica la carpeta database/ del repositorio.</summary>
public static class Rutas
{
    public static string CarpetaDatabase(string[]? args = null)
    {
        var indice = Array.IndexOf(args ?? [], "--directorio");
        if (indice >= 0 && args!.Length > indice + 1)
        {
            return Path.GetFullPath(args[indice + 1]);
        }
        foreach (var inicio in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(inicio); dir is not null; dir = dir.Parent)
            {
                var candidato = Path.Combine(dir.FullName, "database", "migraciones");
                if (Directory.Exists(candidato))
                {
                    return Path.Combine(dir.FullName, "database");
                }
            }
        }
        throw new InvalidOperationException("No se encontró la carpeta database/migraciones. Usa --directorio <ruta>.");
    }
}
