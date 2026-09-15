<script setup>
import { useId } from 'vue';

const modelo = defineModel({ type: [String, Number, Boolean, null], default: '' });

const props = defineProps({
  etiqueta: { type: String, required: true },
  // [{ valor, etiqueta }]
  opciones: { type: Array, required: true },
  error: { type: String, default: null },
  obligatorio: { type: Boolean, default: false },
  deshabilitado: { type: Boolean, default: false },
  textoVacio: { type: String, default: 'Selecciona una opción' },
});

const id = useId();
</script>

<template>
  <div>
    <label :for="id" class="mb-1 block text-sm font-medium text-slate-700">
      {{ props.etiqueta }}
      <span v-if="props.obligatorio" class="text-red-600" aria-hidden="true">*</span>
    </label>
    <select
      :id="id"
      v-model="modelo"
      :required="props.obligatorio"
      :disabled="props.deshabilitado"
      :aria-invalid="Boolean(props.error)"
      :aria-describedby="props.error ? `${id}-error` : null"
      class="block w-full rounded-lg border bg-white px-3 py-2 text-sm text-slate-900 shadow-sm focus:outline-none focus:ring-2 disabled:bg-slate-100 disabled:text-slate-500"
      :class="
        props.error
          ? 'border-red-500 focus:ring-red-200'
          : 'border-slate-300 focus:border-teal-600 focus:ring-teal-100'
      "
    >
      <option :value="null">{{ props.textoVacio }}</option>
      <option v-for="opcion in props.opciones" :key="String(opcion.valor)" :value="opcion.valor">
        {{ opcion.etiqueta }}
      </option>
    </select>
    <p v-if="props.error" :id="`${id}-error`" class="mt-1 text-xs font-medium text-red-600">
      {{ props.error }}
    </p>
  </div>
</template>
