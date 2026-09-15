import { http } from '../http';

export async function listarAuditoria({ pagina = 1, limite = 20, accion } = {}) {
  const params = { pagina, limite };
  if (accion) params.accion = accion;
  const { data } = await http.get('/auditoria', { params });
  return data.data;
}
