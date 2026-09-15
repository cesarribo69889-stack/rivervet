using Microsoft.OpenApi;

namespace RiverVet.Api.Configuracion;

/// <summary>
/// Swagger: la página que abre Visual Studio al ejecutar con IIS Express
/// (/swagger). Lista todos los endpoints y permite probarlos. Solo existe en
/// DESARROLLO: en producción no se publica el mapa de la API.
///
/// Para probar un endpoint protegido: ejecuta POST /api/auth/login, copia el
/// accessToken, pulsa "Authorize" y pégalo.
/// </summary>
public static class ConfiguracionSwagger
{
    public static IServiceCollection AgregarSwagger(this IServiceCollection servicios)
    {
        servicios.AddEndpointsApiExplorer();
        servicios.AddSwaggerGen(opciones =>
        {
            opciones.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "RiverVet API",
                Version = "v1",
                Description = "API de la clínica veterinaria RiverVet. Contrato completo en docs/API.md.",
            });
            opciones.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Pega aquí el accessToken que devuelve POST /api/auth/login.",
            });
            opciones.AddSecurityRequirement(documento => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("bearer", documento)] = [],
            });
        });
        return servicios;
    }
}
