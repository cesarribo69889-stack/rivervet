<script setup>
defineProps({
  tamano: { type: String, default: 'md' },
  etiqueta: { type: String, default: 'Cargando' },
});
</script>

<template>
  <span role="status" class="inline-flex items-center">
    <svg
      class="animate-spin text-current"
      :class="tamano === 'sm' ? 'h-4 w-4' : tamano === 'lg' ? 'h-10 w-10' : 'h-6 w-6'"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
      <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
    </svg>
    <span class="sr-only">{{ etiqueta }}</span>
  </span>
</template>
