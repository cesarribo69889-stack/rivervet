import { describe, expect, test } from 'vitest';
import { mount } from '@vue/test-utils';
import BaseInput from '@/components/base/BaseInput.vue';
import TablaPaginada from '@/components/base/TablaPaginada.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';

describe('BaseInput (accesibilidad)', () => {
  test('el label está asociado al input', () => {
    const w = mount(BaseInput, { props: { etiqueta: 'DNI', modelValue: '' } });
    const input = w.get('input');
    expect(w.get('label').attributes('for')).toBe(input.attributes('id'));
  });

  test('con error: aria-invalid y aria-describedby apuntan al mensaje', () => {
    const w = mount(BaseInput, {
      props: { etiqueta: 'DNI', modelValue: '', error: 'DNI inválido' },
    });
    const input = w.get('input');
    expect(input.attributes('aria-invalid')).toBe('true');
    const idError = input.attributes('aria-describedby');
    expect(w.get(`[id="${idError}"]`).text()).toBe('DNI inválido');
  });

  test('v-model emite el valor escrito', async () => {
    const w = mount(BaseInput, { props: { etiqueta: 'Nombre', modelValue: '' } });
    await w.get('input').setValue('Ana');
    expect(w.emitted('update:modelValue')[0]).toEqual(['Ana']);
  });

  test('los atributos extra van al input, no al contenedor', () => {
    const w = mount(BaseInput, { props: { etiqueta: 'DNI' }, attrs: { maxlength: '8' } });
    expect(w.get('input').attributes('maxlength')).toBe('8');
  });

  test('el texto se muestra escapado (sin interpretar HTML)', () => {
    const w = mount(BaseInput, {
      props: { etiqueta: 'X', modelValue: '', error: '<img src=x onerror=alert(1)>' },
    });
    expect(w.find('img').exists()).toBe(false);
  });
});

describe('TablaPaginada', () => {
  const props = {
    columnas: [{ clave: 'nombre', titulo: 'Nombre' }],
    filas: [
      { id: 1, nombre: 'Ana' },
      { id: 2, nombre: 'Luis' },
    ],
    paginacion: { pagina: 1, limite: 2, total: 5, totalPaginas: 3 },
    descripcion: 'Clientes',
  };

  test('muestra el rango y emite el cambio de página', async () => {
    const w = mount(TablaPaginada, { props });
    expect(w.text()).toContain('1–2 de 5');
    const [anterior, siguiente] = w.findAll('nav button');
    expect(anterior.attributes('disabled')).toBeDefined();
    await siguiente.trigger('click');
    expect(w.emitted('cambiar-pagina')[0]).toEqual([2]);
  });
});

describe('EstadoCarga', () => {
  test('carga, error con reintento y vacío', async () => {
    expect(mount(EstadoCarga, { props: { cargando: true } }).text()).toContain('Cargando');

    const conError = mount(EstadoCarga, { props: { error: 'Sin conexión' } });
    expect(conError.get('[role="alert"]').text()).toContain('Sin conexión');
    await conError.get('button').trigger('click');
    expect(conError.emitted('reintentar')).toHaveLength(1);

    expect(mount(EstadoCarga, { props: { vacio: true, textoVacio: 'Nada' } }).text()).toContain(
      'Nada',
    );
  });
});
