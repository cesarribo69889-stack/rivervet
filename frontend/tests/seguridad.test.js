// Revisión estática del código fuente: reglas de seguridad que no deben
// romperse nunca, aunque alguien desactive ESLint.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, test } from 'vitest';

const RAIZ = join(__dirname, '..', 'src');

function archivos(dir) {
  return readdirSync(dir).flatMap((nombre) => {
    const ruta = join(dir, nombre);
    return statSync(ruta).isDirectory() ? archivos(ruta) : [ruta];
  });
}

const fuentes = archivos(RAIZ)
  .filter((f) => /\.(js|vue)$/.test(f))
  .map((f) => ({ archivo: relative(RAIZ, f), contenido: readFileSync(f, 'utf8') }));

function buscar(regex) {
  return fuentes.filter(({ contenido }) => regex.test(contenido)).map((f) => f.archivo);
}

describe('reglas de seguridad del frontend', () => {
  test('no se usa v-html en ningún componente', () => {
    expect(buscar(/\bv-html\s*=/)).toEqual([]);
  });

  test('no se usa localStorage ni sessionStorage (el token vive en memoria)', () => {
    expect(buscar(/\b(local|session)Storage\s*[.[]/)).toEqual([]);
  });

  test('no hay eval ni new Function', () => {
    expect(buscar(/\beval\s*\(|new Function\s*\(/)).toEqual([]);
  });

  test('las únicas variables de entorno usadas no son secretos', () => {
    const usadas = new Set(
      fuentes.flatMap(({ contenido }) => contenido.match(/VITE_[A-Z_]+/g) ?? []),
    );
    for (const nombre of usadas) {
      expect(nombre, `${nombre} parece un secreto`).not.toMatch(
        /SECRET|PASSWORD|PRIVATE|API_KEY|TOKEN/,
      );
    }
  });
});
