using RiverVet.Aplicacion.Usuarios;

namespace RiverVet.Infraestructura.Datos;

public sealed class UsuariosRepositorio(SesionBd sesion) : RepositorioBase(sesion), IUsuariosRepositorio
{
    // Columnas que se pueden devolver. Nunca SELECT *: evita filtrar
    // password_hash por accidente cuando alguien agregue una columna.
    private const string Columnas = """
        u.id, u.nombre, u.email, u.rol, u.activo,
        u.ultimo_login_en AS ultimoLoginEn, u.creado_en AS creadoEn
        """;

    private const string ColumnasInsertadas = """
        INSERTED.id, INSERTED.nombre, INSERTED.email, INSERTED.rol, INSERTED.activo,
        INSERTED.ultimo_login_en AS ultimoLoginEn, INSERTED.creado_en AS creadoEn
        """;

    public Task<IReadOnlyList<VeterinarioResumen>> ListarVeterinariosAsync(CancellationToken ct = default) =>
        ConsultarAsync<VeterinarioResumen>(
            "SELECT id, nombre FROM dbo.usuarios WHERE rol = N'veterinario' AND activo = 1 AND eliminado = 0 ORDER BY nombre",
            null,
            ct);

    public Task<UsuarioSesion?> ObtenerSesionAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<UsuarioSesion>(
            """
            SELECT u.id, u.nombre, u.email, u.rol, c.id AS clienteId
            FROM dbo.usuarios u
            LEFT JOIN dbo.clientes c ON c.usuario_id = u.id AND c.eliminado = 0
            WHERE u.id = @id AND u.activo = 1 AND u.eliminado = 0
            """,
            new { id },
            ct);

    // Único lugar donde se lee password_hash. El estado de bloqueo se calcula
    // en SQL con la hora del servidor de base de datos (una sola fuente de verdad).
    public Task<UsuarioLogin?> BuscarParaLoginAsync(string email, CancellationToken ct = default) =>
        ConsultarUnoAsync<UsuarioLogin>(
            """
            SELECT id, nombre, email, rol, activo, password_hash AS passwordHash,
                   CAST(CASE WHEN bloqueado_hasta > SYSUTCDATETIME() THEN 1 ELSE 0 END AS BIT) AS bloqueado
            FROM dbo.usuarios
            WHERE email = @email AND eliminado = 0
            """,
            new { email = Unicode(email, 254) },
            ct);

    // Suma un intento fallido de forma ATÓMICA (un solo UPDATE): dos intentos
    // simultáneos no pueden "perder" una suma. Al llegar al máximo, bloquea la
    // cuenta y reinicia el contador.
    public async Task<bool> RegistrarIntentoFallidoAsync(int id, int maxIntentos, int minutosBloqueo, CancellationToken ct = default) =>
        await EscalarOpcionalAsync<bool?>(
            """
            UPDATE dbo.usuarios SET
              intentos_fallidos = CASE WHEN intentos_fallidos + 1 >= @max THEN 0 ELSE intentos_fallidos + 1 END,
              bloqueado_hasta   = CASE WHEN intentos_fallidos + 1 >= @max
                                       THEN DATEADD(MINUTE, @minutos, SYSUTCDATETIME())
                                       ELSE bloqueado_hasta END
            OUTPUT CAST(CASE WHEN deleted.intentos_fallidos + 1 >= @max THEN 1 ELSE 0 END AS BIT)
            WHERE id = @id
            """,
            new { id, max = maxIntentos, minutos = minutosBloqueo },
            ct) == true;

    public Task RegistrarLoginExitosoAsync(int id, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.usuarios
            SET intentos_fallidos = 0, bloqueado_hasta = NULL, ultimo_login_en = SYSUTCDATETIME()
            WHERE id = @id
            """,
            new { id },
            ct);

    public async Task<UsuarioRespuesta> CrearAsync(string nombre, string email, string passwordHash, string rol, CancellationToken ct = default) =>
        await InsertarYDevolverAsync<UsuarioRespuesta>(
            $"""
            INSERT INTO dbo.usuarios (nombre, email, password_hash, rol)
            OUTPUT {ColumnasInsertadas}
            VALUES (@nombre, @email, @hash, @rol)
            """,
            new
            {
                nombre = Unicode(nombre, 100),
                email = Unicode(email, 254),
                hash = Ascii(passwordHash, 60),
                rol = Unicode(rol, 20),
            },
            ct) ?? throw new InvalidOperationException("No se creó el usuario");

    public Task<IReadOnlyList<UsuarioRespuesta>> ListarAsync(ListarUsuariosConsulta consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        return ConsultarAsync<UsuarioRespuesta>(
            $"""
            SELECT {Columnas}, COUNT(*) OVER() AS total
            FROM dbo.usuarios u
            WHERE u.eliminado = 0
              AND (@rol IS NULL OR u.rol = @rol)
            ORDER BY u.nombre, u.id
            OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY
            """,
            new { rol = Unicode(consulta.Rol, 20), offset = consulta.Offset, limite = consulta.Limite },
            ct);
    }

    public Task<UsuarioRespuesta?> ObtenerPorIdAsync(int id, CancellationToken ct = default) =>
        ConsultarUnoAsync<UsuarioRespuesta>(
            $"SELECT {Columnas} FROM dbo.usuarios u WHERE u.id = @id AND u.eliminado = 0",
            new { id },
            ct);

    public Task<UsuarioRespuesta?> ActualizarAsync(int id, string nombre, string rol, bool activo, CancellationToken ct = default) =>
        InsertarYDevolverAsync<UsuarioRespuesta>(
            $"""
            UPDATE dbo.usuarios
            SET nombre = @nombre, rol = @rol, activo = @activo, actualizado_en = SYSUTCDATETIME()
            OUTPUT {ColumnasInsertadas}
            WHERE id = @id AND eliminado = 0
            """,
            new { id, nombre = Unicode(nombre, 100), rol = Unicode(rol, 20), activo },
            ct);

    public Task ActualizarEmailYNombreAsync(int id, string email, string nombre, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.usuarios SET email = @email, nombre = @nombre, actualizado_en = SYSUTCDATETIME()
            WHERE id = @id
            """,
            new { id, email = Unicode(email, 254), nombre = Unicode(nombre, 100) },
            ct);

    public Task<string?> ObtenerHashAsync(int id, CancellationToken ct = default) =>
        EscalarOpcionalAsync<string>(
            "SELECT password_hash FROM dbo.usuarios WHERE id = @id AND eliminado = 0",
            new { id },
            ct);

    public Task ActualizarPasswordAsync(int id, string passwordHash, CancellationToken ct = default) =>
        EjecutarAsync(
            "UPDATE dbo.usuarios SET password_hash = @hash, actualizado_en = SYSUTCDATETIME() WHERE id = @id",
            new { id, hash = Ascii(passwordHash, 60) },
            ct);

    public Task DesactivarYEliminarAsync(int id, CancellationToken ct = default) =>
        EjecutarAsync(
            """
            UPDATE dbo.usuarios
            SET activo = 0, eliminado = 1, eliminado_en = SYSUTCDATETIME(), actualizado_en = SYSUTCDATETIME()
            WHERE id = @id
            """,
            new { id },
            ct);
}
