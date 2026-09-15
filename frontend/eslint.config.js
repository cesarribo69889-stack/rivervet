import js from '@eslint/js';
import pluginVue from 'eslint-plugin-vue';
import globals from 'globals';
import prettier from 'eslint-config-prettier';

const MENSAJE_STORAGE =
  'Prohibido: localStorage/sessionStorage los puede leer cualquier script (XSS). ' +
  'El token vive solo en memoria (store de Pinia).';

export default [
  { ignores: ['dist/', 'coverage/', 'node_modules/'] },
  js.configs.recommended,
  ...pluginVue.configs['flat/recommended'],
  {
    files: ['**/*.{js,vue}'],
    languageOptions: {
      ecmaVersion: 2024,
      sourceType: 'module',
      globals: { ...globals.browser },
    },
    rules: {
      // v-html inserta HTML crudo: con contenido del servidor o del usuario
      // es la puerta de entrada clásica de XSS. Prohibido en todo el proyecto.
      'vue/no-v-html': 'error',
      'no-restricted-globals': [
        'error',
        { name: 'localStorage', message: MENSAJE_STORAGE },
        { name: 'sessionStorage', message: MENSAJE_STORAGE },
      ],
      'no-restricted-properties': [
        'error',
        { object: 'window', property: 'localStorage', message: MENSAJE_STORAGE },
        { object: 'window', property: 'sessionStorage', message: MENSAJE_STORAGE },
      ],
      'no-eval': 'error',
      'no-implied-eval': 'error',
      'no-new-func': 'error',
      eqeqeq: ['error', 'always'],
      'no-unused-vars': ['error', { argsIgnorePattern: '^_', caughtErrors: 'none' }],
    },
  },
  {
    files: ['tests/**/*.js', 'vite.config.js', 'eslint.config.js'],
    languageOptions: { globals: { ...globals.node } },
  },
  {
    // Las pruebas SÍ leen localStorage/sessionStorage: para comprobar que
    // la app no guarda nada ahí.
    files: ['tests/**/*.js'],
    rules: { 'no-restricted-globals': 'off' },
  },
  // Siempre al final: desactiva reglas de formato que chocan con Prettier.
  prettier,
];
