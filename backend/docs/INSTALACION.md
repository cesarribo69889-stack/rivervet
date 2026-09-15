# Instalación desde cero (Windows)

Guía para levantar **base de datos + API + frontend** en una PC con Windows 10/11.
Tiempo estimado la primera vez: 20–30 minutos.

---

## 1. Requisitos

| Programa | Versión | Cómo verificar |
|---|---|---|
| Visual Studio | 2022 17.12+ o 2026, con la carga de trabajo **"Desarrollo de ASP.NET y web"** (instala IIS Express) | Abrir Visual Studio |
| .NET SDK | 10.0 | `dotnet --list-sdks` |
| SQL Server | 2019, 2022 o 2025 (Express o Developer, gratuitos) | servicio "SQL Server (SQLEXPRESS)" en `services.msc` |
| SQL Server Management Studio (SSMS) | 20+ | Abrir SSMS |
| `sqlcmd` | viene con SQL Server | `sqlcmd -?` |
| Node.js | 22 o superior (solo para el frontend) | `node -v` |
| Git | cualquiera reciente | `git --version` |

> **No hace falta habilitar TCP/IP en SQL Server.** El driver de .NET
> (`Microsoft.Data.SqlClient`) se conecta a `.\SQLEXPRESS` por memoria
> compartida cuando la base está en la misma PC.

---

## 2. Autenticación mixta de SQL Server (una sola vez)

La API se conecta con un usuario SQL propio (`rivervet_app`), así que SQL
Server debe aceptar usuarios SQL además de los de Windows.

En SSMS: clic derecho en el servidor → **Propiedades → Seguridad →
"Modo de autenticación de SQL Server y Windows"** → Aceptar → reinicia el
servicio **SQL Server (SQLEXPRESS)** en `services.msc`.

(Si ya estaba así, no hace falta. El script del paso 3 lo comprueba y avisa.)

---

## 3. Base de datos, usuarios y secretos: un solo comando

Desde la carpeta `backend`, en PowerShell:

```powershell
cd backend
.\configurar-desarrollo.ps1
```

> Si PowerShell bloquea el script: `powershell -ExecutionPolicy Bypass -File .\configurar-desarrollo.ps1`.
> Si tu instancia no se llama `SQLEXPRESS`: `.\configurar-desarrollo.ps1 -Servidor "localhost\OTRA"`.

El script:

1. Genera contraseñas aleatorias para dos usuarios de SQL Server, un secreto JWT de 256 bits
   y el secreto de la pasarela de pagos simulada (solo desarrollo).
2. Crea la base `rivervet_db` y los usuarios:

   | Usuario | Para qué | Permisos |
   |---|---|---|
   | `rivervet_migrador` | migraciones y seed | crear/alterar tablas de `rivervet_db` |
   | `rivervet_app` | la API | leer/insertar/actualizar filas. **Sin** DELETE, **sin** DDL, **sin** modificar la auditoría |

3. Guarda las cadenas de conexión y el secreto en los **User Secrets** de .NET
   (`%APPDATA%\Microsoft\UserSecrets\rivervet-dev\secrets.json`). Quedan **fuera
   del repositorio**: nunca se suben a GitHub. En Visual Studio los ves con
   clic derecho en `RiverVet.Api` → **Administrar secretos de usuario**.
4. Aplica las migraciones, da los permisos mínimos a la app y carga los datos de prueba.

Puedes volver a correrlo cuando quieras: es idempotente y regenera las contraseñas.

---

## 4. Ejecutar la API en Visual Studio (IIS Express)

1. Abre **`backend\RiverVet.slnx`** en Visual Studio.
2. En la barra superior, junto al botón verde ▶, elige el perfil **IIS Express**.
3. Pulsa ▶ (o F5).
4. Se abre el navegador en **Swagger** (`https://localhost:44330/swagger`) con
   todos los endpoints.

La API queda escuchando en:

| Dirección | Para qué |
|---|---|
| `http://localhost:3000` | la usa el frontend (proxy de Vite) |
| `https://localhost:44330` | Swagger en el navegador |

> La primera vez, Visual Studio puede preguntar si confías en el certificado
> de IIS Express para HTTPS: responde **Sí**.

### Probar un endpoint protegido en Swagger

1. Abre `POST /api/auth/login` → **Try it out** → body:
   ```json
   { "email": "admin@rivervet.pe", "password": "RiverVet#2026" }
   ```
2. **Execute**, copia el valor de `accessToken`.
3. Arriba a la derecha, **Authorize** → pega el token → **Authorize**.
4. Ahora puedes ejecutar `GET /api/clientes`, `GET /api/usuarios`, etc.

### Sin Visual Studio (línea de comandos)

```powershell
cd backend
dotnet run --project src\RiverVet.Api --launch-profile http
```

Usa Kestrel en `http://localhost:3000` (el mismo código que corre en IIS).

### Probar compras y pagos (Sprints 3 y 4) sin mover dinero

En desarrollo, **Yape** usa un número de demostración (`appsettings.Development.json`)
y la **pasarela de tarjetas es simulada**. `configurar-desarrollo.ps1` la deja
activa con su propio secreto en User Secrets. Si ya habías corrido el script
antes del Sprint 4, activa la pasarela con estos dos comandos (desde `backend`):

```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
dotnet user-secrets set "Pagos:Pasarela:Proveedor" "simulada" --project src\RiverVet.Api\RiverVet.Api.csproj
dotnet user-secrets set "Pagos:Pasarela:SecretoWebhook" (-join ($b | % { $_.ToString('x2') })) --project src\RiverVet.Api\RiverVet.Api.csproj
```

Recorrido completo en Swagger:

1. Como `ana.torres@correo.pe`: `POST /api/carrito/items` y luego `POST /api/pedidos`.
   Anota el `id` y el `total` del pedido.
2. **Tarjeta:** `POST /api/pedidos/{id}/pagos/pasarela` y copia la `referencia`.
   Luego simula el aviso de la pasarela, que llega firmado:
   ```powershell
   .\herramientas\simular-webhook.ps1 -Referencia sim_... -Monto 85.80
   .\herramientas\simular-webhook.ps1 -Referencia sim_... -Monto 85.80 -SecretoFalso   # la API responde 401
   ```
3. **Yape:** en otro pedido, `POST /api/pedidos/{id}/pagos/yape` con un
   `codigoOperacion`. Luego, como `recepcion@rivervet.pe`, usa `GET /api/pagos/pendientes`
   y `POST /api/pagos/{pagoId}/verificar`.
4. Como recepción: `POST /api/pedidos/{id}/comprobante/whatsapp` devuelve un enlace
   `https://wa.me/...` para abrir y enviar.

---

## 5. Pruebas del backend

En Visual Studio: **Prueba → Ejecutar todas las pruebas** (Explorador de pruebas).

En la terminal:

```powershell
cd backend
dotnet test RiverVet.slnx                                        # todas (unitarias + integración)
dotnet test RiverVet.slnx --filter Categoria=Unitaria            # solo unitarias, sin SQL Server
dotnet test RiverVet.slnx /p:CollectCoverage=true /p:Threshold=60 /p:ThresholdType=line "/p:Include=[RiverVet.*]*" "/p:Exclude=[RiverVet.Pruebas]*"
```

> **Si la corrida con cobertura falla con "Una directiva de Control de
> aplicaciones bloqueó este archivo" (0x800711C7):** es el *Control inteligente
> de aplicaciones* de Windows 11. coverlet reescribe las DLL en disco para
> medir, y Windows bloquea esas DLL modificadas (no es un error del código;
> en CI, que corre en Linux, no pasa). En tu PC mide la cobertura con el
> recolector de Microsoft, que instrumenta en memoria sin tocar las DLL:
>
> ```powershell
> dotnet test RiverVet.slnx --collect "Code Coverage;Format=cobertura" --results-directory $env:TEMP\rv-cov
> ```
>
> Genera un `*.cobertura.xml` en esa carpeta (lo abre, por ejemplo, la extensión
> *Coverage Gutters* de VS Code). Después de la falla, `dotnet build` deja las
> DLL normales de nuevo.

Las pruebas de integración crean (y borran) la base **`rivervet_test`** en tu
`.\SQLEXPRESS` usando tu usuario de Windows. Nunca tocan `rivervet_db`. Para
usar otro servidor, define la variable `RIVERVET_PRUEBAS_SQL` con una cadena de
conexión (sin nombre de base).

---

## 6. Frontend

```powershell
cd ..\frontend
npm ci
npm run dev
```

Abre **http://localhost:5173** (no uses `127.0.0.1`: ver [INTEGRACION_LOCAL.md](INTEGRACION_LOCAL.md)).
La API debe estar corriendo (paso 4).

### Usuarios de prueba (contraseña de todos: `RiverVet#2026`)

| Rol | Email |
|---|---|
| Administrador | `admin@rivervet.pe` |
| Veterinaria | `vet.lopez@rivervet.pe` |
| Veterinario | `vet.quispe@rivervet.pe` |
| Recepcionista | `recepcion@rivervet.pe` |
| Cliente (Firulais y Mishi) | `ana.torres@correo.pe` |
| Cliente (Rocky) | `luis.ramirez@correo.pe` |
| Clientes | `maria.flores@correo.pe`, `jorge.castillo@correo.pe`, `lucia.vargas@correo.pe` |

> Estos usuarios **nunca** deben existir en producción: el comando `seed` del
> migrador se niega a ejecutarse en el entorno `Production`.

---

## 7. Herramienta de base de datos (migrador)

```powershell
cd backend
dotnet run --project src\RiverVet.Migrador -- migrar      # aplica migraciones pendientes
dotnet run --project src\RiverVet.Migrador -- verificar   # solo verifica, no cambia nada
dotnet run --project src\RiverVet.Migrador -- seed        # datos de prueba
dotnet run --project src\RiverVet.Migrador -- esquema     # verifica tablas y restricciones críticas
```

Para cambiar el modelo de datos: crea `database\migraciones\0005_descripcion.sql`
y corre `migrar`. **Nunca edites una migración ya aplicada**: el migrador lo
detecta (compara el hash) y se detiene.

---

## 8. Problemas frecuentes

| Síntoma | Causa | Solución |
|---|---|---|
| El script dice "solo acepta autenticación de Windows" | modo mixto apagado | Paso 2 |
| La API no arranca: `Configuración inválida: ConnectionStrings:RiverVet...` | faltan los User Secrets | Ejecuta `configurar-desarrollo.ps1` |
| `Jwt:Secreto: debe tener al menos 64 caracteres` | secreto ausente o corto | Ejecuta `configurar-desarrollo.ps1` |
| `Login failed for user 'rivervet_app'` | se cambió la contraseña a mano | Vuelve a correr `configurar-desarrollo.ps1` |
| `The SELECT permission was denied` | faltó `02_permisos_app.sql` | Vuelve a correr `configurar-desarrollo.ps1` |
| IIS Express: "HTTP Error 500.30 - ASP.NET Core app failed to start" | la API falló al arrancar (casi siempre configuración) | Ventana **Salida → Depurar** de Visual Studio, o `src\RiverVet.Api\logs\` |
| El puerto 3000 está en uso | otra app lo ocupa | Cierra esa app o cambia `applicationUrl` en `Properties\launchSettings.json` y `API_PROXY_DESTINO` en el `.env.local` del frontend |
| `La migración X fue MODIFICADA` | se editó una migración aplicada | Revierte el cambio (`git checkout -- archivo`) y crea una migración nueva |
| Error de CORS o de cookies en el navegador | ver [INTEGRACION_LOCAL.md](INTEGRACION_LOCAL.md) | — |

---

## 9. Producción

Se configura en el **Paso 9** (CD). La API se publica en **Azure App Service
(Windows)**, que usa **IIS** igual que IIS Express en tu PC; la base en
**Azure SQL Database**, y el frontend en **Vercel**. Ver `docs/CICD.md`.
