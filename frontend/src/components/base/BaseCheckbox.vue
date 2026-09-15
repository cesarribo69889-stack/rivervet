<script setup>
import { useId } from 'vue';

const modelo = defineModel({ type: Boolean, default: false });

defineProps({
  etiqueta: { type: String, required: true },
  descripcion: { type: String, default: null },
  error: { type: String, default: null },
});

const id = useId();
</script>

<template>
  <div>
    <div class="flex items-start gap-3">
      <input
        :id="id"
        v-model="modelo"
        type="checkbox"
        :aria-invalid="Boolean(error)"
        :aria-describedby="error ? `${id}-error` : descripcion ? `${id}-desc` : null"
        class="mt-0.5 h-4 w-4 rounded border-slate-300 text-teal-700 focus:ring-teal-600"
      />
      <div>
        <label :for="id" class="text-sm font-medium text-slate-700">{{ etiqueta }}</label>
        <p v-if="descripcion" :id="`${id}-desc`" class="text-xs text-slate-500">
          {{ descripcion }}
        </p>
      </div>
    </div>
    <p v-if="error" :id="`${id}-error`" class="mt-1 text-xs font-medium text-red-600">
      {{ error }}
    </p>
  </div>
</template>
