import { http } from '../http';

// La búsqueda va por POST: el texto (DNI, nombre) no debe viajar en la URL,
// donde quedaría en logs de servidores y en el historial del navegador.
export async function buscarClientes({ texto = '', pagina = 1, limite = 10 } = {}) {
  const cuerpo = { pagina, limite };
  if (texto.trim()) cuerpo.texto = texto.trim();
  const { data } = await http.post('/clientes/buscar', cuerpo);
  return data.data;
}

export async function obtenerCliente(id) {
  const { data } = await http.get(`/clientes/${id}`);
  return data.data;
}

export async function obtenerMiFicha() {
  const { data } = await http.get('/clientes/yo');
  return data.data;
}

export async function crearCliente(datos) {
  const { data } = await http.post('/clientes', datos);
  return data.data;
}

export async function actualizarCliente(id, datos) {
  const { data } = await http.put(`/clientes/${id}`, datos);
  return data.data;
}

export async function eliminarCliente(id) {
  await http.delete(`/clientes/${id}`);
}

export async function listarMascotasDeCliente(id, { pagina = 1, limite = 50 } = {}) {
  const { data } = await http.get(`/clientes/${id}/mascotas`, { params: { pagina, limite } });
  return data.data;
}
