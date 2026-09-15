using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RiverVet.Api.Configuracion;
using RiverVet.Aplicacion.Comprobantes;
using RiverVet.Aplicacion.Pagos;
using RiverVet.Aplicacion.Triage;
using RiverVet.Infraestructura.Externos;

namespace RiverVet.Pruebas.Unitarias;

[Trait("Categoria", "Unitaria")]
public class FirmaWebhookPruebas
{
    private const string Secreto = "secreto-compartido-con-la-pasarela-123456"; // gitleaks:allow (solo pruebas)
    private static readonly byte[] _cuerpo = Encoding.UTF8.GetBytes("""{"id":"evt_1","tipo":"pago.aprobado","monto":59.70}""");
    private static readonly DateTimeOffset _ahora = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static string Tiempo(DateTimeOffset t) => t.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Acepta_una_firma_valida_y_reciente()
    {
        var firma = FirmaWebhook.Calcular(Secreto, _ahora.ToUnixTimeSeconds(), _cuerpo);
        FirmaWebhook.Verificar(Secreto, Tiempo(_ahora), firma, _cuerpo, _ahora.AddSeconds(30), 300).ShouldBeTrue();
    }

    [Fact]
    public void Rechaza_si_cambia_un_solo_byte_del_cuerpo()
    {
        var firma = FirmaWebhook.Calcular(Secreto, _ahora.ToUnixTimeSeconds(), _cuerpo);
        var alterado = Encoding.UTF8.GetBytes("""{"id":"evt_1","tipo":"pago.aprobado","monto":59.71}""");
        FirmaWebhook.Verificar(Secreto, Tiempo(_ahora), firma, alterado, _ahora, 300).ShouldBeFalse();
    }

    [Fact]
    public void Rechaza_otro_secreto_un_tiempo_viejo_o_cambiado_y_firmas_mal_formadas()
    {
        var firma = FirmaWebhook.Calcular(Secreto, _ahora.ToUnixTimeSeconds(), _cuerpo);
        FirmaWebhook.Verificar("otro-secreto-cualquiera-000000000000000", Tiempo(_ahora), firma, _cuerpo, _ahora, 300).ShouldBeFalse();
        // Reenvío 10 minutos después (repetición).
        FirmaWebhook.Verificar(Secreto, Tiempo(_ahora), firma, _cuerpo, _ahora.AddMinutes(10), 300).ShouldBeFalse();
        // El tiempo va dentro de lo firmado: cambiarlo invalida la firma.
        FirmaWebhook.Verificar(Secreto, Tiempo(_ahora.AddSeconds(1)), firma, _cuerpo, _ahora, 300).ShouldBeFalse();
        FirmaWebhook.Verificar(Secreto, Tiempo(_ahora), "no-es-hex", _cuerpo, _ahora, 300).ShouldBeFalse();
        FirmaWebhook.Verificar(Secreto, null, firma, _cuerpo, _ahora, 300).ShouldBeFalse();
        FirmaWebhook.Verificar(Secreto, Tiempo(_ahora), null, _cuerpo, _ahora, 300).ShouldBeFalse();
    }
}

[Trait("Categoria", "Unitaria")]
public class TriagePruebas
{
    private static TriageRespuesta Evaluar(string especie, int? edadMeses = 36, int? horas = null, params string[] sintomas) =>
        TriageServicio.Evaluar(new TriageSolicitud { Especie = especie, Sintomas = sintomas, EdadMeses = edadMeses, HorasConSintomas = horas });

    [Fact]
    public void Un_signo_de_alarma_es_urgencia_alta_aunque_haya_otros_leves() =>
        Evaluar("perro", 36, null, "mal_aliento", "dificultad_respirar").Urgencia.ShouldBe(NivelesUrgencia.Alta);

    [Fact]
    public void Ante_la_duda_urgencia_alta_un_sintoma_no_catalogado() =>
        Evaluar("gato", 36, null, CatalogoSintomas.OtroSintoma).Urgencia.ShouldBe(NivelesUrgencia.Alta);

    [Fact]
    public void Dos_sintomas_moderados_juntos_son_urgencia_alta() =>
        Evaluar("perro", 36, null, "vomitos", "decaimiento").Urgencia.ShouldBe(NivelesUrgencia.Alta);

    [Theory]
    [InlineData("perro", 3, null)] // cachorro
    [InlineData("perro", 150, null)] // mayor
    [InlineData("conejo", 24, null)] // especie delicada
    [InlineData("perro", 36, 72)] // más de 2 días
    public void Un_sintoma_moderado_con_un_factor_de_riesgo_es_urgencia_alta(string especie, int edad, int? horas) =>
        Evaluar(especie, edad, horas, "diarrea").Urgencia.ShouldBe(NivelesUrgencia.Alta);

    [Fact]
    public void Un_sintoma_moderado_en_un_adulto_sano_es_urgencia_media() =>
        Evaluar("perro", 36, 12, "tos").Urgencia.ShouldBe(NivelesUrgencia.Media);

    [Fact]
    public void Sintomas_leves_son_urgencia_baja_salvo_si_son_varios_o_llevan_mucho_tiempo()
    {
        Evaluar("gato", 36, 24, "estornudos").Urgencia.ShouldBe(NivelesUrgencia.Baja);
        Evaluar("gato", 36, 24, "estornudos", "picazon", "caida_pelo").Urgencia.ShouldBe(NivelesUrgencia.Media);
        Evaluar("gato", 36, 24 * 10, "estornudos").Urgencia.ShouldBe(NivelesUrgencia.Media);
    }

    [Fact]
    public void Toda_respuesta_trae_el_descargo_y_motivos()
    {
        foreach (var codigo in CatalogoSintomas.Todos.Select(s => s.Codigo))
        {
            var r = Evaluar("perro", 36, null, codigo);
            r.Descargo.ShouldBe(TriageServicio.Descargo);
            r.Descargo.ShouldContain("NO es un diagnóstico veterinario");
            r.Motivos.ShouldNotBeEmpty();
            r.Recomendacion.ShouldNotBeNullOrWhiteSpace();
        }
    }
}

[Trait("Categoria", "Unitaria")]
public class ConfiguracionSprint4Pruebas
{
    private static IReadOnlyList<string> Validar(Dictionary<string, string?> extra, bool produccion)
    {
        var valores = new Dictionary<string, string?>
        {
            ["ConnectionStrings:RiverVet"] = produccion
                ? "Server=tcp:x.database.windows.net;Database=rivervet_db;User Id=rivervet_app;Password=x;Encrypt=True;TrustServerCertificate=False"
                : "Server=.;Database=rivervet_db;User Id=rivervet_app;Password=x;TrustServerCertificate=True",
            ["Jwt:Secreto"] = new string('a', 64),
            ["Seguridad:OrigenesCors:0"] = produccion ? "https://rivervet.vercel.app" : "http://localhost:5173",
        };
        foreach (var (clave, valor) in extra)
        {
            valores[clave] = valor;
        }
        return ValidadorConfiguracion.Validar(new ConfigurationBuilder().AddInMemoryCollection(valores).Build(), produccion);
    }

    [Fact]
    public void La_pasarela_simulada_no_se_permite_en_produccion() =>
        Validar(new() { ["Pagos:Pasarela:Proveedor"] = "simulada", ["Pagos:Pasarela:SecretoWebhook"] = new string('s', 40) }, produccion: true)
            .ShouldContain(p => p.Contains("simulada", StringComparison.Ordinal));

    [Fact]
    public void Con_pasarela_el_secreto_del_webhook_es_obligatorio_y_largo()
    {
        Validar(new() { ["Pagos:Pasarela:Proveedor"] = "simulada", ["Pagos:Pasarela:SecretoWebhook"] = "corto" }, produccion: false)
            .ShouldContain(p => p.StartsWith("Pagos:Pasarela:SecretoWebhook", StringComparison.Ordinal));
        Validar(new() { ["Pagos:Pasarela:Proveedor"] = "simulada", ["Pagos:Pasarela:SecretoWebhook"] = new string('s', 40) }, produccion: false)
            .ShouldBeEmpty();
        Validar(new() { ["Pagos:Pasarela:Proveedor"] = "simulada", ["Pagos:Pasarela:SecretoWebhook"] = new string('s', 40), ["Pagos:Pasarela:UrlCheckout"] = "javascript:alert(1)" }, produccion: false)
            .ShouldContain(p => p.StartsWith("Pagos:Pasarela:UrlCheckout", StringComparison.Ordinal));
        Validar(new() { ["Pagos:Pasarela:Proveedor"] = "inventada", ["Pagos:Pasarela:SecretoWebhook"] = new string('s', 40) }, produccion: false)
            .ShouldContain(p => p.Contains("no implementado", StringComparison.Ordinal));
    }

    [Fact]
    public void Yape_y_WhatsApp_validan_sus_datos()
    {
        Validar(new() { ["Pagos:Yape:Celular"] = "12345" }, false).ShouldContain(p => p.StartsWith("Pagos:Yape:Celular", StringComparison.Ordinal));
        Validar(new() { ["WhatsApp:Modo"] = "sms" }, false).ShouldContain(p => p.StartsWith("WhatsApp:Modo", StringComparison.Ordinal));
        var cloud = Validar(new() { ["WhatsApp:Modo"] = "cloudapi", ["WhatsApp:VersionApi"] = "v21.0" }, false);
        cloud.ShouldContain(p => p.StartsWith("WhatsApp:Token", StringComparison.Ordinal));
        cloud.ShouldContain(p => p.StartsWith("WhatsApp:NumeroId", StringComparison.Ordinal));
    }
}

[Trait("Categoria", "Unitaria")]
public class WhatsAppPruebas
{
    private static readonly MensajeComprobante _mensaje = new("RV-000123", 59.7m, "13/09/2026");

    /// <summary>Servidor HTTP falso: guarda la petición y responde el código indicado. No sale a internet.</summary>
    private sealed class ServidorFalso(HttpStatusCode codigo) : HttpMessageHandler
    {
        public HttpRequestMessage? Peticion { get; private set; }

        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Peticion = request;
            Cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(codigo);
        }
    }

    private static (WhatsAppCloudApi Cliente, ServidorFalso Servidor) Crear(HttpStatusCode codigo)
    {
        var servidor = new ServidorFalso(codigo);
        var opciones = Options.Create(new OpcionesWhatsApp
        {
            Modo = OpcionesWhatsApp.ModoCloudApi,
            VersionApi = "v21.0",
            NumeroId = "1234567890",
            Token = "token-de-prueba",
            Plantilla = "comprobante_pedido",
        });
        return (new WhatsAppCloudApi(new HttpClient(servidor), opciones, NullLogger<WhatsAppCloudApi>.Instance), servidor);
    }

    [Fact]
    public async Task Cloud_API_envia_la_plantilla_con_solo_codigo_total_y_fecha()
    {
        var (cliente, servidor) = Crear(HttpStatusCode.OK);

        var r = await cliente.EnviarComprobanteAsync("987654321", _mensaje);

        r.Enviado.ShouldBeTrue();
        servidor.Peticion!.RequestUri!.ToString().ShouldBe("https://graph.facebook.com/v21.0/1234567890/messages");
        servidor.Peticion.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        var json = JsonDocument.Parse(servidor.Cuerpo!).RootElement;
        json.GetProperty("to").GetString().ShouldBe("51987654321");
        json.GetProperty("template").GetProperty("name").GetString().ShouldBe("comprobante_pedido");
        var parametros = json.GetProperty("template").GetProperty("components")[0].GetProperty("parameters")
            .EnumerateArray().Select(p => p.GetProperty("text").GetString()).ToList();
        parametros.ShouldBe(["RV-000123", "59.70", "13/09/2026"]);
    }

    [Fact]
    public async Task Cloud_API_con_error_devuelve_fallido_sin_lanzar_excepcion()
    {
        var (cliente, _) = Crear(HttpStatusCode.BadRequest);
        var r = await cliente.EnviarComprobanteAsync("987654321", _mensaje);
        r.Enviado.ShouldBeFalse();
        r.Error.ShouldBe("WhatsApp respondió 400");
    }

    [Fact]
    public async Task Modo_enlace_arma_un_enlace_wa_me_con_el_texto_codificado()
    {
        var r = await new WhatsAppEnlace().EnviarComprobanteAsync("987654321", _mensaje);
        r.Enlace!.AbsoluteUri.ShouldStartWith("https://wa.me/51987654321?text=RiverVet");
        Uri.UnescapeDataString(r.Enlace.Query).ShouldContain("S/ 59.70");
    }

    [Fact]
    public void El_telefono_se_enmascara_en_la_auditoria() =>
        ComprobantesServicio.Enmascarar("987654321").ShouldBe("98*****21");
}

[Trait("Categoria", "Unitaria")]
public class PasarelaSimuladaPruebas
{
    private static PasarelaSimulada Crear(string? url) =>
        new(Options.Create(new OpcionesPagos { Pasarela = new OpcionesPasarela { Proveedor = "simulada", UrlCheckout = url } }));

    /// <summary>Regresión: appsettings.json trae "UrlCheckout": "" (texto vacío, no null) y eso armaba una URL inválida.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sin_url_configurada_usa_la_url_por_defecto(string? url)
    {
        var sesion = await Crear(url).CrearCheckoutAsync("RV-000001", 85.8m);
        sesion.UrlCheckout.AbsoluteUri.ShouldStartWith(PasarelaSimulada.UrlPorDefecto + "?referencia=sim_");
        sesion.UrlCheckout.Query.ShouldContain("monto=85.80");
        sesion.Referencia.ShouldMatch("^sim_[0-9a-f]{24}$");
    }

    [Fact]
    public async Task Usa_la_url_configurada_y_cada_sesion_tiene_otra_referencia()
    {
        var pasarela = Crear("https://pagos.ejemplo.pe/checkout");
        var a = await pasarela.CrearCheckoutAsync("RV-000001", 10m);
        var b = await pasarela.CrearCheckoutAsync("RV-000001", 10m);
        a.UrlCheckout.AbsoluteUri.ShouldStartWith("https://pagos.ejemplo.pe/checkout?");
        a.Referencia.ShouldNotBe(b.Referencia);
    }
}
