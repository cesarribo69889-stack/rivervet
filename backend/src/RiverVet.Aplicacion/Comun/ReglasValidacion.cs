using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;

namespace RiverVet.Aplicacion.Comun;

/// <summary>
/// Reglas de validación reutilizables. El frontend replica estas mismas reglas
/// (frontend/src/utils/validaciones.js) para avisar antes de enviar, pero la
/// validación que CUENTA es esta: el cliente es manipulable.
/// </summary>
public static partial class ReglasValidacion
{
    /// <summary>DNI peruano: 8 dígitos.</summary>
    public static IRuleBuilderOptions<T, string?> Dni<T>(this IRuleBuilder<T, string?> regla) =>
        regla.NotEmpty().WithMessage("El DNI es obligatorio")
            .Matches(RegexDni()).WithMessage("El DNI debe tener 8 dígitos");

    /// <summary>Celular peruano: 9 dígitos, empieza con 9.</summary>
    public static IRuleBuilderOptions<T, string?> Celular<T>(this IRuleBuilder<T, string?> regla) =>
        regla.NotEmpty().WithMessage("El celular es obligatorio")
            .Matches(RegexCelular()).WithMessage("El celular debe tener 9 dígitos y empezar con 9");

    public static IRuleBuilderOptions<T, string?> EmailValido<T>(this IRuleBuilder<T, string?> regla) =>
        regla.MaximumLength(254).WithMessage("Máximo 254 caracteres")
            .Matches(RegexEmail()).WithMessage("Email inválido");

    /// <summary>Nombres de personas o mascotas: letras (con tildes y ñ), espacios, punto, apóstrofo y guion.</summary>
    public static IRuleBuilderOptions<T, string?> NombrePropio<T>(this IRuleBuilder<T, string?> regla, int maximo) =>
        regla.NotEmpty().WithMessage("Es obligatorio")
            .MaximumLength(maximo).WithMessage($"Máximo {maximo} caracteres")
            .Matches(RegexNombre()).WithMessage("Solo letras, espacios, apóstrofos y guiones");

    /// <summary>
    /// Política de contraseñas: 10+ caracteres con mayúscula, minúscula,
    /// número y símbolo. Máximo 72 BYTES porque bcrypt ignora lo que pase de
    /// 72 bytes: sin este límite, dos contraseñas largas con el mismo inicio
    /// serían "iguales".
    /// </summary>
    public static IRuleBuilderOptions<T, string?> PasswordSegura<T>(this IRuleBuilder<T, string?> regla) =>
        regla.NotEmpty().WithMessage("La contraseña es obligatoria")
            .MinimumLength(10).WithMessage("Mínimo 10 caracteres")
            .Must(p => p is null || Encoding.UTF8.GetByteCount(p) <= 72).WithMessage("Máximo 72 caracteres")
            .Matches("[A-ZÁÉÍÓÚÑ]").WithMessage("Debe incluir una mayúscula")
            .Matches("[a-záéíóúñ]").WithMessage("Debe incluir una minúscula")
            .Matches(@"\d").WithMessage("Debe incluir un número")
            .Matches("[^A-Za-z0-9ÁÉÍÓÚÑáéíóúñ]").WithMessage("Debe incluir un símbolo");

    /// <summary>Id de un recurso (clave primaria INT).</summary>
    public static IRuleBuilderOptions<T, int?> IdValido<T>(this IRuleBuilder<T, int?> regla) =>
        regla.NotNull().WithMessage("Es obligatorio").GreaterThan(0).WithMessage("Id inválido");

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex RegexDni();

    [GeneratedRegex(@"^9\d{8}$")]
    private static partial Regex RegexCelular();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$")]
    private static partial Regex RegexEmail();

    [GeneratedRegex(@"^[\p{L}\p{M}][\p{L}\p{M}' .-]*$")]
    private static partial Regex RegexNombre();
}

/// <summary>Validación estándar de los parámetros de paginación.</summary>
public sealed class PaginacionValidador : AbstractValidator<PaginacionConsulta>
{
    public PaginacionValidador()
    {
        RuleFor(x => x.Pagina).GreaterThanOrEqualTo(1).WithMessage("Debe ser 1 o mayor");
        RuleFor(x => x.Limite).InclusiveBetween(1, 100).WithMessage("Debe estar entre 1 y 100");
    }
}

public sealed class BusquedaValidador : AbstractValidator<BusquedaSolicitud>
{
    public BusquedaValidador()
    {
        Include(new PaginacionValidador());
        RuleFor(x => x.Texto).MaximumLength(100).WithMessage("Máximo 100 caracteres");
    }
}
