using System.Text.Json.Serialization;

namespace RiverVet.Aplicacion.Comun;

/// <summary>Parámetros de paginación de los listados (?pagina=1&amp;limite=10).</summary>
public class PaginacionConsulta
{
    public int Pagina { get; set; } = 1;

    public int Limite { get; set; } = 10;

    /// <summary>OFFSET ... FETCH NEXT (SQL Server no tiene LIMIT).</summary>
    [JsonIgnore]
    public int Offset => (Pagina - 1) * Limite;
}

/// <summary>
/// Búsqueda por texto. Va en el BODY de un POST y no en la URL: el texto puede
/// ser un DNI o un nombre, y una URL queda registrada en logs de proxies, del
/// servidor y en el historial del navegador.
/// </summary>
public sealed class BusquedaSolicitud : PaginacionConsulta, INormalizable
{
    public string? Texto { get; set; }

    public void Normalizar() => Texto = string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim();
}

/// <summary>Filas que traen el total del listado calculado con COUNT(*) OVER().</summary>
public interface IConTotal
{
    int Total { get; }
}

public sealed record InfoPaginacion(int Pagina, int Limite, int Total, int TotalPaginas);

/// <summary>Forma estándar de todo listado: { items, paginacion }.</summary>
public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Items, InfoPaginacion Paginacion);

public static class ResultadoPaginado
{
    public static ResultadoPaginado<T> Crear<T>(IReadOnlyList<T> filas, PaginacionConsulta consulta)
        where T : IConTotal
    {
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(consulta);
        var total = filas.Count > 0 ? filas[0].Total : 0;
        var totalPaginas = (int)Math.Ceiling(total / (double)consulta.Limite);
        return new ResultadoPaginado<T>(filas, new InfoPaginacion(consulta.Pagina, consulta.Limite, total, totalPaginas));
    }
}
