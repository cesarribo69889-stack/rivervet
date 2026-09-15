// Interceptor de axios: refresco automático ante 401, una sola vez y encolado.
import { AxiosError } from 'axios';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { configurarSesionHttp, http, httpAuth, leerCookie } from '@/services/http';

let token;
let peticionesRecibidas;

// Adaptador falso: simula la API. Responde 401 si el token no es 'nuevo'.
function adaptadorFalso(config) {
  peticionesRecibidas.push({ url: config.url, auth: config.headers.Authorization });
  if (config.headers.Authorization !== 'Bearer nuevo') {
    const respuesta = { status: 401, data: { success: false }, headers: {}, config };
    return Promise.reject(new AxiosError('401', 'ERR_BAD_REQUEST', config, null, respuesta));
  }
  return Promise.resolve({
    status: 200,
    data: { success: true, data: config.url },
    headers: {},
    config,
  });
}

beforeEach(() => {
  token = 'viejo';
  peticionesRecibidas = [];
  http.defaults.adapter = adaptadorFalso;
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('interceptor de 401', () => {
  test('refresca UNA sola vez aunque fallen varias peticiones a la vez, y las reintenta', async () => {
    const refrescar = vi.fn(async () => {
      await new Promise((r) => setTimeout(r, 20));
      token = 'nuevo';
    });
    configurarSesionHttp({ obtenerToken: () => token, refrescar, alExpirar: vi.fn() });

    const resultados = await Promise.all([http.get('/a'), http.get('/b'), http.get('/c')]);

    expect(refrescar).toHaveBeenCalledTimes(1);
    expect(resultados.map((r) => r.data.data)).toEqual(['/a', '/b', '/c']);
  });

  test('si el refresh falla, avisa que la sesión expiró y propaga el error', async () => {
    const alExpirar = vi.fn();
    configurarSesionHttp({
      obtenerToken: () => token,
      refrescar: vi.fn().mockRejectedValue(new Error('sin sesión')),
      alExpirar,
    });

    await expect(http.get('/a')).rejects.toMatchObject({ response: { status: 401 } });
    expect(alExpirar).toHaveBeenCalled();
  });

  test('no reintenta infinitamente: si tras refrescar vuelve a dar 401, se rinde', async () => {
    const refrescar = vi.fn().mockResolvedValue(); // "refresca" pero el token sigue siendo viejo
    configurarSesionHttp({ obtenerToken: () => token, refrescar, alExpirar: vi.fn() });

    await expect(http.get('/a')).rejects.toMatchObject({ response: { status: 401 } });
    expect(refrescar).toHaveBeenCalledTimes(1);
    expect(peticionesRecibidas).toHaveLength(2); // original + un reintento
  });

  test('el token va en la cabecera Authorization, nunca en la URL', async () => {
    token = 'nuevo';
    configurarSesionHttp({ obtenerToken: () => token });
    await http.get('/clientes', { params: { pagina: 1 } });
    expect(peticionesRecibidas[0].auth).toBe('Bearer nuevo');
    expect(peticionesRecibidas[0].url).not.toContain('nuevo');
  });
});

describe('CSRF', () => {
  test('httpAuth copia la cookie rv_csrf en la cabecera X-CSRF-Token', async () => {
    document.cookie = 'rv_csrf=valor-csrf';
    let cabecera;
    httpAuth.defaults.adapter = (config) => {
      cabecera = config.headers['X-CSRF-Token'];
      return Promise.resolve({ status: 200, data: {}, headers: {}, config });
    };
    await httpAuth.post('/auth/refresh');
    expect(cabecera).toBe('valor-csrf');
    expect(leerCookie('rv_csrf')).toBe('valor-csrf');
    document.cookie = 'rv_csrf=; expires=Thu, 01 Jan 1970 00:00:00 GMT';
  });

  test('las peticiones van con credenciales (cookies)', () => {
    expect(http.defaults.withCredentials).toBe(true);
    expect(httpAuth.defaults.withCredentials).toBe(true);
  });
});
