using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RiverVet.Aplicacion.Abstracciones;

namespace RiverVet.Infraestructura.Datos;

/// <summary>
/// Conexión a SQL Server de UNA petición HTTP (servicio "scoped").
///
/// - La conexión se abre la primera vez que un repositorio la pide y se
///   reutiliza durante toda la petición. SqlClient mantiene un pool de
///   conexiones: "abrir" una conexión es tomarla del pool, no un login nuevo.
/// - Si hay una transacción abierta, TODOS los repositorios la usan
///   automáticamente (propiedad Transaccion).
/// </summary>
public sealed class SesionBd(IConfiguration configuracion) : IUnidadTrabajo, IAsyncDisposable
{
    private SqlConnection? _conexion;

    public SqlTransaction? Transaccion { get; private set; }

    public async Task<SqlConnection> ObtenerConexionAsync(CancellationToken ct = default)
    {
        if (_conexion is null)
        {
            var cadena = configuracion.GetConnectionString("RiverVet")
                ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:RiverVet");
            _conexion = new SqlConnection(cadena);
        }
        if (_conexion.State != ConnectionState.Open)
        {
            await _conexion.OpenAsync(ct);
        }
        return _conexion;
    }

    public async Task<ITransaccion> IniciarTransaccionAsync(
        IsolationLevel nivel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        if (Transaccion is not null)
        {
            throw new InvalidOperationException("Ya hay una transacción abierta en esta petición");
        }
        var conexion = await ObtenerConexionAsync(ct);
        Transaccion = (SqlTransaction)await conexion.BeginTransactionAsync(nivel, ct);
        return new TransaccionSql(this, Transaccion);
    }

    internal void Liberar(SqlTransaction transaccion)
    {
        if (ReferenceEquals(Transaccion, transaccion))
        {
            Transaccion = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Transaccion is not null)
        {
            await Transaccion.DisposeAsync();
        }
        if (_conexion is not null)
        {
            await _conexion.DisposeAsync();
        }
    }

    /// <summary>
    /// Transacción abierta. Si se descarta sin confirmar (porque hubo una
    /// excepción), se hace ROLLBACK de TODO.
    /// </summary>
    private sealed class TransaccionSql(SesionBd sesion, SqlTransaction transaccion) : ITransaccion
    {
        private bool _confirmada;

        public async Task ConfirmarAsync(CancellationToken ct = default)
        {
            await transaccion.CommitAsync(ct);
            _confirmada = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_confirmada && transaccion.Connection is not null)
                {
                    await transaccion.RollbackAsync();
                }
            }
            catch (InvalidOperationException)
            {
                // SQL Server ya abortó la transacción por su cuenta: no hay
                // nada que revertir; el error original es el que importa.
            }
            finally
            {
                sesion.Liberar(transaccion);
                await transaccion.DisposeAsync();
            }
        }
    }
}
