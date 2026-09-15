using RiverVet.Aplicacion.Clientes;
using RiverVet.Aplicacion.Comun;

namespace RiverVet.Infraestructura.Datos;

public sealed class ClientesRepositorio(SesionBd sesion) : RepositorioBase(sesion), IClientesRepositorio
{
    private const string Columnas = """
        c.id, c.dni, c.nombres, c.apellidos, c.email, c.telefono, c.direccion,
        c.consentimiento_datos AS consentimientoDatos, c.consentimiento_en AS consentimientoEn,
        CAST(CASE WHEN c.usuario_id IS NULL THEN 0 ELSE 1 END AS BIT) AS tieneCuenta,
        c.creado_en AS creadoEn, c.actualizado_en AS actualizadoEn
        """;

    public Task<IReadOnlyList<ClienteListado>> ListarAsync(PaginacionConsulta consulta, string? texto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultarAsync<ClienteListado>(
            $"""
            SELECT {Columnas},
                   (SELECT COUNT(*) FROM dbo.mascotas m WHERE m.cliente_id = c.id AND m.eliminado = 0) AS totalMascotas,
                   COUNT(*) OVER() AS total
            FROM dbo.clientes c
            WHERE c.eliminado = 0
              AND (@patron IS NULL
                   OR c.nombres   LIKE @patron ESCAPE '\'
                   OR c.apellidos LIKE @patron ESCAPE '\'
                   OR c.dni       LIKE @patron ESCAPE '\'
                   OR c.email     LIKE @patron ESCAPE '\'
                   OR c.telefono  LIKE @patron ESCAPE '\')
            ORDER BY c.apellidos, c.nombres, c.id
            OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY
            """,
            new { patron = PatronBusqueda(texto), offset = consulta.Offset, limite = consulta.Limite },
            ct);
    }

    public Task<ClienteInterno?> ObtenerPorIdAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<ClienteInterno>(
            $"""
            SELECT {Columnas}, c.usuario_id AS usuarioId
            FROM dbo.clientes c
            WHERE c.id = @id AND c.eliminado = 0
            """,
            new { id },
            ct);

    public Task<ClienteRespuesta?> ObtenerPorUsuarioAsync(int usuarioId, CancellationToken ct = default) =>
        ConsultarUnoAsync<ClienteRespuesta>(
            $"SELECT {Columnas} FROM dbo.clientes c WHERE c.usuario_id = @usuarioId AND c.eliminado = 0",
            new { usuarioId },
            ct);

    // Control de propiedad DENTRO del SQL: con @propietarioId = NULL
    // (personal) no filtra; con un id, solo encuentra el cliente si es suyo.
    public async Task<bool> ExisteParaUsuarioAsync(int id, int? propietarioId, CancellationToken ct = default) =>
        await EscalarOpcionalAsync<int?>(
            """
            SELECT 1 FROM dbo.clientes c
            WHERE c.id = @id AND c.eliminado = 0
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            """,
            new { id, propietarioId },
            ct) == 1;

    public Task<int> CrearAsync(DatosClienteSolicitud datos, int? usuarioId, int? creadoPor, CancellationToken ct = default) =>
        EscalarAsync<int>(
            """
            INSERT INTO dbo.clientes (usuario_id, dni, nombres, apellidos, email, telefono, direccion,
                                      consentimiento_datos, consentimiento_en, creado_por)
            OUTPUT INSERTED.id
            VALUES (@usuarioId, @dni, @nombres, @apellidos, @email, @telefono, @direccion,
                    1, SYSUTCDATETIME(), @creadoPor)
            """,
            Parametros(datos, new { usuarioId, creadoPor }),
            ct);

    public async Task<bool> ActualizarAsync(int id, DatosClienteSolicitud datos, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.clientes
            SET dni = @dni, nombres = @nombres, apellidos = @apellidos, email = @email,
                telefono = @telefono, direccion = @direccion, actualizado_en = SYSUTCDATETIME()
            WHERE id = @id AND eliminado = 0
            """,
            Parametros(datos, new { id }),
            ct) > 0;

    public Task EliminarAsync(int id, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.mascotas SET eliminado = 1, eliminado_en = SYSUTCDATETIME()
            WHERE cliente_id = @id AND eliminado = 0;

            UPDATE dbo.clientes SET eliminado = 1, eliminado_en = SYSUTCDATETIME()
            WHERE id = @id AND eliminado = 0;
            """,
            new { id },
            ct);

    private static Dapper.DynamicParameters Parametros(DatosClienteSolicitud datos, object extra)
    {
        var parametros = new Dapper.DynamicParameters(extra);
        parametros.AddDynamicParams(new
        {
            dni = Ascii(datos.Dni, 8, fija: true),
            nombres = Unicode(datos.Nombres, 100),
            apellidos = Unicode(datos.Apellidos, 100),
            email = Unicode(datos.Email, 254),
            telefono = Ascii(datos.Telefono, 9, fija: true),
            direccion = Unicode(datos.Direccion, 250),
        });
        return parametros;
    }
}
