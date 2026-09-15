using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using RiverVet.Aplicacion.Comun;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Auditoria;

public sealed class RegistroAuditoria : IConTotal
{
    public long Id { get; init; }

    public int? UsuarioId { get; init; }

    public string? UsuarioNombre { get; init; }

    public string? UsuarioRol { get; init; }

    public string Accion { get; init; } = string.Empty;

    public string Recurso { get; init; } = string.Empty;

    public string? RecursoId { get; init; }

    /// <summary>JSON tal como está guardado (ya redactado al escribirse).</summary>
    [JsonIgnore]
    public string? DetalleJson { get; init; }

    public JsonElement? Detalle =>
        DetalleJson is null ? null : JsonSerializer.Deserialize<JsonElement>(DetalleJson);

    public string? Ip { get; init; }

    public DateTime CreadoEn { get; init; }

    [JsonIgnore]
    public int Total { get; init; }
}

public sealed class ConsultaAuditoria : PaginacionConsulta
{
    public string? Accion { get; set; }

    public string? Recurso { get; set; }

    public int? UsuarioId { get; set; }
}

public sealed class ConsultaAuditoriaValidador : AbstractValidator<ConsultaAuditoria>
{
    public ConsultaAuditoriaValidador()
    {
        Include(new PaginacionValidador());
        RuleFor(x => x.Accion).Must(a => a is null || AccionesAuditoria.Todas.Contains(a)).WithMessage("Acción inválida");
        RuleFor(x => x.Recurso).MaximumLength(40).WithMessage("Máximo 40 caracteres");
        RuleFor(x => x.UsuarioId).GreaterThan(0).When(x => x.UsuarioId is not null).WithMessage("Id inválido");
    }
}

public sealed record NuevoRegistroAuditoria(
    int? UsuarioId, string Accion, string Recurso, string? RecursoId, string? DetalleJson, string? Ip, string? UserAgent);

public interface IAuditoriaRepositorio
{
    Task InsertarAsync(NuevoRegistroAuditoria registro, CancellationToken ct = default);

    Task<IReadOnlyList<RegistroAuditoria>> ListarAsync(ConsultaAuditoria consulta, CancellationToken ct = default);
}

/// <summary>Consulta de la auditoría (solo admin). Solo lectura: no hay forma de modificarla.</summary>
public sealed class AuditoriaServicio(IAuditoriaRepositorio repositorio)
{
    public async Task<ResultadoPaginado<RegistroAuditoria>> ListarAsync(ConsultaAuditoria consulta, CancellationToken ct = default) =>
        ResultadoPaginado.Crear(await repositorio.ListarAsync(consulta, ct), consulta);
}
