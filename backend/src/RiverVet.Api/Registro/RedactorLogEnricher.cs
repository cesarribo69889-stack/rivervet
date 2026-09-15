using RiverVet.Aplicacion.Comun;
using Serilog.Core;
using Serilog.Events;

namespace RiverVet.Api.Registro;

/// <summary>
/// Pasa CADA evento de log por el redactor antes de escribirlo: aunque alguien
/// haga logger.LogInformation("{Password}", password) por error, la
/// contraseña no llega al archivo. Redacta propiedades con nombre sensible y
/// JWT, "Bearer ..." y números de tarjeta dentro de cualquier texto.
/// </summary>
public sealed class RedactorLogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        foreach (var (nombre, valor) in logEvent.Properties.ToList())
        {
            var nuevo = RedactorDatos.EsClaveSensible(nombre)
                ? new ScalarValue(RedactorDatos.Redactado)
                : Redactar(valor);
            if (!ReferenceEquals(nuevo, valor))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(nombre, nuevo));
            }
        }
    }

    private static LogEventPropertyValue Redactar(LogEventPropertyValue valor) => valor switch
    {
        ScalarValue { Value: string texto } => RedactorDatos.RedactarTexto(texto) is var r && r != texto ? new ScalarValue(r) : valor,
        StructureValue estructura => new StructureValue(
            estructura.Properties.Select(p => new LogEventProperty(
                p.Name,
                RedactorDatos.EsClaveSensible(p.Name) ? new ScalarValue(RedactorDatos.Redactado) : Redactar(p.Value))),
            estructura.TypeTag),
        SequenceValue secuencia => new SequenceValue(secuencia.Elements.Select(Redactar)),
        DictionaryValue diccionario => new DictionaryValue(diccionario.Elements.Select(e =>
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                e.Key,
                e.Key.Value is string clave && RedactorDatos.EsClaveSensible(clave)
                    ? new ScalarValue(RedactorDatos.Redactado)
                    : Redactar(e.Value)))),
        _ => valor,
    };
}
