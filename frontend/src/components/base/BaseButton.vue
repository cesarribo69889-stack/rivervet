<script setup>
import BaseSpinner from './BaseSpinner.vue';

const props = defineProps({
  variante: {
    type: String,
    default: 'primario',
    validator: (v) => ['primario', 'secundario', 'peligro', 'fantasma'].includes(v),
  },
  tipo: { type: String, default: 'button' },
  cargando: { type: Boolean, default: false },
  deshabilitado: { type: Boolean, default: false },
  bloque: { type: Boolean, default: false },
});

const estilos = {
  primario: 'bg-teal-700 text-white hover:bg-teal-800 disabled:bg-teal-700/60',
  secundario:
    'bg-white text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:text-slate-400',
  peligro: 'bg-red-600 text-white hover:bg-red-700 disabled:bg-red-600/60',
  fantasma: 'text-teal-800 hover:bg-teal-50 disabled:text-slate-400',
};
</script>

<template>
  <button
    :type="props.tipo"
    :disabled="props.deshabilitado || props.cargando"
    :aria-busy="props.cargando"
    class="inline-flex min-h-10 items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold transition-colors disabled:cursor-not-allowed"
    :class="[estilos[props.variante], props.bloque ? 'w-full' : '']"
  >
    <BaseSpinner v-if="props.cargando" tamano="sm" etiqueta="Procesando" />
    <slot />
  </button>
</template>
