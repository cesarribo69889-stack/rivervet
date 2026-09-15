namespace RiverVet.Aplicacion.Comun;

/// <summary>
/// Parámetros de seguridad (sección "Seguridad" de appsettings.json). Todos
/// tienen un valor por defecto seguro; se pueden ajustar por entorno.
/// </summary>
public sealed class OpcionesSeguridad
{
    public const string Seccion = "Seguridad";

    /// <summary>Contraseñas incorrectas seguidas antes de bloquear la cuenta.</summary>
    public int BloqueoIntentosMax { get; set; } = 5;

    public int BloqueoMinutos { get; set; } = 15;

    /// <summary>Intentos fallidos de login por IP + email dentro de la ventana.</summary>
    public int LoginIntentosMax { get; set; } = 5;

    /// <summary>Solicitudes por IP dentro de la ventana (límite global de la API).</summary>
    public int GlobalMax { get; set; } = 100;

    public int VentanaMinutos { get; set; } = 15;

    public int DiasRefresh { get; set; } = 7;

    /// <summary>
    /// Tolerancia si dos pestañas refrescan la sesión a la vez con el mismo
    /// token. Fuera de esta ventana, reutilizar un token = robo.
    /// </summary>
    public int GraciaRefreshSegundos { get; set; } = 15;

    /// <summary>Cookies solo por HTTPS. true en producción; false en desarrollo local por HTTP.</summary>
    public bool CookiesSeguras { get; set; } = true;

    /// <summary>Cuántos proxies hay delante (Vercel + Azure = 2). 0 = ninguno.</summary>
    public int ProxiesConfiables { get; set; }

    /// <summary>Orígenes que pueden llamar a la API desde el navegador. Nunca "*".</summary>
    public IList<string> OrigenesCors { get; set; } = [];
}
