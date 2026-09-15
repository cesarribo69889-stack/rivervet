export const ROLES = Object.freeze({
  ADMIN: 'admin',
  VETERINARIO: 'veterinario',
  RECEPCIONISTA: 'recepcionista',
  CLIENTE: 'cliente',
});

export const PERSONAL = Object.freeze([ROLES.ADMIN, ROLES.VETERINARIO, ROLES.RECEPCIONISTA]);
export const TODOS = Object.freeze([...PERSONAL, ROLES.CLIENTE]);

export const ETIQUETAS_ROL = Object.freeze({
  admin: 'Administrador',
  veterinario: 'Veterinario',
  recepcionista: 'Recepcionista',
  cliente: 'Cliente',
});

export const ESPECIES = Object.freeze([
  { valor: 'perro', etiqueta: 'Perro' },
  { valor: 'gato', etiqueta: 'Gato' },
  { valor: 'ave', etiqueta: 'Ave' },
  { valor: 'conejo', etiqueta: 'Conejo' },
  { valor: 'roedor', etiqueta: 'Roedor' },
  { valor: 'reptil', etiqueta: 'Reptil' },
  { valor: 'otro', etiqueta: 'Otro' },
]);
