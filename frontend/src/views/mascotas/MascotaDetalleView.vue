<script setup>
// Ficha de una mascota. La ven el personal y el dueño. Si un cliente escribe
// en la URL el id de una mascota ajena, el backend responde 404 (control de
// propiedad en SQL) y aquí se muestra "no encontrada".
import { onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import { useNotificacionesStore } from '@/stores/notificaciones';
import { useConfirmacion } from '@/composables/useConfirmacion';
import { usePeticion } from '@/composables/usePeticion';
import { eliminarMascota, obtenerMascota } from '@/services/api/mascotas';
import { PERSONAL, ROLES } from '@/utils/roles';
import { mensajeError } from '@/utils/errores';
import { calcularEdad, capitalizar, formatearFecha } from '@/utils/formato';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import BaseButton from '@/components/base/BaseButton.vue';

const props = defineProps({ id: { type: String, required: true } });

const auth = useAuthStore();
const router = useRouter();
const notificaciones = useNotificacionesStore();
const { confirmar } = useConfirmacion();

// Visibilidad de botones (UX). El backend valida el rol de todas formas.
const esPersonal = auth.tieneRol(PERSONAL);
const puedeEliminar = auth.tieneRol(ROLES.ADMIN, ROLES.RECEPCIONISTA);

const { datos: mascota, cargando, error, ejecutar } = usePeticion(obtenerMascota);
const cargar = () => ejecutar(props.id).catch(() => {});
onMounted(cargar);

function volver() {
  if (esPersonal) router.push({ name: 'cliente-detalle', params: { id: mascota.value.clienteId } });
  else router.push({ name: 'mis-mascotas' });
}

async function eliminar() {
  const ok = await confirmar({
    titulo: 'Eliminar mascota',
    mensaje: `¿Dar de baja a ${mascota.value.nombre}? Dejará de aparecer en los listados.`,
    textoConfirmar: 'Eliminar',
  });
  if (!ok) return;
  try {
    const clienteId = mascota.value.clienteId;
    await eliminarMascota(props.id);
    notificaciones.exito('Mascota eliminada');
    router.push({ name: 'cliente-detalle', params: { id: clienteId } });
  } catch (err) {
    notificaciones.error(mensajeError(err));
  }
}

const SEXOS = { macho: 'Macho', hembra: 'Hembra' };
</script>

<template>
  <div class="max-w-4xl">
    <EstadoCarga :cargando="cargando" :error="error" @reintentar="cargar">
      <template v-if="mascota">
        <EncabezadoPagina
          :titulo="mascota.nombre"
          :subtitulo="`${capitalizar(mascota.especie)}${mascota.raza ? ` · ${mascota.raza}` : ''}`"
        >
          <template #migas>
            <button type="button" class="text-sm text-teal-700 hover:underline" @click="volver">
              ← {{ esPersonal ? mascota.clienteNombre : 'Mis mascotas' }}
            </button>
          </template>
          <template v-if="esPersonal" #acciones>
            <RouterLink
              :to="{ name: 'mascota-editar', params: { id: props.id } }"
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
          aria-label="Datos de la mascota"
        >
          <dl class="grid gap-5 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <div>
              <dt class="text-slate-500">Sexo</dt>
              <dd class="font-medium text-slate-900">
                {{ SEXOS[mascota.sexo] || 'Sin especificar' }}
              </dd>
            </div>
            <div>
              <dt class="text-slate-500">Fecha de nacimiento</dt>
              <dd class="font-medium text-slate-900">
                {{ formatearFecha(mascota.fechaNacimiento) }}
                <span v-if="mascota.fechaNacimiento" class="text-slate-400">
                  ({{ calcularEdad(mascota.fechaNacimiento) }})
                </span>
              </dd>
            </div>
            <div>
              <dt class="text-slate-500">Peso</dt>
              <dd class="font-medium text-slate-900">
                {{ mascota.pesoKg ? `${mascota.pesoKg} kg` : '—' }}
              </dd>
            </div>
            <div>
              <dt class="text-slate-500">Color</dt>
              <dd class="font-medium text-slate-900">{{ mascota.color || '—' }}</dd>
            </div>
            <div>
              <dt class="text-slate-500">Esterilizado</dt>
              <dd class="font-medium text-slate-900">
                {{
                  mascota.esterilizado === null ? 'No se sabe' : mascota.esterilizado ? 'Sí' : 'No'
                }}
              </dd>
            </div>
            <div>
              <dt class="text-slate-500">Dueño</dt>
              <dd class="font-medium text-slate-900">{{ mascota.clienteNombre }}</dd>
            </div>
            <div class="sm:col-span-2 lg:col-span-3">
              <dt class="text-slate-500">Señas particulares</dt>
              <!-- Texto del servidor mostrado con {{ }}: Vue lo escapa. Nunca v-html. -->
              <dd class="font-medium whitespace-pre-line text-slate-900">
                {{ mascota.senasParticulares || '—' }}
              </dd>
            </div>
          </dl>
        </section>

        <p class="mt-6 text-xs text-slate-400">
          El historial clínico y las vacunas se habilitan en el Sprint 2.
        </p>
      </template>
    </EstadoCarga>
  </div>
</template>
