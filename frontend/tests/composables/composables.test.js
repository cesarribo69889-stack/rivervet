import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { mount } from '@vue/test-utils';
import { useInactividad } from '@/composables/useInactividad';
import { useConfirmacion } from '@/composables/useConfirmacion';
import { usePeticion } from '@/composables/usePeticion';

describe('useInactividad (cierre a los N minutos)', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function montar(alExpirar) {
    let api;
    const Comp = defineComponent({
      setup() {
        api = useInactividad({ minutos: 30, alExpirar });
        return () => h('div');
      },
    });
    const wrapper = mount(Comp);
    return { wrapper, api: () => api };
  }

  test('avisa un minuto antes y cierra a los 30 minutos sin actividad', () => {
    const alExpirar = vi.fn();
    const { api } = montar(alExpirar);
    vi.advanceTimersByTime(29 * 60 * 1000);
    expect(api().mostrarAviso.value).toBe(true);
    expect(alExpirar).not.toHaveBeenCalled();
    vi.advanceTimersByTime(60 * 1000);
    expect(alExpirar).toHaveBeenCalledTimes(1);
  });

  test('la actividad del usuario reinicia el contador', () => {
    const alExpirar = vi.fn();
    montar(alExpirar);
    vi.advanceTimersByTime(20 * 60 * 1000);
    window.dispatchEvent(new Event('keydown'));
    vi.advanceTimersByTime(20 * 60 * 1000);
    expect(alExpirar).not.toHaveBeenCalled();
  });

  test('"Seguir conectado" cierra el aviso y reinicia', () => {
    const alExpirar = vi.fn();
    const { api } = montar(alExpirar);
    vi.advanceTimersByTime(29 * 60 * 1000 + 1000);
    api().seguirConectado();
    expect(api().mostrarAviso.value).toBe(false);
    vi.advanceTimersByTime(29 * 60 * 1000);
    expect(alExpirar).not.toHaveBeenCalled();
  });
});

describe('useConfirmacion', () => {
  test('resuelve true al aceptar y false al cancelar', async () => {
    const { confirmar, aceptar, cancelar, estado } = useConfirmacion();
    const p1 = confirmar({ titulo: 'Eliminar', mensaje: '¿Seguro?' });
    expect(estado.visible).toBe(true);
    aceptar();
    expect(await p1).toBe(true);

    const p2 = confirmar({ titulo: 'Eliminar', mensaje: '¿Seguro?' });
    cancelar();
    expect(await p2).toBe(false);
    expect(estado.visible).toBe(false);
  });
});

describe('usePeticion', () => {
  test('maneja cargando, datos y error con mensaje amigable', async () => {
    const funcion = vi
      .fn()
      .mockResolvedValueOnce('ok')
      .mockRejectedValueOnce(new Error('Network Error'));
    const { datos, cargando, error, ejecutar } = usePeticion(funcion);

    const promesa = ejecutar();
    expect(cargando.value).toBe(true);
    await promesa;
    expect(datos.value).toBe('ok');
    expect(cargando.value).toBe(false);

    await expect(ejecutar()).rejects.toThrow();
    expect(error.value).toMatch(/No se pudo conectar/);
  });

  test('ignora respuestas viejas si llegó una búsqueda más nueva', async () => {
    let resolverLenta;
    const funcion = vi
      .fn()
      .mockImplementationOnce(() => new Promise((r) => (resolverLenta = r)))
      .mockResolvedValueOnce('nueva');
    const { datos, ejecutar } = usePeticion(funcion);
    const lenta = ejecutar();
    await ejecutar();
    resolverLenta('vieja');
    await lenta;
    expect(datos.value).toBe('nueva');
  });
});
