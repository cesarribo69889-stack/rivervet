using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RiverVet.Migrador;

/// <summary>
/// Crea el PRIMER administrador en una base nueva (producción), donde no se
/// carga el seed. La contraseña llega por variable de entorno
/// (RIVERVET_ADMIN_PASSWORD), nunca como argumento: los argumentos quedan en
/// el historial de la terminal y en la lista de procesos.
/// </summary>
public static partial class PrimerAdministrador
{
    /// <summary>Misma política que la API: 10+ caracteres, mayúscula, minúscula, número y símbolo; máx. 72 bytes.</summary>
    public static IReadOnlyList<string> ValidarPassword(string? password)
    {
        var errores = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < 10)
        {
            errores.Add("mínimo 10 caracteres");
        }
        password ??= string.Empty;
        if (Encoding.UTF8.GetByteCount(password) > 72)
        {
            errores.Add("máximo 72 caracteres");
        }
        if (!Mayuscula().IsMatch(password))
        {
            errores.Add("una mayúscula");
        }
        if (!Minuscula().IsMatch(password))
        {
            errores.Add("una minúscula");
        }
        if (!Digito().IsMatch(password))
        {
            errores.Add("un número");
        }
        if (!Simbolo().IsMatch(password))
        {
            errores.Add("un símbolo");
        }
        return errores;
    }

    /// <summary>Inserta el administrador si el email no existe. Devuelve false si ya existía.</summary>
    public static async Task<bool> CrearAsync(SqlConnection conexion, string email, string nombre, string password, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        var hash = BCrypt.Net.BCrypt.HashPassword(password, 12);
        await using var comando = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM dbo.usuarios WHERE email = @email)
                SELECT CAST(0 AS BIT);
            ELSE
            BEGIN
                INSERT INTO dbo.usuarios (nombre, email, password_hash, rol) VALUES (@nombre, @email, @hash, N'admin');
                INSERT INTO dbo.auditoria (usuario_id, accion, recurso, recurso_id, detalle)
                VALUES (NULL, 'CREAR', 'usuario', CAST(SCOPE_IDENTITY() AS VARCHAR(40)), N'{"origen":"migrador","rol":"admin"}');
                SELECT CAST(1 AS BIT);
            END
            """, conexion);
        comando.Parameters.Add("@email", SqlDbType.NVarChar, 254).Value = email.Trim().ToLowerInvariant();
        comando.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = nombre.Trim();
        comando.Parameters.Add("@hash", SqlDbType.VarChar, 60).Value = hash;
        return (bool)(await comando.ExecuteScalarAsync(ct))!;
    }

    [GeneratedRegex("[A-ZÁÉÍÓÚÑ]")]
    private static partial Regex Mayuscula();

    [GeneratedRegex("[a-záéíóúñ]")]
    private static partial Regex Minuscula();

    [GeneratedRegex(@"\d")]
    private static partial Regex Digito();

    [GeneratedRegex("[^A-Za-z0-9ÁÉÍÓÚÑáéíóúñ]")]
    private static partial Regex Simbolo();
}
