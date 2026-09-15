# RiverVet — Frontend

![CI](https://github.com/cesarribo69889-stack/rivervet/actions/workflows/frontend-ci.yml/badge.svg?branch=develop)

Interfaz web de RiverVet. **Vue 3 (Composition API) · Vite · Vue Router 4 · Pinia · axios · Tailwind CSS 4 · Vitest**

## Inicio rápido

Requiere la API (ASP.NET Core) corriendo en `http://localhost:3000`: abre `backend\RiverVet.slnx` en Visual Studio y ejecuta el perfil **IIS Express** (ver `backend/docs/INSTALACION.md`).

```powershell
npm ci
npm run dev        # http://localhost:5173  (usa localhost, no 127.0.0.1)
```

Usuarios de prueba: contraseña `RiverVet#2026` para `admin@rivervet.pe`, `recepcion@rivervet.pe`, `vet.lopez@rivervet.pe`, `ana.torres@correo.pe`.

## Scripts

| Script | Qué hace |
|---|---|
| `npm run dev` | servidor de desarrollo con proxy `/api → localhost:3000` |
| `npm run build` | compila a `dist/` |
| `npm run preview` | sirve `dist/` localmente |
| `npm test` | pruebas (Vitest) |
| `npm run test:cobertura` | pruebas con cobertura |
| `npm run lint` / `npm run format` | ESLint / Prettier |

## Cómo se conecta con la API

El frontend siempre llama a `/api/...` (mismo origen):

- **Desarrollo:** Vite reenvía `/api` a `localhost:3000` (`vite.config.js`).
- **Producción:** Vercel reenvía `/api` a la API en Azure App Service (`vercel.json`). **Antes de desplegar, cambia en `vercel.json` la URL de destino por la de tu App Service.**

Así las cookies de sesión pueden ser `SameSite=Strict` y no hay problemas de CORS. Detalle en `backend/docs/INTEGRACION_LOCAL.md`.

## Seguridad en el frontend

| Regla | Dónde |
|---|---|
| El access token vive **solo en memoria** (Pinia), nunca en localStorage/sessionStorage | `src/stores/auth.js` |
| Al recargar, la sesión se recupera con la cookie HttpOnly (`/auth/refresh`) | `src/stores/auth.js` |
| Ante un 401: refresh **una sola vez**, con las peticiones concurrentes **encoladas** | `src/services/http.js` |
| CSRF: cookie `rv_csrf` → cabecera `X-CSRF-Token` | `src/services/http.js` |
| Guards por ruta y rol (**solo experiencia de usuario**: la seguridad real es el backend) | `src/router/guardia.js` |
| Prohibido `v-html`, `localStorage`, `sessionStorage`, `eval` (ESLint + prueba estática) | `eslint.config.js`, `tests/seguridad.test.js` |
| Cierre de sesión tras 30 min de inactividad, con aviso 1 min antes | `src/composables/useInactividad.js` |
| Nada de secretos en variables `VITE_*` (son públicas) | `.env.example` |
| Ids y datos personales fuera de las URLs (búsquedas por POST, ruta pendiente en memoria) | `src/services/api/*`, `src/stores/auth.js` |
| Cabeceras HTTP de seguridad y CSP estricta en producción | `vercel.json` |

## Pantallas (Sprint 1)

Login · Inicio (según rol) · Mi perfil (y cambio de contraseña) · Clientes (lista, búsqueda, alta, edición, ficha) · Mascotas (lista, alta, edición, ficha) · Mis mascotas (cliente) · Usuarios (admin) · Auditoría (admin) · 404 · Sin permisos.
