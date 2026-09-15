// Estado de una llamada a la API: cargando / error / datos.
// Toda vista que consume la API muestra estos tres estados (nunca una
// pantalla en blanco mientras carga).
import { ref, shallowRef } from 'vue';
import { mensajeError } from '@/utils/errores';

export function usePeticion(funcion, { inmediata = false, argumentos = [] } = {}) {
  const datos = shallowRef(null);
  const cargando = ref(false);
  const error = ref(null);
  let ultimaLlamada = 0;

  async function ejecutar(...args) {
    const llamada = ++ultimaLlamada;
    cargando.value = true;
    error.value = null;
    try {
      const resultado = await funcion(...args);
      // Si mientras tanto se lanzó otra búsqueda, ignoramos esta respuesta
      // vieja para no mostrar resultados desordenados.
      if (llamada === ultimaLlamada) datos.value = resultado;
      return resultado;
    } catch (err) {
      if (llamada === ultimaLlamada) error.value = mensajeError(err);
      throw err;
    } finally {
      if (llamada === ultimaLlamada) cargando.value = false;
    }
  }

  if (inmediata) ejecutar(...argumentos).catch(() => {});

  return { datos, cargando, error, ejecutar };
}
