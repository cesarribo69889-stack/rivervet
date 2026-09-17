# RiverVet

Plataforma web de gestión para una clínica veterinaria en Lima, Perú.
Proyecto académico — curso de Aplicaciones para Procesos de Software (UPN).

Este repositorio contiene el **backend**, con su propia integración y despliegue
continuos (`.github/workflows/`):

| Carpeta | Qué es | Cómo se abre | Despliegue |
|---|---|---|---|
| `backend/` | API REST: **ASP.NET Core Web API (.NET 10)** + SQL Server | Visual Studio → `RiverVet.slnx` → **IIS Express** ▶ | IIS (Azure App Service) + Azure SQL |

El frontend lo desarrolla otro equipo en su propio repositorio. Lo único que los
une es el **contrato de la API**: [`backend/docs/API.md`](backend/docs/API.md).
La base de datos (migraciones, seed y usuarios) vive en `backend/database/`,
porque el backend es el único que habla con ella.

## Por dónde empezar

1. [`backend/docs/INSTALACION.md`](backend/docs/INSTALACION.md) — base de datos y API en Windows (un script + Visual Studio).
2. [`backend/docs/INTEGRACION_LOCAL.md`](backend/docs/INTEGRACION_LOCAL.md) — verificar la API conectada a la base de datos y a un frontend local.

## Documentación

- [`backend/docs/ARQUITECTURA.md`](backend/docs/ARQUITECTURA.md) — capas, estructura de ambos proyectos y decisiones
- [`backend/docs/MODELO_DATOS.md`](backend/docs/MODELO_DATOS.md) — modelo de datos de los 4 sprints
- [`backend/docs/API.md`](backend/docs/API.md) — contrato exacto de la API
- [`backend/docs/CI.md`](backend/docs/CI.md) — integración continua y protección de ramas
- [`backend/docs/DESPLIEGUE.md`](backend/docs/DESPLIEGUE.md) — despliegue continuo: Azure SQL, App Service (IIS), Vercel
- [`backend/docs/SEGURIDAD.md`](backend/docs/SEGURIDAD.md) — cada control de seguridad, qué ataque previene y en qué archivo está
- [`backend/docs/CICD.md`](backend/docs/CICD.md) — el pipeline de CI/CD explicado para alguien no técnico
