# Contrato de la API — RiverVet

> **Versión:** Sprints 1 a 4 (HU-001 a HU-021) · **Base URL:** `/api`
>
> Este documento es el acuerdo entre backend y frontend. Si un endpoint cambia,
> **primero se actualiza este archivo** (en el mismo PR) y se avisa al frontend.
> Las pruebas de integración de `tests/RiverVet.Pruebas/Integracion/` verifican cada punto.
>
> **Probarla en vivo:** ejecuta la API en Visual Studio (perfil *IIS Express*) y se
> abre **Swagger** (`/swagger`, solo en desarrollo) con todos los endpoints.
> Para los protegidos: `POST /api/auth/login`, copia el `accessToken`, pulsa
> **Authorize** y pégalo.

---

## 1. Convenciones generales

### 1.1 Forma de las respuestas

Toda respuesta es JSON con una de estas dos formas:

```json
{ "success": true, "data": { } }
```

```json
{
  "success": false,
  "message": "Texto en español, apto para mostrar al usuario",
  "errors": [{ "campo": "dni", "mensaje": "El DNI debe tener 8 dígitos" }],
  "traceId": "3f2a9c1e-7b4d-4e8a-9f1c-2d6b8a0e5c71"
}
```

- `errors` solo trae elementos en los 422 (validación); en el resto es `[]`.
- `traceId` también viaja en la cabecera `X-Trace-Id` de **todas** las respuestas. En un error 500, el frontend debe mostrárselo al usuario: con ese código se encuentra el error completo en el log del servidor.

### 1.2 Códigos HTTP

| Código | Significado | Qué hace el frontend |
|---|---|---|
| 200 | OK | — |
| 201 | Creado | — |
| 400 | JSON mal formado | Error genérico |
| 401 | Sin sesión / sesión vencida / credenciales incorrectas | Intentar `POST /auth/refresh` **una vez**; si falla, ir al login |
| 403 | Autenticado, pero su rol no puede hacer esto (o CSRF inválido) | "No tienes permiso…" |
| 404 | No existe **o no es tuyo** (ver 1.6) | "No se encontró…" |
| 409 | Conflicto: duplicado (DNI, email) o regla de negocio | Mostrar `message` |
| 413 | Body de más de 1 MB | Error genérico |
| 422 | Validación: ver `errors[]` campo por campo | Marcar cada campo |
| 429 | Demasiadas solicitudes | "Espera unos minutos" |
| 500 | Error inesperado | Mensaje genérico + `traceId` |
| 503 | Servicio externo no configurado (Yape, pasarela) o base de datos caída (`/health`) | "Este servicio no está disponible" |

### 1.3 Autenticación

- **Access token** (JWT, 15 min): se envía en cada petición como `Authorization: Bearer <token>`. El frontend lo guarda **solo en memoria**.
- **Refresh token** (7 días): lo pone el servidor en la cookie `rv_refresh` (`HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, `Secure` en producción). JavaScript no puede leerla, y eso es a propósito.
- **Token CSRF**: el servidor pone la cookie `rv_csrf` (legible por JS). En `POST /auth/refresh` y `POST /auth/logout` el frontend debe copiar su valor en la cabecera `X-CSRF-Token`.
- Las peticiones deben ir con credenciales (`withCredentials: true` en axios) para que viajen las cookies.

### 1.4 Tipos de datos

| Tipo | Formato en JSON | Ejemplo |
|---|---|---|
| Fecha (DATE) | texto `AAAA-MM-DD`, **sin zona horaria** | `"2019-03-15"` |
| Fecha y hora (DATETIME2) | ISO 8601 en **UTC** | `"2026-09-12T15:30:00.000Z"` → mostrar en hora de Lima |
| Horario de cita (entrada) | texto `AAAA-MM-DDTHH:mm` en **hora de Lima**, sin zona | `"2026-09-21T10:30"` |
| Dinero | número con 2 decimales, en soles | `189.9` → `S/ 189.90` |
| Booleano | `true` / `false` | |
| DNI | texto de 8 dígitos | `"70123456"` |
| Celular | texto de 9 dígitos que empieza con 9 | `"987654321"` |

Los nombres de campo del JSON están en camelCase (`fechaNacimiento`). En la base de datos están en snake_case (`fecha_nacimiento`).

### 1.5 Paginación

Los listados aceptan `pagina` (desde 1, por defecto 1) y `limite` (1–100, por defecto 10) y responden:

```json
{
  "success": true,
  "data": {
    "items": [],
    "paginacion": { "pagina": 1, "limite": 10, "total": 57, "totalPaginas": 6 }
  }
}
```

**Búsquedas por texto:** van por `POST .../buscar` con el texto en el body, **nunca** en la URL. El texto buscado puede ser un DNI o un nombre, y una URL queda registrada en los logs de proxies y servidores y en el historial del navegador.

### 1.6 Control de propiedad (IDOR)

Si un usuario con rol `cliente` pide un recurso de **otro** cliente (por ejemplo `GET /mascotas/8` cuando la mascota 8 es de otra persona), la API responde **404**, no 403. Responder 403 confirmaría que el id existe. El filtro está dentro de la consulta SQL.

### 1.7 Lista blanca de campos

Todo body se valida en modo estricto: un campo que no esté documentado aquí devuelve **422** con `{ "campo": "<nombre>", "mensaje": "Campo no permitido" }`. Esto evita que alguien agregue, por ejemplo, `"rol": "admin"` a un formulario.

### 1.8 Límites

- Global: 100 solicitudes cada 15 minutos por IP (configurable).
- Login: 5 intentos **fallidos** cada 15 minutos por IP + email.
- Cuenta: se bloquea 15 minutos tras 5 contraseñas incorrectas seguidas. Mientras está bloqueada, el login responde **igual** que con credenciales incorrectas.

### 1.9 Roles

`admin` · `veterinario` · `recepcionista` · `cliente`. "Personal" = los tres primeros.

---

## 2. Salud

### `GET /health` — público

Lo usan Azure App Service y el pipeline de despliegue. No está sujeto al rate limit.

**200**
```json
{ "success": true, "data": { "estado": "ok", "baseDatos": "ok", "fecha": "2026-09-12T15:30:00.000Z" } }
```
**503** si la base de datos no responde (sin detalles técnicos).

---

## 3. Autenticación — HU-001

### `POST /auth/login` — público

**Body**
```json
{ "email": "recepcion@rivervet.pe", "password": "RiverVet#2026" }
```

**200** — además pone las cookies `rv_refresh` y `rv_csrf`.
```json
{
  "success": true,
  "data": {
    "accessToken": "<JWT de 3 partes: cabecera.contenido.firma>",
    "expiraEn": 900,
    "usuario": { "id": 4, "nombre": "Rosa Huamán", "email": "recepcion@rivervet.pe", "rol": "recepcionista" }
  }
}
```

| Error | Cuándo |
|---|---|
| 401 `"Email o contraseña incorrectos"` | Email inexistente, contraseña incorrecta, cuenta bloqueada o desactivada: **siempre el mismo mensaje** |
| 422 | Email con formato inválido, falta un campo o hay campos extra |
| 429 | Superó el límite de intentos |

### `POST /auth/refresh` — cookie `rv_refresh` + cabecera `X-CSRF-Token`

Sin body. Entrega un access token nuevo y **rota** el refresh token: la cookie se reemplaza y la anterior deja de servir. Si alguien reutiliza un refresh token ya rotado, se revoca toda la sesión.

**200** — misma forma que el login.

| Error | Cuándo |
|---|---|
| 401 `"Tu sesión expiró. Vuelve a iniciar sesión."` | Sin cookie, token vencido, revocado o reutilizado. El servidor además borra las cookies |
| 403 | Falta `X-CSRF-Token` o no coincide con la cookie `rv_csrf` |

> El frontend llama a este endpoint (a) al cargar la app, para recuperar la sesión, y (b) cuando una petición recibe 401. Nunca debe haber dos llamadas simultáneas: hay que encolarlas.

### `POST /auth/logout` — cookie + cabecera `X-CSRF-Token`

Sin body. Revoca la sesión en la base de datos y borra las cookies. Responde 200 aunque la sesión ya no existiera.

**200**
```json
{ "success": true, "data": { "mensaje": "Sesión cerrada" } }
```
**403** si el CSRF no es válido.

### `GET /auth/yo` — los 4 roles

**200**
```json
{
  "success": true,
  "data": { "id": 5, "nombre": "Ana Torres", "email": "ana.torres@correo.pe", "rol": "cliente", "clienteId": 1 }
}
```
`clienteId` es `null` para el personal.

### `PUT /auth/password` — los 4 roles

**Body**
```json
{ "passwordActual": "RiverVet#2026", "passwordNueva": "NuevaClave#2026" }
```
Política de `passwordNueva`: 10 a 72 caracteres, con mayúscula, minúscula, número y símbolo, y distinta de la actual.

**200** — cierra **todas** las sesiones del usuario y borra las cookies. El frontend debe mandarlo al login.
```json
{ "success": true, "data": { "mensaje": "Contraseña actualizada. Vuelve a iniciar sesión." } }
```
**422** con `campo: "passwordActual"` si la actual no es correcta, o `campo: "passwordNueva"` si no cumple la política.

---

## 4. Usuarios del personal — HU-001 (alta por admin)

Todos los endpoints son **solo `admin`**.

Objeto `Usuario`:
```json
{
  "id": 2, "nombre": "Dra. Carla López", "email": "vet.lopez@rivervet.pe",
  "rol": "veterinario", "activo": true,
  "ultimoLoginEn": "2026-09-12T14:02:11.000Z", "creadoEn": "2026-09-01T12:00:00.000Z"
}
```

### `GET /usuarios?pagina=1&limite=10&rol=veterinario`
`rol` es opcional. **200**: listado paginado de `Usuario`.

### `POST /usuarios`
```json
{ "nombre": "Dr. Pedro Salas", "email": "pedro.salas@rivervet.pe", "password": "Temporal#2026", "rol": "veterinario" }
```
`rol` ∈ `admin`, `veterinario`, `recepcionista`. Las cuentas `cliente` se crean desde `POST /clientes`.

**201**: `Usuario`. **409** `"Ya existe un usuario con ese email"`. **422** validación.

### `GET /usuarios/:id`
**200**: `Usuario`. **404**.

### `PUT /usuarios/:id`
```json
{ "nombre": "Dr. Pedro Salas", "rol": "veterinario", "activo": false }
```
Los tres campos son obligatorios. Al desactivar a alguien se cierran todas sus sesiones y pierde el acceso en su siguiente petición.

**200**: `Usuario`. **404**. **409** si el admin intenta quitarse su propio rol o desactivarse. **422** si se intenta asignar o quitar el rol `cliente`.

---

## 5. Clientes — HU-002 y HU-003

Objeto `Cliente`:
```json
{
  "id": 1, "dni": "70123456", "nombres": "Ana", "apellidos": "Torres Medina",
  "email": "ana.torres@correo.pe", "telefono": "987654321",
  "direccion": "Av. Arequipa 1234, Lince",
  "consentimientoDatos": true, "consentimientoEn": "2026-09-01T12:00:00.000Z",
  "tieneCuenta": true,
  "creadoEn": "2026-09-01T12:00:00.000Z", "actualizadoEn": "2026-09-01T12:00:00.000Z"
}
```
En los listados cada cliente trae además `totalMascotas` (número).

### `GET /clientes?pagina=1&limite=10` — personal
**200**: listado paginado de `Cliente`, ordenado por apellidos.

### `POST /clientes/buscar` — personal
```json
{ "texto": "70123456", "pagina": 1, "limite": 10 }
```
`texto` es opcional (sin texto = listado completo). Busca en nombres, apellidos, DNI, email y teléfono. **200**: listado paginado.

### `GET /clientes/yo` — cliente
**200**: el `Cliente` asociado a la cuenta que hace la petición. **404** si no hay ficha.

### `POST /clientes` — admin, recepcionista
```json
{
  "dni": "75896321",
  "nombres": "Carmen Rosa",
  "apellidos": "Quispe Mamani",
  "telefono": "987111222",
  "email": "carmen.quispe@correo.pe",
  "direccion": "Av. Túpac Amaru 123, Comas",
  "consentimientoDatos": true,
  "crearCuenta": true,
  "password": "Bienvenida#2026"
}
```

| Campo | Regla |
|---|---|
| `dni` | obligatorio, 8 dígitos, único entre clientes activos |
| `nombres`, `apellidos` | obligatorios, máx. 100, solo letras, espacios, `'`, `.` y `-` |
| `telefono` | obligatorio, 9 dígitos, empieza con 9 |
| `email` | opcional (obligatorio si `crearCuenta`), único |
| `direccion` | opcional, máx. 250. Se eliminan etiquetas HTML |
| `consentimientoDatos` | **debe ser `true`** (Ley N° 29733) |
| `crearCuenta` | opcional, por defecto `false`. Si es `true`, crea un usuario de rol `cliente` |
| `password` | obligatorio si `crearCuenta`; misma política que en 3 |

La ficha y la cuenta se crean en **una sola transacción**: si falla una, no se guarda ninguna.

**201**: `Cliente`. **409** `"Ya existe un cliente con ese DNI"` / `"Ya existe un cliente con ese email"` / `"Ya existe una cuenta de usuario con ese email"`. **422** validación.

### `GET /clientes/:id` — personal
**200**: `Cliente`. **404**. **422** si `:id` no es numérico.

### `PUT /clientes/:id` — admin, recepcionista
Body: `dni`, `nombres`, `apellidos`, `telefono`, `email`, `direccion` (mismas reglas; `consentimientoDatos`, `crearCuenta` y `password` **no** se aceptan). Si el cliente tiene cuenta web, `email` es obligatorio y también cambia su email de inicio de sesión.

**200**: `Cliente`. **404**. **409**. **422**.

### `DELETE /clientes/:id` — admin
Baja **lógica** del cliente y sus mascotas. Si tenía cuenta web, se desactiva y se cierran sus sesiones.

**200** `{ "success": true, "data": { "mensaje": "Cliente eliminado" } }`. **404**.

### `GET /clientes/:id/mascotas?pagina=1&limite=10` — los 4 roles — HU-005
Mascotas de un cliente. Un `cliente` solo puede consultar **su propio** id; con otro id recibe **404**.

**200**: listado paginado de `Mascota`. **404**.

---

## 6. Mascotas — HU-004 y HU-005

Objeto `Mascota`:
```json
{
  "id": 1, "clienteId": 1, "clienteNombre": "Ana Torres Medina",
  "nombre": "Firulais", "especie": "perro", "raza": "Mestizo", "sexo": "macho",
  "fechaNacimiento": "2019-03-15", "pesoKg": 18.5, "color": "Marrón",
  "senasParticulares": null, "esterilizado": true,
  "creadoEn": "2026-09-01T12:00:00.000Z", "actualizadoEn": "2026-09-01T12:00:00.000Z"
}
```

### `GET /mascotas?pagina=1&limite=10` — los 4 roles
Personal: todas. Cliente: **solo las suyas**. **200**: listado paginado.

### `POST /mascotas/buscar` — los 4 roles
```json
{ "texto": "Firulais", "pagina": 1, "limite": 10 }
```
Busca por nombre de la mascota y por nombres, apellidos o DNI del dueño. Cliente: solo entre las suyas.

### `POST /mascotas` — admin, recepcionista
```json
{
  "clienteId": 1,
  "nombre": "Canela",
  "especie": "perro",
  "raza": "Chihuahua",
  "sexo": "hembra",
  "fechaNacimiento": "2021-06-15",
  "pesoKg": 2.35,
  "color": "Beige",
  "senasParticulares": "Mancha blanca en el pecho",
  "esterilizado": false
}
```

| Campo | Regla |
|---|---|
| `clienteId` | obligatorio, cliente existente |
| `nombre` | obligatorio, máx. 60, solo letras |
| `especie` | obligatorio: `perro`, `gato`, `ave`, `conejo`, `roedor`, `reptil`, `otro` |
| `raza` | opcional, máx. 60 |
| `sexo` | opcional: `macho`, `hembra` |
| `fechaNacimiento` | opcional, `AAAA-MM-DD`, no futura |
| `pesoKg` | opcional, número > 0, máx. 999.99, 2 decimales |
| `color` | opcional, máx. 40 |
| `senasParticulares` | opcional, máx. 250. Se eliminan etiquetas HTML |
| `esterilizado` | opcional, booleano |

**201**: `Mascota`. **422** (incluye `campo: "clienteId"` si el cliente no existe).

### `GET /mascotas/:id` — los 4 roles
**200**: `Mascota`. **404** si no existe o si es de otro cliente.

### `PUT /mascotas/:id` — admin, recepcionista, veterinario
Mismos campos que el POST **excepto `clienteId`**: el dueño no se cambia editando. **200**: `Mascota`. **404**. **422**.

### `DELETE /mascotas/:id` — admin, recepcionista
Baja lógica. **200** `{ "success": true, "data": { "mensaje": "Mascota eliminada" } }`. **404**.

---

## 7. Auditoría

### `GET /auditoria?pagina=1&limite=20&accion=LOGIN_FALLIDO&recurso=cliente&usuarioId=3` — admin

Filtros opcionales. `accion` ∈ `LOGIN`, `LOGIN_FALLIDO`, `CUENTA_BLOQUEADA`, `LOGOUT`, `REUSO_REFRESH_TOKEN`, `CAMBIO_PASSWORD`, `CREAR`, `ACTUALIZAR`, `ELIMINAR`, `CANCELAR`. `recurso` ∈ `usuario`, `cliente`, `mascota`, `sesion`, `cita`, `historial`, `vacuna`.

**200**: listado paginado de:
```json
{
  "id": 120, "usuarioId": 4, "usuarioNombre": "Rosa Huamán", "usuarioRol": "recepcionista",
  "accion": "ACTUALIZAR", "recurso": "cliente", "recursoId": "7",
  "detalle": { "campos": ["telefono", "direccion"] },
  "ip": "190.12.34.56", "creadoEn": "2026-09-12T15:30:00.123Z"
}
```
La auditoría no contiene contraseñas, tokens ni valores de datos personales: guarda **qué campos** cambiaron, no sus valores.

---

## 8. Citas — HU-008, HU-009 y HU-010

### 8.1 Horario de atención

- Lunes a sábado de **08:00 a 20:00** (hora de Lima). Domingo cerrado.
- Bloques fijos de **30 minutos** que empiezan en `:00` o `:30`. La última cita del día es a las 19:30.
- Solo a futuro y con hasta **180 días** de anticipación.
- Un veterinario no puede tener dos citas en el mismo bloque, y una mascota tampoco. **Lo garantiza la base de datos** (índice único): si dos recepcionistas reservan el mismo horario a la vez, una recibe 201 y la otra 409.

**Zona horaria:** al **enviar** un horario se usa texto en hora de Lima (`"2026-09-21T10:30"`, sin `Z`). Al **recibir**, cada cita trae `fechaHora` en UTC y `fechaHoraLima` ya convertida. Para mostrarla, usa `fechaHoraLima` o convierte `fechaHora` con `America/Lima`, **no** con la zona del navegador.

Objeto `Cita`:
```json
{
  "id": 12, "mascotaId": 1, "mascotaNombre": "Firulais", "mascotaEspecie": "perro",
  "clienteId": 1, "clienteNombre": "Ana Torres Medina", "clienteTelefono": "987654321",
  "veterinarioId": 2, "veterinarioNombre": "Dra. Carla López",
  "fechaHora": "2026-09-21T15:30:00.000Z", "fechaHoraLima": "2026-09-21T10:30",
  "motivo": "Control anual", "estado": "programada", "motivoCancelacion": null,
  "creadoEn": "2026-09-13T16:00:00.000Z"
}
```
`estado` ∈ `programada`, `atendida`, `cancelada`, `no_asistio`.

### `GET /citas?desde=2026-09-21&hasta=2026-09-27&veterinarioId=2&estado=programada` — los 4 roles
Calendario. Todos los filtros son opcionales:

| Filtro | Regla |
|---|---|
| `desde`, `hasta` | `AAAA-MM-DD` (días de Lima, ambos incluidos). Sin `desde` = hoy; sin `hasta` = `desde` + 30 días. Rango máximo 93 días |
| `veterinarioId` | solo las de ese veterinario (p. ej. "mis citas" del veterinario con su propio id) |
| `mascotaId` | solo las de esa mascota. Si además no vienen fechas: su último año y todo lo agendado |
| `estado` | uno de los 4 estados |

Un `cliente` recibe **solo las citas de sus mascotas**. **200**: arreglo de `Cita` ordenado por fecha (no paginado: el rango ya lo acota). **422** si un filtro es inválido.

### `GET /citas/mascota/:mascotaId` y `GET /citas/veterinario/:veterinarioId` — los 4 roles
Atajos del contrato del Sprint 2: equivalen a `GET /citas?mascotaId=` y `GET /citas?veterinarioId=` y aceptan los mismos filtros. Un `cliente` solo ve las de sus mascotas: con una ajena recibe un arreglo vacío.

### `GET /citas/disponibilidad?veterinarioId=2&fecha=2026-09-21` — personal
Bloques de un día para un veterinario.

**200**
```json
{
  "success": true,
  "data": {
    "fecha": "2026-09-21", "atiende": true,
    "horarios": [
      { "hora": "08:00", "fechaHoraLima": "2026-09-21T08:00", "libre": true },
      { "hora": "08:30", "fechaHoraLima": "2026-09-21T08:30", "libre": false }
    ]
  }
}
```
Siempre son 24 bloques (08:00 a 19:30). `libre` es `false` si el bloque ya pasó o si tiene una cita que no esté cancelada (`programada`, `atendida` o `no_asistio`). Un domingo responde `"atiende": false` con `horarios: []`. **422** si falta `veterinarioId` o `fecha`.

> La disponibilidad es solo una ayuda visual. Entre que se consulta y se agenda, otra persona puede tomar el bloque: el frontend debe manejar el **409** de `POST /citas`.

### `GET /citas/:id` — los 4 roles
**200**: `Cita`. **404** si no existe o si es de otro cliente.

### `POST /citas` — admin, recepcionista
```json
{ "mascotaId": 1, "veterinarioId": 2, "fechaHora": "2026-09-21T10:30", "motivo": "Control anual" }
```

| Campo | Regla |
|---|---|
| `mascotaId` | obligatorio, mascota existente |
| `veterinarioId` | obligatorio, usuario veterinario **activo** |
| `fechaHora` | obligatorio, hora de Lima, dentro del horario de 8.1 |
| `motivo` | obligatorio, máx. 250. Se eliminan etiquetas HTML |

**201**: `Cita` en estado `programada`.

| Error | Cuándo |
|---|---|
| 409 `"El veterinario ya tiene una cita en ese horario. Elige otro."` | bloque ocupado |
| 409 `"La mascota ya tiene otra cita en ese horario."` | la mascota ya tiene cita a esa hora con otro veterinario |
| 422 `campo: "fechaHora"` | formato, minuto distinto de `:00`/`:30`, fuera de horario, en el pasado o a más de 180 días |
| 422 `campo: "mascotaId"` / `"veterinarioId"` | no existe, o el veterinario está inactivo |

### `PUT /citas/:id` — admin, recepcionista (reprogramar)
```json
{ "veterinarioId": 3, "fechaHora": "2026-09-22T16:00", "motivo": "Control anual" }
```
Los tres campos son obligatorios (mismas reglas que el POST). La mascota **no** se cambia: para otra mascota se cancela la cita y se agenda una nueva.

**200**: `Cita`. **404**. **409** si la cita no está `programada` o el nuevo horario está ocupado. **422**.

### `POST /citas/:id/cancelar` — admin, recepcionista, cliente
```json
{ "motivo": "El cliente viaja esa semana" }
```
`motivo`: obligatorio, máx. 250. Un `cliente` solo puede cancelar las citas de **sus** mascotas: con una ajena recibe **404**.

**200**: `Cita` en estado `cancelada`, con `motivoCancelacion`. El bloque queda libre de inmediato.

| Error | Cuándo |
|---|---|
| 409 `"No se puede cancelar una cita ya atendida"` | estado `atendida` |
| 409 `"La cita ya está cancelada"` | estado `cancelada` |
| 409 `"La cita ya pasó: no se puede cancelar"` | la hora de la cita ya pasó |

### `POST /citas/:id/no-asistio` — admin, recepcionista
Cierra como **inasistencia** una cita `programada` cuya hora ya pasó (sin cuerpo). La otra forma de cerrarla es registrar su atención en el historial con `citaId`, que la deja `atendida`.

**200**: `Cita` en estado `no_asistio`. **409** si no está `programada` o si su hora aún no llega. **403** para veterinario y cliente.

### `GET /veterinarios` — personal
Veterinarios activos, para el selector al agendar. **200**:
```json
{ "success": true, "data": [{ "id": 2, "nombre": "Dra. Carla López" }, { "id": 3, "nombre": "Dr. Jorge Ríos" }] }
```

---

## 9. Historial clínico y vacunas — HU-006, HU-007 y HU-011

Una atención o una vacuna **solo la corrige o la elimina el veterinario que la registró**: es su acto médico (otro veterinario o el admin reciben **403**). Nunca se pierde el rastro: la corrección deja `actualizadoEn` y `editado: true`, la eliminación es **lógica** (la fila queda con `eliminado = 1`) y la auditoría guarda el contenido anterior en ambos casos.

En todos los endpoints, `:id` es la mascota. Un `cliente` solo accede a **sus** mascotas: con una ajena recibe **404**. Una atención o vacuna que exista pero sea de otra mascota también responde **404**.

Objeto `Atencion`:
```json
{
  "id": 30, "mascotaId": 1, "veterinarioId": 2, "veterinarioNombre": "Dra. Carla López",
  "citaId": 12, "fechaAtencion": "2026-09-21T15:42:10.000Z",
  "motivo": "Control anual", "diagnostico": "Sano. Sarro leve.", "tratamiento": "Limpieza dental en 3 meses",
  "observaciones": null, "pesoKg": 18.7, "temperaturaC": 38.5,
  "creadoEn": "2026-09-21T15:42:10.000Z", "actualizadoEn": "2026-09-21T15:42:10.000Z", "editado": false
}
```

### `GET /mascotas/:id/historial?orden=asc` — los 4 roles — HU-007
`orden`: `asc` (más antigua primero, por defecto) o `desc` (más reciente primero). **200**: arreglo de `Atencion`. **404**. **422** si `orden` es otro valor.

### `GET /mascotas/:id/historial/:atencionId` — los 4 roles
**200**: `Atencion`. **404**.

### `PUT /mascotas/:id/historial/:atencionId` — el veterinario que la registró
Mismo cuerpo y reglas que el `POST`, **sin** `citaId` (la cita atendida no cambia). **200**: `Atencion` con `editado: true`. **403** si es otro usuario. **404**. **422**.

### `DELETE /mascotas/:id/historial/:atencionId` — el veterinario que la registró
Borrado lógico. **200** `{ "mensaje": "Atención eliminada" }`. **403**. **404**.

### `POST /mascotas/:id/historial` — veterinario — HU-006
```json
{
  "citaId": 12,
  "motivo": "Control anual",
  "diagnostico": "Sano. Sarro leve.",
  "tratamiento": "Limpieza dental en 3 meses",
  "observaciones": "Muy tranquilo durante el examen",
  "pesoKg": 18.7,
  "temperaturaC": 38.5
}
```

| Campo | Regla |
|---|---|
| `citaId` | opcional. Si viene, la cita debe ser **de esta mascota** y estar `programada`; pasa a `atendida` en la misma transacción |
| `motivo` | obligatorio, máx. 250 |
| `diagnostico`, `tratamiento` | obligatorios, máx. 1000 |
| `observaciones` | opcional, máx. 2000 |
| `pesoKg` | opcional, > 0, máx. 999.99, 2 decimales. Si viene, **actualiza el peso de la mascota** |
| `temperaturaC` | opcional, 30 a 45, 1 decimal |

En todo el texto se eliminan las etiquetas HTML. El veterinario que atiende es el de la sesión: no se envía.

**201**: `Atencion`.

| Error | Cuándo |
|---|---|
| 404 | la mascota no existe |
| 409 `"La cita ya fue atendida o cancelada"` | la cita no está `programada` |
| 409 `"Esta cita ya tiene una atención registrada"` | dos registros simultáneos para la misma cita |
| 422 `campo: "citaId"` | la cita no existe o es de otra mascota |

### `GET /mascotas/:id/vacunas` — los 4 roles
**200**: arreglo de `Vacuna`, la más reciente primero:
```json
{
  "id": 8, "mascotaId": 1, "veterinarioId": 2, "veterinarioNombre": "Dra. Carla López",
  "tipoVacuna": "Antirrábica", "fechaAplicacion": "2026-09-21", "lote": "RB-2026-114",
  "proximaDosis": "2027-09-21", "observaciones": null, "creadoEn": "2026-09-21T15:50:00.000Z"
}
```

### `POST /mascotas/:id/vacunas` — veterinario — HU-011
```json
{
  "tipoVacuna": "Antirrábica",
  "fechaAplicacion": "2026-09-21",
  "lote": "rb-2026-114",
  "proximaDosis": "2027-09-21",
  "observaciones": "Sin reacción"
}
```

| Campo | Regla |
|---|---|
| `tipoVacuna` | obligatorio, máx. 100 |
| `fechaAplicacion` | obligatorio, `AAAA-MM-DD`, **no futura** |
| `lote` | obligatorio (trazabilidad), máx. 50, letras, números, `-`, `/` y `.`. Se guarda en mayúsculas |
| `proximaDosis` | opcional, `AAAA-MM-DD`, posterior a la aplicación y a máx. 5 años. Alimenta los recordatorios |
| `observaciones` | opcional, máx. 500 |

**201**: `Vacuna`. **404**. **422**.

### `GET /mascotas/:id/vacunas/:vacunaId` — los 4 roles
**200**: `Vacuna`. **404**.

### `PUT /mascotas/:id/vacunas/:vacunaId` — el veterinario que la registró
Mismo cuerpo y reglas que el `POST`. **200**: `Vacuna`. **403**. **404**. **422**.

### `DELETE /mascotas/:id/vacunas/:vacunaId` — el veterinario que la registró
Borrado lógico: deja de listarse y de generar recordatorios. **200** `{ "mensaje": "Vacuna eliminada" }`. **403**. **404**.

---

## 10. Recordatorios — HU-012

### `GET /recordatorios?dias=7` — los 4 roles
Vacunas por vencer y citas programadas en los próximos `dias` días (1–60, por defecto 7). El personal ve todos, para llamar a los clientes. Un `cliente` ve solo los de sus mascotas.

**200**
```json
{
  "success": true,
  "data": {
    "dias": 7,
    "vacunas": [
      {
        "vacunaId": 8, "mascotaId": 1, "mascotaNombre": "Firulais",
        "clienteId": 1, "clienteNombre": "Ana Torres Medina", "clienteTelefono": "987654321",
        "tipoVacuna": "Antirrábica", "proximaDosis": "2026-09-18", "diasRestantes": 5
      }
    ],
    "citas": [
      {
        "citaId": 12, "mascotaId": 1, "mascotaNombre": "Firulais",
        "clienteNombre": "Ana Torres Medina", "clienteTelefono": "987654321",
        "veterinarioNombre": "Dra. Carla López",
        "fechaHora": "2026-09-14T15:30:00.000Z", "fechaHoraLima": "2026-09-14T10:30"
      }
    ]
  }
}
```
De cada tipo de vacuna solo se avisa la **última** aplicación: si ya se puso una dosis más nueva, la anterior deja de aparecer. `diasRestantes` = 0 significa "vence hoy". **422** si `dias` está fuera de rango.

---

## 11. Productos y categorías — HU-013 y HU-014

Objeto `ProductoCatalogo` (lo que ve el **público**, sin el stock exacto):
```json
{
  "id": 7, "sku": "HIG-002", "nombre": "Arena sanitaria 10 kg",
  "descripcion": "Arena aglomerante con control de olores.", "precio": 42.9,
  "categoriaId": 3, "categoriaNombre": "Higiene", "imagenUrl": null, "disponible": true
}
```

Objeto `ProductoInventario` (admin y recepción): lo anterior **más** `stock`, `stockMinimo`, `stockBajo` (`stock <= stockMinimo`), `activo`, `creadoEn` y `actualizadoEn`.

### `GET /productos?pagina=1&limite=12&texto=croquetas&categoriaId=1&precioMin=10&precioMax=200&soloDisponibles=true&orden=precio_asc` — PÚBLICO
Catálogo paginado. Solo trae productos activos de categorías activas. Todos los filtros son opcionales:

| Filtro | Regla |
|---|---|
| `texto` | máx. 100. Busca en nombre, descripción y SKU. Los comodines (`%`, `_`) se buscan literalmente |
| `categoriaId` | id de la categoría |
| `precioMin`, `precioMax` | en soles; `precioMax >= precioMin` |
| `soloDisponibles` | `true` oculta los agotados |
| `orden` | `nombre` (por defecto), `precio_asc`, `precio_desc`, `recientes` |

> A diferencia de las búsquedas de personas (§1.5), aquí el texto va en la URL. Buscar "croquetas" no es un dato personal, y así un enlace al catálogo filtrado se puede compartir.

**200**: listado paginado de `ProductoCatalogo`. **422** si un filtro es inválido.

### `GET /productos/:id` — PÚBLICO
**200**: `ProductoCatalogo`. **404** si no existe, está inactivo o fue eliminado.

### `GET /productos/inventario?pagina=1&limite=20&texto=&categoriaId=&soloStockBajo=true&incluirInactivos=true` — admin, recepcionista
**200**: listado paginado de `ProductoInventario`.

### `GET /productos/:id/inventario` — admin, recepcionista
**200**: `ProductoInventario` (también de los inactivos). **404**.

### `POST /productos` — admin
```json
{
  "categoriaId": 1, "sku": "ali-004", "nombre": "Croquetas cachorro 3 kg",
  "descripcion": "Para cachorros de raza pequeña", "precio": 58.9,
  "stock": 20, "stockMinimo": 5, "imagenUrl": "https://cdn.rivervet.pe/ali-004.jpg", "activo": true
}
```

| Campo | Regla |
|---|---|
| `categoriaId` | obligatorio; categoría existente y activa |
| `sku` | obligatorio, de 3 a 30 caracteres: letras, números y guiones. Se guarda en MAYÚSCULAS. Único |
| `nombre` | obligatorio, máx. 120 |
| `descripcion` | opcional, máx. 1000 |
| `precio` | obligatorio, > 0, máx. 99 999.99, 2 decimales |
| `stock` | obligatorio **solo al crear**: stock inicial, de 0 a 100 000 |
| `stockMinimo` | obligatorio, de 0 a 10 000. Por debajo de este valor, el producto aparece con stock bajo |
| `imagenUrl` | opcional, **solo `https://`**, máx. 500 |
| `activo` | opcional, por defecto `true` |

Del nombre y la descripción se eliminan las etiquetas HTML. **201**: `ProductoInventario`. **409** `"Ya existe un producto con ese SKU"`. **422** (incluye `campo: "categoriaId"`).

### `PUT /productos/:id` — admin
Mismos campos que el POST, **sin `stock`**; aquí `activo` es obligatorio. Si se envía `stock`, responde **422** `"Campo no permitido"`.

> **Por qué el stock no se edita aquí:** si el admin escribiera el stock absoluto ("ahora hay 15"), podría borrar una venta hecha mientras editaba. Leyó 10, alguien compró 1, y su "15" se come esa venta. Por eso el stock solo cambia con ajustes **relativos**, en `POST /productos/:id/stock`.

**200**: `ProductoInventario`. **404**. **409**. **422**.

### `POST /productos/:id/stock` — admin
```json
{ "cantidad": 6, "motivo": "Llegó el pedido del proveedor" }
```
`cantidad` es un entero distinto de 0, entre −10 000 y 10 000: **+** repone, **−** descuenta mermas. `motivo` es obligatorio (máx. 250) y queda en la auditoría (`AJUSTE_STOCK`).

**200** `{ "productoId": 7, "stockAnterior": 4, "stockActual": 10 }`. **409** `"El stock no puede quedar negativo (hay N unidades)"`. **404**.

### `DELETE /productos/:id` — admin
Baja lógica: también desactiva el producto. Los pedidos antiguos conservan su detalle. **200** `{ "mensaje": "Producto eliminado" }`. **404**.

### Categorías

| Método y ruta | Roles | Qué hace |
|---|---|---|
| `GET /categorias` | PÚBLICO | Las activas: `[{ "id": 1, "nombre": "Alimentos", "activo": true, "totalProductos": 3 }]` |
| `GET /categorias/todas` | admin | También las inactivas |
| `POST /categorias` | admin | `{ "nombre": "Snacks" }` → **201**; **409** `"Ya existe una categoría con ese nombre"` |
| `PUT /categorias/:id` | admin | `{ "nombre": "Snacks", "activo": false }` → **200**. Una categoría inactiva desaparece del catálogo junto con sus productos |

---

## 12. Carrito — HU-015

Solo para el rol **cliente**. Cada usuario tiene **un** carrito y solo puede tocar el suyo: la ruta no lleva id de carrito, porque se obtiene de la sesión.

El carrito **no guarda precios ni reserva stock**. Muestra el precio actual y avisa si algo se agotó, pero la verificación que cuenta es la que se hace al confirmar el pedido (§13).

Objeto `Carrito`:
```json
{
  "items": [
    {
      "productoId": 7, "sku": "HIG-002", "nombre": "Arena sanitaria 10 kg", "imagenUrl": null,
      "precioUnitario": 42.9, "cantidad": 2, "subtotal": 85.8, "advertencia": null
    }
  ],
  "cantidadProductos": 2, "total": 85.8, "listoParaConfirmar": true
}
```
`advertencia` ∈ `null`, `"Agotado"`, `"Solo quedan N unidades"`, `"Este producto ya no está disponible"`. Si alguna línea tiene advertencia, `listoParaConfirmar` es `false`.

| Método y ruta | Body | Respuesta |
|---|---|---|
| `GET /carrito` | — | **200** `Carrito` |
| `POST /carrito/items` | `{ "productoId": 7, "cantidad": 1 }` | **200** `Carrito`. Si el producto ya estaba, **suma** la cantidad |
| `PUT /carrito/items/:productoId` | `{ "cantidad": 3 }` | **200** `Carrito`. **404** si no está en el carrito |
| `DELETE /carrito/items/:productoId` | — | **200** `Carrito`. **404** si no está en el carrito |
| `DELETE /carrito` | — | **200** `Carrito` vacío |

`cantidad`: de 1 a 99 por producto.

| Error | Cuándo |
|---|---|
| **422** `campo: "productoId"` | el producto no existe o no está a la venta |
| **422** `campo: "cantidad"` | cantidad fuera de rango |
| **409** `"X está agotado"` / `"Solo quedan N unidades de X"` | no hay stock suficiente |

---

## 13. Pedidos y ventas — HU-016

### 13.1 Estados de un pedido

```
pendiente_pago ──(registra Yape)──► pendiente_verificacion ──(recepción confirma)──► pagado ──► entregado
     │   ▲                                  │
     │   └─────────(recepción rechaza)──────┘
     ├──(pasarela aprobada o efectivo)──► pagado
     └──(cancelación)──► cancelado        (el stock vuelve al inventario)
```

### 13.2 Garantías

- **El precio sale de la base de datos**, en el mismo momento en que se descuenta el stock. Ninguna petición lleva precios: si se agrega un campo `precio` o `precioUnitario`, la respuesta es **422**.
- **El stock nunca queda negativo, aunque haya compras simultáneas.** Cada producto se descuenta con una sola sentencia: `UPDATE ... SET stock = stock - @cantidad WHERE stock >= @cantidad`. Si 10 personas compran a la vez la última unidad, 1 recibe **201** y 9 reciben **409**. Lo verifica una prueba automática.
- **Todo o nada.** Si una línea no tiene stock, no se descuenta ninguna y no se crea el pedido.
- `pedido_items.precioUnitario` conserva el precio cobrado aunque el producto cambie de precio después.

Objeto `Pedido`:
```json
{
  "id": 12, "codigo": "RV-000012", "clienteId": 1, "clienteNombre": "Ana Torres Medina",
  "canal": "web", "estado": "pendiente_pago", "total": 85.8,
  "registradoPorNombre": null,
  "items": [
    { "productoId": 7, "sku": "HIG-002", "nombre": "Arena sanitaria 10 kg", "cantidad": 2, "precioUnitario": 42.9, "subtotal": 85.8 }
  ],
  "pagos": [],
  "creadoEn": "2026-09-14T15:30:00.000Z", "actualizadoEn": "2026-09-14T15:30:00.000Z"
}
```
- `codigo` es el número que se muestra al cliente, en lugar del id interno.
- `canal` ∈ `web`, `presencial`.
- `registradoPorNombre` es la recepcionista que registró una venta presencial.
- `pagos`: ver §15.

### `POST /pedidos` — cliente
**Sin body.** Convierte **su** carrito en un pedido y lo vacía, todo en una sola transacción.

**201**: `Pedido` en `pendiente_pago`.

**409** con uno de estos mensajes:
- `"Tu carrito está vacío"`
- `"X está agotado"`
- `"Stock insuficiente de X: quedan N unidades"`
- `"X ya no está a la venta"`
- `"Tu cuenta no tiene una ficha de cliente..."`

### `POST /pedidos/presencial` — admin, recepcionista
```json
{ "clienteId": 1, "items": [ { "productoId": 7, "cantidad": 2 }, { "productoId": 3, "cantidad": 1 } ] }
```
`items` lleva de 1 a 50 productos **distintos** (sin repetir `productoId`), cada uno con `cantidad` de 1 a 99. Los errores de una línea llegan como `campo: "items[0].cantidad"`.

**201**: `Pedido` con `canal: "presencial"`. **409** por falta de stock. **422** `campo: "clienteId"` si el cliente no existe.

### `GET /pedidos?pagina=1&limite=10&estado=pagado&canal=web&desde=2026-09-01&hasta=2026-09-30&clienteId=1` — admin, recepcionista, cliente
Filtros opcionales; `desde` y `hasta` son días de Lima. Un `cliente` recibe **solo sus pedidos**, y `clienteId` no le sirve para ver los de otros. **200**: listado paginado de pedidos **sin** `items` ni `pagos`, con `cantidadItems`.

### `GET /pedidos/:id` — admin, recepcionista, cliente
**200**: `Pedido` completo. **404** si no existe **o si es de otro cliente**.

### `POST /pedidos/:id/cancelar` — admin, recepcionista, cliente
```json
{ "motivo": "Me equivoqué de producto" }
```
Devuelve el stock y marca como `rechazado` cualquier pago pendiente del pedido.

| Quién | Puede cancelar |
|---|---|
| Cliente | solo sus pedidos, y solo en `pendiente_pago` |
| Recepción y admin | también los que están en `pendiente_verificacion` |

**200**: `Pedido` cancelado. **404**.

**409** con uno de estos mensajes:
- `"El pedido ya está cancelado"`
- `"Un pedido pagado no se puede cancelar desde el sistema"`
- `"Tu pago está en verificación..."`

### `POST /pedidos/:id/entregar` — admin, recepcionista
Sin body. **200**: `Pedido` en `entregado`. **409** `"Solo se entregan pedidos pagados"`.

---

## 14. Indicadores — HU-018

### `GET /indicadores?desde=2026-09-01&hasta=2026-09-30` — admin
Periodo en días de Lima, de máximo 366 días. Sin fechas: del día 1 del mes actual hasta hoy.

**200**
```json
{
  "success": true,
  "data": {
    "desde": "2026-09-01", "hasta": "2026-09-14",
    "ventas": { "total": 1289.7, "pedidos": 21, "ticketPromedio": 61.41, "totalWeb": 540.2, "totalPresencial": 749.5, "pendientesDeCobro": 3 },
    "ventasPorDia": [ { "fecha": "2026-09-13", "total": 128.7, "pedidos": 2 } ],
    "citas": { "atendidas": 34, "programadas": 12, "canceladas": 4, "noAsistio": 1 },
    "clientesNuevos": 6,
    "productosStockBajo": [ { "id": 3, "sku": "ALI-003", "nombre": "Snack dental para perro", "stock": 3, "stockMinimo": 5 } ],
    "productosMasVendidos": [ { "productoId": 1, "nombre": "Croquetas perro adulto 15 kg", "cantidad": 9, "monto": 1709.1 } ]
  }
}
```
- "Ventas" son los pedidos **pagados o entregados** creados en el periodo.
- `productosStockBajo`: hasta 20 productos, los de menor stock primero. No depende del periodo.
- `productosMasVendidos`: los 5 con más unidades vendidas.

**403** para los otros roles. **422** si el periodo es inválido.

### Tablero del personal — `/dashboard` (Sprint 2) — admin, recepcionista, veterinario
Lo que se mira a diario en recepción y en consulta. Las ventas no están aquí: siguen en `/indicadores`, solo admin.

#### `GET /dashboard/stats`
```json
{
  "success": true,
  "data": {
    "fecha": "2026-10-06",
    "citasHoy": 9, "citasHoyAtendidas": 4,
    "citasPendientes": 23, "citasSinCerrar": 1,
    "citasAtendidasMes": 18, "clientesNuevosMes": 5,
    "vacunasPorVencer": 7, "vacunasUrgentes": 2
  }
}
```
- `citasHoy`: las de hoy que no se cancelaron. `citasPendientes`: programadas desde ahora.
- `citasSinCerrar`: programadas cuya hora ya pasó; falta registrar su atención o la inasistencia.
- `vacunasPorVencer` / `vacunasUrgentes`: próxima dosis en 30 / 7 días, contando solo la última aplicación de cada tipo (igual que los recordatorios).

#### `GET /dashboard/citas-hoy` y `GET /dashboard/citas-semana`
Arreglo de `Cita` de hoy o de lunes a domingo de esta semana; equivalen a `GET /citas` con esas fechas.

**403** para el cliente.

### Colección de Postman
`docs/RiverVet.postman_collection.json`, con los entornos `RiverVet.local` y `RiverVet.produccion`. Se regenera con `node herramientas/generar-coleccion-postman.mjs` después de agregar un endpoint.

---

## 15. Pagos — HU-017 y HU-019

**El sistema no almacena, procesa ni transmite datos de tarjeta.** No existen campos para número, CVV ni vencimiento. En todos los métodos de pago, el **monto** sale del pedido guardado: ninguna petición lo lleva.

Objeto `Pago` (dentro de `Pedido.pagos`):
```json
{
  "id": 5, "metodo": "yape", "estado": "pendiente_verificacion", "monto": 85.8,
  "codigoOperacion": "OP12345678", "proveedor": null,
  "verificadoPorNombre": null, "verificadoEn": null, "creadoEn": "2026-09-14T15:40:00.000Z"
}
```
- `metodo` ∈ `yape`, `pasarela`, `efectivo`.
- `estado` ∈ `pendiente` (pasarela, sin aviso todavía), `pendiente_verificacion` (Yape), `aprobado`, `rechazado`.

### 15.1 Yape por QR (HU-017)

**`GET /pagos/yape`** — admin, recepcionista, cliente
**200** `{ "celular": "987000111", "titular": "RiverVet SAC", "instrucciones": ["..."] }`. **503** si Yape no está configurado.

**`POST /pedidos/:id/pagos/yape`** — cliente (sus pedidos) o recepción
```json
{ "codigoOperacion": "OP12345678" }
```
`codigoOperacion` son de 6 a 30 letras o números; se guarda en mayúsculas. El pedido pasa a `pendiente_verificacion` y **no se da por pagado** hasta que recepción confirme, en la app de Yape del negocio, que el dinero llegó.

**201**: `Pedido`. **404** si el pedido es de otro cliente.

**409** con uno de estos mensajes:
- `"Ese número de operación de Yape ya fue registrado"`: un mismo código no paga dos pedidos.
- `"Este pedido ya tiene un pago en verificación"`
- `"El pedido ya está pagado"`

**`GET /pagos/pendientes`** — admin, recepcionista
**200** `[{ "pagoId": 5, "pedidoId": 12, "pedidoCodigo": "RV-000012", "clienteNombre": "Ana Torres Medina", "monto": 85.8, "codigoOperacion": "OP12345678", "creadoEn": "..." }]`

**`POST /pagos/:id/verificar`** — admin, recepcionista
```json
{ "aprobado": false, "observacion": "El Yape no llegó a la cuenta" }
```
- `aprobado: true`: el pago pasa a `aprobado` y el pedido a `pagado`.
- `aprobado: false`: el pago pasa a `rechazado` y el pedido vuelve a `pendiente_pago`. La `observacion` es obligatoria.

**200**: `Pedido`. **409** `"Este pago ya fue verificado o ya no está pendiente"`.

### 15.2 Efectivo en caja

**`POST /pedidos/:id/pagos/efectivo`** — admin, recepcionista. Sin body. **201**: `Pedido` en `pagado`. **409** si el pedido no está pendiente de pago.

### 15.3 Pasarela con checkout hospedado (HU-019)

**`POST /pedidos/:id/pagos/pasarela`** — cliente (sus pedidos). Sin body.

**201**
```json
{ "success": true, "data": { "pagoId": 9, "referencia": "sim_0a72f52b3c87bb6d7c4c27aa", "urlCheckout": "https://pasarela-simulada.invalid/checkout?referencia=..." } }
```
El frontend **redirige al cliente a `urlCheckout`**, que es la página de la pasarela. Ahí el cliente escribe su tarjeta, y esos datos nunca pasan por nuestro servidor. El resultado llega después por webhook. **503** si no hay pasarela configurada.

> En desarrollo la pasarela es **simulada** (`Pagos:Pasarela:Proveedor = "simulada"`) y no mueve dinero. La API no arranca si se intenta usar en producción. Para conectar una pasarela real hay que implementar `IPasarelaPagos` con el SDK oficial del proveedor.

### `POST /webhooks/pasarela` — lo llama la PASARELA, no el navegador

No lleva sesión. Su forma de autenticarse es la **firma HMAC-SHA256** que viaja en las cabeceras:

| Cabecera | Valor |
|---|---|
| `X-Firma-Tiempo` | segundos Unix del envío |
| `X-Firma` | `hex(HMAC-SHA256(SecretoWebhook, "{X-Firma-Tiempo}.{cuerpo exacto}"))` |

```json
{ "id": "evt_123", "tipo": "pago.aprobado", "referencia": "sim_0a72f52b3c87bb6d7c4c27aa", "monto": 85.8, "moneda": "PEN" }
```
`tipo` ∈ `pago.aprobado`, `pago.rechazado`.

**200** `{ "resultado": "..." }`, donde `resultado` puede ser:

| `resultado` | Significa |
|---|---|
| `aprobado` | pago aprobado y pedido `pagado` |
| `rechazado` | pago rechazado; el pedido sigue en `pendiente_pago` |
| `duplicado` | ese `id` de evento ya se había procesado (la pasarela reintenta); no se repite nada |
| `monto_no_coincide` | el monto o la moneda no coinciden con el pago guardado; **no se aprueba** |
| `requiere_devolucion` | la pasarela cobró un pedido que ya estaba cancelado o pagado; queda en el log para devolver el dinero |
| `sin_cambios`, `ignorado`, `referencia_desconocida` | no hay nada que hacer |

**401** si la firma no coincide, si falta, o si el aviso tiene más de 5 minutos (sería una repetición). Ese intento queda en la auditoría como `WEBHOOK_FIRMA_INVALIDA`. **503** si no hay pasarela configurada.

**Cómo simularlo en desarrollo:**
```powershell
.\herramientas\simular-webhook.ps1 -Referencia sim_0a72f52b3c87bb6d7c4c27aa -Monto 85.80
.\herramientas\simular-webhook.ps1 -Referencia sim_... -Monto 85.80 -SecretoFalso   # → 401
```

---

## 16. Comprobante por WhatsApp — HU-020

### `POST /pedidos/:id/comprobante/whatsapp` — admin, recepcionista
```json
{ "telefono": "912345678" }
```
`telefono` es opcional: si no se envía, se usa el celular de la ficha del cliente (9 dígitos, empieza con 9). Solo funciona para pedidos en `pagado` o `entregado`.

**201**
```json
{
  "success": true,
  "data": {
    "envioId": 3, "estado": "pendiente", "telefono": "987654321",
    "mensaje": "RiverVet: comprobante de tu pedido RV-000012. Total pagado: S/ 85.80. Fecha: 14/09/2026. ¡Gracias por tu compra!",
    "enlace": "https://wa.me/51987654321?text=RiverVet%3A%20comprobante..."
  }
}
```
Hay dos modos:
- **`enlace`** (por defecto): responde con `estado: "pendiente"` y un `enlace` de WhatsApp. El frontend lo abre en una pestaña nueva y la recepcionista pulsa **Enviar** desde el WhatsApp de la clínica.
- **`cloudapi`**: el mensaje se envía automáticamente con una plantilla aprobada por Meta. `estado` es `enviado` o `fallido`, y `enlace` es `null`.

El mensaje lleva **solo** el código, el total y la fecha. No incluye DNI, dirección, nombre de la mascota ni productos, porque los productos pueden revelar tratamientos.

**409** si el pedido no está pagado. **429** después de 3 envíos del mismo pedido en 24 horas. **422** `campo: "telefono"`.

---

## 17. Triage orientativo — HU-021

Es **público** y no guarda nada de lo consultado.

### `GET /triage/sintomas`
**200** `{ "sintomas": [ { "codigo": "dificultad_respirar", "descripcion": "Le cuesta respirar...", "nivel": "alta" }, ... ], "descargo": "..." }`

### `POST /triage`
```json
{ "especie": "gato", "sintomas": ["vomitos", "no_come"], "edadMeses": 30, "horasConSintomas": 20 }
```
`sintomas` lleva de 1 a 15 códigos **del catálogo**, sin repetir. No se acepta texto libre. `edadMeses` (0–600) y `horasConSintomas` (0–8760) son opcionales.

**200**
```json
{
  "success": true,
  "data": {
    "urgencia": "alta",
    "recomendacion": "Acude de inmediato a una clínica veterinaria o llama a la clínica de emergencia más cercana.",
    "motivos": ["Tiene varios síntomas moderados a la vez"],
    "descargo": "Esta orientación es automática y referencial. NO es un diagnóstico veterinario y NO reemplaza la consulta con un médico veterinario. Si tu mascota empeora o tienes cualquier duda, acude de inmediato a una clínica veterinaria."
  }
}
```

**Reglas.** Son conservadoras: ante la duda, urgencia alta.
1. Cualquier signo de alarma, o el código `otro` (síntoma que no se puede evaluar) → **alta**.
2. Dos o más síntomas moderados → **alta**.
3. Un síntoma moderado junto con un factor de riesgo → **alta**. Los factores de riesgo son: menos de 6 meses, 10 años o más, ser ave, conejo, roedor o reptil, o llevar más de 48 horas con síntomas.
4. Un síntoma moderado sin factor de riesgo → **media**.
5. Solo síntomas leves → **baja**. Sube a **media** si son 3 o más, si duran más de 7 días o si hay un factor de riesgo.

El `descargo` viene **siempre**, y el frontend debe mostrarlo de forma visible. **422** si un síntoma no está en el catálogo o si hay campos extra.

---

## 18. Mis datos personales — Ley N° 29733

### `GET /clientes/yo/exportar` — cliente
Es el **derecho de acceso**: el cliente descarga TODOS los datos personales que la clínica guarda sobre él. La ruta no lleva id; siempre son los datos del dueño de la sesión.

La respuesta trae `Content-Disposition: attachment; filename="mis-datos-rivervet.json"`, para que el navegador la ofrezca como descarga. Cada exportación queda en la auditoría como `EXPORTAR_DATOS`.

**200**
```json
{
  "success": true,
  "data": {
    "generadoEn": "2026-09-14T15:50:00.000Z",
    "responsable": "RiverVet — Clínica veterinaria",
    "finalidad": "Tus datos se usan para: ...",
    "derechos": "Según la Ley N° 29733 puedes acceder a tus datos (esta descarga), y pedir que se rectifiquen, actualicen o supriman...",
    "titular": { "id": 1, "dni": "70123456", "nombres": "Ana", "apellidos": "Torres Medina", "email": "...", "telefono": "...", "direccion": "...", "consentimientoDatos": true, "consentimientoEn": "...", "creadoEn": "..." },
    "cuenta": { "nombre": "Ana Torres", "email": "ana.torres@correo.pe", "rol": "cliente", "creadoEn": "...", "ultimoLoginEn": "..." },
    "mascotas": [], "citas": [], "historialClinico": [], "vacunas": [],
    "pedidos": [ { "codigo": "RV-000012", "canal": "web", "estado": "pagado", "total": 85.8, "creadoEn": "...", "detalle": "2 x Arena sanitaria 10 kg (S/ 42.90)" } ],
    "pagos": [], "enviosComprobante": [],
    "actividad": [ { "accion": "LOGIN", "recurso": "sesion", "ip": "190.12.34.56", "creadoEn": "..." } ]
  }
}
```
- Incluye también los datos dados de baja, porque el titular tiene derecho a saber qué datos suyos se **conservan**.
- No incluye hashes de contraseña ni tokens.
- `actividad` son las últimas 500 acciones de la cuenta. Se incluye porque la IP también es un dato personal.

**403** para el personal. **404** si la cuenta no tiene ficha de cliente.

> **Rectificación y supresión:** el cliente las solicita en la clínica. Recepción corrige la ficha (`PUT /clientes/:id`) y el admin la da de baja (`DELETE /clientes/:id`), una baja lógica que también desactiva su cuenta. Las ventas y la historia clínica se conservan el tiempo que exigen otras normas.
