import { http } from '../http';

export async function buscarMascotas({ texto = '', pagina = 1, limite = 10 } = {}) {
  const cuerpo = { pagina, limite };
  if (texto.trim()) cuerpo.texto = texto.trim();
  const { data } = await http.post('/mascotas/buscar', cuerpo);
  return data.data;
}

export async function obtenerMascota(id) {
  const { data } = await http.get(`/mascotas/${id}`);
  return data.data;
}

export async function crearMascota(datos) {
  const { data } = await http.post('/mascotas', datos);
  return data.data;
}

export async function actualizarMascota(id, datos) {
  const { data } = await http.put(`/mascotas/${id}`, datos);
  return data.data;
}

export async function eliminarMascota(id) {
  await http.delete(`/mascotas/${id}`);
}
