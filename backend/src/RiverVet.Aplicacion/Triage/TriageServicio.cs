using FluentValidation;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Triage;

/// <summary>Un síntoma del catálogo cerrado y el nivel de urgencia que implica por sí solo.</summary>
public sealed record Sintoma(string Codigo, string Descripcion, string Nivel);

public static class NivelesUrgencia
{
    public const string Alta = "alta";
    public const string Media = "media";
    public const string Baja = "baja";
}

/// <summary>
/// Catálogo CERRADO de síntomas. No se acepta texto libre: un texto no se
/// puede evaluar con reglas simples (y "no sé evaluarlo" obligaría a
/// responder urgencia alta), y además no guardamos ni procesamos
/// descripciones de salud que no necesitamos (minimización, Ley N° 29733).
/// </summary>
public static class CatalogoSintomas
{
    public const string OtroSintoma = "otro";

    public static readonly IReadOnlyList<Sintoma> Todos =
    [
        // ── Signos de alarma: urgencia ALTA por sí solos ──
        new("dificultad_respirar", "Le cuesta respirar, respira con la boca abierta o tiene las encías moradas", NivelesUrgencia.Alta),
        new("convulsiones", "Convulsiones o temblores que no ceden", NivelesUrgencia.Alta),
        new("inconsciente", "Se desmayó, no responde o no puede ponerse de pie", NivelesUrgencia.Alta),
        new("sangrado_abundante", "Sangrado abundante o que no se detiene", NivelesUrgencia.Alta),
        new("trauma", "Atropello, caída de altura o golpe fuerte", NivelesUrgencia.Alta),
        new("envenenamiento", "Pudo comer veneno, medicamentos de personas, chocolate, uvas u otro tóxico", NivelesUrgencia.Alta),
        new("abdomen_hinchado", "Abdomen hinchado y duro, con arcadas sin poder vomitar", NivelesUrgencia.Alta),
        new("no_orina", "Hace fuerza para orinar y no le sale", NivelesUrgencia.Alta),
        new("parto_complicado", "Parto que se prolonga o puja sin que nazcan las crías", NivelesUrgencia.Alta),
        new("golpe_calor", "Jadeo intenso, babeo y mucho calor después de estar al sol", NivelesUrgencia.Alta),
        new("vomito_sangre", "Vómito o diarrea con sangre", NivelesUrgencia.Alta),
        new("dolor_intenso", "Llora, grita o se queja de dolor intenso", NivelesUrgencia.Alta),
        new("ojo_lesionado", "Ojo lesionado, salido o que no puede abrir", NivelesUrgencia.Alta),

        // ── Urgencia MEDIA: consulta en las próximas horas ──
        new("vomitos", "Vómitos (sin sangre)", NivelesUrgencia.Media),
        new("diarrea", "Diarrea (sin sangre)", NivelesUrgencia.Media),
        new("no_come", "No come desde hace más de un día", NivelesUrgencia.Media),
        new("decaimiento", "Está decaído o sin energía", NivelesUrgencia.Media),
        new("cojera", "Cojea o no apoya una pata", NivelesUrgencia.Media),
        new("tos", "Tos que no se le pasa", NivelesUrgencia.Media),
        new("herida_leve", "Herida pequeña o rasguño", NivelesUrgencia.Media),
        new("ojo_rojo", "Ojo rojo o con legañas", NivelesUrgencia.Media),
        new("orina_frecuente", "Orina muy seguido o fuera de su lugar", NivelesUrgencia.Media),
        new("picazon_intensa", "Se rasca hasta hacerse heridas", NivelesUrgencia.Media),

        // ── Urgencia BAJA: consulta de rutina ──
        new("picazon", "Se rasca un poco más de lo normal", NivelesUrgencia.Baja),
        new("caida_pelo", "Se le cae el pelo", NivelesUrgencia.Baja),
        new("estornudos", "Estornuda de vez en cuando", NivelesUrgencia.Baja),
        new("mal_aliento", "Tiene mal aliento", NivelesUrgencia.Baja),

        // Ante la duda, urgencia alta: un síntoma que no está en la lista no
        // se puede evaluar, así que se trata como potencialmente grave.
        new(OtroSintoma, "Otro síntoma que no está en esta lista", NivelesUrgencia.Alta),
    ];

    public static readonly IReadOnlyDictionary<string, Sintoma> PorCodigo = Todos.ToDictionary(s => s.Codigo);
}

public sealed class TriageSolicitud
{
    public string? Especie { get; set; }

    public IReadOnlyList<string>? Sintomas { get; set; }

    /// <summary>Edad aproximada en meses (opcional).</summary>
    public int? EdadMeses { get; set; }

    /// <summary>Hace cuántas horas empezaron los síntomas (opcional).</summary>
    public int? HorasConSintomas { get; set; }
}

public sealed class TriageValidador : AbstractValidator<TriageSolicitud>
{
    public TriageValidador()
    {
        RuleFor(x => x.Especie).Must(e => e is not null && Catalogos.Especies.Contains(e))
            .WithMessage("Especie inválida: " + string.Join(", ", Catalogos.Especies));
        RuleFor(x => x.Sintomas)
            .NotEmpty().WithMessage("Marca al menos un síntoma")
            .Must(s => s is null || s.Count <= 15).WithMessage("Máximo 15 síntomas")
            .Must(s => s is null || s.Distinct().Count() == s.Count).WithMessage("Hay síntomas repetidos")
            .Must(s => s is null || s.All(CatalogoSintomas.PorCodigo.ContainsKey)).WithMessage("Hay síntomas que no están en el catálogo");
        RuleFor(x => x.EdadMeses).InclusiveBetween(0, 600).When(x => x.EdadMeses is not null).WithMessage("Edad inválida");
        RuleFor(x => x.HorasConSintomas).InclusiveBetween(0, 8760).When(x => x.HorasConSintomas is not null).WithMessage("Valor inválido");
    }
}

public sealed record TriageRespuesta(
    string Urgencia,
    string Recomendacion,
    IReadOnlyList<string> Motivos,
    string Descargo);

/// <summary>
/// HU-021: triage ORIENTATIVO. Reglas simples y CONSERVADORAS: ante la duda,
/// urgencia alta (mandar a consulta a una mascota sana cuesta poco; hacer
/// esperar a una grave puede costarle la vida).
///
/// Nada de lo consultado se guarda (no hay tabla de triage a propósito).
/// </summary>
public static class TriageServicio
{
    /// <summary>Descargo OBLIGATORIO: se devuelve en TODA respuesta.</summary>
    public const string Descargo =
        "Esta orientación es automática y referencial. NO es un diagnóstico veterinario y NO reemplaza la " +
        "consulta con un médico veterinario. Si tu mascota empeora o tienes cualquier duda, acude de inmediato " +
        "a una clínica veterinaria.";

    // Animales pequeños o exóticos se descompensan muy rápido.
    private static readonly string[] _especiesDelicadas = ["ave", "conejo", "roedor", "reptil"];

    public static TriageRespuesta Evaluar(TriageSolicitud solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var sintomas = (solicitud.Sintomas ?? []).Select(c => CatalogoSintomas.PorCodigo[c]).ToList();
        var altas = sintomas.Where(s => s.Nivel == NivelesUrgencia.Alta).ToList();
        var medias = sintomas.Where(s => s.Nivel == NivelesUrgencia.Media).ToList();
        var motivos = new List<string>();

        // 1) Cualquier signo de alarma.
        if (altas.Count > 0)
        {
            motivos.AddRange(altas.Select(s => s.Codigo == CatalogoSintomas.OtroSintoma
                ? "Indicaste un síntoma que no podemos evaluar: por precaución se considera urgente"
                : $"Signo de alarma: {s.Descripcion}"));
            return Respuesta(NivelesUrgencia.Alta, motivos);
        }

        var factoresRiesgo = FactoresDeRiesgo(solicitud);
        // 2) Varios síntomas moderados juntos, o uno moderado con un factor de riesgo.
        if (medias.Count >= 2)
        {
            motivos.Add("Tiene varios síntomas moderados a la vez");
            return Respuesta(NivelesUrgencia.Alta, motivos);
        }
        if (medias.Count == 1 && factoresRiesgo.Count > 0)
        {
            motivos.Add($"Síntoma moderado: {medias[0].Descripcion}");
            motivos.AddRange(factoresRiesgo);
            return Respuesta(NivelesUrgencia.Alta, motivos);
        }
        if (medias.Count == 1)
        {
            motivos.Add($"Síntoma moderado: {medias[0].Descripcion}");
            return Respuesta(NivelesUrgencia.Media, motivos);
        }

        // 3) Solo síntomas leves: suben a media si son varios o llevan mucho tiempo.
        if (sintomas.Count >= 3 || solicitud.HorasConSintomas > 7 * 24 || factoresRiesgo.Count > 0)
        {
            motivos.Add("Síntomas leves, pero varios, prolongados o en una mascota más vulnerable");
            motivos.AddRange(factoresRiesgo);
            return Respuesta(NivelesUrgencia.Media, motivos);
        }
        motivos.Add("Síntomas leves");
        return Respuesta(NivelesUrgencia.Baja, motivos);
    }

    private static List<string> FactoresDeRiesgo(TriageSolicitud solicitud)
    {
        var factores = new List<string>();
        if (solicitud.EdadMeses < 6)
        {
            factores.Add("Es muy joven (menos de 6 meses)");
        }
        if (solicitud.EdadMeses >= 120)
        {
            factores.Add("Es de edad avanzada (10 años o más)");
        }
        if (solicitud.Especie is not null && _especiesDelicadas.Contains(solicitud.Especie))
        {
            factores.Add("Las aves, conejos, roedores y reptiles se descompensan muy rápido");
        }
        if (solicitud.HorasConSintomas > 48)
        {
            factores.Add("Los síntomas llevan más de 2 días");
        }
        return factores;
    }

    private static TriageRespuesta Respuesta(string urgencia, IReadOnlyList<string> motivos) => new(
        urgencia,
        urgencia switch
        {
            NivelesUrgencia.Alta => "Acude de inmediato a una clínica veterinaria o llama a la clínica de emergencia más cercana.",
            NivelesUrgencia.Media => "Agenda una consulta veterinaria para hoy o mañana. Si aparece un signo de alarma, trátalo como urgencia.",
            _ => "Agenda una consulta de rutina y observa a tu mascota. Si empeora, consulta antes.",
        },
        motivos,
        Descargo);
}
