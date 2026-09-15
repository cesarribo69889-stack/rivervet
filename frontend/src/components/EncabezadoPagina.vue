<script setup>
defineProps({
  titulo: { type: String, required: true },
  subtitulo: { type: String, default: null },
});
</script>

<template>
  <header class="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
    <div>
      <slot name="migas" />
      <h1 class="text-2xl font-bold tracking-tight text-slate-900">{{ titulo }}</h1>
      <p v-if="subtitulo" class="mt-1 text-sm text-slate-500">{{ subtitulo }}</p>
    </div>
    <div v-if="$slots.acciones" class="flex flex-wrap gap-2">
      <slot name="acciones" />
    </div>
  </header>
</template>
