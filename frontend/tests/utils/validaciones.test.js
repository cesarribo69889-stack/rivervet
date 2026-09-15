import { describe, expect, test } from 'vitest';
import {
  requisitosPassword,
  validarCelular,
  validarCliente,
  validarDni,
  validarEmail,
  validarMascota,
  validarNombre,
  validarPassword,
} from '@/utils/validaciones';

// Estas reglas deben coincidir con las del backend (backend/src/utils/validaciones.js).
describe('validaciones (espejo del backend)', () => {
  test('DNI de 8 dígitos', () => {
    expect(validarDni('70123456')).toBeNull();
    expect(validarDni('7012345')).toMatch(/8 dígitos/);
    expect(validarDni('')).toMatch(/obligatorio/);
  });

  test('celular de 9 dígitos que empieza con 9', () => {
    expect(validarCelular('987654321')).toBeNull();
    expect(validarCelular('887654321')).not.toBeNull();
    expect(validarCelular('98765432')).not.toBeNull();
  });

  test('email', () => {
    expect(validarEmail('ana@correo.pe')).toBeNull();
    expect(validarEmail('ana@')).toBe('Email inválido');
    expect(validarEmail('', { obligatorio: false })).toBeNull();
  });

  test('nombres con tildes, ñ y apóstrofos', () => {
    expect(validarNombre("María O'Higgins-Ñahui")).toBeNull();
    expect(validarNombre('R2D2')).not.toBeNull();
    expect(validarNombre('<script>')).not.toBeNull();
  });

  test('política de contraseñas: lista lo que falta', () => {
    expect(requisitosPassword('RiverVet#2026')).toEqual([]);
    expect(requisitosPassword('abc')).toEqual(
      expect.arrayContaining(['Mínimo 10 caracteres', 'Una mayúscula', 'Un número', 'Un símbolo']),
    );
    expect(validarPassword('SinSimbolo2026')).toMatch(/símbolo/);
  });

  test('cliente: con cuenta web el email y la contraseña son obligatorios', () => {
    const base = {
      dni: '70123456',
      nombres: 'Ana',
      apellidos: 'Torres',
      telefono: '987654321',
      email: '',
      password: '',
    };
    expect(validarCliente(base)).toEqual({});
    expect(
      Object.keys(validarCliente(base, { emailObligatorio: true, conPassword: true })),
    ).toEqual(['email', 'password']);
    // Editar un cliente que ya tiene cuenta: email sí, contraseña no.
    expect(Object.keys(validarCliente(base, { emailObligatorio: true }))).toEqual(['email']);
  });

  test('mascota: especie obligatoria, sin fechas futuras ni pesos inválidos', () => {
    const errores = validarMascota({
      clienteId: 1,
      nombre: 'Firulais',
      especie: null,
      fechaNacimiento: '2999-01-01',
      pesoKg: -2,
    });
    expect(Object.keys(errores).sort()).toEqual(['especie', 'fechaNacimiento', 'pesoKg']);
    expect(validarMascota({ clienteId: 1, nombre: 'Luna', especie: 'gato', pesoKg: '' })).toEqual(
      {},
    );
  });
});
