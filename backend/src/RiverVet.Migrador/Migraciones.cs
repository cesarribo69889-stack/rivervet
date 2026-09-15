using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RiverVet.Migrador;

/// <summary>Una migración leída del disco.</summary>
public sealed record Migracion(string Nombre, string Contenido, string Hash);

/// <summary>
/// Motor de migraciones versionadas.
///
/// Reglas:
///  1. Las migraciones son archivos NUMERADOS e INMUTABLES en
///     database/migraciones/ (0001_..., 0002_...). Nunca se edita una ya
///     aplicada: si el modelo cambia, se agrega una nueva.
///  2. dbo.migraciones_aplicadas registra cuáles se aplicaron y el hash
///     SHA-256 de su contenido.
///  3. Si el hash de una migración ya aplicada no coincide con el archivo,
///     alguien la editó: se DETIENE todo con error (si no, la base de
///     producción y la de un compañero podrían divergir sin que nadie lo note).
///  4. Cada migración se aplica en su propia transacción: completa o nada.
///  5. Es idempotente: correrlo dos veces no hace nada la segunda vez.
///  6. Un bloqueo de aplicación (sp_getapplock) impide que dos procesos
///     migren a la vez (p. ej. dos despliegues simultáneos).
/// </summary>
public static partial class Migraciones
{
    /// <summary>
    /// Normaliza fin de línea (CRLF -> LF) y quita el BOM antes de calcular el
    /// hash: el mismo archivo debe dar el mismo hash en Windows y en Linux.
    /// </summary>
    public static string Normalizar(string contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        const char Bom = (char)0xFEFF;
        var sinBom = contenido.Length > 0 && contenido[0] == Bom ? contenido[1..] : contenido;
        return sinBom.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string CalcularHash(string contenido) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalizar(contenido))));

    /// <summary>
    /// Divide el script en lotes por las líneas "GO". GO no es SQL: es un
    /// separador que entienden SSMS y sqlcmd, pero no el driver.
    /// </summary>
    public static IReadOnlyList<string> DividirEnLotes(string contenido) =>
        [.. SeparadorGo().Split(Normalizar(contenido))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !SoloComentarios(l))];

    private static bool SoloComentarios(string lote) =>
        lote.Split('\n').Select(l => l.Trim()).All(l => l.Length == 0 || l.StartsWith("--", StringComparison.Ordinal));

    public static IReadOnlyList<Migracion> Leer(string directorio)
    {
        var archivos = Directory.GetFiles(directorio, "*.sql").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        var invalidos = archivos.Where(a => !NombreValido().IsMatch(a!)).ToList();
        if (invalidos.Count > 0)
        {
            throw new InvalidOperationException(
                $"Nombres de migración inválidos (formato 0001_nombre.sql): {string.Join(", ", invalidos)}");
        }
        return [.. archivos.Select(a =>
        {
            var contenido = File.ReadAllText(Path.Combine(directorio, a!), Encoding.UTF8);
            return new Migracion(a!, contenido, CalcularHash(contenido));
        })];
    }

    /// <summary>
    /// Compara archivos vs. base. Falla si una aplicada fue editada, si falta
    /// su archivo o si aparece una nueva con número menor que la última aplicada.
    /// Devuelve las pendientes, en orden.
    /// </summary>
    public static IReadOnlyList<Migracion> Planificar(IReadOnlyList<Migracion> migraciones, IReadOnlyDictionary<string, string> aplicadas)
    {
        ArgumentNullException.ThrowIfNull(migraciones);
        ArgumentNullException.ThrowIfNull(aplicadas);
        var errores = new List<string>();
        var porNombre = migraciones.ToDictionary(m => m.Nombre, StringComparer.Ordinal);

        foreach (var (nombre, hash) in aplicadas)
        {
            if (!porNombre.TryGetValue(nombre, out var archivo))
            {
                errores.Add($"La migración {nombre} está aplicada en la base pero su archivo no existe.");
            }
            else if (!string.Equals(archivo.Hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                errores.Add($"La migración {nombre} fue MODIFICADA después de aplicarse (hash distinto). " +
                            "Revierte el cambio y crea una migración nueva.");
            }
        }
        if (errores.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errores));
        }

        var pendientes = migraciones.Where(m => !aplicadas.ContainsKey(m.Nombre)).ToList();
        var ultima = aplicadas.Keys.Order(StringComparer.Ordinal).LastOrDefault();
        var fueraDeOrden = pendientes.Where(m => ultima is not null && string.CompareOrdinal(m.Nombre, ultima) < 0).ToList();
        if (fueraDeOrden.Count > 0)
        {
            throw new InvalidOperationException(
                $"Migraciones fuera de orden (número menor que la última aplicada, {ultima}): " +
                $"{string.Join(", ", fueraDeOrden.Select(m => m.Nombre))}. Renómbralas con un número mayor.");
        }
        return pendientes;
    }

    /// <summary>
    /// Aplica las pendientes. Con <paramref name="soloVerificar"/> no cambia
    /// nada: comprueba hashes y lista las pendientes (útil antes de desplegar).
    /// </summary>
    public static async Task<IReadOnlyList<string>> AplicarAsync(
        SqlConnection conexion, string directorio, bool soloVerificar, Action<string> log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        ArgumentNullException.ThrowIfNull(log);

        var lock_ = await EscalarAsync<int>(conexion, null, """
            DECLARE @r INT;
            EXEC @r = sp_getapplock @Resource = 'rivervet_migraciones', @LockMode = 'Exclusive',
                                    @LockOwner = 'Session', @LockTimeout = 60000;
            SELECT @r;
            """, ct);
        if (lock_ < 0)
        {
            throw new InvalidOperationException("Otro proceso está aplicando migraciones. Intenta de nuevo en un momento.");
        }

        try
        {
            await EjecutarAsync(conexion, null, """
                IF OBJECT_ID(N'dbo.migraciones_aplicadas', N'U') IS NULL
                CREATE TABLE dbo.migraciones_aplicadas (
                    nombre      VARCHAR(200) NOT NULL CONSTRAINT PK_migraciones_aplicadas PRIMARY KEY,
                    hash        CHAR(64)     NOT NULL,
                    aplicada_en DATETIME2(0) NOT NULL CONSTRAINT DF_migraciones_aplicadas_en DEFAULT (SYSUTCDATETIME())
                );
                """, ct);

            var aplicadas = await LeerAplicadasAsync(conexion, ct);
            var pendientes = Planificar(Leer(directorio), aplicadas);

            if (pendientes.Count == 0)
            {
                log("No hay migraciones pendientes. La base está al día.");
                return [];
            }
            if (soloVerificar)
            {
                log($"Migraciones pendientes: {string.Join(", ", pendientes.Select(m => m.Nombre))}");
                return [.. pendientes.Select(m => m.Nombre)];
            }

            var hechas = new List<string>();
            foreach (var migracion in pendientes)
            {
                log($"Aplicando {migracion.Nombre}...");
                await AplicarUnaAsync(conexion, migracion, ct);
                hechas.Add(migracion.Nombre);
                log($"  OK {migracion.Nombre}");
            }
            return hechas;
        }
        finally
        {
            await EjecutarAsync(conexion, null,
                "EXEC sp_releaseapplock @Resource = 'rivervet_migraciones', @LockOwner = 'Session';", CancellationToken.None);
        }
    }

    private static async Task AplicarUnaAsync(SqlConnection conexion, Migracion migracion, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await conexion.BeginTransactionAsync(ct);
        try
        {
            foreach (var lote in DividirEnLotes(migracion.Contenido))
            {
                await EjecutarAsync(conexion, tx, lote, ct);
            }
            await using (var registro = new SqlCommand(
                "INSERT INTO dbo.migraciones_aplicadas (nombre, hash) VALUES (@nombre, @hash)", conexion, tx))
            {
                registro.Parameters.Add("@nombre", System.Data.SqlDbType.VarChar, 200).Value = migracion.Nombre;
                registro.Parameters.Add("@hash", System.Data.SqlDbType.Char, 64).Value = migracion.Hash;
                await registro.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (SqlException ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException($"Falló la migración {migracion.Nombre}: {ex.Message}", ex);
        }
    }

    private static async Task<Dictionary<string, string>> LeerAplicadasAsync(SqlConnection conexion, CancellationToken ct)
    {
        var aplicadas = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var comando = new SqlCommand("SELECT nombre, hash FROM dbo.migraciones_aplicadas", conexion);
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            aplicadas[lector.GetString(0)] = lector.GetString(1);
        }
        return aplicadas;
    }

    // Los lotes vienen de archivos del propio repositorio (no de un usuario).
#pragma warning disable CA2100
    internal static async Task EjecutarAsync(SqlConnection conexion, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var comando = new SqlCommand(sql, conexion, tx) { CommandTimeout = 120 };
        await comando.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<T> EscalarAsync<T>(SqlConnection conexion, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var comando = new SqlCommand(sql, conexion, tx) { CommandTimeout = 120 };
        return (T)Convert.ChangeType(await comando.ExecuteScalarAsync(ct), typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }
#pragma warning restore CA2100

    [GeneratedRegex(@"^\s*GO\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SeparadorGo();

    [GeneratedRegex(@"^\d{4}_[a-z0-9_]+\.sql$")]
    private static partial Regex NombreValido();
}
