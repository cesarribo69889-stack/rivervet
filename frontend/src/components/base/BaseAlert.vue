<script setup>
const props = defineProps({
  tipo: {
    type: String,
    default: 'info',
    validator: (v) => ['info', 'exito', 'advertencia', 'error'].includes(v),
  },
  titulo: { type: String, default: null },
});

const estilos = {
  info: 'bg-sky-50 text-sky-900 ring-sky-200',
  exito: 'bg-emerald-50 text-emerald-900 ring-emerald-200',
  advertencia: 'bg-amber-50 text-amber-900 ring-amber-200',
  error: 'bg-red-50 text-red-900 ring-red-200',
};
</script>

<template>
  <div
    :role="props.tipo === 'error' ? 'alert' : 'status'"
    class="rounded-lg px-4 py-3 text-sm ring-1"
    :class="estilos[props.tipo]"
  >
    <p v-if="props.titulo" class="font-semibold">{{ props.titulo }}</p>
    <div :class="props.titulo ? 'mt-1' : ''"><slot /></div>
  </div>
</template>
