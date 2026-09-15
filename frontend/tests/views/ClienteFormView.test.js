import { beforeEach, describe, expect, test, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import ClienteFormView from '@/views/clientes/ClienteFormView.vue';
import * as clientesApi from '@/services/api/clientes';

vi.mock('@/services/api/clientes', () => ({
  crearCliente: vi.fn(),
  actualizarCliente: vi.fn(),
  obtenerCliente: vi.fn(),
}));

async function montar(props = {}) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const vacio = { template: '<div />' };
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/clientes', name: 'clientes', component: vacio },
      { path: '/clientes/:id', name: 'cliente-detalle', component: vacio },
    ],
  });
  const wrapper = mount(ClienteFormView, { props, global: { plugins: [pinia, router] } });
  await flushPromises();
  return { wrapper, router };
}

async function llenar(wrapper, valores) {
  for (const [etiqueta, valor] of Object.entries(valores)) {
    const label = wrapper.findAll('label').find((l) => l.text().startsWith(etiqueta));
    await wrapper.get(`[id="${label.attributes('for')}"]`).setValue(valor);
  }
}

const DATOS = {
  Nombres: 'Carmen',
  Apellidos: 'Quispe',
  DNI: '75896321',
  Celular: '987111222',
  Email: 'carmen@correo.pe',
};

describe('ClienteFormView', () => {
  beforeEach(() => vi.clearAllMocks());

  test('no envía nada si falta el consentimiento (Ley 29733)', async () => {
    const { wrapper } = await montar();
    await llenar(wrapper, DATOS);
    await wrapper.get('form').trigger('submit');
    expect(clientesApi.crearCliente).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('Se requiere el consentimiento');
  });

  test('envía SOLO los campos que acepta la API (sin password si no crea cuenta)', async () => {
    clientesApi.crearCliente.mockResolvedValue({ id: 9 });
    const { wrapper, router } = await montar();
    const push = vi.spyOn(router, 'push');
    await llenar(wrapper, DATOS);
    await wrapper.findAll('input[type="checkbox"]')[0].setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(clientesApi.crearCliente).toHaveBeenCalledWith({
      dni: '75896321',
      nombres: 'Carmen',
      apellidos: 'Quispe',
      telefono: '987111222',
      email: 'carmen@correo.pe',
      direccion: null,
      consentimientoDatos: true,
      crearCuenta: false,
    });
    expect(push).toHaveBeenCalledWith({ name: 'cliente-detalle', params: { id: 9 } });
  });

  test('muestra junto a cada campo los errores 422 del backend', async () => {
    clientesApi.crearCliente.mockRejectedValue({
      response: {
        status: 409,
        data: { message: 'Ya existe un cliente con ese DNI', errors: [] },
        headers: {},
      },
    });
    const { wrapper } = await montar();
    await llenar(wrapper, DATOS);
    await wrapper.findAll('input[type="checkbox"]')[0].setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    expect(wrapper.get('[role="alert"]').text()).toBe('Ya existe un cliente con ese DNI');
  });

  test('en edición carga los datos y no envía campos de alta', async () => {
    clientesApi.obtenerCliente.mockResolvedValue({
      id: 3,
      dni: '70123456',
      nombres: 'Ana',
      apellidos: 'Torres',
      telefono: '987654321',
      email: 'ana@correo.pe',
      direccion: null,
      tieneCuenta: true,
    });
    clientesApi.actualizarCliente.mockResolvedValue({ id: 3 });
    const { wrapper } = await montar({ id: '3' });
    expect(wrapper.text()).not.toContain('Crear cuenta web');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    const [, cuerpo] = clientesApi.actualizarCliente.mock.calls[0];
    expect(Object.keys(cuerpo).sort()).toEqual(
      ['apellidos', 'direccion', 'dni', 'email', 'nombres', 'telefono'].sort(),
    );
  });
});
