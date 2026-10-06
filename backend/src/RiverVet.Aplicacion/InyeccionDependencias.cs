using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
using RiverVet.Aplicacion.Ventas;

namespace RiverVet.Aplicacion;

public static class InyeccionDependencias
{
    public static IServiceCollection AgregarAplicacion(this IServiceCollection servicios)
    {
        // Mensajes de FluentValidation en español para las reglas sin mensaje propio.
        ValidatorOptions.Global.LanguageManager.Culture = new System.Globalization.CultureInfo("es");
        servicios.AddValidatorsFromAssemblyContaining<LoginValidador>(ServiceLifetime.Singleton);

        servicios.AddScoped<AutenticacionServicio>();
        servicios.AddScoped<UsuariosServicio>();
        servicios.AddScoped<ClientesServicio>();
        servicios.AddScoped<MascotasServicio>();
        servicios.AddScoped<AuditoriaServicio>();
        servicios.AddScoped<CitasServicio>();
        servicios.AddScoped<AtencionServicio>();
        servicios.AddScoped<RecordatoriosServicio>();
        servicios.AddScoped<TableroServicio>();
        servicios.AddScoped<ProductosServicio>();
        servicios.AddScoped<CarritoServicio>();
        servicios.AddScoped<PedidosServicio>();
        servicios.AddScoped<IndicadoresServicio>();
        servicios.AddScoped<PagosServicio>();
        servicios.AddScoped<ComprobantesServicio>();
        servicios.AddScoped<ExportacionServicio>();
        // Reloj inyectable: las pruebas pueden fijar la hora (p. ej. webhooks vencidos).
        servicios.TryAddSingleton(TimeProvider.System);
        return servicios;
    }
}
