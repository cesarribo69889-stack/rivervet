using System.Text.Json.Nodes;
using RiverVet.Aplicacion.Comun;
using RiverVet.Infraestructura.Seguridad;

namespace RiverVet.Pruebas.Unitarias;

[Trait("Categoria", "Unitaria")]
public class TextoPruebas
{
    [Theory]
    [InlineData("<script>alert(1)</script>Hola", "alert(1)Hola")]
    [InlineData("<img src=x onerror=alert(1)>", "")]
    [InlineData("Mancha <b onmouseover=alert(1)>blanca</b>", "Mancha blanca")]
    [InlineData("temperatura < 39 y > 37", "temperatura < 39 y > 37")]
    [InlineData("ver javascript:alert(1)", "ver alert(1)")]
    public void NeutralizarHtml_quita_etiquetas_y_conserva_el_texto(string entrada, string esperado) =>
        Texto.NeutralizarHtml(entrada).ShouldBe(esperado);

    [Fact]
    public void QuitarControl_elimina_caracteres_de_control_pero_conserva_saltos_de_linea() =>
        Texto.QuitarControl("linea1\nlinea2\0").ShouldBe("linea1\nlinea2");

    [Fact]
    public void Libre_limpia_recorta_y_convierte_vacio_en_null()
    {
        Texto.Libre("  <b>hola</b>  ").ShouldBe("hola");
        Texto.Libre("   ").ShouldBeNull();
        Texto.Libre(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("%", @"%\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData("[x]", @"%\[x]%")]
    [InlineData("Torres", "%Torres%")]
    public void PatronLike_escapa_los_comodines(string texto, string esperado) =>
        Texto.PatronLike(texto).ShouldBe(esperado);

    [Fact]
    public void EnmascararEmail_oculta_parte_del_usuario()
    {
        Texto.EnmascararEmail("juan.perez@gmail.com").ShouldBe("ju***@gmail.com");
        Texto.EnmascararEmail("no-es-email").ShouldBe("[EMAIL INVALIDO]");
    }
}

[Trait("Categoria", "Unitaria")]
public class RedactorDatosPruebas
{
    [Fact]
    public void Redacta_contrasenas_tokens_y_cookies_en_cualquier_nivel()
    {
        var json = RedactorDatos.SerializarRedactado(new
        {
            email = "ana@correo.pe",
            password = "Secreta#2026",
            datos = new { accessToken = "abc", refreshToken = "def", otros = new[] { new { contrasena = "x" } } },
            headers = new { authorization = "Bearer xyz", cookie = "rv_refresh=123" },
        })!;
        var nodo = JsonNode.Parse(json)!;
        nodo["email"]!.GetValue<string>().ShouldBe("ana@correo.pe");
        nodo["password"]!.GetValue<string>().ShouldBe(RedactorDatos.Redactado);
        nodo["datos"]!["accessToken"]!.GetValue<string>().ShouldBe(RedactorDatos.Redactado);
        nodo["datos"]!["refreshToken"]!.GetValue<string>().ShouldBe(RedactorDatos.Redactado);
        nodo["datos"]!["otros"]![0]!["contrasena"]!.GetValue<string>().ShouldBe(RedactorDatos.Redactado);
        nodo["headers"]!["authorization"]!.GetValue<string>().ShouldBe(RedactorDatos.Redactado);
        nodo["headers"]!["cookie"]!.GetValue<string>().ShouldBe(RedactorDatos.Redactado);
    }

    [Fact]
    public void Redacta_JWT_Bearer_y_numeros_de_tarjeta_dentro_de_textos()
    {
        RedactorDatos.RedactarTexto("token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.firmaFirmaFirma").ShouldBe("token=[JWT REDACTADO]");
        RedactorDatos.RedactarTexto("Authorization: Bearer abc.def").ShouldBe("Authorization: Bearer [REDACTADO]");
        RedactorDatos.RedactarTexto("tarjeta 4111 1111 1111 1111 ok").ShouldBe("tarjeta [NUMERO REDACTADO] ok");
    }

    [Fact]
    public void Conserva_DNI_y_celular() =>
        RedactorDatos.RedactarTexto("DNI 70123456 cel 987654321").ShouldBe("DNI 70123456 cel 987654321");

    [Fact]
    public void Trunca_detalles_demasiado_largos() =>
        RedactorDatos.SerializarRedactado(new { texto = new string('x', 3000) }).ShouldBe("""{"truncado":true}""");
}

[Trait("Categoria", "Unitaria")]
public class ClaveIpPruebas
{
    [Theory]
    [InlineData("190.12.34.56", "190.12.34.56")]
    [InlineData("::ffff:190.12.34.56", "190.12.34.56")]
    [InlineData(null, "desconocida")]
    [InlineData("no-es-ip", "desconocida")]
    public void Normaliza_IPv4(string? ip, string esperado) => ClaveIp.Normalizar(ip).ShouldBe(esperado);

    [Fact]
    public void Agrupa_IPv6_por_red_56() =>
        ClaveIp.Normalizar("2001:db8:abcd:12ff:1:2:3:4").ShouldBe(ClaveIp.Normalizar("2001:db8:abcd:1299:9:9:9:9"));
}
