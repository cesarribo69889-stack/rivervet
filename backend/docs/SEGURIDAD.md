# Seguridad — RiverVet

Este documento lista **cada control de seguridad** del sistema, **qué ataque previene** y **en qué archivo está**, para poder revisarlo o demostrarlo en la sustentación.

**Cómo leerlo**
- Las rutas del backend son relativas a la carpeta `backend/` y tienen enlace.
- Las rutas marcadas *(frontend)* están en la carpeta `frontend/`.
- La columna **Prueba** indica qué prueba automática demuestra que el control funciona. Todas corren en la CI en cada cambio.

---

## 0. La idea general: defensa en profundidad

Ningún control es perfecto, así que cada riesgo importante tiene **al menos dos barreras**. Algunos ejemplos:

| Riesgo | Barrera 1 | Barrera 2 | Barrera 3 |
|---|---|---|---|
| Un cliente ve datos de otro | rol en cada endpoint | filtro de propiedad **dentro del SQL** | respuesta 404 (no confirma que el dato exista) |
| XSS almacenado | se eliminan etiquetas HTML al guardar | Vue escapa todo lo que muestra; `v-html` prohibido | CSP estricta en el navegador |
| Stock negativo | `UPDATE ... WHERE stock >= cantidad` | transacción: todo o nada | `CHECK (stock >= 0)` en la tabla |
| Inyección SQL | consultas 100 % parametrizadas | el compilador rechaza SQL concatenado (CA2100) | usuario de base sin DELETE ni DDL |

**Ocultar un botón en el frontend NO es seguridad.** Los guards del router y los botones por rol son solo comodidad. La autorización real está en el backend, que valida cada petición.

---

## 1. Autenticación (quién eres)

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| Contraseñas con **bcrypt, costo 12** (~250 ms por intento) | Si roban la base, descifrar las contraseñas por fuerza bruta es lentísimo | [HasheadorBcrypt.cs](../src/RiverVet.Infraestructura/Seguridad/HasheadorBcrypt.cs) | `AuthPruebas` |
| Política: 10+ caracteres con mayúscula, minúscula, número y símbolo; máx. 72 bytes | Contraseñas adivinables; el límite de 72 evita que bcrypt ignore el final | [ReglasValidacion.cs](../src/RiverVet.Aplicacion/Comun/ReglasValidacion.cs) | `ValidadoresPruebas` |
| **Anti-enumeración**: mismo mensaje y mismo tiempo exista o no el email (bcrypt corre siempre contra un hash falso) | Descubrir qué emails tienen cuenta | [AutenticacionServicio.cs](../src/RiverVet.Aplicacion/Autenticacion/AutenticacionServicio.cs) (`HashFalso`) | `AuthPruebas` |
| **Bloqueo de cuenta** 15 min tras 5 contraseñas incorrectas; mientras dura, el login responde igual que con clave incorrecta | Adivinar la contraseña de una cuenta desde muchas IPs | `AutenticacionServicio.cs`, tabla `usuarios.bloqueado_hasta` | `Bloqueo_tras_5_intentos_fallidos...` |
| Límite de 5 intentos fallidos cada 15 min por **IP + email** | Fuerza bruta desde una misma máquina | [LimitadorIntentosLogin.cs](../src/RiverVet.Infraestructura/Seguridad/LimitadorIntentosLogin.cs) | `SeguridadHttpPruebas` |
| **JWT de 15 minutos**, firmado con HS256 y un secreto de 256 bits | Un token robado sirve muy poco tiempo | [ServicioTokens.cs](../src/RiverVet.Infraestructura/Seguridad/ServicioTokens.cs) | `ServicioTokensPruebas` |
| El algoritmo del JWT es **fijo** (`ValidAlgorithms = HS256`) | Ataque "alg: none" y confusión de algoritmos: aceptar tokens sin firma | `ServicioTokens.ParametrosValidacion` | `ServicioTokensPruebas` |
| El JWT solo lleva id, rol y un id único (`jti`) | Filtrar datos personales: el contenido de un JWT se lee en jwt.io | `ServicioTokens.cs` | `ServicioTokensPruebas` |
| En cada petición se **relee el usuario** de la base: si fue desactivado, el token deja de valer al instante | Seguir usando la sesión de un empleado despedido | [EventosJwt.cs](../src/RiverVet.Api/Seguridad/EventosJwt.cs) | `Un_usuario_desactivado_pierde_el_acceso_de_inmediato` |
| **Refresh token** de 7 días en cookie `HttpOnly` + `Secure` + `SameSite=Strict`, con `Path=/api/auth`; en la base solo se guarda su hash SHA-256 | Robo del token con JavaScript (XSS), envío desde otros sitios, lectura desde la base | [CookiesSesion.cs](../src/RiverVet.Api/Seguridad/CookiesSesion.cs), tabla `refresh_tokens` | `AuthPruebas` |
| **Rotación** del refresh token, con **detección de reutilización**: si alguien usa uno ya rotado, se revoca toda la familia de sesiones | Que un ladrón use indefinidamente un refresh token robado | `AutenticacionServicio.cs` (familia de tokens) | `AuthPruebas` |
| Cambiar la contraseña cierra **todas** las sesiones | Que un intruso que ya entró siga dentro | `AutenticacionServicio.cs` | `AuthPruebas` |
| El primer administrador de producción se crea con un comando; su contraseña va en una variable de entorno, **nunca** en el seed | Tener en producción una cuenta con contraseña conocida | [PrimerAdministrador.cs](../src/RiverVet.Migrador/PrimerAdministrador.cs) | `PrimerAdministradorPruebas` |

## 2. Autorización (qué puedes hacer)

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| **Todo endpoint exige sesión** salvo que diga `[AllowAnonymous]` (política por defecto) | Olvidar proteger un endpoint nuevo | [Program.cs](../src/RiverVet.Api/Program.cs) (`SetFallbackPolicy`) | `SeguridadHttpPruebas` |
| **RBAC**: cada endpoint declara sus roles (`[Authorize(Roles = ...)]`) | Que un cliente o un veterinario haga acciones de admin | `src/RiverVet.Api/Controllers/*.cs`, [Roles.cs](../src/RiverVet.Dominio/Constantes/Roles.cs) | pruebas `RBAC_*` en cada módulo |
| **Control de propiedad (IDOR) dentro del SQL**: `(@propietarioId IS NULL OR c.usuario_id = @propietarioId)` en mascotas, citas, historial, pedidos y pagos | Que un cliente cambie el id de la URL y vea mascotas, citas o pedidos de otro | `src/RiverVet.Infraestructura/Datos/*Repositorio*.cs`, [ContextoPeticionHttp.cs](../src/RiverVet.Api/Seguridad/ContextoPeticionHttp.cs) | `MascotasPruebas`, `CitasPruebas`, `VentasPruebas`, `PagosPruebas` |
| Pedir un recurso ajeno responde **404, no 403** | Confirmar que un id existe (enumeración) | [Excepciones.cs](../src/RiverVet.Aplicacion/Comun/Excepciones.cs) | mismas pruebas |
| El carrito, "mis datos" y la exportación **no reciben ids**: se resuelven desde la sesión | IDOR por diseño imposible | [CarritoServicio.cs](../src/RiverVet.Aplicacion/Ventas/CarritoServicio.cs), [ExportacionServicio.cs](../src/RiverVet.Aplicacion/DatosPersonales/ExportacionServicio.cs) | `Sprint4Pruebas` |
| Los guards del router y los botones por rol son **solo experiencia de usuario** | — (no protegen nada; el backend sí) | *(frontend)* `src/router/guardia.js` | *(frontend)* `tests/router/guardia.test.js` |

## 3. Base de datos

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| **Todas** las consultas son parametrizadas (Dapper), sin concatenar datos | **Inyección SQL** | `src/RiverVet.Infraestructura/Datos/RepositorioBase.cs` y repositorios | pruebas con `' OR 1=1 --` en `ClientesPruebas` |
| El analizador **CA2100** está configurado como **error de compilación** | Que alguien concatene SQL por descuido: el código no compila | [.editorconfig](../.editorconfig), [Directory.Build.props](../Directory.Build.props) | la CI (compilar) |
| La app usa `rivervet_app`, **nunca `sa`**: sin DDL y sin DELETE (salvo `carrito_items`); no puede tocar ni la auditoría ni el control de migraciones | Si hubiera una inyección SQL, el atacante no podría borrar tablas, datos ni el rastro | [02_permisos_app.sql](../database/local/02_permisos_app.sql), [crear_usuarios_azure.sql](../database/azure/crear_usuarios_azure.sql) | prueba de humo con `rivervet_app` |
| La API **no arranca** en producción con `sa`, `TrustServerCertificate=True` o sin cifrado | Conexión espiable o suplantable; usar un superusuario | [ValidadorConfiguracion.cs](../src/RiverVet.Api/Configuracion/ValidadorConfiguracion.cs) | `ValidadorConfiguracionPruebas` |
| **Borrado lógico** (`eliminado = 1`) | Pérdida de datos por error o sabotaje | migraciones `0001`–`0004` | `ClientesPruebas`, `ProductosPruebas` |
| **Transacciones** en toda operación de varias tablas (ficha + cuenta + auditoría; pedido + stock + carrito) | Datos a medias si algo falla en medio | [SesionBd.cs](../src/RiverVet.Infraestructura/Datos/SesionBd.cs) | `Si_una_linea_no_tiene_stock_se_revierte_todo_el_pedido` |
| **Restricciones únicas** como última defensa: doble cita (`UX_citas_veterinario_horario`), código de Yape reutilizado (`UX_pagos_yape_operacion`), webhook repetido (`UX_webhook_eventos_evento`) | Condiciones de carrera (dos peticiones a la vez) y fraude | migraciones `0002` y `0004` | pruebas de **concurrencia** en `CitasPruebas` y `VentasPruebas` |
| **Stock atómico**: `UPDATE ... SET stock = stock - @n WHERE stock >= @n` más `CHECK (stock >= 0)` | Vender más unidades de las que hay cuando compran a la vez | [VentasRepositorios.cs](../src/RiverVet.Infraestructura/Datos/VentasRepositorios.cs), [PedidosServicio.cs](../src/RiverVet.Aplicacion/Ventas/PedidosServicio.cs) | `Concurrencia_diez_compras_simultaneas...` |
| **Migraciones inmutables** con hash: el migrador se detiene si alguien edita una migración ya aplicada | Cambios silenciosos al esquema de producción | [Migraciones.cs](../src/RiverVet.Migrador/Migraciones.cs) | `MigracionesPruebas`, job `base-de-datos` de la CI |

## 4. Validación de entrada

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| **Lista blanca de campos**: un campo no declarado responde 422 | *Mass assignment* (agregar `"rol": "admin"` o `"precio": 0.01` al JSON) | [Program.cs](../src/RiverVet.Api/Program.cs) (`JsonUnmappedMemberHandling.Disallow`), [FabricaErroresModelo.cs](../src/RiverVet.Api/Respuestas/FabricaErroresModelo.cs) | `El_precio_sale_de_la_base_nunca_de_la_peticion`, `SeguridadHttpPruebas` |
| Validación declarativa (FluentValidation) de **todo** DTO, antes de ejecutar la acción | Datos inválidos o maliciosos que llegan a la lógica | [FiltroValidacion.cs](../src/RiverVet.Api/Filtros/FiltroValidacion.cs), validadores en `src/RiverVet.Aplicacion/**/*Modelos.cs` | `ValidadoresPruebas` |
| Formatos peruanos: DNI de 8 dígitos, celular de 9 que empieza con 9 | Datos basura; teléfono inválido para WhatsApp | [ReglasValidacion.cs](../src/RiverVet.Aplicacion/Comun/ReglasValidacion.cs) | `ValidadoresPruebas` |
| Límite de **1 MB** en el body | Agotar la memoria del servidor con JSON gigantes (DoS) | [ManejadorErroresMiddleware.cs](../src/RiverVet.Api/Middlewares/ManejadorErroresMiddleware.cs), `Program.cs` | `SeguridadHttpPruebas` |
| El texto libre se guarda **sin etiquetas HTML** ni `javascript:` (historial, observaciones, productos) | **XSS almacenado** | [Texto.cs](../src/RiverVet.Aplicacion/Comun/Texto.cs) (`Libre`) | `TextoYRedactorPruebas`, `El_texto_libre_del_producto_se_guarda_sin_HTML` |
| URLs de imagen solo `https://` | XSS con `javascript:` y contenido mixto | [ProductosModelos.cs](../src/RiverVet.Aplicacion/Productos/ProductosModelos.cs) | `Validaciones_de_precio_imagen_y_categoria` |
| Las búsquedas de personas (DNI, nombre) van en el **body**, no en la URL | Datos personales grabados en logs de proxies y en el historial del navegador | `POST /clientes/buscar`, `POST /mascotas/buscar` | — |
| Los comodines de `LIKE` se escapan | Que buscar `%` traiga todos los registros | `Texto.PatronLike` | `Buscar_un_comodin_no_trae_todo_el_catalogo` |

## 5. Transporte, cabeceras y navegador

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| **CSP** `default-src 'none'` en la API; CSP estricta en el frontend (`script-src 'self'`) | Que un XSS cargue scripts externos | [CabecerasSeguridadMiddleware.cs](../src/RiverVet.Api/Middlewares/CabecerasSeguridadMiddleware.cs), *(frontend)* `vercel.json` | `SeguridadHttpPruebas` |
| `X-Frame-Options: DENY` y `frame-ancestors 'none'` | **Clickjacking** (mostrar la app dentro de un iframe engañoso) | ídem | `SeguridadHttpPruebas` |
| `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy` | Que el navegador adivine tipos de contenido; fuga de URLs; uso de cámara o micrófono | ídem | `SeguridadHttpPruebas` |
| **HSTS** (1 año) y solo HTTPS | Degradar la conexión a HTTP para espiarla | `Program.cs` (`UseHsts`), `vercel.json`, App Service "Solo HTTPS" | — |
| `Cache-Control: no-store` en la API | Que el navegador o un proxy guarden fichas, historial o la exportación de datos | `CabecerasSeguridadMiddleware.cs` | `El_cliente_exporta_todos_sus_datos...` |
| Se quitan `Server` y `X-Powered-By` | Revelar la tecnología y su versión a un atacante | `CabecerasSeguridadMiddleware.cs`, [web.config](../src/RiverVet.Api/web.config) | `SeguridadHttpPruebas` |
| **CORS con lista blanca** (nunca `*`) y credenciales | Que otro sitio llame a la API con la sesión del usuario | `Program.cs` (`AddCors`), `ValidadorConfiguracion.cs` | `SeguridadHttpPruebas` |
| **CSRF double-submit** (cookie `rv_csrf` + cabecera `X-CSRF-Token`) en refresh y logout, las únicas rutas que usan cookies | Que otro sitio renueve o cierre la sesión del usuario | [ValidarCsrfAttribute.cs](../src/RiverVet.Api/Seguridad/ValidarCsrfAttribute.cs) | `AuthPruebas` |
| **Rate limit global**: 100 peticiones cada 15 min por IP, contadas **antes** de la autenticación | Inundar la API (también sin iniciar sesión) | `Program.cs` (`AddRateLimiter` / `UseRateLimiter`) | `SeguridadHttpPruebas` |

## 6. Frontend

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| El access token vive **solo en memoria** (Pinia), nunca en `localStorage` ni `sessionStorage` | Robo del token con un XSS o una librería comprometida | *(frontend)* `src/stores/auth.js` | *(frontend)* `tests/seguridad.test.js` |
| Refresh **único** con cola: si 5 peticiones reciben 401 a la vez, se hace un solo refresh | Disparar la detección de reutilización por error y cerrar la sesión | *(frontend)* `src/services/http.js` | *(frontend)* `tests/services/http.test.js` |
| **Prohibido `v-html`** (lo verifica una prueba que revisa el código fuente) | XSS | *(frontend)* `tests/seguridad.test.js` | ídem |
| Nada secreto en variables `VITE_*`: todo lo que compila Vite es público | Filtrar claves de API en el navegador | *(frontend)* `tests/seguridad.test.js` | ídem |
| Cierre de sesión tras **30 minutos de inactividad**, con aviso previo | Sesión abierta en una PC compartida de la clínica | *(frontend)* `src/composables/useInactividad.js` | *(frontend)* `tests/composables/composables.test.js` |
| Nunca se muestra el error crudo del servidor: mensaje entendible + código de soporte | Filtrar detalles internos al usuario | *(frontend)* `src/utils/errores.js` | *(frontend)* `tests/utils/errores.test.js` |
| No hay ids ni datos personales en los query strings (la ruta pendiente se guarda en memoria) | Datos en logs e historial | *(frontend)* `src/stores/auth.js` (`rutaPendiente`) | *(frontend)* `tests/router/guardia.test.js` |

## 7. Pagos (PCI DSS)

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| **Ningún dato de tarjeta** existe en el sistema: el cliente paga en la página hospedada de la pasarela, y aquí solo se guarda una referencia y el resultado | Robo de tarjetas. Además, el sistema queda fuera de casi todo el alcance de PCI DSS | [0004_pagos_notificaciones.sql](../database/migraciones/0004_pagos_notificaciones.sql), [PagosModelos.cs](../src/RiverVet.Aplicacion/Pagos/PagosModelos.cs) (`IPasarelaPagos`) | — |
| **Firma HMAC-SHA256** de cada webhook, verificada sobre los bytes exactos y comparada en **tiempo constante** | Que alguien "avise" que un pedido está pagado sin haber pagado | [FirmaWebhook.cs](../src/RiverVet.Aplicacion/Pagos/FirmaWebhook.cs) | `FirmaWebhookPruebas`, `Webhook_con_firma_falsa_o_cuerpo_alterado...` |
| El tiempo va **dentro** de la firma y se rechazan avisos de más de 5 minutos | **Repetición**: reenviar un aviso legítimo capturado | `FirmaWebhook.cs` | `Webhook_viejo_se_rechaza...` |
| **Idempotencia**: un evento ya procesado no se procesa otra vez (índice único) | Procesar dos veces un reintento de la pasarela | [PagosRepositorios.cs](../src/RiverVet.Infraestructura/Datos/PagosRepositorios.cs) | `Pasarela_checkout_y_webhook_firmado...` |
| Los intentos con firma inválida se registran con un **id propio**, no con el que trae el cuerpo | Que un atacante "gaste" por adelantado el id de un evento real para que después se descarte | [PagosServicio.cs](../src/RiverVet.Aplicacion/Pagos/PagosServicio.cs) (`RegistrarFirmaInvalidaAsync`) | `Webhook_con_firma_falsa...` |
| El **monto** siempre sale del pedido guardado, y el del webhook debe coincidir | Pagar menos de lo debido | `PagosServicio.cs` | `Webhook_con_otro_monto_no_aprueba_el_pago` |
| **Yape** queda "pendiente de verificación" hasta que recepción lo confirme, y un número de operación no sirve para dos pedidos | Capturas falsas o reutilizadas | `PagosServicio.cs`, índice `UX_pagos_yape_operacion` | `Un_codigo_de_Yape_no_sirve_para_pagar_dos_pedidos` |
| La pasarela **simulada** no puede usarse en producción (la API no arranca) | Aprobar pagos que nadie cobró | `ValidadorConfiguracion.cs` | `La_pasarela_simulada_no_se_permite_en_produccion` |

## 8. Auditoría, logs y errores

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| Tabla **`auditoria`**: quién, qué, sobre qué, cuándo y desde qué IP. Cubre login, logout, fallos, altas, cambios, bajas, citas, historial, ventas, pagos y exportaciones | Acciones sin rastro; no poder investigar un incidente | [ServicioAuditoria.cs](../src/RiverVet.Infraestructura/Seguridad/ServicioAuditoria.cs), [AccionesAuditoria.cs](../src/RiverVet.Dominio/Constantes/AccionesAuditoria.cs) | `UsuariosYAuditoriaPruebas` |
| La auditoría se escribe **en la misma transacción** que el cambio; la app no puede modificarla ni borrarla (`DENY`) | "Cambios sin rastro" y borrado de huellas | `ServicioAuditoria.cs`, `02_permisos_app.sql` | — |
| La auditoría guarda **qué campos** cambiaron, no sus valores; los teléfonos van enmascarados (`98*****21`) | Que la auditoría se convierta en una copia de los datos personales | `ClientesServicio.cs`, [ComprobantesModelos.cs](../src/RiverVet.Aplicacion/Comprobantes/ComprobantesModelos.cs) | `El_telefono_se_enmascara_en_la_auditoria` |
| **Redactor de logs**: contraseñas, tokens, cookies y números de tarjeta se reemplazan antes de escribir | Secretos filtrados en archivos de log | [RedactorDatos.cs](../src/RiverVet.Aplicacion/Comun/RedactorDatos.cs), [RedactorLogEnricher.cs](../src/RiverVet.Api/Registro/RedactorLogEnricher.cs) | `TextoYRedactorPruebas` |
| Logs en archivo diario que se conservan **14 días** | Guardar IPs más tiempo del necesario | `Program.cs` (Serilog) | — |
| **Sin stack traces** en producción: mensaje genérico + `traceId`, que en el log lleva al detalle completo | Revelar rutas, versiones y tablas a un atacante | [ManejadorErroresMiddleware.cs](../src/RiverVet.Api/Middlewares/ManejadorErroresMiddleware.cs), [TrazaMiddleware.cs](../src/RiverVet.Api/Middlewares/TrazaMiddleware.cs) | `SeguridadHttpPruebas` |

## 9. Secretos y configuración

| Control | Qué ataque previene | Dónde está | Prueba |
|---|---|---|---|
| **Fail-fast**: la API no arranca si falta un secreto o es inseguro (JWT de menos de 256 bits, CORS `*`, cookies sin `Secure` en producción, secreto de webhook corto…) | Descubrir en producción que el sistema corría con una clave vacía | [ValidadorConfiguracion.cs](../src/RiverVet.Api/Configuracion/ValidadorConfiguracion.cs) | `ValidadorConfiguracionPruebas`, `ConfiguracionSprint4Pruebas` |
| En desarrollo, secretos en **User Secrets** (fuera del repositorio), generados con un generador **criptográfico** | Subir claves a GitHub; claves predecibles | [configurar-desarrollo.ps1](../configurar-desarrollo.ps1) | — |
| En producción, secretos en la **configuración de App Service** y en **GitHub Secrets**; los workflows nunca los imprimen | Claves en el código, en el YAML o en los logs de la CI | [DESPLIEGUE.md](DESPLIEGUE.md) §4 y §6 | — |
| **gitleaks** revisa todo el historial de git en cada cambio | Un secreto que alguien subió por error | `.github/workflows/backend-ci.yml` y `frontend-ci.yml` (job `seguridad`) | la CI |
| `appsettings.Development.json` **no viaja** al paquete de producción | Que la configuración de desarrollo termine en producción | [RiverVet.Api.csproj](../src/RiverVet.Api/RiverVet.Api.csproj) | — |
| Procedimiento de **rotación** de cada secreto | Un secreto filtrado que sigue sirviendo | [DESPLIEGUE.md](DESPLIEGUE.md) §9 | — |

## 10. Cadena de suministro

| Control | Qué ataque previene | Dónde está |
|---|---|---|
| Versiones **fijadas** de todos los paquetes NuGet y npm | Que una actualización maliciosa entre sin revisión | `*.csproj`, *(frontend)* `package.json` |
| **NuGet Audit** en cada compilación y `dotnet list package --vulnerable` en la CI (falla con severidad alta); `npm audit` en el frontend | Usar una librería con una vulnerabilidad conocida | [Directory.Build.props](../Directory.Build.props), `backend-ci.yml` |
| **CodeQL** (análisis estático) en cada cambio | Patrones de código inseguros | `backend-ci.yml` y `frontend-ci.yml` (job `seguridad`); en repos privados se omite (requiere GitHub Advanced Security) |
| **Dependabot** semanal para paquetes y GitHub Actions | Quedarse con dependencias vulnerables sin enterarse | `.github/dependabot.yml` (raíz) |
| Las advertencias del compilador y de los analizadores son **errores** | Ignorar avisos de seguridad | `Directory.Build.props` (`TreatWarningsAsErrors`) |

## 11. Protección de datos personales — Ley N° 29733

| Principio o derecho | Cómo se cumple | Dónde está |
|---|---|---|
| **Consentimiento** | No se registra un cliente sin `consentimientoDatos: true`; se guarda la fecha | `ClientesModelos.cs`, columna `clientes.consentimiento_en` |
| **Derecho de acceso** | `GET /api/clientes/yo/exportar` descarga todos sus datos en JSON (auditado) | [ExportacionServicio.cs](../src/RiverVet.Aplicacion/DatosPersonales/ExportacionServicio.cs), [ExportacionRepositorio.cs](../src/RiverVet.Infraestructura/Datos/ExportacionRepositorio.cs) |
| **Finalidad declarada** | La exportación incluye para qué se usan los datos y cuáles son los derechos del titular | `ExportacionServicio.Finalidad` / `Derechos` |
| **Rectificación y supresión** | Recepción corrige la ficha; el admin la da de baja (baja lógica + cuenta desactivada + sesiones cerradas) | `ClientesServicio.cs` |
| **Minimización** | El triage no guarda nada; WhatsApp solo envía código, total y fecha; la auditoría no copia valores personales | [TriageServicio.cs](../src/RiverVet.Aplicacion/Triage/TriageServicio.cs), `ComprobantesModelos.cs` |
| **Seguridad** | Todo lo de este documento | — |

---

## 12. Límites conocidos y mejoras recomendadas

Se documentan para ser transparentes; ninguno expone datos hoy.

| Límite | Riesgo | Mejora sugerida |
|---|---|---|
| El rate limit y el límite de login viven en la **memoria** de cada servidor | Con varias instancias, cada una cuenta por separado | Usar Redis si se escala a más de una instancia |
| La **pasarela real** no está integrada (solo la simulada) | — (en producción, el pago con tarjeta responde 503) | Implementar `IPasarelaPagos` con el SDK oficial del proveedor y adaptar su esquema de firma |
| La **supresión** es manual y es una baja lógica (los datos se conservan) | La norma exige conservar ventas e historia clínica por otras leyes; el resto podría anonimizarse | Proceso de anonimización al vencer los plazos legales |
| No hay **segundo factor** (MFA) para el personal | Una contraseña robada del admin da acceso completo | Agregar TOTP para los roles admin y recepcionista |
| Las pruebas de integración usan un usuario administrador de SQL (para crear la base de pruebas) | Los permisos mínimos de `rivervet_app` no se prueban en la CI | Se verificaron con una prueba de humo contra la base de desarrollo; se puede automatizar con un segundo login en la CI |
