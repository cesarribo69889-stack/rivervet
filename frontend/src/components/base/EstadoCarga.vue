<script setup>
// Envoltorio para TODA vista que consume la API: muestra carga, error (con
// botón para reintentar) o vacío. Nunca una pantalla en blanco.
import BaseSpinner from './BaseSpinner.vue';
import BaseAlert from './BaseAlert.vue';
import BaseButton from './BaseButton.vue';

defineProps({
  cargando: { type: Boolean, default: false },
  error: { type: String, default: null },
  vacio: { type: Boolean, default: false },
  textoVacio: { type: String, default: 'No hay registros para mostrar.' },
});
defineEmits(['reintentar']);
</script>

<template>
  <div v-if="cargando" class="flex flex-col items-center justify-center gap-3 py-16 text-teal-700">
    <BaseSpinner tamano="lg" />
    <p class="text-sm text-slate-500">Cargando…</p>
  </div>
  <BaseAlert v-else-if="error" tipo="error" titulo="No se pudo cargar la información">
    <p>{{ error }}</p>
    <BaseButton variante="secundario" class="mt-3" @click="$emit('reintentar')">
      Reintentar
    </BaseButton>
  </BaseAlert>
  <div
    v-else-if="vacio"
    class="rounded-xl border border-dashed border-slate-300 bg-white px-6 py-12 text-center"
  >
    <p class="text-sm text-slate-500">{{ textoVacio }}</p>
    <slot name="vacio" />
  </div>
  <slot v-else />
</template>
