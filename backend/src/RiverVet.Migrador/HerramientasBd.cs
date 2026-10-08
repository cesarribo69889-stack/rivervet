using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RiverVet.Migrador;

/// <summary>Seed, creación de la base, espera y verificación del esquema.</summary>
public static partial class HerramientasBd
{
    /// <summary>
    /// Carga database/seed/datos_desarrollo.sql en una transacción. El script
    /// es idempotente (si el admin ya existe, no hace nada).
    /// </summary>
    public static async Task AplicarSeedAsync(SqlConnection conexion, string archivo, Action<string> log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        ArgumentNullException.ThrowIfNull(log);
        var contenido = await File.ReadAllTextAsync(archivo, Encoding.UTF8, ct);
        void MostrarPrint(object? _, SqlInfoMessageEventArgs e) => log(e.Message);
        conexion.InfoMessage += MostrarPrint;
        await using var tx = (SqlTransaction)await conexion.BeginTransactionAsync(ct);
        try
        {
            foreach (var lote in Migraciones.DividirEnLotes(contenido))
            {
                await Migraciones.EjecutarAsync(conexion, tx, lote, ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            conexion.InfoMessage -= MostrarPrint;
        }
    }

    /// <summary>
    /// Crea la base si no existe (conectándose a master). Con
    /// <paramref name="recrear"/> la borra y la crea de nuevo, pero SOLO si su
    /// nombre termina en _test: así es imposible destruir una base real por error.
    /// </summary>
    public static async Task CrearBaseDatosAsync(string cadenaConexion, bool recrear = false, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(cadenaConexion);
        var nombre = builder.InitialCatalog;
        // El nombre de una base no puede ir como parámetro en CREATE DATABASE:
        // se valida con lista blanca y además se escapa con QUOTENAME en SQL.
        if (!NombreBaseValido().IsMatch(nombre))
        {
            throw new InvalidOperationException($"Nombre de base inválido: '{nombre}'");
        }
        if (recrear && !nombre.EndsWith("_test", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Por seguridad, solo se pueden recrear bases cuyo nombre termine en _test");
        }

        builder.InitialCatalog = "master";
        await using var conexion = new SqlConnection(builder.ConnectionString);
        await conexion.OpenAsync(ct);
        await using var comando = new SqlCommand("""
            DECLARE @q NVARCHAR(300) = QUOTENAME(@nombre);
            IF @recrear = 1 AND DB_ID(@nombre) IS NOT NULL
            BEGIN
                EXEC (N'ALTER DATABASE ' + @q + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE');
                EXEC (N'DROP DATABASE ' + @q);
            END
            IF DB_ID(@nombre) IS NULL
                EXEC (N'CREATE DATABASE ' + @q);
            """, conexion) { CommandTimeout = 120 };
        comando.Parameters.Add("@nombre", SqlDbType.NVarChar, 128).Value = nombre;
        comando.Parameters.Add("@recrear", SqlDbType.Bit).Value = recrear;
        await comando.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Espera a que SQL Server acepte conexiones (en la CI el contenedor tarda en arrancar).</summary>
    public static async Task EsperarAsync(string cadenaConexion, TimeSpan limite, Action<string> log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        var builder = new SqlConnectionStringBuilder(cadenaConexion) { InitialCatalog = "master", ConnectTimeout = 5 };
        var fin = DateTime.UtcNow + limite;
        while (true)
        {
            try
            {
                await using var conexion = new SqlConnection(builder.ConnectionString);
                await conexion.OpenAsync(ct);
                log("SQL Server está listo.");
                return;
            }
            catch (SqlException) when (DateTime.UtcNow < fin)
            {
                log("Esperando a SQL Server...");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    private static readonly string[] _tablas =
    [
        "usuarios", "refresh_tokens", "auditoria", "clientes", "mascotas",
        "citas", "historial_clinico", "vacunas",
        "categorias", "productos", "carritos", "carrito_items", "pedidos", "pedido_items",
        "pagos", "webhook_eventos", "envios_comprobante", "configuracion_yape", "migraciones_aplicadas",
    ];

    private static readonly (string Tipo, string Nombre, string Motivo)[] _restriccionesCriticas =
    [
        ("indice", "UX_citas_veterinario_horario", "evita el doble agendamiento"),
        ("indice", "UX_usuarios_email", "email de usuario único"),
        ("indice", "UX_refresh_tokens_hash", "refresh token único"),
        ("check", "CK_productos_stock", "evita stock negativo"),
        ("check", "CK_citas_bloque", "citas en bloques de 30 min"),
        ("indice", "UX_pagos_yape_operacion", "un código Yape por pago"),
    ];

    /// <summary>
    /// Verifica que el esquema quedó consistente: migraciones aplicadas, tablas
    /// esperadas, restricciones críticas y ninguna clave foránea deshabilitada.
    /// Devuelve la lista de problemas (vacía = todo bien).
    /// </summary>
    public static async Task<IReadOnlyList<string>> VerificarEsquemaAsync(SqlConnection conexion, string directorioMigraciones, CancellationToken ct = default)
    {
        var problemas = new List<string>();
        var archivos = Migraciones.Leer(directorioMigraciones).Select(m => m.Nombre);
        var aplicadas = await ListaAsync(conexion, "SELECT nombre FROM dbo.migraciones_aplicadas", ct);
        problemas.AddRange(archivos.Except(aplicadas).Select(m => $"Migración sin aplicar: {m}"));

        var tablas = await ListaAsync(conexion, "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo')", ct);
        problemas.AddRange(_tablas.Except(tablas).Select(t => $"Falta la tabla dbo.{t}"));

        var indices = await ListaAsync(conexion, "SELECT name FROM sys.indexes WHERE name IS NOT NULL", ct);
        var checks = await ListaAsync(conexion, "SELECT name FROM sys.check_constraints", ct);
        foreach (var (tipo, nombre, motivo) in _restriccionesCriticas)
        {
            if (!(tipo == "indice" ? indices : checks).Contains(nombre))
            {
                problemas.Add($"Falta {nombre} ({motivo})");
            }
        }

        var fkMalas = await ListaAsync(conexion, "SELECT name FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1", ct);
        problemas.AddRange(fkMalas.Select(f => $"Clave foránea deshabilitada o no verificada: {f}"));
        return problemas;
    }

    private static async Task<List<string>> ListaAsync(SqlConnection conexion, string sql, CancellationToken ct)
    {
        var lista = new List<string>();
#pragma warning disable CA2100 // Consultas fijas del propio código.
        await using var comando = new SqlCommand(sql, conexion);
#pragma warning restore CA2100
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            lista.Add(lector.GetString(0));
        }
        return lista;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,99}$")]
    private static partial Regex NombreBaseValido();
}
