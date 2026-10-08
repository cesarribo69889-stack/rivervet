using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Auditoria;
using RiverVet.Aplicacion.Autenticacion;
using RiverVet.Aplicacion.Citas;
using RiverVet.Aplicacion.Clientes;
using RiverVet.Aplicacion.Comprobantes;
using RiverVet.Aplicacion.DatosPersonales;
using RiverVet.Aplicacion.Historial;
using RiverVet.Aplicacion.Indicadores;
using RiverVet.Aplicacion.Mascotas;
using RiverVet.Aplicacion.Pagos;
using RiverVet.Aplicacion.Productos;
using RiverVet.Aplicacion.Recordatorios;
using RiverVet.Aplicacion.Tablero;
using RiverVet.Aplicacion.Usuarios;
using RiverVet.Aplicacion.Vacunas;
using RiverVet.Aplicacion.Ventas;
using RiverVet.Infraestructura.Datos;
using RiverVet.Infraestructura.Externos;
using RiverVet.Infraestructura.Seguridad;

namespace RiverVet.Infraestructura;

public static class InyeccionDependencias
{
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios)
    {
        // Dapper envía DateTime como DATETIME por defecto; nuestras columnas
        // son DATETIME2. Con el mismo tipo no hay conversiones ni pérdida de
        // precisión al comparar horarios de citas.
        Dapper.SqlMapper.AddTypeMap(typeof(DateTime), System.Data.DbType.DateTime2);
        Dapper.SqlMapper.AddTypeMap(typeof(DateTime?), System.Data.DbType.DateTime2);

        servicios.AddMemoryCache();

        // Una sesión de base de datos por petición HTTP.
        servicios.AddScoped<SesionBd>();
        servicios.AddScoped<IUnidadTrabajo>(sp => sp.GetRequiredService<SesionBd>());

        servicios.AddScoped<IUsuariosRepositorio, UsuariosRepositorio>();
        servicios.AddScoped<IRefreshTokensRepositorio, RefreshTokensRepositorio>();
        servicios.AddScoped<IClientesRepositorio, ClientesRepositorio>();
        servicios.AddScoped<IMascotasRepositorio, MascotasRepositorio>();
        servicios.AddScoped<IAuditoriaRepositorio, AuditoriaRepositorio>();
        servicios.AddScoped<ICitasRepositorio, CitasRepositorio>();
        servicios.AddScoped<IHistorialRepositorio, HistorialRepositorio>();
        servicios.AddScoped<IVacunasRepositorio, VacunasRepositorio>();
        servicios.AddScoped<IRecordatoriosRepositorio, RecordatoriosRepositorio>();
        servicios.AddScoped<ITableroRepositorio, TableroRepositorio>();
        servicios.AddScoped<ICategoriasRepositorio, CategoriasRepositorio>();
        servicios.AddScoped<IProductosRepositorio, ProductosRepositorio>();
        servicios.AddScoped<ICarritoRepositorio, CarritoRepositorio>();
        servicios.AddScoped<IPedidosRepositorio, PedidosRepositorio>();
        servicios.AddScoped<IIndicadoresRepositorio, IndicadoresRepositorio>();
        servicios.AddScoped<IPagosRepositorio, PagosRepositorio>();
        servicios.AddScoped<IConfiguracionYapeRepositorio, ConfiguracionYapeRepositorio>();
        servicios.AddScoped<IComprobantesRepositorio, ComprobantesRepositorio>();
        servicios.AddScoped<IExportacionRepositorio, ExportacionRepositorio>();

        servicios.AddSingleton<IHasheadorPassword, HasheadorBcrypt>();
        servicios.AddSingleton<IServicioTokens, ServicioTokens>();
        servicios.AddScoped<IServicioAuditoria, ServicioAuditoria>();
        servicios.AddScoped<ILimitadorIntentosLogin, LimitadorIntentosLogin>();

        // ── Servicios externos: la implementación se elige por configuración ──
        // Pasarela: "simulada" (desarrollo y pruebas) o ninguna. Una pasarela
        // real se agrega con su propia clase (ver PasarelaSimulada).
        servicios.AddSingleton<IPasarelaPagos>(sp =>
            string.Equals(sp.GetRequiredService<IOptions<OpcionesPagos>>().Value.Pasarela.Proveedor, PasarelaSimulada.Nombre, StringComparison.Ordinal)
                ? new PasarelaSimulada(sp.GetRequiredService<IOptions<OpcionesPagos>>())
                : new PasarelaNoConfigurada());

        // WhatsApp: enlace wa.me (por defecto) o API Cloud de Meta, con un
        // tiempo máximo de espera para no dejar colgada la petición.
        servicios.AddHttpClient<WhatsAppCloudApi>(http => http.Timeout = TimeSpan.FromSeconds(10));
        servicios.AddSingleton<WhatsAppEnlace>();
        servicios.AddScoped<IMensajeriaWhatsApp>(sp =>
            sp.GetRequiredService<IOptions<OpcionesWhatsApp>>().Value.Modo == OpcionesWhatsApp.ModoCloudApi
                ? sp.GetRequiredService<WhatsAppCloudApi>()
                : sp.GetRequiredService<WhatsAppEnlace>());
        return servicios;
    }
}
