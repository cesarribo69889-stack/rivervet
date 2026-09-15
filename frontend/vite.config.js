import { fileURLToPath, URL } from 'node:url';
import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ mode }) => {
  // loadEnv con prefijo '' lee TODAS las variables, pero solo para esta
  // configuración (Node). Al código del navegador solo llegan las VITE_*.
  const env = loadEnv(mode, process.cwd(), '');

  return {
    plugins: [vue(), tailwindcss()],
    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    },
    server: {
      port: 5173,
      strictPort: true,
      // PROXY DE DESARROLLO: el navegador llama a http://localhost:5173/api y
      // Vite reenvía la petición a la API (localhost:3000). Para el navegador
      // todo es el MISMO origen, así que:
      //  - las cookies SameSite=Strict (refresh y CSRF) viajan sin problemas;
      //  - el frontend puede leer la cookie rv_csrf;
      //  - no hay errores de CORS.
      // En producción, vercel.json hace lo mismo con un "rewrite".
      proxy: {
        '/api': {
          target: env.API_PROXY_DESTINO || 'http://localhost:3000',
          changeOrigin: false,
        },
      },
    },
    build: {
      // Sin source maps en producción: no publicamos el código fuente legible.
      sourcemap: false,
    },
    test: {
      environment: 'jsdom',
      include: ['tests/**/*.test.js'],
      coverage: {
        provider: 'v8',
        include: ['src/**/*.{js,vue}'],
        exclude: ['src/main.js'],
        reporter: ['text-summary', 'lcov'],
      },
    },
  };
});
