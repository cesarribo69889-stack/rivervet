using System.Text;
using System.Text.RegularExpressions;

namespace RiverVet.Aplicacion.Comun;

/// <summary>Limpieza de texto recibido del usuario.</summary>
public static partial class Texto
{
    /// <summary>
    /// Neutraliza HTML en texto libre (XSS almacenado).
    ///
    /// Decisión de diseño: NO convertimos "&lt;" en "&amp;lt;" al guardar. Si lo
    /// hiciéramos, la base quedaría con texto "codificado para HTML", que se
    /// vería mal en PDF, WhatsApp o Excel y se codificaría DOS veces al
    /// mostrarse en Vue. En cambio:
    ///   1) Al GUARDAR se elimina todo lo que tenga forma de etiqueta HTML
    ///      (&lt;script&gt;, &lt;img onerror=...&gt;). "temperatura &lt; 39" se conserva.
    ///   2) Al MOSTRAR, Vue escapa todo lo que va en {{ }} y el frontend tiene
    ///      prohibido v-html.
    /// Dos capas: aunque una falle, la otra detiene el ataque.
    /// </summary>
    public static string NeutralizarHtml(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var sinEtiquetas = EtiquetaHtml().Replace(texto, string.Empty);
        return EsquemaJavascript().Replace(sinEtiquetas, string.Empty);
    }

    /// <summary>
    /// Quita caracteres de control (salvo salto de línea y tabulación), usados
    /// para inyectar líneas falsas en logs.
    /// </summary>
    public static string QuitarControl(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (!char.IsControl(c) || c == '\n' || c == '\t')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Texto libre listo para guardar: sin control, sin HTML, recortado. Vacío = null.</summary>
    public static string? Libre(string? texto)
    {
        if (texto is null)
        {
            return null;
        }
        var limpio = NeutralizarHtml(QuitarControl(texto)).Trim();
        return limpio.Length == 0 ? null : limpio;
    }

    /// <summary>Recorta; vacío = null.</summary>
    public static string? Recortar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>
    /// Arma el patrón de LIKE escapando los comodines (% _ [) para que el
    /// texto se busque LITERALMENTE: buscar "%" no debe traer todo. Esto no es
    /// para prevenir inyección SQL (eso lo hacen los parámetros); es para que
    /// la búsqueda haga lo que el usuario espera. Se usa con ESCAPE '\'.
    /// </summary>
    public static string PatronLike(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var sb = new StringBuilder("%", texto.Length + 4);
        foreach (var c in texto)
        {
            if (c is '\\' or '%' or '_' or '[')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.Append('%').ToString();
    }

    /// <summary>"juan.perez@gmail.com" -> "ju***@gmail.com" (para auditar sin guardar el dato completo).</summary>
    public static string EnmascararEmail(string? email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            return "[EMAIL INVALIDO]";
        }
        var partes = email.Split('@', 2);
        var inicio = partes[0].Length <= 2 ? partes[0] : partes[0][..2];
        return $"{inicio}***@{partes[1]}";
    }

    [GeneratedRegex(@"</?[a-zA-Z!][^>]*>")]
    private static partial Regex EtiquetaHtml();

    [GeneratedRegex(@"javascript\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex EsquemaJavascript();
}
