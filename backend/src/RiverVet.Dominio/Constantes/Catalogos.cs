namespace RiverVet.Dominio.Constantes;

/// <summary>
/// Valores permitidos que la base de datos también exige con CHECK constraints
/// (database/migraciones/0001_esquema_inicial.sql). Validar aquí da mensajes
/// claros; el CHECK es la última línea de defensa.
/// </summary>
public static class Catalogos
{
    public static readonly IReadOnlyList<string> Especies = ["perro", "gato", "ave", "conejo", "roedor", "reptil", "otro"];

    public static readonly IReadOnlyList<string> Sexos = ["macho", "hembra"];
}

/// <summary>Motivos de revocación de un refresh token (CHECK en refresh_tokens).</summary>
public static class MotivosRevocacion
{
    public const string Rotado = "rotado";
    public const string Logout = "logout";
    public const string ReusoDetectado = "reuso_detectado";
    public const string CambioPassword = "cambio_password";
    public const string UsuarioDesactivado = "usuario_desactivado";
}
