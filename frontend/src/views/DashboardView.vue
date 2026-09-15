<script setup>
import { computed, onMounted } from 'vue';
import { useAuthStore } from '@/stores/auth';
import { usePeticion } from '@/composables/usePeticion';
import { buscarMascotas } from '@/services/api/mascotas';
import { ROLES } from '@/utils/roles';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';

const auth = useAuthStore();
const esCliente = computed(() => auth.rol === ROLES.CLIENTE);

// Accesos rápidos según el rol (solo interfaz; el backend valida cada acción).
const ACCESOS = [
  {
    nombre: 'clientes',
    titulo: 'Clientes',
    texto: 'Buscar, registrar y actualizar clientes.',
    roles: [ROLES.ADMIN, ROLES.RECEPCIONISTA, ROLES.VETERINARIO],
  },
  {
    nombre: 'cliente-nuevo',
    titulo: 'Nuevo cliente',
    texto: 'Registrar un cliente y, si quiere, su cuenta web.',
    roles: [ROLES.ADMIN, ROLES.RECEPCIONISTA],
  },
  {
    nombre: 'mascotas',
    titulo: 'Mascotas',
    texto: 'Consultar las mascotas registradas.',
    roles: [ROLES.ADMIN, ROLES.RECEPCIONISTA, ROLES.VETERINARIO],
  },
  {
    nombre: 'usuarios',
    titulo: 'Usuarios',
    texto: 'Dar de alta al personal y gestionar accesos.',
    roles: [ROLES.ADMIN],
  },
  {
    nombre: 'auditoria',
    titulo: 'Auditoría',
    texto: 'Quién hizo qué y cuándo.',
    roles: [ROLES.ADMIN],
  },
];
const accesos = computed(() => ACCESOS.filter((a) => auth.tieneRol(a.roles)));

// Para el cliente: resumen de sus mascotas (el backend solo devuelve las suyas).
const mascotas = usePeticion(() => buscarMascotas({ limite: 6 }));
onMounted(() => {
  if (esCliente.value) mascotas.ejecutar().catch(() => {});
});

const primerNombre = computed(
  () => (auth.usuario?.nombre || '').replace(/^(Dra?\.)\s*/i, '').split(' ')[0],
);
</script>

<template>
  <div>
    <EncabezadoPagina :titulo="`Hola, ${primerNombre}`" subtitulo="¿Qué quieres hacer hoy?" />

    <section v-if="esCliente" aria-labelledby="titulo-mis-mascotas">
      <h2 id="titulo-mis-mascotas" class="mb-3 text-lg font-semibold text-slate-800">
        Mis mascotas
      </h2>
      <EstadoCarga
        :cargando="mascotas.cargando.value"
        :error="mascotas.error.value"
        :vacio="mascotas.datos.value?.items.length === 0"
        texto-vacio="Aún no tienes mascotas registradas. Acércate a recepción para registrarlas."
        @reintentar="mascotas.ejecutar()"
      >
        <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <li v-for="m in mascotas.datos.value?.items" :key="m.id">
            <RouterLink
              :to="{ name: 'mascota-detalle', params: { id: m.id } }"
              class="block rounded-xl bg-white p-5 shadow-sm ring-1 ring-slate-200 hover:ring-teal-400"
            >
              <p class="text-base font-semibold text-slate-900">{{ m.nombre }}</p>
              <p class="text-sm capitalize text-slate-500">
                {{ m.especie }} · {{ m.raza || 'Sin raza' }}
              </p>
            </RouterLink>
          </li>
        </ul>
      </EstadoCarga>
    </section>

    <section v-else aria-label="Accesos rápidos">
      <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <li v-for="acceso in accesos" :key="acceso.nombre">
          <RouterLink
            :to="{ name: acceso.nombre }"
            class="block h-full rounded-xl bg-white p-5 shadow-sm ring-1 ring-slate-200 transition hover:ring-teal-400"
          >
            <p class="text-base font-semibold text-teal-800">{{ acceso.titulo }}</p>
            <p class="mt-1 text-sm text-slate-500">{{ acceso.texto }}</p>
          </RouterLink>
        </li>
      </ul>
      <p class="mt-8 text-xs text-slate-400">
        Citas, historial clínico, productos y ventas llegan en los próximos sprints.
      </p>
    </section>
  </div>
</template>
