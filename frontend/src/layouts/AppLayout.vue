<script setup>
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import { useInactividad } from '@/composables/useInactividad';
import { ETIQUETAS_ROL, ROLES, PERSONAL } from '@/utils/roles';
import BaseModal from '@/components/base/BaseModal.vue';
import BaseButton from '@/components/base/BaseButton.vue';

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();
const menuAbierto = ref(false);

// Menú según el rol.
// OJO: ocultar una opción del menú es solo comodidad visual. Si alguien
// escribe la URL a mano, el guard del router lo frena; y si se salta el
// guard, el backend rechaza la petición con 403. La seguridad real está allá.
const OPCIONES = [
  { nombre: 'inicio', texto: 'Inicio', roles: null },
  { nombre: 'clientes', texto: 'Clientes', roles: PERSONAL },
  { nombre: 'mascotas', texto: 'Mascotas', roles: PERSONAL },
  { nombre: 'mis-mascotas', texto: 'Mis mascotas', roles: [ROLES.CLIENTE] },
  { nombre: 'usuarios', texto: 'Usuarios', roles: [ROLES.ADMIN] },
  { nombre: 'auditoria', texto: 'Auditoría', roles: [ROLES.ADMIN] },
  { nombre: 'perfil', texto: 'Mi perfil', roles: null },
];

const opcionesVisibles = computed(() => OPCIONES.filter((o) => !o.roles || auth.tieneRol(o.roles)));

// "Clientes" también se resalta en /clientes/5 o /clientes/5/editar.
function estaActiva(nombre) {
  const ruta = router.resolve({ name: nombre }).path;
  if (ruta === '/') return route.path === '/';
  return route.path === ruta || route.path.startsWith(`${ruta}/`);
}

const iniciales = computed(() =>
  (auth.usuario?.nombre || '?')
    .replace(/^(Dra?\.)\s*/i, '')
    .split(' ')
    .slice(0, 2)
    .map((p) => p[0])
    .join('')
    .toUpperCase(),
);

// En móvil, el menú se cierra al navegar.
watch(
  () => route.fullPath,
  () => {
    menuAbierto.value = false;
  },
);

async function cerrarSesion(motivo = null) {
  await auth.logout(motivo);
  router.push({ name: 'login' });
}

const minutos = Number(import.meta.env.VITE_INACTIVIDAD_MINUTOS) || 30;
const { mostrarAviso, seguirConectado } = useInactividad({
  minutos,
  alExpirar: () => cerrarSesion('inactividad'),
});
</script>

<template>
  <div class="min-h-screen lg:flex">
    <!-- Barra superior (móvil) -->
    <div class="flex items-center justify-between bg-teal-800 px-4 py-3 text-white lg:hidden">
      <span class="text-lg font-bold">RiverVet</span>
      <button
        type="button"
        class="rounded-md p-2 hover:bg-teal-700"
        :aria-expanded="menuAbierto"
        aria-controls="menu-lateral"
        @click="menuAbierto = !menuAbierto"
      >
        <span class="sr-only">{{ menuAbierto ? 'Cerrar menú' : 'Abrir menú' }}</span>
        <svg
          class="h-6 w-6"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          aria-hidden="true"
        >
          <path stroke-linecap="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
        </svg>
      </button>
    </div>

    <!-- Menú lateral -->
    <aside
      id="menu-lateral"
      class="bg-teal-800 text-teal-50 lg:sticky lg:top-0 lg:flex lg:h-screen lg:w-64 lg:flex-col"
      :class="menuAbierto ? 'block' : 'hidden lg:flex'"
    >
      <div class="hidden px-6 py-6 lg:block">
        <p class="text-xl font-bold text-white">RiverVet</p>
        <p class="text-xs text-teal-200">Clínica veterinaria</p>
      </div>
      <nav class="flex-1 space-y-1 px-3 py-3" aria-label="Menú principal">
        <RouterLink
          v-for="opcion in opcionesVisibles"
          :key="opcion.nombre"
          :to="{ name: opcion.nombre }"
          class="block rounded-lg px-3 py-2 text-sm font-medium"
          :class="estaActiva(opcion.nombre) ? 'bg-teal-900 text-white' : 'hover:bg-teal-700'"
          :aria-current="estaActiva(opcion.nombre) ? 'page' : undefined"
          active-class=""
          exact-active-class=""
        >
          {{ opcion.texto }}
        </RouterLink>
      </nav>
      <div class="border-t border-teal-700 px-4 py-4">
        <div class="flex items-center gap-3">
          <span
            class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-teal-600 text-sm font-semibold text-white"
            aria-hidden="true"
          >
            {{ iniciales }}
          </span>
          <div class="min-w-0">
            <p class="truncate text-sm font-medium text-white">{{ auth.usuario?.nombre }}</p>
            <p class="text-xs text-teal-200">{{ ETIQUETAS_ROL[auth.rol] }}</p>
          </div>
        </div>
        <button
          type="button"
          class="mt-3 w-full rounded-lg px-3 py-2 text-left text-sm font-medium text-teal-100 ring-1 ring-teal-600 hover:bg-teal-700"
          @click="cerrarSesion()"
        >
          Cerrar sesión
        </button>
      </div>
    </aside>

    <main id="contenido" class="min-w-0 flex-1 px-4 py-6 sm:px-6 lg:px-10 lg:py-8">
      <RouterView />
    </main>

    <BaseModal
      :abierto="mostrarAviso"
      titulo="¿Sigues ahí?"
      ancho="max-w-md"
      @cerrar="seguirConectado"
    >
      <p class="text-sm text-slate-600">
        Por seguridad, tu sesión se cerrará en 1 minuto por inactividad.
      </p>
      <template #pie>
        <BaseButton variante="secundario" @click="cerrarSesion()">Cerrar sesión</BaseButton>
        <BaseButton @click="seguirConectado">Seguir conectado</BaseButton>
      </template>
    </BaseModal>
  </div>
</template>
