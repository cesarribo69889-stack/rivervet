using System.Net;
using Microsoft.Data.SqlClient;
using RiverVet.Migrador;

namespace RiverVet.Pruebas.Integracion;

/// <summary>Comando crear-admin del migrador (primer administrador en producción).</summary>
[Collection(FabricaApi.Coleccion)]
[Trait("Categoria", "Integracion")]
public class PrimerAdministradorPruebas(FabricaApi fabrica)
{
    [Theory]
    [InlineData("corta#1A")]
    [InlineData("sinmayuscula#2026")]
    [InlineData("SinSimbolo2026")]
    public void Rechaza_contrasenas_debiles(string password) =>
        PrimerAdministrador.ValidarPassword(password).ShouldNotBeEmpty();

    [Fact]
    public async Task Crea_el_administrador_una_sola_vez_y_puede_iniciar_sesion()
    {
        var email = ClienteApi.EmailUnico("primer.admin");
        const string Password = "Clinica#Segura2026";
        PrimerAdministrador.ValidarPassword(Password).ShouldBeEmpty();

        await using var conexion = new SqlConnection(fabrica.CadenaConexion);
        await conexion.OpenAsync();
        (await PrimerAdministrador.CrearAsync(conexion, email, "Primer Admin", Password)).ShouldBeTrue();
        (await PrimerAdministrador.CrearAsync(conexion, email, "Otro", Password)).ShouldBeFalse();

        var api = fabrica.CrearClienteApi();
        var sesion = await api.IniciarSesionAsync(email, Password);
        sesion.Usuario.GetProperty("rol").GetString().ShouldBe("admin");
        (await api.GetAsync("/api/usuarios", sesion.Token)).Estado.ShouldBe(HttpStatusCode.OK);
    }
}
