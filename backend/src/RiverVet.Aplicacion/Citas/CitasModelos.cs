using FluentValidation;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Citas;

/// <summary>Cita tal como la devuelve la API.</summary>
public sealed class CitaRespuesta
{
    public int Id { get; init; }

    public int MascotaId { get; init; }

    public string MascotaNombre { get; init; } = string.Empty;

    public string MascotaEspecie { get; init; } = string.Empty;

    public int ClienteId { get; init; }

    public string ClienteNombre { get; init; } = string.Empty;

    public string ClienteTelefono { get; init; } = string.Empty;

    public int VeterinarioId { get; init; }

    public string VeterinarioNombre { get; init; } = string.Empty;

    /// <summary>Inicio del bloque en UTC (se guarda así en la base).</summary>
    public DateTime FechaHora { get; init; }

    /// <summary>La misma hora, en hora de Lima: "2026-09-20T10:30".</summary>
    public string FechaHoraLima => HoraLima.Formatear(HoraLima.DeUtc(FechaHora));

    public string Motivo { get; init; } = string.Empty;

    public string Estado { get; init; } = string.Empty;

    public string? MotivoCancelacion { get; init; }

    public DateTime CreadoEn { get; init; }
}

/// <summary>HU-008: agendar cita. fechaHora en hora de Lima: "AAAA-MM-DDTHH:mm".</summary>
public sealed class AgendarCitaSolicitud : INormalizable
{
    public int? MascotaId { get; set; }

    public int? VeterinarioId { get; set; }

    public string? FechaHora { get; set; }

    public string? Motivo { get; set; }

    public void Normalizar()
    {
        FechaHora = Texto.Recortar(FechaHora);
        Motivo = Texto.Libre(Motivo);
    }
}

/// <summary>HU-009: reprogramar (cambiar horario, veterinario o motivo). La mascota no cambia.</summary>
public sealed class ReprogramarCitaSolicitud : INormalizable
{
    public int? VeterinarioId { get; set; }

    public string? FechaHora { get; set; }

    public string? Motivo { get; set; }

    public void Normalizar()
    {
        FechaHora = Texto.Recortar(FechaHora);
        Motivo = Texto.Libre(Motivo);
    }
}

/// <summary>HU-009: cancelar.</summary>
public sealed class CancelarCitaSolicitud : INormalizable
{
    public string? Motivo { get; set; }

    public void Normalizar() => Motivo = Texto.Libre(Motivo);
}

/// <summary>HU-010: calendario por rango de fechas (de Lima), filtrable por veterinario.</summary>
public sealed class ConsultaCitas : INormalizable
{
    public const int DiasMaximos = 93;

    public string? Desde { get; set; }

    public string? Hasta { get; set; }

    public int? VeterinarioId { get; set; }

    /// <summary>Citas de una mascota (también por ruta: GET /api/citas/mascota/{id}).</summary>
    public int? MascotaId { get; set; }

    public string? Estado { get; set; }

    public void Normalizar()
    {
        Desde = Texto.Recortar(Desde);
        Hasta = Texto.Recortar(Hasta);
        Estado = Texto.Recortar(Estado);
    }
}

public sealed class ConsultaDisponibilidad
{
    public int? VeterinarioId { get; set; }

    public string? Fecha { get; set; }
}

public sealed record HorarioDisponible(string Hora, string FechaHoraLima, bool Libre);

public sealed record DisponibilidadRespuesta(string Fecha, bool Atiende, IReadOnlyList<HorarioDisponible> Horarios);

/// <summary>Reglas del horario de una cita, compartidas por agendar y reprogramar.</summary>
public static class ReglasHorario
{
    /// <summary>Devuelve el error del horario, o null si es válido.</summary>
    public static string? Validar(string? texto, DateTime ahoraLima)
    {
        if (HoraLima.LeerFechaHora(texto) is not DateTime fecha)
        {
            return "Fecha y hora inválidas (formato AAAA-MM-DDTHH:mm, hora de Lima)";
        }
        if (fecha.Minute % HorarioAtencion.MinutosBloque != 0)
        {
            return "Las citas empiezan a la hora en punto o a la media hora (:00 o :30)";
        }
        var hora = TimeOnly.FromDateTime(fecha);
        if (!HorarioAtencion.AtiendeEl(fecha.DayOfWeek) || hora < HorarioAtencion.Apertura
            || hora.AddMinutes(HorarioAtencion.MinutosBloque) > HorarioAtencion.Cierre)
        {
            return $"Fuera del horario de atención ({HorarioAtencion.Descripcion})";
        }
        if (fecha <= ahoraLima)
        {
            return "No se puede agendar una cita en el pasado";
        }
        if (fecha > ahoraLima.AddDays(HorarioAtencion.DiasMaximosAnticipacion))
        {
            return $"Solo se agenda con hasta {HorarioAtencion.DiasMaximosAnticipacion} días de anticipación";
        }
        return null;
    }
}

public sealed class AgendarCitaValidador : AbstractValidator<AgendarCitaSolicitud>
{
    public AgendarCitaValidador()
    {
        RuleFor(x => x.MascotaId).IdValido();
        RuleFor(x => x.VeterinarioId).IdValido();
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo es obligatorio").MaximumLength(250).WithMessage("Máximo 250 caracteres");
        RuleFor(x => x.FechaHora).Custom((valor, ctx) =>
        {
            if (ReglasHorario.Validar(valor, HoraLima.Ahora()) is string error)
            {
                ctx.AddFailure(error);
            }
        });
    }
}

public sealed class ReprogramarCitaValidador : AbstractValidator<ReprogramarCitaSolicitud>
{
    public ReprogramarCitaValidador()
    {
        RuleFor(x => x.VeterinarioId).IdValido();
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo es obligatorio").MaximumLength(250).WithMessage("Máximo 250 caracteres");
        RuleFor(x => x.FechaHora).Custom((valor, ctx) =>
        {
            if (ReglasHorario.Validar(valor, HoraLima.Ahora()) is string error)
            {
                ctx.AddFailure(error);
            }
        });
    }
}

public sealed class CancelarCitaValidador : AbstractValidator<CancelarCitaSolicitud>
{
    public CancelarCitaValidador() =>
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Indica el motivo de la cancelación").MaximumLength(250).WithMessage("Máximo 250 caracteres");
}

public sealed class ConsultaCitasValidador : AbstractValidator<ConsultaCitas>
{
    public ConsultaCitasValidador()
    {
        RuleFor(x => x.Desde).Must(d => HoraLima.LeerFecha(d) is not null).When(x => x.Desde is not null).WithMessage("Fecha inválida (AAAA-MM-DD)");
        RuleFor(x => x.Hasta).Must(d => HoraLima.LeerFecha(d) is not null).When(x => x.Hasta is not null).WithMessage("Fecha inválida (AAAA-MM-DD)");
        RuleFor(x => x.Estado).Must(e => e is null || EstadosCita.Todos.Contains(e)).WithMessage("Estado inválido");
        RuleFor(x => x.VeterinarioId).GreaterThan(0).When(x => x.VeterinarioId is not null).WithMessage("Id inválido");
        RuleFor(x => x.MascotaId).GreaterThan(0).When(x => x.MascotaId is not null).WithMessage("Id inválido");
        RuleFor(x => x).Custom((c, ctx) =>
        {
            if (HoraLima.LeerFecha(c.Desde) is DateOnly desde && HoraLima.LeerFecha(c.Hasta) is DateOnly hasta)
            {
                if (hasta < desde)
                {
                    ctx.AddFailure("hasta", "Debe ser igual o posterior a 'desde'");
                }
                else if (hasta.DayNumber - desde.DayNumber > ConsultaCitas.DiasMaximos)
                {
                    ctx.AddFailure("hasta", $"El rango no puede superar {ConsultaCitas.DiasMaximos} días");
                }
            }
        });
    }
}

public sealed class ConsultaDisponibilidadValidador : AbstractValidator<ConsultaDisponibilidad>
{
    public ConsultaDisponibilidadValidador()
    {
        RuleFor(x => x.VeterinarioId).IdValido();
        RuleFor(x => x.Fecha).Must(f => HoraLima.LeerFecha(f) is not null).WithMessage("Fecha inválida (AAAA-MM-DD)");
    }
}

/// <summary>Estado de una cita leído (y bloqueado) al registrar su atención.</summary>
public sealed record CitaParaAtencion(int Id, string Estado);

/// <summary>
/// Todas las lecturas reciben propietarioId: con un cliente, el SQL filtra
/// por "c.usuario_id = @propietarioId" (control de propiedad, IDOR).
/// </summary>
public interface ICitasRepositorio
{
    Task<IReadOnlyList<CitaRespuesta>> ListarAsync(
        DateTime desdeUtc, DateTime hastaUtc, int? veterinarioId, int? mascotaId, string? estado, int? propietarioId, CancellationToken ct = default);

    Task<CitaRespuesta?> ObtenerAsync(int id, int? propietarioId, CancellationToken ct = default);

    Task<bool> VeterinarioActivoAsync(int veterinarioId, CancellationToken ct = default);

    /// <summary>INSERT protegido por el índice único: dos inserciones simultáneas del mismo horario → una falla.</summary>
    Task<int> CrearAsync(int mascotaId, int veterinarioId, DateTime fechaHoraUtc, string motivo, int? creadoPor, CancellationToken ct = default);

    Task<bool> ReprogramarAsync(int id, int veterinarioId, DateTime fechaHoraUtc, string motivo, CancellationToken ct = default);

    Task<bool> CancelarAsync(int id, string motivo, int? canceladaPor, int? propietarioId, CancellationToken ct = default);

    Task<IReadOnlyList<DateTime>> HorariosOcupadosAsync(int veterinarioId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);

    Task<CitaParaAtencion?> ObtenerParaAtencionAsync(int citaId, int mascotaId, CancellationToken ct = default);

    Task MarcarAtendidaAsync(int citaId, CancellationToken ct = default);

    /// <summary>Programada → no_asistio, solo si su hora ya pasó. false = cambió de estado o aún no llega.</summary>
    Task<bool> MarcarNoAsistioAsync(int citaId, CancellationToken ct = default);
}
