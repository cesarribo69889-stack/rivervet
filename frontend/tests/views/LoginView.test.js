import { beforeEach, describe, expect, test, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import LoginView from '@/views/LoginView.vue';
import { useAuthStore } from '@/stores/auth';

function montar() {
  const pinia = createPinia();
  setActivePinia(pinia);
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'inicio', component: { template: '<div>inicio</div>' } },
      { path: '/login', name: 'login', component: LoginView },
    ],
  });
  const wrapper = mount(LoginView, { global: { plugins: [pinia, router] } });
  return { wrapper, router, auth: useAuthStore() };
}

describe('LoginView', () => {
  beforeEach(() => vi.restoreAllMocks());

  test('valida en el cliente antes de llamar a la API', async () => {
    const { wrapper, auth } = montar();
    const login = vi.spyOn(auth, 'login');
    await wrapper.get('form').trigger('submit');
    expect(login).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('El email es obligatorio');
    expect(wrapper.text()).toContain('La contraseña es obligatoria');
  });

  test('con datos válidos inicia sesión y va al inicio', async () => {
    const { wrapper, auth, router } = montar();
    const login = vi.spyOn(auth, 'login').mockResolvedValue();
    const push = vi.spyOn(router, 'push');
    const [email, password] = wrapper.findAll('input');
    await email.setValue('  recepcion@rivervet.pe ');
    await password.setValue('RiverVet#2026');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    expect(login).toHaveBeenCalledWith('recepcion@rivervet.pe', 'RiverVet#2026');
    expect(push).toHaveBeenCalledWith({ name: 'inicio' });
  });

  test('credenciales incorrectas: muestra el mensaje del backend y borra la contraseña', async () => {
    const { wrapper, auth } = montar();
    vi.spyOn(auth, 'login').mockRejectedValue({
      response: { status: 401, data: { message: 'Email o contraseña incorrectos' }, headers: {} },
    });
    const [email, password] = wrapper.findAll('input');
    await email.setValue('ana@correo.pe');
    await password.setValue('mala');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    expect(wrapper.get('[role="alert"]').text()).toBe('Email o contraseña incorrectos');
    expect(password.element.value).toBe('');
  });

  test('avisa si la sesión se cerró por inactividad', async () => {
    const { wrapper, auth } = montar();
    auth.limpiarSesion('inactividad');
    await flushPromises();
    expect(wrapper.text()).toContain('por inactividad');
  });
});
