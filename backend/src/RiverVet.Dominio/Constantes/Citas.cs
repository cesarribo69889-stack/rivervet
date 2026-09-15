namespace RiverVet.Dominio.Constantes;

/// <summary>Estados de una cita (CHECK CK_citas_estado en la base).</summary>
public static class EstadosCita
{
    public const string Programada = "programada";
    public const string Atendida = "atendida";
    public const string Cancelada = "cancelada";
    public const string NoAsistio = "no_asistio";

    public static readonly IReadOnlyList<string> Todos = [Programada, Atendida, Cancelada, NoAsistio];
}

/// <summary>
/// Horario de atención de la clínica, en hora de Lima. Las citas ocupan
/// bloques fijos de 30 minutos que empiezan en :00 o :30 (así un índice único
/// basta para impedir el doble agendamiento; ver migración 0002).
/// </summary>
public static class HorarioAtencion
{
    public static readonly TimeOnly Apertura = new(8, 0);

    /// <summary>Hora de cierre: el último bloque empieza a las 19:30.</summary>
    public static readonly TimeOnly Cierre = new(20, 0);

    public const int MinutosBloque = 30;

    /// <summary>De lunes a sábado.</summary>
    public static bool AtiendeEl(DayOfWeek dia) => dia != DayOfWeek.Sunday;

    /// <summary>Máximo de días hacia adelante para agendar.</summary>
    public const int DiasMaximosAnticipacion = 180;

    public const string Descripcion = "de lunes a sábado, de 08:00 a 20:00";
}
