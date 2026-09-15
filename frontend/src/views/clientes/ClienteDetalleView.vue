<script setup>
// Ficha del cliente (HU-003) con sus mascotas (HU-005).
import { onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import { useNotificacionesStore } from '@/stores/notificaciones';
import { useConfirmacion } from '@/composables/useConfirmacion';
import { usePeticion } from '@/composables/usePeticion';
import { eliminarCliente, listarMascotasDeCliente, obtenerCliente } from '@/services/api/clientes';
import { ROLES } from '@/utils/roles';
import { mensajeError } from '@/utils/errores';
import { calcularEdad, capitalizar, formatearFechaHora } from '@/utils/formato';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import BadgeEstado from '@/components/base/BadgeEstado.vue';
import BaseButton from '@/components/base/BaseButton.vue';

const props = defineProps({ id: { type: String, required: true } });

const auth = useAuthStore();
const router = useRouter();
const notificaciones = useNotificacionesStore();
const { confirmar } = useConfirmacion();

// Botones visibles según el rol. Solo interfaz: el backend responde 403 a
// quien intente la acción sin permiso.
const puedeEditar = auth.tieneRol(ROLES.ADMIN, ROLES.RECEPCIONISTA);
const puedeEliminar = auth.tieneRol(ROLES.ADMIN);

const cliente = usePeticion(obtenerCliente);
const mascotas = usePeticion(listarMascotasDeCliente);

function cargar() {
  cliente.ejecutar(props.id).catch(() => {});
  mascotas.ejecutar(props.id).catch(() => {});
}
onMounted(cargar);

async function eliminar() {
  const c = cliente.datos.value;
  const ok = await confirmar({
    titulo: 'Eliminar cliente',
    mensaje: `Se dará de baja a ${c.nombres} ${c.apellidos} y a sus mascotas. Si tiene cuenta web, perderá el acceso. ¿Continuar?`,
    textoConfirmar: 'Eliminar',
  });
  if (!ok) return;
  try {
    await eliminarCliente(props.id);
    notificaciones.exito('Cliente eliminado');
    router.push({ name: 'clientes' });
  } catch (err) {
    notificaciones.error(mensajeError(err));
  }
}
</script>

<template>
  <div class="max-w-5xl">
    <EstadoCarga
      :cargando="cliente.cargando.value"
      :error="cliente.error.value"
      @reintentar="cargar"
    >
      <template v-if="cliente.datos.value">
        <EncabezadoPagina
          :titulo="`${cliente.datos.value.nombres} ${cliente.datos.value.apellidos}`"
          :subtitulo="`DNI ${cliente.datos.value.dni}`"
        >
          <template #migas>
            <RouterLink :to="{ name: 'clientes' }" class="text-sm text-teal-700 hover:underline">
              ← Clientes
            </RouterLink>
          </template>
          <template #acciones>
            <RouterLink
              v-if="puedeEditar"
              :to="{ name: 'cliente-editar', params: { id: props.id } }"
              class="inline-flex min-h-10 items-center rounded-lg bg-white px-4 py-2 text-sm font-semibold text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50"
            >
              Editar
            </RouterLink>
            <BaseButton v-if="puedeEliminar" variante="peligro" @click="eliminar"
              >Eliminar</BaseButton
            >
          </template>
        </EncabezadoPagina>

        <section
          class="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200"
          aria-label="Datos del cliente"
        >
          <dl class="grid gap-4 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <div>
              <dt class="text-slate-500">Celular</dt>
              <dd class="font-medium text-slate-900">{{ cliente.datos.value.telefono }}</dd>
            </div>
            <div>
              <dt class="text-slate-500">Email</dt>
              <dd class="font-medium break-all text-slate-900">
                {{ cliente.datos.value.email || '—' }}
              </dd>
            </div>
            <div>
              <dt class="text-slate-500">Cuenta web</dt>
              <dd>
                <BadgeEstado :color="cliente.datos.value.tieneCuenta ? 'teal' : 'gris'">
                  {{ cliente.datos.value.tieneCuenta ? 'Activa' : 'Sin cuenta' }}
                </BadgeEstado>
              </dd>
            </div>
            <div class="sm:col-span-2">
              <dt class="text-slate-500">Dirección</dt>
              <dd class="font-medium text-slate-900">{{ cliente.datos.value.direccion || '—' }}</dd>
            </div>
            <div>
              <dt class="text-slate-500">Consentimiento de datos</dt>
              <dd class="font-medium text-slate-900">
                {{ formatearFechaHora(cliente.datos.value.consentimientoEn) }}
              </dd>
            </div>
          </dl>
        </section>

        <section class="mt-8" aria-labelledby="titulo-mascotas">
          <div class="mb-3 flex items-center justify-between gap-3">
            <h2 id="titulo-mascotas" class="text-lg font-semibold text-slate-900">Mascotas</h2>
            <RouterLink
              v-if="puedeEditar"
              :to="{ name: 'mascota-nueva', params: { clienteId: props.id } }"
              class="inline-flex min-h-10 items-center rounded-lg bg-teal-700 px-4 py-2 text-sm font-semibold text-white hover:bg-teal-800"
            >
              Agregar mascota
            </RouterLink>
          </div>
          <EstadoCarga
            :cargando="mascotas.cargando.value"
            :error="mascotas.error.value"
            :vacio="mascotas.datos.value?.items.length === 0"
            texto-vacio="Este cliente todavía no tiene mascotas registradas."
            @reintentar="cargar"
          >
            <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <li v-for="m in mascotas.datos.value?.items" :key="m.id">
                <RouterLink
                  :to="{ name: 'mascota-detalle', params: { id: m.id } }"
                  class="block rounded-xl bg-white p-5 shadow-sm ring-1 ring-slate-200 hover:ring-teal-400"
                >
                  <p class="text-base font-semibold text-slate-900">{{ m.nombre }}</p>
                  <p class="text-sm text-slate-500">
                    {{ capitalizar(m.especie) }} · {{ m.raza || 'Sin raza' }}
                  </p>
                  <p class="mt-2 text-xs text-slate-400">
                    Edad: {{ calcularEdad(m.fechaNacimiento) }}
                  </p>
                </RouterLink>
              </li>
            </ul>
          </EstadoCarga>
        </section>
      </template>
    </EstadoCarga>
  </div>
</template>
