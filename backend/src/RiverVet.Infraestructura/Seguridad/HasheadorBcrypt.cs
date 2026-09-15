using RiverVet.Aplicacion.Abstracciones;

namespace RiverVet.Infraestructura.Seguridad;

/// <summary>
/// Contraseñas con bcrypt, costo 12 (2^12 rondas). Tarda ~250 ms por intento:
/// imperceptible para una persona, pero convierte un ataque de diccionario de
/// horas en años. La sal es aleatoria y va dentro del propio hash.
/// </summary>
public sealed class HasheadorBcrypt : IHasheadorPassword
{
    public const int Costo = 12;

    public string Hashear(string password) => BCrypt.Net.BCrypt.HashPassword(password, Costo);

    public bool Verificar(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Hash corrupto o con formato inesperado: se trata como "no coincide".
            return false;
        }
    }
}
