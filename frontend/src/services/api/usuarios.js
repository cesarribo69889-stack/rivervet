import { http } from '../http';

export async function listarUsuarios({ pagina = 1, limite = 10, rol } = {}) {
  const params = { pagina, limite };
  if (rol) params.rol = rol;
  const { data } = await http.get('/usuarios', { params });
  return data.data;
}

export async function crearUsuario(datos) {
  const { data } = await http.post('/usuarios', datos);
  return data.data;
}

export async function actualizarUsuario(id, datos) {
  const { data } = await http.put(`/usuarios/${id}`, datos);
  return data.data;
}
