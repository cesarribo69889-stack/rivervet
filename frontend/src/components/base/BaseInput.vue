<script setup>
// Input con label ASOCIADO (for/id), mensaje de error enlazado con
// aria-describedby y aria-invalid: un lector de pantalla anuncia el nombre del
// campo y su error.
import { computed, useId } from 'vue';

// Los atributos extra (placeholder, autocomplete, maxlength...) van al
// <input>, no al <div> contenedor.
defineOptions({ inheritAttrs: false });

const modelo = defineModel({ type: [String, Number, null], default: '' });

const props = defineProps({
  etiqueta: { type: String, required: true },
  tipo: { type: String, default: 'text' },
  error: { type: String, default: null },
  ayuda: { type: String, default: null },
  obligatorio: { type: Boolean, default: false },
  multilinea: { type: Boolean, default: false },
  filas: { type: Number, default: 3 },
});

const id = useId();
const idAyuda = `${id}-ayuda`;
const idError = `${id}-error`;

const describedBy = computed(
  () =>
    [props.error ? idError : null, props.ayuda ? idAyuda : null].filter(Boolean).join(' ') || null,
);

const clases = computed(() => [
  'block w-full rounded-lg border bg-white px-3 py-2 text-sm text-slate-900 shadow-sm placeholder:text-slate-400',
  'focus:outline-none focus:ring-2',
  props.error
    ? 'border-red-500 focus:border-red-500 focus:ring-red-200'
    : 'border-slate-300 focus:border-teal-600 focus:ring-teal-100',
]);
</script>

<template>
  <div>
    <label :for="id" class="mb-1 block text-sm font-medium text-slate-700">
      {{ props.etiqueta }}
      <span v-if="props.obligatorio" class="text-red-600" aria-hidden="true">*</span>
    </label>
    <textarea
      v-if="props.multilinea"
      :id="id"
      v-model="modelo"
      :rows="props.filas"
      :required="props.obligatorio"
      :aria-invalid="Boolean(props.error)"
      :aria-describedby="describedBy"
      :class="clases"
      v-bind="$attrs"
    />
    <input
      v-else
      :id="id"
      v-model="modelo"
      :type="props.tipo"
      :required="props.obligatorio"
      :aria-invalid="Boolean(props.error)"
      :aria-describedby="describedBy"
      :class="clases"
      v-bind="$attrs"
    />
    <p v-if="props.ayuda && !props.error" :id="idAyuda" class="mt-1 text-xs text-slate-500">
      {{ props.ayuda }}
    </p>
    <p v-if="props.error" :id="idError" class="mt-1 text-xs font-medium text-red-600">
      {{ props.error }}
    </p>
  </div>
</template>
