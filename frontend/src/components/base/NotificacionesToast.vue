<script setup>
import { useNotificacionesStore } from '@/stores/notificaciones';

const notificaciones = useNotificacionesStore();

const estilos = {
  exito: 'bg-emerald-600',
  error: 'bg-red-600',
  info: 'bg-slate-800',
};
</script>

<template>
  <!-- aria-live: los lectores de pantalla anuncian los avisos nuevos. -->
  <div
    aria-live="polite"
    class="pointer-events-none fixed inset-x-0 bottom-0 z-[60] flex flex-col items-center gap-2 p-4 sm:items-end"
  >
    <div
      v-for="n in notificaciones.lista"
      :key="n.id"
      class="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-lg px-4 py-3 text-sm text-white shadow-lg"
      :class="estilos[n.tipo]"
    >
      <p class="flex-1">{{ n.mensaje }}</p>
      <button
        type="button"
        class="rounded p-0.5 text-white/80 hover:text-white"
        aria-label="Cerrar aviso"
        @click="notificaciones.cerrar(n.id)"
      >
        ✕
      </button>
    </div>
  </div>
</template>
