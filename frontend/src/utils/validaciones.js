// Validaciones del lado del cliente.
//
// Replican las reglas del backend (backend/src/utils/validaciones.js) para
// avisar al usuario ANTES de enviar el formulario. Son solo experiencia de
// usuario: cualquiera puede saltárselas con las herramientas del navegador,
// y por eso el backend vuelve a validar todo, siempre.

const REGEX_DNI = /^\d{8}$/;
const REGEX_CELULAR = /^9\d{8}$/;
const REGEX_EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
const REGEX_NOMBRE = /^[\p{L}\p{M}][\p{L}\p{M}' .-]*$/u;

export function validarDni(valor) {
  if (!valor) return 'El DNI es obligatorio';
  return REGEX_DNI.test(valor.trim()) ? null : 'El DNI debe tener 8 dígitos';
}

export function validarCelular(valor) {
  if (!valor) return 'El celular es obligatorio';
  return REGEX_CELULAR.test(valor.trim())
    ? null
    : 'El celular debe tener 9 dígitos y empezar con 9';
}

export function validarEmail(valor, { obligatorio = true } = {}) {
  if (!valor) return obligatorio ? 'El email es obligatorio' : null;
  return REGEX_EMAIL.test(valor.trim()) ? null : 'Email inválido';
}

export function validarNombre(valor, { max = 100, etiqueta = 'Este campo' } = {}) {
  const texto = (valor || '').trim();
  if (!texto) return `${etiqueta} es obligatorio`;
  if (texto.length > max) return `Máximo ${max} caracteres`;
  return REGEX_NOMBRE.test(texto) ? null : 'Solo letras, espacios, apóstrofos y guiones';
}

// Devuelve la lista de requisitos que la contraseña NO cumple (vacía = válida).
export function requisitosPassword(valor = '') {
  const faltan = [];
  if (valor.length < 10) faltan.push('Mínimo 10 caracteres');
  if (new TextEncoder().encode(valor).length > 72) faltan.push('Máximo 72 caracteres');
  if (!/[A-ZÁÉÍÓÚÑ]/.test(valor)) faltan.push('Una mayúscula');
  if (!/[a-záéíóúñ]/.test(valor)) faltan.push('Una minúscula');
  if (!/\d/.test(valor)) faltan.push('Un número');
  if (!/[^A-Za-z0-9ÁÉÍÓÚÑáéíóúñ]/.test(valor)) faltan.push('Un símbolo');
  return faltan;
}

export function validarPassword(valor) {
  if (!valor) return 'La contraseña es obligatoria';
  const faltan = requisitosPassword(valor);
  return faltan.length ? `Falta: ${faltan.join(', ').toLowerCase()}` : null;
}

// Quita las claves sin error: { dni: null, email: 'x' } -> { email: 'x' }.
function soloErrores(errores) {
  return Object.fromEntries(Object.entries(errores).filter(([, v]) => v));
}

// emailObligatorio: al crear la cuenta web o si el cliente ya tiene una.
// conPassword: solo al CREAR la cuenta (al editar no se toca la contraseña).
export function validarCliente(datos, { emailObligatorio = false, conPassword = false } = {}) {
  return soloErrores({
    dni: validarDni(datos.dni),
    nombres: validarNombre(datos.nombres, { etiqueta: 'El nombre' }),
    apellidos: validarNombre(datos.apellidos, { etiqueta: 'El apellido' }),
    telefono: validarCelular(datos.telefono),
    email: validarEmail(datos.email, { obligatorio: emailObligatorio }),
    direccion: (datos.direccion || '').length > 250 ? 'Máximo 250 caracteres' : null,
    password: conPassword ? validarPassword(datos.password) : null,
  });
}

export function validarMascota(datos) {
  const hoy = new Date().toISOString().slice(0, 10);
  const peso = datos.pesoKg === '' || datos.pesoKg === null ? null : Number(datos.pesoKg);
  return soloErrores({
    clienteId: datos.clienteId ? null : 'Selecciona el cliente',
    nombre: validarNombre(datos.nombre, { max: 60, etiqueta: 'El nombre' }),
    especie: datos.especie ? null : 'Selecciona la especie',
    fechaNacimiento:
      datos.fechaNacimiento && datos.fechaNacimiento > hoy ? 'No puede ser una fecha futura' : null,
    pesoKg:
      peso !== null && (Number.isNaN(peso) || peso <= 0 || peso > 999.99)
        ? 'Peso inválido (mayor a 0, hasta 999.99)'
        : null,
  });
}
