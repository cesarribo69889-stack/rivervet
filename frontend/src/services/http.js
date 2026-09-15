// Clientes HTTP (axios).
//
//  - `http`: para toda la API. Adjunta el access token y, ante un 401, intenta
//    refrescar la sesión UNA vez y reintenta la petición original.
//  - `httpAuth`: SOLO para login, refresh y logout. No tiene el interceptor de
//    401 (si el refresh devolviera 401 y lo interceptáramos, entraríamos en un
//    bucle infinito de refresh).
import axios from 'axios';

const BASE_URL = import.meta.env.VITE_API_URL || '/api';

const opcionesComunes = {
  baseURL: BASE_URL,
  // Envía las cookies (refresh y CSRF). Sin esto el navegador no las adjunta.
  withCredentials: true,
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' },
};

export const http = axios.create(opcionesComunes);
export const httpAuth = axios.create(opcionesComunes);

export function leerCookie(nombre) {
  const par = document.cookie.split('; ').find((c) => c.startsWith(`${nombre}=`));
  return par ? decodeURIComponent(par.slice(nombre.length + 1)) : null;
}

// Double-submit CSRF: el backend puso la cookie rv_csrf; la devolvemos en una
// cabecera. Una página de otro sitio no puede leer nuestras cookies, así que
// no puede fabricar esta cabecera.
httpAuth.interceptors.request.use((config) => {
  const csrf = leerCookie('rv_csrf');
  if (csrf) config.headers['X-CSRF-Token'] = csrf;
  return config;
});

// El store de autenticación se registra aquí al arrancar la app (main.js).
// Así evitamos una dependencia circular store <-> http.
let sesion = {
  obtenerToken: () => null,
  refrescar: () => Promise.reject(new Error('Sesión no configurada')),
  alExpirar: () => {},
};

export function configurarSesionHttp(manejadores) {
  sesion = { ...sesion, ...manejadores };
}

http.interceptors.request.use((config) => {
  const token = sesion.obtenerToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// COLA DE REFRESH: si llegan cinco 401 a la vez (p. ej. una pantalla que pide
// cinco cosas justo cuando venció el token), NO se disparan cinco refresh.
// El primero crea la promesa y los demás esperan esa misma promesa. Esto
// importa porque el refresh token es de UN solo uso (rotación): cinco refresh
// simultáneos se verían como una reutilización y cerrarían la sesión.
let refrescoEnCurso = null;

export function refrescarUnaVez() {
  if (!refrescoEnCurso) {
    refrescoEnCurso = Promise.resolve()
      .then(() => sesion.refrescar())
      .finally(() => {
        refrescoEnCurso = null;
      });
  }
  return refrescoEnCurso;
}

http.interceptors.response.use(
  (respuesta) => respuesta,
  async (error) => {
    const original = error.config;
    // Solo se reintenta ante 401, y una sola vez por petición.
    if (error.response?.status !== 401 || !original || original._reintentado) {
      throw error;
    }
    original._reintentado = true;
    try {
      await refrescarUnaVez();
    } catch {
      // El refresh falló: la sesión terminó de verdad.
      sesion.alExpirar();
      throw error;
    }
    // El interceptor de petición pondrá el access token nuevo.
    return http(original);
  },
);
