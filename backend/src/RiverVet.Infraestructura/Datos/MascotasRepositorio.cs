using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Mascotas;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// ★ CONTROL DE PROPIEDAD (IDOR) ★ — cada lectura filtra con
/// "(@propietarioId IS NULL OR c.usuario_id = @propietarioId)" DENTRO del SQL.
/// </summary>
public sealed class MascotasRepositorio(SesionBd sesion) : RepositorioBase(sesion), IMascotasRepositorio
{
    // Las fechas DATE se devuelven como texto 'AAAA-MM-DD' (CONVERT estilo 23)
    // para que no sufran corrimientos de zona horaria.
    private const string Columnas = """
        m.id, m.cliente_id AS clienteId, m.nombre, m.especie, m.raza, m.sexo,
        CONVERT(CHAR(10), m.fecha_nacimiento, 23) AS fechaNacimiento,
        m.peso_kg AS pesoKg, m.color, m.senas_particulares AS senasParticulares, m.esterilizado,
        CONCAT(c.nombres, N' ', c.apellidos) AS clienteNombre,
        m.creado_en AS creadoEn, m.actualizado_en AS actualizadoEn
        """;

    public Task<IReadOnlyList<MascotaRespuesta>> ListarAsync(
        PaginacionConsulta consulta, string? texto, int? clienteId, int? propietarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultarAsync<MascotaRespuesta>(
            $"""
            SELECT {Columnas}, COUNT(*) OVER() AS total
            FROM dbo.mascotas m
            JOIN dbo.clientes c ON c.id = m.cliente_id AND c.eliminado = 0
            WHERE m.eliminado = 0
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
              AND (@clienteId IS NULL OR m.cliente_id = @clienteId)
              AND (@patron IS NULL
                   OR m.nombre    LIKE @patron ESCAPE '\'
                   OR c.nombres   LIKE @patron ESCAPE '\'
                   OR c.apellidos LIKE @patron ESCAPE '\'
                   OR c.dni       LIKE @patron ESCAPE '\')
            ORDER BY m.nombre, m.id
            OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY
            """,
            new
            {
                propietarioId,
                clienteId,
                patron = PatronBusqueda(texto),
                offset = consulta.Offset,
                limite = consulta.Limite,
            },
            ct);
    }

    public Task<MascotaRespuesta?> ObtenerPorIdAsync(int id, int? propietarioId, CancellationToken ct = default) =>
        ConsultarUnoAsync<MascotaRespuesta>(
            $"""
            SELECT {Columnas}
            FROM dbo.mascotas m
            JOIN dbo.clientes c ON c.id = m.cliente_id AND c.eliminado = 0
            WHERE m.id = @id AND m.eliminado = 0
              AND (@propietarioId IS NULL OR c.usuario_id = @propietarioId)
            """,
            new { id, propietarioId },
            ct);

    // INSERT ... SELECT: la verificación de que el cliente existe y la
    // inserción son UNA sola sentencia, sin ventana para que el cliente se
    // elimine en medio. Si no existe, no inserta nada y devuelve null.
    public Task<int?> CrearAsync(CrearMascotaSolicitud datos, int? creadoPor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var parametros = Parametros(datos);
        parametros.Add("clienteId", datos.ClienteId);
        parametros.Add("creadoPor", creadoPor);
        return EscalarOpcionalAsync<int?>(
            """
            INSERT INTO dbo.mascotas (cliente_id, nombre, especie, raza, sexo, fecha_nacimiento,
                                      peso_kg, color, senas_particulares, esterilizado, creado_por)
            OUTPUT INSERTED.id
            SELECT c.id, @nombre, @especie, @raza, @sexo, CAST(@fechaNacimiento AS DATE),
                   @pesoKg, @color, @senas, @esterilizado, @creadoPor
            FROM dbo.clientes c
            WHERE c.id = @clienteId AND c.eliminado = 0
            """,
            parametros,
            ct);
    }

    public async Task<bool> ActualizarAsync(int id, DatosMascotaSolicitud datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var parametros = Parametros(datos);
        parametros.Add("id", id);
        return await EjecutarAsync(
            """
            UPDATE dbo.mascotas
            SET nombre = @nombre, especie = @especie, raza = @raza, sexo = @sexo,
                fecha_nacimiento = CAST(@fechaNacimiento AS DATE), peso_kg = @pesoKg, color = @color,
                senas_particulares = @senas, esterilizado = @esterilizado,
                actualizado_en = SYSUTCDATETIME()
            WHERE id = @id AND eliminado = 0
            """,
            parametros,
            ct) > 0;
    }

    public async Task<bool> EliminarAsync(int id, CancellationToken ct = default) =>
        await EjecutarAsync(
            """
            UPDATE dbo.mascotas SET eliminado = 1, eliminado_en = SYSUTCDATETIME()
            WHERE id = @id AND eliminado = 0
            """,
            new { id },
            ct) > 0;

    public Task ActualizarPesoAsync(int id, decimal pesoKg, CancellationToken ct = default) =>
        EjecutarAsync(
            "UPDATE dbo.mascotas SET peso_kg = @pesoKg, actualizado_en = SYSUTCDATETIME() WHERE id = @id AND eliminado = 0",
            new { id, pesoKg },
            ct);

    private static Dapper.DynamicParameters Parametros(DatosMascotaSolicitud datos) =>
        new(new
        {
            nombre = Unicode(datos.Nombre, 60),
            especie = Unicode(datos.Especie, 20),
            raza = Unicode(datos.Raza, 60),
            sexo = Unicode(datos.Sexo, 10),
            // La fecha viaja como texto 'AAAA-MM-DD' (ya validado) y SQL la
            // convierte con CAST: sin conversiones de zona horaria en el camino.
            fechaNacimiento = Ascii(datos.FechaNacimiento, 10),
            pesoKg = datos.PesoKg,
            color = Unicode(datos.Color, 40),
            senas = Unicode(datos.SenasParticulares, 250),
            esterilizado = datos.Esterilizado,
        });
}
