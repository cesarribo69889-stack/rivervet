// Formatos para mostrar datos. La API guarda y envía fechas en UTC; aquí se
// convierten a la hora de Lima.
const ZONA = 'America/Lima';

const formatoFechaHora = new Intl.DateTimeFormat('es-PE', {
  timeZone: ZONA,
  dateStyle: 'short',
  timeStyle: 'short',
});

const formatoMoneda = new Intl.NumberFormat('es-PE', { style: 'currency', currency: 'PEN' });

// "2026-09-12T15:30:00.000Z" (UTC) -> "12/09/26, 10:30" (Lima)
export function formatearFechaHora(iso) {
  if (!iso) return '—';
  const fecha = new Date(iso);
  return Number.isNaN(fecha.getTime()) ? '—' : formatoFechaHora.format(fecha);
}

// "2019-03-15" (DATE, sin zona horaria) -> "15/03/2019".
// Se formatea como texto a propósito: new Date('2019-03-15') es medianoche
// UTC, que en Lima sería ¡el 14 de marzo a las 7 p. m.!
export function formatearFecha(textoFecha) {
  if (!textoFecha || !/^\d{4}-\d{2}-\d{2}$/.test(textoFecha)) return '—';
  const [anio, mes, dia] = textoFecha.split('-');
  return `${dia}/${mes}/${anio}`;
}

export function formatearMoneda(monto) {
  return monto === null || monto === undefined ? '—' : formatoMoneda.format(monto);
}

// Edad legible a partir de 'AAAA-MM-DD': "4 años", "7 meses", "Menos de 1 mes".
export function calcularEdad(textoFecha, hoy = new Date()) {
  if (!textoFecha) return '—';
  const [anio, mes, dia] = textoFecha.split('-').map(Number);
  let meses = (hoy.getFullYear() - anio) * 12 + (hoy.getMonth() + 1 - mes);
  if (hoy.getDate() < dia) meses -= 1;
  if (meses < 1) return 'Menos de 1 mes';
  if (meses < 12) return `${meses} ${meses === 1 ? 'mes' : 'meses'}`;
  const anios = Math.floor(meses / 12);
  return `${anios} ${anios === 1 ? 'año' : 'años'}`;
}

export function capitalizar(texto) {
  return texto ? texto.charAt(0).toUpperCase() + texto.slice(1) : '';
}
