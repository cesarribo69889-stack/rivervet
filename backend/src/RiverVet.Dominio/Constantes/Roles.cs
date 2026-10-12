namespace RiverVet.Dominio.Constantes;

/// <summary>
/// Los cuatro roles del sistema. Los grupos (Personal, Todos) son cadenas
/// separadas por comas porque así los recibe el atributo [Authorize(Roles = ...)].
/// </summary>
public static class Roles
{
    public const string Admin = "admin";
    public const string Veterinario = "veterinario";
    public const string Recepcionista = "recepcionista";
    public const string Cliente = "cliente";

    public const string AdminORecepcion = Admin + "," + Recepcionista;
    public const string Personal = Admin + "," + Veterinario + "," + Recepcionista;
    public const string Todos = Personal + "," + Cliente;

    /// <summary>Registran atenciones y vacunas: el veterinario y el admin (dueño de la clínica).</summary>
    public const string Clinico = Admin + "," + Veterinario;

    /// <summary>Ventas: recepción y admin gestionan; el cliente ve y opera lo suyo.</summary>
    public const string VentasYCliente = AdminORecepcion + "," + Cliente;

    /// <summary>Roles que el admin puede asignar desde el módulo de usuarios.</summary>
    public static readonly IReadOnlyList<string> DePersonal = [Admin, Veterinario, Recepcionista];

    public static readonly IReadOnlyList<string> Validos = [Admin, Veterinario, Recepcionista, Cliente];
}
