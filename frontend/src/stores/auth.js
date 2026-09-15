// Store de autenticación.
//
// ★ El access token vive SOLO en esta variable reactiva, en MEMORIA. ★
// Nunca en localStorage ni sessionStorage: cualquier script que corra en la
// página (incluido uno inyectado por un XSS o por una librería comprometida)
// puede leer ese almacenamiento. En memoria, el token desaparece al cerrar la
// pestaña y no queda escrito en ningún lado.
//
// ¿Y al recargar la página? La memoria se borra, pero la cookie httpOnly del
// refresh token sigue ahí: restaurarSesion() llama a /auth/refresh y obtiene
// un access token nuevo sin pedir la contraseña.
import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import * as authApi from '@/services/api/auth';
import { leerCookie, refrescarUnaVez } from '@/services/http';

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref(null);
  const usuario = ref(null);
  const inicializado = ref(false);
  // Por qué se cerró la sesión ('expirada' | 'inactividad' | 'password'),
  // para mostrar el mensaje correcto en el login.
  const motivoCierre = ref(null);
  // Ruta a la que quería ir antes de que le pidiéramos iniciar sesión. Se
  // guarda aquí y NO en la URL (?redirect=/clientes/5): no ponemos ids ni
  // datos en query strings.
  const rutaPendiente = ref(null);

  const estaAutenticado = computed(() => Boolean(accessToken.value && usuario.value));
  const rol = computed(() => usuario.value?.rol ?? null);

  // OJO: tieneRol() sirve para decidir qué MOSTRAR (menús, botones). No es
  // seguridad: la autorización real la hace el backend en cada petición.
  function tieneRol(...roles) {
    return roles.flat().includes(rol.value);
  }

  function establecerSesion({ accessToken: token, usuario: datosUsuario }) {
    accessToken.value = token;
    usuario.value = datosUsuario;
    motivoCierre.value = null;
  }

  function limpiarSesion(motivo = null) {
    accessToken.value = null;
    usuario.value = null;
    motivoCierre.value = motivo;
  }

  async function login(email, password) {
    establecerSesion(await authApi.login(email, password));
  }

  // La usa el interceptor de axios (a través de refrescarUnaVez).
  async function refrescar() {
    const datos = await authApi.refrescar();
    establecerSesion(datos);
    return datos.accessToken;
  }

  // Se llama una vez al cargar la app, antes de la primera navegación.
  async function restaurarSesion() {
    try {
      // Sin cookie CSRF no hay sesión que recuperar: nos ahorramos una
      // petición que de todas formas fallaría.
      if (leerCookie('rv_csrf')) await refrescarUnaVez();
    } catch {
      limpiarSesion();
    } finally {
      inicializado.value = true;
    }
  }

  async function logout(motivo = null) {
    try {
      await authApi.logout();
    } catch {
      // Aunque el servidor no responda, la sesión local se cierra igual.
    } finally {
      limpiarSesion(motivo);
    }
  }

  return {
    accessToken,
    usuario,
    inicializado,
    motivoCierre,
    rutaPendiente,
    estaAutenticado,
    rol,
    tieneRol,
    establecerSesion,
    limpiarSesion,
    login,
    refrescar,
    restaurarSesion,
    logout,
  };
});
