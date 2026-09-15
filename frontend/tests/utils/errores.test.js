import { describe, expect, test } from 'vitest';
import { erroresPorCampo, mensajeError } from '@/utils/errores';

const conRespuesta = (status, data = {}) => ({ response: { status, data, headers: {} } });

describe('mensajeError: nunca muestra el error crudo', () => {
  test('sin conexión', () => {
    expect(mensajeError(new Error('Network Error'))).toMatch(/No se pudo conectar/);
  });

  test('usa el mensaje del backend en los 4xx pensados para el usuario', () => {
    expect(mensajeError(conRespuesta(409, { message: 'Ya existe un cliente con ese DNI' }))).toBe(
      'Ya existe un cliente con ese DNI',
    );
  });

  test('500: mensaje genérico con código corto de soporte, sin detalles técnicos', () => {
    const msg = mensajeError(
      conRespuesta(500, { message: 'SQL error en dbo.clientes', traceId: 'abcdef12-3456' }),
    );
    expect(msg).toContain('abcdef12');
    expect(msg).not.toContain('SQL');
  });

  test('429', () => {
    expect(mensajeError(conRespuesta(429))).toMatch(/Espera unos minutos/);
  });
});

describe('erroresPorCampo', () => {
  test('convierte los errores 422 en un objeto por campo', () => {
    const error = conRespuesta(422, {
      errors: [
        { campo: 'dni', mensaje: 'El DNI debe tener 8 dígitos' },
        { campo: 'email', mensaje: 'Email inválido' },
      ],
    });
    expect(erroresPorCampo(error)).toEqual({
      dni: 'El DNI debe tener 8 dígitos',
      email: 'Email inválido',
    });
  });

  test('otros códigos no generan errores de campo', () => {
    expect(erroresPorCampo(conRespuesta(409, { errors: [] }))).toEqual({});
  });
});
