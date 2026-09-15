import { beforeEach, describe, expect, test, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useAuthStore } from '@/stores/auth';
import { configurarSesionHttp } from '@/services/http';
import * as authApi from '@/services/api/auth';

vi.mock('@/services/api/auth', () => ({
  login: vi.fn(),
  refrescar: vi.fn(),
  logout: vi.fn(),
}));

const SESION = {
  accessToken: 'token-de-prueba',
  expiraEn: 900,
  usuario: { id: 4, nombre: 'Rosa Huamán', email: 'recepcion@rivervet.pe', rol: 'recepcionista' },
};

function borrarCookies() {
  document.cookie = 'rv_csrf=; expires=Thu, 01 Jan 1970 00:00:00 GMT';
}

let auth;

beforeEach(() => {
  vi.clearAllMocks();
  borrarCookies();
  localStorage.clear();
  sessionStorage.clear();
  setActivePinia(createPinia());
  auth = useAuthStore();
  configurarSesionHttp({ refrescar: () => auth.refrescar() });
});

describe('store de autenticación', () => {
  test('login guarda el token SOLO en memoria (nada en localStorage ni sessionStorage)', async () => {
    authApi.login.mockResolvedValue(SESION);
    await auth.login('recepcion@rivervet.pe', 'RiverVet#2026');

    expect(auth.accessToken).toBe('token-de-prueba');
    expect(auth.estaAutenticado).toBe(true);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).not.toContain('token-de-prueba');
  });

  test('tieneRol', async () => {
    authApi.login.mockResolvedValue(SESION);
    await auth.login('x', 'y');
    expect(auth.tieneRol('admin', 'recepcionista')).toBe(true);
    expect(auth.tieneRol(['admin'])).toBe(false);
  });

  test('restaurarSesion sin cookie de sesión no llama a la API', async () => {
    await auth.restaurarSesion();
    expect(authApi.refrescar).not.toHaveBeenCalled();
    expect(auth.inicializado).toBe(true);
    expect(auth.estaAutenticado).toBe(false);
  });

  test('restaurarSesion con cookie recupera la sesión con el refresh token', async () => {
    document.cookie = 'rv_csrf=abc';
    authApi.refrescar.mockResolvedValue(SESION);
    await auth.restaurarSesion();
    expect(authApi.refrescar).toHaveBeenCalledTimes(1);
    expect(auth.usuario.nombre).toBe('Rosa Huamán');
  });

  test('restaurarSesion con refresh inválido deja la sesión cerrada, sin romper', async () => {
    document.cookie = 'rv_csrf=abc';
    authApi.refrescar.mockRejectedValue(new Error('401'));
    await auth.restaurarSesion();
    expect(auth.estaAutenticado).toBe(false);
    expect(auth.inicializado).toBe(true);
  });

  test('logout limpia la sesión aunque el servidor no responda', async () => {
    authApi.login.mockResolvedValue(SESION);
    authApi.logout.mockRejectedValue(new Error('sin red'));
    await auth.login('x', 'y');
    await auth.logout('inactividad');
    expect(auth.accessToken).toBeNull();
    expect(auth.usuario).toBeNull();
    expect(auth.motivoCierre).toBe('inactividad');
  });
});
