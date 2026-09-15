<script setup>
import DialogoConfirmacion from '@/components/base/DialogoConfirmacion.vue';
import NotificacionesToast from '@/components/base/NotificacionesToast.vue';
</script>

<template>
  <!-- Enlace "saltar al contenido" para quien navega con teclado. -->
  <a
    href="#contenido"
    class="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[70] focus:rounded-lg focus:bg-white focus:px-4 focus:py-2 focus:shadow"
  >
    Saltar al contenido
  </a>
  <RouterView />
  <DialogoConfirmacion />
  <NotificacionesToast />
</template>
