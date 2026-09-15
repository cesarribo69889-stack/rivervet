<script setup>
// Modal accesible: role="dialog", aria-modal, título enlazado, se cierra con
// Escape, el foco entra al abrir, queda atrapado dentro con Tab y vuelve al
// elemento que lo abrió al cerrar.
import { nextTick, ref, useId, watch } from 'vue';

const props = defineProps({
  abierto: { type: Boolean, required: true },
  titulo: { type: String, required: true },
  ancho: { type: String, default: 'max-w-lg' },
});
const emit = defineEmits(['cerrar']);

const idTitulo = useId();
const panel = ref(null);
let focoAnterior = null;

const SELECTOR_FOCO =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function enfocables() {
  return panel.value ? [...panel.value.querySelectorAll(SELECTOR_FOCO)] : [];
}

function alTeclado(evento) {
  if (evento.key === 'Escape') {
    emit('cerrar');
    return;
  }
  if (evento.key !== 'Tab') return;
  const lista = enfocables();
  if (!lista.length) return;
  const primero = lista[0];
  const ultimo = lista[lista.length - 1];
  if (evento.shiftKey && document.activeElement === primero) {
    evento.preventDefault();
    ultimo.focus();
  } else if (!evento.shiftKey && document.activeElement === ultimo) {
    evento.preventDefault();
    primero.focus();
  }
}

watch(
  () => props.abierto,
  async (abierto) => {
    if (abierto) {
      focoAnterior = document.activeElement;
      await nextTick();
      (enfocables()[0] || panel.value)?.focus();
    } else if (focoAnterior instanceof HTMLElement) {
      focoAnterior.focus();
    }
  },
);
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.abierto"
      class="fixed inset-0 z-50 flex items-end justify-center bg-slate-900/50 p-4 sm:items-center"
      @mousedown.self="emit('cerrar')"
    >
      <div
        ref="panel"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="idTitulo"
        tabindex="-1"
        class="max-h-[90vh] w-full overflow-y-auto rounded-xl bg-white shadow-xl"
        :class="props.ancho"
        @keydown="alTeclado"
      >
        <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <h2 :id="idTitulo" class="text-lg font-semibold text-slate-900">{{ props.titulo }}</h2>
          <button
            type="button"
            class="rounded-md p-1 text-slate-500 hover:bg-slate-100 hover:text-slate-700"
            aria-label="Cerrar"
            @click="emit('cerrar')"
          >
            <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path
                d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z"
              />
            </svg>
          </button>
        </div>
        <div class="px-5 py-4"><slot /></div>
        <div
          v-if="$slots.pie"
          class="flex flex-col-reverse gap-2 border-t border-slate-200 px-5 py-4 sm:flex-row sm:justify-end"
        >
          <slot name="pie" />
        </div>
      </div>
    </div>
  </Teleport>
</template>
