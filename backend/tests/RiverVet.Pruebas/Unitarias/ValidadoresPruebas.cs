using RiverVet.Aplicacion.Autenticacion;
using RiverVet.Aplicacion.Clientes;
using RiverVet.Aplicacion.Mascotas;
using RiverVet.Aplicacion.Usuarios;

namespace RiverVet.Pruebas.Unitarias;

[Trait("Categoria", "Unitaria")]
public class ValidadoresPruebas
{
    private static CrearUsuarioSolicitud Usuario(string password) =>
        new() { Nombre = "Rosa Huamán", Email = "rosa@rivervet.pe", Password = password, Rol = "recepcionista" };

    [Theory]
    [InlineData("Corta#1a", "Mínimo 10 caracteres")]
    [InlineData("sinmayuscula#2026", "Debe incluir una mayúscula")]
    [InlineData("SINMINUSCULA#2026", "Debe incluir una minúscula")]
    [InlineData("SinNumeros#abc", "Debe incluir un número")]
    [InlineData("SinSimbolo2026", "Debe incluir un símbolo")]
    public void Politica_de_contrasenas_rechaza_las_debiles(string password, string mensaje)
    {
        var resultado = new CrearUsuarioValidador().Validate(Usuario(password));
        resultado.Errors.ShouldContain(e => e.PropertyName == "Password" && e.ErrorMessage == mensaje);
    }

    [Fact]
    public void Politica_de_contrasenas_limita_a_72_bytes_por_bcrypt() =>
        new CrearUsuarioValidador().Validate(Usuario("Aa1#" + new string('x', 70)))
            .Errors.ShouldContain(e => e.ErrorMessage == "Máximo 72 caracteres");

    [Fact]
    public void Politica_de_contrasenas_acepta_una_valida() =>
        new CrearUsuarioValidador().Validate(Usuario("RiverVet#2026")).IsValid.ShouldBeTrue();

    [Fact]
    public void El_admin_no_puede_crear_cuentas_de_cliente_desde_usuarios()
    {
        var solicitud = Usuario("RiverVet#2026");
        solicitud.Rol = "cliente";
        new CrearUsuarioValidador().Validate(solicitud).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Login_normaliza_el_email_y_no_aplica_la_politica_de_contrasenas()
    {
        var login = new LoginSolicitud { Email = "  Ana.Torres@Correo.PE ", Password = "vieja" };
        login.Normalizar();
        login.Email.ShouldBe("ana.torres@correo.pe");
        new LoginValidador().Validate(login).IsValid.ShouldBeTrue();
    }

    private static CrearClienteSolicitud Cliente() => new()
    {
        Dni = "70123456",
        Nombres = "María José",
        Apellidos = "O'Higgins-Ñahui",
        Telefono = "987654321",
        ConsentimientoDatos = true,
    };

    [Fact]
    public void Cliente_valido_con_tildes_enie_y_apostrofos() =>
        new CrearClienteValidador().Validate(Cliente()).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("7012345", "987654321", "Dni")]
    [InlineData("7012345a", "987654321", "Dni")]
    [InlineData("70123456", "887654321", "Telefono")]
    [InlineData("70123456", "98765432", "Telefono")]
    public void Formatos_peruanos_DNI_y_celular(string dni, string telefono, string campoConError)
    {
        var solicitud = Cliente();
        solicitud.Dni = dni;
        solicitud.Telefono = telefono;
        new CrearClienteValidador().Validate(solicitud).Errors.ShouldContain(e => e.PropertyName == campoConError);
    }

    [Fact]
    public void Sin_consentimiento_Ley_29733_no_se_registra()
    {
        var solicitud = Cliente();
        solicitud.ConsentimientoDatos = false;
        new CrearClienteValidador().Validate(solicitud).Errors.ShouldContain(e => e.PropertyName == "ConsentimientoDatos");
    }

    [Fact]
    public void Crear_cuenta_exige_email_y_contrasena()
    {
        var solicitud = Cliente();
        solicitud.CrearCuenta = true;
        var errores = new CrearClienteValidador().Validate(solicitud).Errors.Select(e => e.PropertyName).ToList();
        errores.ShouldContain("Email");
        errores.ShouldContain("Password");
    }

    [Fact]
    public void Nombres_con_numeros_o_simbolos_se_rechazan()
    {
        var solicitud = Cliente();
        solicitud.Nombres = "Ana<script>";
        new CrearClienteValidador().Validate(solicitud).Errors.ShouldContain(e => e.PropertyName == "Nombres");
    }

    [Fact]
    public void Normalizar_cliente_quita_HTML_de_la_direccion()
    {
        var solicitud = Cliente();
        solicitud.Direccion = "Calle 1 <script>alert('x')</script>";
        solicitud.Normalizar();
        solicitud.Direccion.ShouldBe("Calle 1 alert('x')");
    }

    [Fact]
    public void Mascota_con_especie_invalida_fecha_futura_y_peso_negativo()
    {
        var solicitud = new CrearMascotaSolicitud
        {
            ClienteId = 1,
            Nombre = "X",
            Especie = "dragón",
            FechaNacimiento = "2999-01-01",
            PesoKg = -1,
        };
        var campos = new CrearMascotaValidador().Validate(solicitud).Errors.Select(e => e.PropertyName).ToList();
        campos.ShouldContain("Especie");
        campos.ShouldContain("FechaNacimiento");
        campos.ShouldContain("PesoKg");
    }

    [Theory]
    [InlineData("2021-13-01")]
    [InlineData("15/03/2019")]
    [InlineData("1970-01-01")]
    public void Mascota_con_fecha_mal_formada(string fecha) =>
        new CrearMascotaValidador()
            .Validate(new CrearMascotaSolicitud { ClienteId = 1, Nombre = "Luna", Especie = "gato", FechaNacimiento = fecha })
            .Errors.ShouldContain(e => e.PropertyName == "FechaNacimiento");

    [Fact]
    public void Mascota_con_mas_de_dos_decimales_en_el_peso() =>
        new CrearMascotaValidador()
            .Validate(new CrearMascotaSolicitud { ClienteId = 1, Nombre = "Luna", Especie = "gato", PesoKg = 2.345m })
            .Errors.ShouldContain(e => e.PropertyName == "PesoKg");

    [Fact]
    public void Actualizar_usuario_exige_el_estado_activo() =>
        new ActualizarUsuarioValidador()
            .Validate(new ActualizarUsuarioSolicitud { Nombre = "Rosa", Rol = "recepcionista" })
            .Errors.ShouldContain(e => e.PropertyName == "Activo");
}
