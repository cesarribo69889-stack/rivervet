// Traduce errores de la API a mensajes entendibles para una recepcionista.
//
// Regla: NUNCA se muestra el error crudo (stack, SQL, "Network Error"...). Los
// mensajes 4xx de nuestra API ya vienen en español y pensados para el
// usuario; para todo lo demás se usa un mensaje propio.

const MENSAJES_POR_ESTADO = {
  400: 'La solicitud no es válida. Revisa los datos e inténtalo de nuevo.',
  401: 'Tu sesión expiró. Vuelve a iniciar sesión.',
  403: 'No tienes permiso para realizar esta acción.',
  404: 'No se encontró lo que buscabas. Puede que haya sido eliminado.',
  409: 'Ya existe un registro con esos datos.',
  413: 'La información enviada es demasiado grande.',
  422: 'Revisa los campos marcados en rojo.',
  429: 'Demasiados intentos. Espera unos minutos e inténtalo de nuevo.',
};

// Estados en los que el `message` del backend está pensado para el usuario.
const ESTADOS_CON_MENSAJE_PROPIO = new Set([401, 403, 404, 409, 422, 429]);

export function mensajeError(error) {
  const respuesta = error?.response;
  if (!respuesta) {
    if (error?.code === 'ECONNABORTED')
      return 'El servidor tardó demasiado en responder. Inténtalo de nuevo.';
    return 'No se pudo conectar con el servidor. Revisa tu conexión a internet.';
  }
  const { status, data } = respuesta;
  if (ESTADOS_CON_MENSAJE_PROPIO.has(status) && typeof data?.message === 'string') {
    return data.message;
  }
  if (MENSAJES_POR_ESTADO[status]) return MENSAJES_POR_ESTADO[status];

  // 500 y demás: mensaje genérico + código corto para soporte.
  const traza = data?.traceId || respuesta.headers?.['x-trace-id'];
  const codigo = traza ? ` Código de soporte: ${String(traza).slice(0, 8)}.` : '';
  return `Ocurrió un problema en el sistema. Inténtalo de nuevo en unos minutos.${codigo}`;
}

// Convierte los errores 422 de la API en { campo: mensaje } para mostrarlos
// junto a cada input del formulario.
export function erroresPorCampo(error) {
  const lista = error?.response?.status === 422 ? error.response.data?.errors : null;
  if (!Array.isArray(lista)) return {};
  return lista.reduce((acc, { campo, mensaje }) => {
    if (campo && !acc[campo]) acc[campo] = mensaje;
    return acc;
  }, {});
}
