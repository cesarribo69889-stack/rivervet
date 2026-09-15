<script setup>
// Tabla responsive con paginación.
//  - En escritorio: tabla.
//  - En móvil: cada fila se muestra como tarjeta (una tabla ancha no cabe).
// Cada columna puede personalizarse con un slot: #celda-<clave>="{ fila }".
import { computed } from 'vue';

const props = defineProps({
  // [{ clave, titulo, clase? }]
  columnas: { type: Array, required: true },
  filas: { type: Array, required: true },
  // { pagina, limite, total, totalPaginas }
  paginacion: { type: Object, default: null },
  claveFila: { type: String, default: 'id' },
  descripcion: { type: String, required: true },
});
const emit = defineEmits(['cambiar-pagina']);

const rango = computed(() => {
  if (!props.paginacion || props.paginacion.total === 0) return '';
  const { pagina, limite, total } = props.paginacion;
  const desde = (pagina - 1) * limite + 1;
  const hasta = Math.min(pagina * limite, total);
  return `${desde}–${hasta} de ${total}`;
});
</script>

<template>
  <div class="overflow-hidden rounded-xl bg-white shadow-sm ring-1 ring-slate-200">
    <!-- Escritorio -->
    <div class="hidden overflow-x-auto md:block">
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <caption class="sr-only">
          {{
            props.descripcion
          }}
        </caption>
        <thead class="bg-slate-50">
          <tr>
            <th
              v-for="col in props.columnas"
              :key="col.clave"
              scope="col"
              class="px-4 py-3 text-left font-semibold text-slate-600"
              :class="col.clase"
            >
              {{ col.titulo }}
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr v-for="fila in props.filas" :key="fila[props.claveFila]" class="hover:bg-slate-50">
            <td
              v-for="col in props.columnas"
              :key="col.clave"
              class="px-4 py-3 text-slate-700"
              :class="col.clase"
            >
              <slot :name="`celda-${col.clave}`" :fila="fila">{{ fila[col.clave] ?? '—' }}</slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Móvil -->
    <ul class="divide-y divide-slate-100 md:hidden" :aria-label="props.descripcion">
      <li v-for="fila in props.filas" :key="fila[props.claveFila]" class="space-y-1.5 px-4 py-3">
        <div
          v-for="col in props.columnas"
          :key="col.clave"
          class="flex justify-between gap-4 text-sm"
        >
          <span class="font-medium text-slate-500">{{ col.titulo }}</span>
          <span class="text-right text-slate-800">
            <slot :name="`celda-${col.clave}`" :fila="fila">{{ fila[col.clave] ?? '—' }}</slot>
          </span>
        </div>
      </li>
    </ul>

    <nav
      v-if="props.paginacion && props.paginacion.totalPaginas > 1"
      class="flex items-center justify-between gap-3 border-t border-slate-200 px-4 py-3 text-sm"
      aria-label="Paginación"
    >
      <p class="text-slate-500">{{ rango }}</p>
      <div class="flex gap-2">
        <button
          type="button"
          class="rounded-lg px-3 py-1.5 font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:opacity-40"
          :disabled="props.paginacion.pagina <= 1"
          @click="emit('cambiar-pagina', props.paginacion.pagina - 1)"
        >
          Anterior
        </button>
        <span class="self-center text-slate-600" aria-current="page">
          {{ props.paginacion.pagina }} / {{ props.paginacion.totalPaginas }}
        </span>
        <button
          type="button"
          class="rounded-lg px-3 py-1.5 font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:opacity-40"
          :disabled="props.paginacion.pagina >= props.paginacion.totalPaginas"
          @click="emit('cambiar-pagina', props.paginacion.pagina + 1)"
        >
          Siguiente
        </button>
      </div>
    </nav>
  </div>
</template>
