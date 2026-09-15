import { http, httpAuth } from '../http';

// Todas las funciones devuelven directamente `data` (lo que va dentro de
// { success: true, data }).

export async function login(email, password) {
  const { data } = await httpAuth.post('/auth/login', { email, password });
  return data.data;
}

export async function refrescar() {
  const { data } = await httpAuth.post('/auth/refresh');
  return data.data;
}

export async function logout() {
  await httpAuth.post('/auth/logout');
}

export async function obtenerYo() {
  const { data } = await http.get('/auth/yo');
  return data.data;
}

export async function cambiarPassword(passwordActual, passwordNueva) {
  const { data } = await http.put('/auth/password', { passwordActual, passwordNueva });
  return data.data;
}
