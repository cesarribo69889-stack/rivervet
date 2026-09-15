// Avisos breves ("Cliente guardado") que aparecen en una esquina.
import { ref } from 'vue';
import { defineStore } from 'pinia';

let siguienteId = 1;

export const useNotificacionesStore = defineStore('notificaciones', () => {
  const lista = ref([]);

  function cerrar(id) {
    lista.value = lista.value.filter((n) => n.id !== id);
  }

  function mostrar(mensaje, tipo = 'exito', duracionMs = 4000) {
    const id = siguienteId++;
    lista.value.push({ id, mensaje, tipo });
    if (duracionMs > 0) setTimeout(() => cerrar(id), duracionMs);
    return id;
  }

  return {
    lista,
    cerrar,
    mostrar,
    exito: (m) => mostrar(m, 'exito'),
    error: (m) => mostrar(m, 'error', 6000),
    info: (m) => mostrar(m, 'info'),
  };
});
