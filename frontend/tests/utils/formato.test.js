import { describe, expect, test } from 'vitest';
import { calcularEdad, formatearFecha, formatearFechaHora, formatearMoneda } from '@/utils/formato';

describe('formatos', () => {
  test('fecha DATE sin corrimiento de zona horaria', () => {
    // new Date('2019-03-15') en Lima sería el 14 de marzo: por eso se formatea como texto.
    expect(formatearFecha('2019-03-15')).toBe('15/03/2019');
    expect(formatearFecha(null)).toBe('—');
  });

  test('fecha y hora UTC se muestran en hora de Lima (UTC-5)', () => {
    expect(formatearFechaHora('2026-09-12T15:30:00.000Z')).toMatch(/10:30/);
  });

  test('moneda en soles', () => {
    expect(formatearMoneda(189.9)).toMatch(/S\/\s?189\.90/);
  });

  test('edad legible', () => {
    const hoy = new Date(2026, 8, 12);
    expect(calcularEdad('2019-03-15', hoy)).toBe('7 años');
    expect(calcularEdad('2026-02-01', hoy)).toBe('7 meses');
    expect(calcularEdad('2026-09-01', hoy)).toBe('Menos de 1 mes');
  });
});
