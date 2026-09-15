import { createRouter, createWebHistory } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import { ROLES, PERSONAL, TODOS } from '@/utils/roles';
import { crearGuardia } from './guardia';

const { ADMIN, RECEPCIONISTA, CLIENTE } = ROLES;

// Las vistas se cargan bajo demanda (import dinámico): el navegador descarga
// el código de una pantalla solo cuando se visita.
export const rutas = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { soloInvitados: true, titulo: 'Iniciar sesión' },
  },
  {
    path: '/',
    component: () => import('@/layouts/AppLayout.vue'),
    children: [
      {
        path: '',
        name: 'inicio',
        component: () => import('@/views/DashboardView.vue'),
        meta: { roles: TODOS, titulo: 'Inicio' },
      },
      {
        path: 'perfil',
        name: 'perfil',
        component: () => import('@/views/PerfilView.vue'),
        meta: { roles: TODOS, titulo: 'Mi perfil' },
      },

      // Recepción / personal
      {
        path: 'clientes',
        name: 'clientes',
        component: () => import('@/views/clientes/ClientesView.vue'),
        meta: { roles: PERSONAL, titulo: 'Clientes' },
      },
      {
        path: 'clientes/nuevo',
        name: 'cliente-nuevo',
        component: () => import('@/views/clientes/ClienteFormView.vue'),
        meta: { roles: [ADMIN, RECEPCIONISTA], titulo: 'Nuevo cliente' },
      },
      {
        path: 'clientes/:id(\\d+)',
        name: 'cliente-detalle',
        component: () => import('@/views/clientes/ClienteDetalleView.vue'),
        props: true,
        meta: { roles: PERSONAL, titulo: 'Cliente' },
      },
      {
        path: 'clientes/:id(\\d+)/editar',
        name: 'cliente-editar',
        component: () => import('@/views/clientes/ClienteFormView.vue'),
        props: true,
        meta: { roles: [ADMIN, RECEPCIONISTA], titulo: 'Editar cliente' },
      },
      {
        path: 'clientes/:clienteId(\\d+)/mascotas/nueva',
        name: 'mascota-nueva',
        component: () => import('@/views/mascotas/MascotaFormView.vue'),
        props: true,
        meta: { roles: [ADMIN, RECEPCIONISTA], titulo: 'Nueva mascota' },
      },
      {
        path: 'mascotas',
        name: 'mascotas',
        component: () => import('@/views/mascotas/MascotasView.vue'),
        meta: { roles: PERSONAL, titulo: 'Mascotas' },
      },
      {
        path: 'mascotas/:id(\\d+)',
        name: 'mascota-detalle',
        component: () => import('@/views/mascotas/MascotaDetalleView.vue'),
        props: true,
        meta: { roles: TODOS, titulo: 'Mascota' },
      },
      {
        path: 'mascotas/:id(\\d+)/editar',
        name: 'mascota-editar',
        component: () => import('@/views/mascotas/MascotaFormView.vue'),
        props: true,
        meta: { roles: PERSONAL, titulo: 'Editar mascota' },
      },

      // Cliente
      {
        path: 'mis-mascotas',
        name: 'mis-mascotas',
        component: () => import('@/views/mascotas/MisMascotasView.vue'),
        meta: { roles: [CLIENTE], titulo: 'Mis mascotas' },
      },

      // Admin
      {
        path: 'usuarios',
        name: 'usuarios',
        component: () => import('@/views/admin/UsuariosView.vue'),
        meta: { roles: [ADMIN], titulo: 'Usuarios' },
      },
      {
        path: 'auditoria',
        name: 'auditoria',
        component: () => import('@/views/admin/AuditoriaView.vue'),
        meta: { roles: [ADMIN], titulo: 'Auditoría' },
      },

      {
        path: 'sin-permisos',
        name: 'sin-permisos',
        component: () => import('@/views/SinPermisosView.vue'),
        meta: { titulo: 'Sin permisos' },
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'no-encontrado',
    component: () => import('@/views/NotFoundView.vue'),
    meta: { publica: true, titulo: 'Página no encontrada' },
  },
];

export function crearRouter(historial = createWebHistory()) {
  const router = createRouter({
    history: historial,
    routes: rutas,
    scrollBehavior: () => ({ top: 0 }),
  });
  router.beforeEach(crearGuardia(useAuthStore));
  router.afterEach((to) => {
    document.title = to.meta.titulo ? `${to.meta.titulo} · RiverVet` : 'RiverVet';
  });
  return router;
}
