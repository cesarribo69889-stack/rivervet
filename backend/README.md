# RiverVet — Backend

![CI](https://github.com/cesarribo69889-stack/rivervet/actions/workflows/backend-ci.yml/badge.svg?branch=develop)

API REST de la plataforma de gestión veterinaria RiverVet.
**ASP.NET Core Web API (.NET 10) · IIS / IIS Express · SQL Server (Dapper, SQL a mano) · JWT · xUnit**

## Inicio rápido

```powershell
cd backend
.\configurar-desarrollo.ps1      # base de datos, usuarios, secretos, migraciones y seed
```

Luego abre **`RiverVet.slnx`** en Visual Studio, elige el perfil **IIS Express**
y pulsa ▶. Se abre **Swagger** en el navegador.

Guía completa: [docs/INSTALACION.md](docs/INSTALACION.md).

## Comandos

| Comando | Qué hace |
|---|---|
| `dotnet build RiverVet.slnx` | compila (las advertencias son errores) |
| `dotnet test RiverVet.slnx` | pruebas unitarias + integración |
| `dotnet test RiverVet.slnx --filter Categoria=Unitaria` | solo unitarias (sin SQL Server) |
| `dotnet format RiverVet.slnx` | corrige el formato |
| `dotnet run --project src\RiverVet.Api --launch-profile http` | API sin Visual Studio (`http://localhost:3000`) |
| `dotnet run --project src\RiverVet.Migrador -- migrar` | aplica migraciones pendientes |
| `dotnet run --project src\RiverVet.Migrador -- verificar` | solo verifica (no cambia nada) |
| `dotnet run --project src\RiverVet.Migrador -- seed` | datos de prueba (bloqueado en producción) |
| `dotnet run --project src\RiverVet.Migrador -- esquema` | verifica tablas y restricciones críticas |
| `dotnet run --project src\RiverVet.Migrador -- crear-admin <email> "<nombre>"` | primer administrador en producción (contraseña en `RIVERVET_ADMIN_PASSWORD`) |
| `.\herramientas\simular-webhook.ps1 -Referencia sim_... -Monto 85.80` | simula el aviso firmado de la pasarela de pagos (solo desarrollo) |

## Documentación

- [API.md](docs/API.md) — contrato exacto (lo usa el frontend)
- [MODELO_DATOS.md](docs/MODELO_DATOS.md) — tablas de los 4 sprints y restricciones
- [ARQUITECTURA.md](docs/ARQUITECTURA.md) — capas, estructura y decisiones
- [INSTALACION.md](docs/INSTALACION.md) — levantar todo en Windows
- [INTEGRACION_LOCAL.md](docs/INTEGRACION_LOCAL.md) — verificar las 3 capas; errores de CORS/cookies
- [CI.md](docs/CI.md) — pipeline, ramas y protección en GitHub
- [DESPLIEGUE.md](docs/DESPLIEGUE.md) — CD: Azure SQL, App Service (IIS), Vercel, secretos, health check y rollback
- [SEGURIDAD.md](docs/SEGURIDAD.md) — cada control de seguridad, qué ataque previene y en qué archivo está
- [CICD.md](docs/CICD.md) — el pipeline explicado para alguien no técnico

## Estado

| Sprint | Historias | Estado |
|---|---|---|
| 1 | HU-001 a HU-005 | ✅ implementado y probado |
| 2 | HU-006 a HU-012 | ✅ backend implementado y probado |
| 3 | HU-013 a HU-016, HU-018 | ✅ backend implementado y probado (incluida la concurrencia de stock) |
| 4 | HU-017, HU-019 a HU-021 | ✅ backend implementado y probado (pasarela simulada: falta integrar una real) |

Backend: **253 pruebas** (unitarias + integración contra SQL Server real), **87 % de cobertura de líneas**.
Frontend: Sprint 1 (las pantallas de los Sprints 2 a 4 están pendientes).
