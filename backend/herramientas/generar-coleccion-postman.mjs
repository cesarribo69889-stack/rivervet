// Genera la colección de Postman de la API RiverVet:
//   node herramientas/generar-coleccion-postman.mjs
// Escribe docs/RiverVet.postman_collection.json y los dos entornos (local y
// producción) en docs/. Si agregas un endpoint, súmalo a CARPETAS y vuelve a
// ejecutar: es más seguro que editar a mano un JSON de cientos de líneas.
//
// Uso en Postman: importa la colección y un entorno, ejecuta "Auth > Login"
// (guarda el token en {{token}}) y el resto ya va autenticado. El encabezado
// X-CSRF-Token (refresh y logout) se toma solo de la cookie rv_csrf.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const docs = join(dirname(fileURLToPath(import.meta.url)), '..', 'docs');

// [nombre, método, ruta, cuerpo?, descripción?]  — las rutas son relativas a {{baseUrl}}.
const CARPETAS = {
  'Salud': [
    ['Health check (incluye la base)', 'GET', '/health'],
  ],
  'Auth': [
    ['Login', 'POST', '/auth/login', { email: '{{email}}', password: '{{password}}' }, 'Guarda el access token en {{token}}.'],
    ['Refrescar sesión', 'POST', '/auth/refresh', null, 'Usa la cookie httpOnly rv_refresh y el encabezado X-CSRF-Token.'],
    ['Usuario actual', 'GET', '/auth/yo'],
    ['Cambiar contraseña', 'PUT', '/auth/password', { passwordActual: '{{password}}', passwordNueva: 'NuevaClave#2026' }],
    ['Logout', 'POST', '/auth/logout'],
  ],
  'Usuarios (admin)': [
    ['Listar', 'GET', '/usuarios?pagina=1&limite=20'],
    ['Crear personal', 'POST', '/usuarios', { nombre: 'Dra. Ana Pérez', email: 'ana.perez@rivervet.pe', password: 'ClaveSegura#2026', rol: 'veterinario' }],
    ['Obtener', 'GET', '/usuarios/{{usuarioId}}'],
    ['Actualizar', 'PUT', '/usuarios/{{usuarioId}}', { nombre: 'Dra. Ana Pérez', rol: 'veterinario', activo: true }],
    ['Veterinarios activos', 'GET', '/veterinarios'],
  ],
  'Clientes': [
    ['Listar', 'GET', '/clientes?pagina=1&limite=10'],
    ['Buscar', 'POST', '/clientes/buscar', { texto: 'ana', pagina: 1, limite: 10 }],
    ['Crear (con cuenta web)', 'POST', '/clientes', { dni: '45678912', nombres: 'Rosa', apellidos: 'Quispe', email: 'rosa@correo.pe', telefono: '987654321', direccion: 'Av. Siempre Viva 123', consentimientoDatos: true, crearCuenta: true, password: 'ClaveSegura#2026' }],
    ['Obtener', 'GET', '/clientes/{{clienteId}}'],
    ['Actualizar', 'PUT', '/clientes/{{clienteId}}', { dni: '45678912', nombres: 'Rosa', apellidos: 'Quispe', email: 'rosa@correo.pe', telefono: '987654321', direccion: 'Av. Siempre Viva 123' }],
    ['Eliminar (admin)', 'DELETE', '/clientes/{{clienteId}}'],
    ['Mascotas del cliente', 'GET', '/clientes/{{clienteId}}/mascotas?pagina=1&limite=50'],
    ['Mi ficha (cliente)', 'GET', '/clientes/yo'],
    ['Exportar mis datos (cliente, Ley 29733)', 'GET', '/clientes/yo/exportar'],
  ],
  'Mascotas': [
    ['Listar', 'GET', '/mascotas?pagina=1&limite=10'],
    ['Buscar', 'POST', '/mascotas/buscar', { texto: 'luna', pagina: 1, limite: 10 }],
    ['Crear', 'POST', '/mascotas', { clienteId: '{{clienteId}}', nombre: 'Toby', especie: 'perro', raza: 'Mestizo', sexo: 'macho', fechaNacimiento: '2022-05-10', pesoKg: 12.5, color: 'Marrón', esterilizado: true }],
    ['Obtener', 'GET', '/mascotas/{{mascotaId}}'],
  ],
  'Citas': [
    ['Listar (filtros)', 'GET', '/citas?desde=2026-10-01&hasta=2026-10-31&estado=programada', null, 'Filtros opcionales: desde, hasta (máx. 93 días), estado, veterinarioId, mascotaId.'],
    ['Citas de una mascota', 'GET', '/citas/mascota/{{mascotaId}}', null, 'Sin fechas: su último año y todo lo agendado.'],
    ['Citas de un veterinario', 'GET', '/citas/veterinario/{{veterinarioId}}'],
    ['Disponibilidad', 'GET', '/citas/disponibilidad?veterinarioId={{veterinarioId}}&fecha=2026-10-20'],
    ['Obtener', 'GET', '/citas/{{citaId}}'],
    ['Agendar', 'POST', '/citas', { mascotaId: '{{mascotaId}}', veterinarioId: '{{veterinarioId}}', fechaHora: '2026-10-20T10:30', motivo: 'Control anual' }, 'fechaHora en hora de Lima, a las :00 o :30, lunes a sábado de 08:00 a 20:00.'],
    ['Reprogramar', 'PUT', '/citas/{{citaId}}', { veterinarioId: '{{veterinarioId}}', fechaHora: '2026-10-21T09:00', motivo: 'Control anual' }],
    ['Cancelar', 'POST', '/citas/{{citaId}}/cancelar', { motivo: 'El cliente reprogramará' }],
    ['Registrar inasistencia', 'POST', '/citas/{{citaId}}/no-asistio', null, 'Solo citas programadas cuya hora ya pasó.'],
  ],
  'Historial clínico': [
    ['Listar (más reciente primero)', 'GET', '/mascotas/{{mascotaId}}/historial?orden=desc'],
    ['Detalle', 'GET', '/mascotas/{{mascotaId}}/historial/{{atencionId}}'],
    ['Registrar atención (veterinario)', 'POST', '/mascotas/{{mascotaId}}/historial', { citaId: null, motivo: 'Control anual', diagnostico: 'Paciente sano', tratamiento: 'Desparasitación oral', receta: 'Antiparasitario oral: 1 tableta hoy y otra en 15 días', observaciones: 'Próximo control en 6 meses', pesoKg: 12.5, temperaturaC: 38.5 }, 'Con citaId, la cita queda atendida.'],
    ['Corregir (solo el veterinario autor)', 'PUT', '/mascotas/{{mascotaId}}/historial/{{atencionId}}', { motivo: 'Control anual', diagnostico: 'Otitis leve', tratamiento: 'Gotas óticas 7 días', receta: 'Gotas óticas: 4 gotas cada 12 h por 7 días', observaciones: 'Control en 1 semana', pesoKg: 12.5, temperaturaC: 38.6 }],
    ['Eliminar (solo el veterinario autor, lógico)', 'DELETE', '/mascotas/{{mascotaId}}/historial/{{atencionId}}'],
  ],
  'Vacunas': [
    ['Listar', 'GET', '/mascotas/{{mascotaId}}/vacunas'],
    ['Detalle', 'GET', '/mascotas/{{mascotaId}}/vacunas/{{vacunaId}}'],
    ['Registrar (veterinario)', 'POST', '/mascotas/{{mascotaId}}/vacunas', { tipoVacuna: 'Antirrábica', fechaAplicacion: '2026-10-01', lote: 'AB-1234', proximaDosis: '2027-10-01', observaciones: 'Sin reacción' }],
    ['Corregir (solo el veterinario autor)', 'PUT', '/mascotas/{{mascotaId}}/vacunas/{{vacunaId}}', { tipoVacuna: 'Antirrábica', fechaAplicacion: '2026-10-01', lote: 'AB-1235', proximaDosis: '2027-10-01', observaciones: 'Lote corregido' }],
    ['Eliminar (solo el veterinario autor, lógico)', 'DELETE', '/mascotas/{{mascotaId}}/vacunas/{{vacunaId}}'],
    ['Recordatorios (vacunas y citas por venir)', 'GET', '/recordatorios?dias=30'],
  ],
  'Dashboard (personal)': [
    ['Estadísticas', 'GET', '/dashboard/stats'],
    ['Citas de hoy', 'GET', '/dashboard/citas-hoy'],
    ['Citas de la semana', 'GET', '/dashboard/citas-semana'],
    ['Indicadores de ventas (admin)', 'GET', '/indicadores?desde=2026-10-01&hasta=2026-10-31'],
  ],
  'Productos y categorías': [
    ['Catálogo público', 'GET', '/productos?pagina=1&limite=12&soloDisponibles=true'],
    ['Producto (público)', 'GET', '/productos/{{productoId}}'],
    ['Inventario (admin, recepción)', 'GET', '/productos/inventario?pagina=1&limite=15'],
    ['Crear producto (admin)', 'POST', '/productos', { categoriaId: '{{categoriaId}}', sku: 'ALI-PERRO-15KG', nombre: 'Alimento para perro adulto 15 kg', descripcion: 'Croquetas balanceadas', precio: 189.9, stock: 12, stockMinimo: 3, activo: true }],
    ['Editar producto (admin)', 'PUT', '/productos/{{productoId}}', { categoriaId: '{{categoriaId}}', sku: 'ALI-PERRO-15KG', nombre: 'Alimento para perro adulto 15 kg', descripcion: 'Croquetas balanceadas', precio: 179.9, stockMinimo: 3, activo: true }],
    ['Ajustar stock (admin)', 'POST', '/productos/{{productoId}}/stock', { cantidad: 10, motivo: 'Llegó pedido del proveedor' }],
    ['Categorías activas', 'GET', '/categorias'],
    ['Todas las categorías (admin)', 'GET', '/categorias/todas'],
    ['Crear categoría (admin)', 'POST', '/categorias', { nombre: 'Alimentos', activo: true }],
  ],
  'Carrito y pedidos': [
    ['Ver carrito (cliente)', 'GET', '/carrito'],
    ['Agregar al carrito', 'POST', '/carrito/items', { productoId: '{{productoId}}', cantidad: 2 }],
    ['Cambiar cantidad', 'PUT', '/carrito/items/{{productoId}}', { cantidad: 3 }],
    ['Quitar del carrito', 'DELETE', '/carrito/items/{{productoId}}'],
    ['Confirmar pedido (cliente)', 'POST', '/pedidos'],
    ['Venta presencial (recepción)', 'POST', '/pedidos/presencial', { clienteId: '{{clienteId}}', items: [{ productoId: '{{productoId}}', cantidad: 1 }] }],
    ['Listar pedidos', 'GET', '/pedidos?pagina=1&limite=15'],
    ['Obtener pedido', 'GET', '/pedidos/{{pedidoId}}'],
    ['Cancelar pedido', 'POST', '/pedidos/{{pedidoId}}/cancelar', { motivo: 'El cliente ya no lo necesita' }],
    ['Entregar pedido (recepción)', 'POST', '/pedidos/{{pedidoId}}/entregar'],
  ],
  'Pagos': [
    ['Datos de Yape', 'GET', '/pagos/yape'],
    ['QR de Yape (público)', 'GET', '/pagos/yape/qr'],
    ['Métodos de pago: ver Yape (admin)', 'GET', '/pagos/yape/configuracion'],
    ['Métodos de pago: cambiar número y titular (admin)', 'PUT', '/pagos/yape/configuracion', { celular: '987000111', titular: 'RiverVet SAC' }],
    ['Métodos de pago: subir QR (admin)', 'PUT', '/pagos/yape/qr', { imagen: 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==' }, 'PNG, JPG o WEBP de hasta 500 KB, como data URL.'],
    ['Pagar con Yape (cliente)', 'POST', '/pedidos/{{pedidoId}}/pagos/yape', { codigoOperacion: 'ABC123456' }],
    ['Pagar con tarjeta (cliente)', 'POST', '/pedidos/{{pedidoId}}/pagos/pasarela', null, 'Devuelve la URL del checkout hospedado de la pasarela.'],
    ['Cobro en efectivo (recepción)', 'POST', '/pedidos/{{pedidoId}}/pagos/efectivo'],
    ['Pagos por verificar (recepción)', 'GET', '/pagos/pendientes'],
    ['Verificar pago (recepción)', 'POST', '/pagos/{{pagoId}}/verificar', { aprobado: true, observacion: null }],
    ['Comprobante por WhatsApp (recepción)', 'POST', '/pedidos/{{pedidoId}}/comprobante/whatsapp', {}],
  ],
  'Triage (público)': [
    ['Catálogo de síntomas', 'GET', '/triage/sintomas'],
    ['Evaluar', 'POST', '/triage', { especie: 'perro', sintomas: ['vomitos', 'decaimiento'], edadMeses: 24, horasConSintomas: 12 }],
  ],
  'Auditoría (admin)': [
    ['Listar', 'GET', '/auditoria?pagina=1&limite=20'],
  ],
};

// Las peticiones públicas no llevan el token (así se prueba también que lo son).
const PUBLICAS = new Set([
  'GET /health', 'POST /auth/login', 'GET /triage/sintomas', 'POST /triage',
  'GET /categorias', 'GET /productos', 'GET /productos/{{productoId}}', 'GET /pagos/yape/qr',
]);

// Los ids van como variables; en el JSON del cuerpo se escriben sin comillas.
const cuerpoJson = (cuerpo) => JSON.stringify(cuerpo, null, 2).replace(/"(\{\{[a-zA-Z]+\}\})"/g, '$1');

function peticion([nombre, metodo, ruta, cuerpo, descripcion]) {
  const [camino, consulta] = ruta.split('?');
  const publica = PUBLICAS.has(`${metodo} ${camino}`);
  const req = {
    method: metodo,
    header: [],
    url: {
      raw: `{{baseUrl}}${ruta}`,
      host: ['{{baseUrl}}'],
      path: camino.split('/').filter(Boolean),
      ...(consulta ? { query: consulta.split('&').map((p) => { const [key, value] = p.split('='); return { key, value }; }) } : {}),
    },
    ...(publica ? { auth: { type: 'noauth' } } : {}),
    ...(descripcion ? { description: descripcion } : {}),
  };
  if (cuerpo !== undefined && cuerpo !== null) {
    req.header.push({ key: 'Content-Type', value: 'application/json' });
    req.body = { mode: 'raw', raw: cuerpoJson(cuerpo), options: { raw: { language: 'json' } } };
  }
  const item = { name: nombre, request: req };
  if (ruta === '/auth/login') {
    item.event = [{
      listen: 'test',
      script: { type: 'text/javascript', exec: [
        "const r = pm.response.json();",
        "if (r && r.data && r.data.accessToken) {",
        "  pm.collectionVariables.set('token', r.data.accessToken);",
        "  pm.collectionVariables.set('usuarioId', r.data.usuario.id);",
        "}",
        "pm.test('Login 200', () => pm.response.to.have.status(200));",
      ] },
    }];
  }
  return item;
}

const coleccion = {
  info: {
    name: 'RiverVet API',
    description: 'API del centro veterinario RiverVet (.NET 10). Ejecuta primero "Auth > Login". '
      + 'Toda respuesta tiene la forma { success, data } o { success: false, message, errors, traceId }.',
    schema: 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json',
  },
  auth: { type: 'bearer', bearer: [{ key: 'token', value: '{{token}}', type: 'string' }] },
  event: [{
    listen: 'prerequest',
    script: { type: 'text/javascript', exec: [
      '// Patrón double-submit: refresh y logout exigen el token CSRF de la cookie rv_csrf.',
      "const csrf = pm.cookies.get('rv_csrf');",
      "if (csrf) { pm.request.headers.upsert({ key: 'X-CSRF-Token', value: csrf }); }",
    ] },
  }],
  variable: [
    ['token', ''], ['usuarioId', '1'], ['clienteId', '1'], ['mascotaId', '1'], ['veterinarioId', '2'],
    ['citaId', '1'], ['atencionId', '1'], ['vacunaId', '1'], ['categoriaId', '1'], ['productoId', '1'],
    ['pedidoId', '1'], ['pagoId', '1'],
  ].map(([key, value]) => ({ key, value })),
  item: Object.entries(CARPETAS).map(([name, items]) => ({ name, item: items.map(peticion) })),
};

const entorno = (name, baseUrl, email, password) => ({
  name,
  values: [
    { key: 'baseUrl', value: baseUrl, enabled: true },
    { key: 'email', value: email, enabled: true },
    { key: 'password', value: password, type: 'secret', enabled: true },
  ],
});

writeFileSync(join(docs, 'RiverVet.postman_collection.json'), JSON.stringify(coleccion, null, 2) + '\n');
// Local: usuarios del seed de desarrollo (datos_desarrollo.sql). Su contraseña está
// en docs/INSTALACION.md; no se repite en un JSON para que gitleaks no la confunda
// con un secreto (y para no acostumbrar a guardar claves aquí). Se escribe en Postman.
writeFileSync(join(docs, 'RiverVet.local.postman_environment.json'),
  JSON.stringify(entorno('RiverVet local', 'http://localhost:3000/api', 'admin@rivervet.pe', ''), null, 2) + '\n');
// Producción: la contraseña NO se guarda en el repositorio; se escribe en Postman.
writeFileSync(join(docs, 'RiverVet.produccion.postman_environment.json'),
  JSON.stringify(entorno('RiverVet producción', 'https://rivervet.vercel.app/api', '', ''), null, 2) + '\n');

const total = Object.values(CARPETAS).reduce((n, items) => n + items.length, 0);
console.log(`Colección generada: ${Object.keys(CARPETAS).length} carpetas, ${total} peticiones.`);
