<script setup>
const props = defineProps({
  color: {
    type: String,
    default: 'gris',
    validator: (v) => ['gris', 'verde', 'rojo', 'ambar', 'azul', 'teal'].includes(v),
  },
});

const estilos = {
  gris: 'bg-slate-100 text-slate-700 ring-slate-200',
  verde: 'bg-emerald-50 text-emerald-800 ring-emerald-200',
  rojo: 'bg-red-50 text-red-800 ring-red-200',
  ambar: 'bg-amber-50 text-amber-800 ring-amber-200',
  azul: 'bg-sky-50 text-sky-800 ring-sky-200',
  teal: 'bg-teal-50 text-teal-800 ring-teal-200',
};
</script>

<template>
  <span
    class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
    :class="estilos[props.color]"
  >
    <slot />
  </span>
</template>
