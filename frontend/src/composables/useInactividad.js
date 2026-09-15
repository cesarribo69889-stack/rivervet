// Cierre de sesión por inactividad (30 minutos por defecto).
//
// Una computadora de recepción queda muchas veces sola y encendida. Si nadie
// la usa durante N minutos, la sesión se cierra para que otra persona no
// pueda ver ni modificar datos de clientes. Un minuto antes se avisa.
import { onBeforeUnmount, onMounted, ref } from 'vue';

const EVENTOS = ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'wheel'];
const AVISO_ANTES_MS = 60 * 1000;

export function useInactividad({ minutos, alExpirar }) {
  const limiteMs = minutos * 60 * 1000;
  const mostrarAviso = ref(false);
  let temporizadorAviso = null;
  let temporizadorCierre = null;
  let ultimoReinicio = 0;

  function limpiar() {
    clearTimeout(temporizadorAviso);
    clearTimeout(temporizadorCierre);
  }

  function reiniciar() {
    limpiar();
    mostrarAviso.value = false;
    ultimoReinicio = Date.now();
    temporizadorAviso = setTimeout(
      () => {
        mostrarAviso.value = true;
      },
      Math.max(limiteMs - AVISO_ANTES_MS, 0),
    );
    temporizadorCierre = setTimeout(() => {
      mostrarAviso.value = false;
      alExpirar();
    }, limiteMs);
  }

  // mousemove se dispara cientos de veces por segundo: se reinicia como
  // máximo una vez cada 5 s. Mientras el aviso está visible, solo el botón
  // "Seguir conectado" reinicia (moverse sin querer no debe ocultarlo).
  function alActividad() {
    if (mostrarAviso.value) return;
    if (Date.now() - ultimoReinicio > 5000) reiniciar();
  }

  onMounted(() => {
    EVENTOS.forEach((e) => window.addEventListener(e, alActividad, { passive: true }));
    reiniciar();
  });

  onBeforeUnmount(() => {
    EVENTOS.forEach((e) => window.removeEventListener(e, alActividad));
    limpiar();
  });

  return { mostrarAviso, seguirConectado: reiniciar };
}
