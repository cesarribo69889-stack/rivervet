import { describe, expect, test, vi } from 'vitest';
import { crearGuardia } from '@/router/guardia';
import { rutas } from '@/router';

function authFalso({ autenticado = false, rol = null, inicializado = true } = {}) {
  return {
    inicializado,
    estaAutenticado: autenticado,
    rutaPendiente: null,
    restaurarSesion: vi.fn(async function () {
      this.inicializado = true;
    }),
    tieneRol(...roles) {
      return roles.flat().includes(rol);
    },
  };
}

const ruta = (meta, fullPath = '/x') => ({ meta, fullPath });

describe('guard del router (experiencia de usuario, no seguridad)', () => {
  test('en la primera navegación intenta recuperar la sesión', async () => {
    const auth = authFalso({ inicializado: false });
    await crearGuardia(() => auth)(ruta({ publica: true }));
    expect(auth.restaurarSesion).toHaveBeenCalled();
  });

  test('sin sesión manda al login y recuerda la ruta en memoria (no en la URL)', async () => {
    const auth = authFalso();
    const resultado = await crearGuardia(() => auth)(ruta({ roles: ['admin'] }, '/clientes/5'));
    expect(resultado).toEqual({ name: 'login' });
    expect(auth.rutaPendiente).toBe('/clientes/5');
  });

  test('con sesión pero sin el rol: pantalla de sin permisos', async () => {
    const auth = authFalso({ autenticado: true, rol: 'cliente' });
    expect(await crearGuardia(() => auth)(ruta({ roles: ['admin'] }))).toEqual({
      name: 'sin-permisos',
    });
  });

  test('con el rol correcto deja pasar', async () => {
    const auth = authFalso({ autenticado: true, rol: 'admin' });
    expect(await crearGuardia(() => auth)(ruta({ roles: ['admin'] }))).toBe(true);
  });

  test('un usuario con sesión que va al login es enviado al inicio', async () => {
    const auth = authFalso({ autenticado: true, rol: 'admin' });
    expect(await crearGuardia(() => auth)(ruta({ soloInvitados: true }))).toEqual({
      name: 'inicio',
    });
  });

  test('toda ruta privada declara sus roles explícitamente', () => {
    const privadas = rutas
      .flatMap((r) => r.children ?? [])
      .filter((r) => r.name !== 'sin-permisos');
    for (const r of privadas) {
      expect(r.meta?.roles, `la ruta ${r.name} no declara roles`).toBeDefined();
    }
  });
});
