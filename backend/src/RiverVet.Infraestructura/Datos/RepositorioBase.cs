using Dapper;
using Microsoft.Data.SqlClient;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// Base de los repositorios. Toda consulta es SQL escrito a mano y
/// PARAMETRIZADO: los valores viajan aparte del texto de la consulta, así que
/// un email como "x' OR 1=1 --" se busca literalmente y nunca se ejecuta.
/// Nunca se arma SQL concatenando datos del usuario (el analizador CA2100 lo
/// marca como error de compilación).
/// </summary>
public abstract class RepositorioBase(SesionBd sesion)
{
    private const int ViolacionUniqueConstraint = 2627;
    private const int ViolacionUniqueIndex = 2601;

    protected async Task<IReadOnlyList<T>> ConsultarAsync<T>(string sql, object? parametros, CancellationToken ct)
    {
        var conexion = await sesion.ObtenerConexionAsync(ct);
        var filas = await conexion.QueryAsync<T>(Comando(sql, parametros, ct));
        return filas.AsList();
    }

    protected async Task<T?> ConsultarUnoAsync<T>(string sql, object? parametros, CancellationToken ct)
    {
        var conexion = await sesion.ObtenerConexionAsync(ct);
        return await conexion.QuerySingleOrDefaultAsync<T>(Comando(sql, parametros, ct));
    }

    protected async Task<T> EscalarAsync<T>(string sql, object? parametros, CancellationToken ct)
    {
        var conexion = await sesion.ObtenerConexionAsync(ct);
        return await TraducirDuplicadosAsync(() => conexion.ExecuteScalarAsync<T>(Comando(sql, parametros, ct)))
            ?? throw new InvalidOperationException("La consulta no devolvió resultado");
    }

    protected async Task<T?> EscalarOpcionalAsync<T>(string sql, object? parametros, CancellationToken ct)
    {
        var conexion = await sesion.ObtenerConexionAsync(ct);
        return await TraducirDuplicadosAsync(() => conexion.ExecuteScalarAsync<T>(Comando(sql, parametros, ct)));
    }

    protected async Task<T?> InsertarYDevolverAsync<T>(string sql, object? parametros, CancellationToken ct)
    {
        var conexion = await sesion.ObtenerConexionAsync(ct);
        return await TraducirDuplicadosAsync(() => conexion.QuerySingleOrDefaultAsync<T>(Comando(sql, parametros, ct)));
    }

    /// <summary>Ejecuta y devuelve las filas afectadas.</summary>
    protected async Task<int> EjecutarAsync(string sql, object? parametros, CancellationToken ct)
    {
        var conexion = await sesion.ObtenerConexionAsync(ct);
        return await TraducirDuplicadosAsync(() => conexion.ExecuteAsync(Comando(sql, parametros, ct)));
    }

    private CommandDefinition Comando(string sql, object? parametros, CancellationToken ct) =>
        new(sql, parametros, sesion.Transaccion, commandTimeout: 15, cancellationToken: ct);

    /// <summary>
    /// Traduce las violaciones de unicidad (2627/2601) a RegistroDuplicadoException
    /// con el nombre de la restricción, para que el servicio responda 409 con
    /// un mensaje claro. SQL Server puede reportar varios errores juntos (p. ej.
    /// 2601 seguido de 3621 "The statement has been terminated"): se revisan todos.
    /// </summary>
    private static async Task<T> TraducirDuplicadosAsync<T>(Func<Task<T>> operacion)
    {
        try
        {
            return await operacion();
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number is ViolacionUniqueConstraint or ViolacionUniqueIndex))
        {
            throw new RegistroDuplicadoException(ExtraerRestriccion(ex.Message), ex);
        }
    }

    /// <summary>Del mensaje "...with unique index 'UX_clientes_dni'..." extrae UX_clientes_dni.</summary>
    internal static string? ExtraerRestriccion(string mensaje)
    {
        var inicio = mensaje.IndexOf("'UX_", StringComparison.Ordinal);
        if (inicio < 0)
        {
            return null;
        }
        var fin = mensaje.IndexOf('\'', inicio + 1);
        return fin > inicio ? mensaje[(inicio + 1)..fin] : null;
    }

    /// <summary>
    /// Texto para columnas VARCHAR/CHAR (ASCII). Con el tipo correcto SQL
    /// Server usa el índice; con NVARCHAR convertiría la columna fila por fila.
    /// </summary>
    protected static DbString Ascii(string? valor, int longitud, bool fija = false) =>
        new() { Value = valor, IsAnsi = true, Length = longitud, IsFixedLength = fija };

    /// <summary>Texto para columnas NVARCHAR (tildes, ñ).</summary>
    protected static DbString Unicode(string? valor, int longitud) =>
        new() { Value = valor, IsAnsi = false, Length = longitud };

    /// <summary>Patrón de LIKE con comodines escapados (se usa con ESCAPE '\').</summary>
    protected static DbString PatronBusqueda(string? texto) =>
        Unicode(string.IsNullOrWhiteSpace(texto) ? null : Texto.PatronLike(texto), 210);
}
