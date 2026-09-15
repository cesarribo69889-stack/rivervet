<script setup>
// Diálogo único de confirmación de la app (se monta una vez en App.vue).
import { useConfirmacion } from '@/composables/useConfirmacion';
import BaseModal from './BaseModal.vue';
import BaseButton from './BaseButton.vue';

const { estado, aceptar, cancelar } = useConfirmacion();
</script>

<template>
  <BaseModal :abierto="estado.visible" :titulo="estado.titulo" ancho="max-w-md" @cerrar="cancelar">
    <p class="text-sm text-slate-600">{{ estado.mensaje }}</p>
    <template #pie>
      <BaseButton variante="secundario" @click="cancelar">Cancelar</BaseButton>
      <BaseButton :variante="estado.peligro ? 'peligro' : 'primario'" @click="aceptar">
        {{ estado.textoConfirmar }}
      </BaseButton>
    </template>
  </BaseModal>
</template>
