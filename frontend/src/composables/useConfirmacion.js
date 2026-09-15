// Diálogo de confirmación para acciones destructivas, usable como promesa:
//
//   const { confirmar } = useConfirmacion();
//   if (await confirmar({ titulo: '¿Eliminar?', mensaje: '...' })) { ... }
//
// Un único diálogo compartido por toda la app (DialogoConfirmacion.vue).
import { reactive } from 'vue';

const estado = reactive({
  visible: false,
  titulo: '',
  mensaje: '',
  textoConfirmar: 'Confirmar',
  peligro: true,
  resolver: null,
});

function responder(valor) {
  estado.visible = false;
  estado.resolver?.(valor);
  estado.resolver = null;
}

export function useConfirmacion() {
  function confirmar({ titulo, mensaje, textoConfirmar = 'Confirmar', peligro = true }) {
    // Si había otro diálogo abierto, se cancela.
    if (estado.resolver) responder(false);
    Object.assign(estado, { visible: true, titulo, mensaje, textoConfirmar, peligro });
    return new Promise((resolve) => {
      estado.resolver = resolve;
    });
  }
  return { estado, confirmar, aceptar: () => responder(true), cancelar: () => responder(false) };
}
