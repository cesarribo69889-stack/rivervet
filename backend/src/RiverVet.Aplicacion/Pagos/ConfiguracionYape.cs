using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Pagos;

/// <summary>Fila de dbo.configuracion_yape: lo que el administrador guardó desde el panel.</summary>
public sealed class ConfiguracionYapeGuardada
{
    public string? Celular { get; init; }

    public string? Titular { get; init; }

    public bool TieneQr { get; init; }

    public DateTime? QrActualizadoEn { get; init; }
}

/// <summary>Imagen del QR de Yape tal como se guarda y se sirve.</summary>
public sealed class ImagenQr
{
    public byte[] Contenido { get; init; } = [];

    public string Tipo { get; init; } = string.Empty;
}

/// <summary>
/// Configuración de Yape que rige: la del panel si el administrador ya la
/// guardó; si no, la del App Service (Pagos:Yape:*), como antes de 0006.
/// </summary>
public sealed record ConfiguracionYapeVigente(string? Celular, string? Titular, bool TieneQr, string? QrVersion)
{
    public bool Habilitado => !string.IsNullOrWhiteSpace(Celular);
}

/// <summary>Lo que ve el administrador en "Métodos de pago".</summary>
public sealed record ConfiguracionYapeRespuesta(string? Celular, string? Titular, bool Habilitado, bool TieneQr, string? QrVersion);

/// <summary>El administrador cambia el número y el titular del Yape del negocio.</summary>
public sealed class ActualizarYapeSolicitud : INormalizable
{
    public string? Celular { get; set; }

    public string? Titular { get; set; }

    public void Normalizar()
    {
        Celular = Texto.Recortar(Celular)?.Replace(" ", string.Empty, StringComparison.Ordinal);
        Titular = Texto.Recortar(Titular);
    }
}

/// <summary>
/// El administrador sube la imagen del QR como data URL
/// ("data:image/png;base64,..."), que es lo que entrega el navegador al leer
/// un archivo. Va en JSON: así pasa por la misma validación que el resto.
/// </summary>
public sealed class SubirQrYapeSolicitud : INormalizable
{
    public string? Imagen { get; set; }

    public void Normalizar() => Imagen = Imagen?.Trim();
}

public sealed class ActualizarYapeValidador : AbstractValidator<ActualizarYapeSolicitud>
{
    public ActualizarYapeValidador()
    {
        RuleFor(x => x.Celular).Celular();
        RuleFor(x => x.Titular).NombrePropio(100);
    }
}

public sealed class SubirQrYapeValidador : AbstractValidator<SubirQrYapeSolicitud>
{
    public SubirQrYapeValidador()
    {
        RuleFor(x => x.Imagen).NotEmpty().WithMessage("Elige la imagen del QR");
        RuleFor(x => x.Imagen).Custom((imagen, contexto) =>
        {
            if (!string.IsNullOrEmpty(imagen) && ImagenesQr.Leer(imagen, out _) is { } error)
            {
                contexto.AddFailure(error);
            }
        });
    }
}

/// <summary>Lectura y control de la imagen subida: tipo, tamaño y contenido real.</summary>
public static class ImagenesQr
{
    /// <summary>Tamaño máximo de la imagen (el body de la API admite 1 MB y el base64 ocupa un tercio más).</summary>
    public const int TamanoMaximo = 500 * 1024;

    private static readonly string[] _tipos = ["image/png", "image/jpeg", "image/webp"];

    /// <summary>Devuelve null si la imagen es válida (y la entrega en <paramref name="imagen"/>), o el motivo del rechazo.</summary>
    public static string? Leer(string dataUrl, out ImagenQr? imagen)
    {
        imagen = null;
        const string Base64 = ";base64,";
        var separador = dataUrl.IndexOf(Base64, StringComparison.Ordinal);
        if (!dataUrl.StartsWith("data:", StringComparison.Ordinal) || separador < 0)
        {
            return "La imagen no llegó en un formato válido";
        }
        var declarado = dataUrl[5..separador];
        var tipo = _tipos.FirstOrDefault(t => string.Equals(t, declarado, StringComparison.OrdinalIgnoreCase));
        if (tipo is null)
        {
            return "La imagen debe ser PNG, JPG o WEBP";
        }
        var datos = dataUrl[(separador + Base64.Length)..];
        // Antes de decodificar: un base64 de más de 4/3 del máximo ya es grande.
        if (datos.Length > (TamanoMaximo / 3 * 4) + 4)
        {
            return "La imagen debe pesar como máximo 500 KB";
        }
        byte[] contenido;
        try
        {
            contenido = Convert.FromBase64String(datos);
        }
        catch (FormatException)
        {
            return "La imagen no llegó en un formato válido";
        }
        if (contenido.Length is 0 or > TamanoMaximo)
        {
            return "La imagen debe pesar como máximo 500 KB";
        }
        // El tipo declarado se comprueba contra los primeros bytes: un archivo
        // cualquiera renombrado a .png no se guarda ni se sirve como imagen.
        if (!CoincideFirma(tipo, contenido))
        {
            return "El archivo no es una imagen PNG, JPG o WEBP válida";
        }
        imagen = new ImagenQr { Contenido = contenido, Tipo = tipo };
        return null;
    }

    private static bool CoincideFirma(string tipo, byte[] c) => tipo switch
    {
        "image/png" => c.Length > 8 && c[0] == 0x89 && c[1] == 0x50 && c[2] == 0x4E && c[3] == 0x47,
        "image/jpeg" => c.Length > 3 && c[0] == 0xFF && c[1] == 0xD8 && c[2] == 0xFF,
        "image/webp" => c.Length > 12 && c[0] == 'R' && c[1] == 'I' && c[2] == 'F' && c[3] == 'F'
                        && c[8] == 'W' && c[9] == 'E' && c[10] == 'B' && c[11] == 'P',
        _ => false,
    };
}

public interface IConfiguracionYapeRepositorio
{
    Task<ConfiguracionYapeGuardada?> ObtenerAsync(CancellationToken ct = default);

    Task<ImagenQr?> ObtenerQrAsync(CancellationToken ct = default);

    Task GuardarDatosAsync(string celular, string titular, int? usuarioId, CancellationToken ct = default);

    Task GuardarQrAsync(ImagenQr imagen, int? usuarioId, CancellationToken ct = default);
}

/// <summary>
/// "Métodos de pago" del panel (solo administrador): número, titular y QR
/// de Yape. Cada cambio queda en la auditoría con el valor anterior.
/// </summary>
public sealed class ConfiguracionYapeServicio(
    IConfiguracionYapeRepositorio repositorio,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto,
    IOptions<OpcionesPagos> opciones)
{
    public async Task<ConfiguracionYapeVigente> VigenteAsync(CancellationToken ct = default)
    {
        var guardada = await repositorio.ObtenerAsync(ct);
        var servidor = opciones.Value.Yape;
        // El panel manda en cuanto se guarda un número; el titular va con él.
        var (celular, titular) = guardada?.Celular is not null
            ? (guardada.Celular, guardada.Titular)
            : (servidor.Celular, servidor.Titular);
        var version = guardada is { TieneQr: true, QrActualizadoEn: { } fecha }
            ? fecha.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            : null;
        return new ConfiguracionYapeVigente(celular, titular, guardada?.TieneQr ?? false, version);
    }

    public async Task<ConfiguracionYapeRespuesta> ObtenerAsync(CancellationToken ct = default)
    {
        var v = await VigenteAsync(ct);
        return new ConfiguracionYapeRespuesta(v.Celular, v.Titular, v.Habilitado, v.TieneQr, v.QrVersion);
    }

    public async Task<ConfiguracionYapeRespuesta> ActualizarAsync(ActualizarYapeSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var antes = await VigenteAsync(ct);
        await repositorio.GuardarDatosAsync(solicitud.Celular!, solicitud.Titular!, contexto.UsuarioId, ct);
        await auditoria.RegistrarAsync(
            AccionesAuditoria.Actualizar, RecursosAuditoria.ConfiguracionPagos, "yape",
            new
            {
                antes = new { antes.Celular, antes.Titular },
                despues = new { solicitud.Celular, solicitud.Titular },
            },
            ct: ct);
        return await ObtenerAsync(ct);
    }

    public async Task<ConfiguracionYapeRespuesta> SubirQrAsync(SubirQrYapeSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        // El validador ya la aceptó; se vuelve a leer para obtener los bytes.
        if (ImagenesQr.Leer(solicitud.Imagen ?? string.Empty, out var imagen) is { } error)
        {
            throw new ValidacionException("imagen", error);
        }
        await repositorio.GuardarQrAsync(imagen!, contexto.UsuarioId, ct);
        await auditoria.RegistrarAsync(
            AccionesAuditoria.Actualizar, RecursosAuditoria.ConfiguracionPagos, "yape_qr",
            new { imagen!.Tipo, bytes = imagen.Contenido.Length },
            ct: ct);
        return await ObtenerAsync(ct);
    }

    public Task<ImagenQr?> QrAsync(CancellationToken ct = default) => repositorio.ObtenerQrAsync(ct);
}
