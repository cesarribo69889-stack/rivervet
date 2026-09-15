import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import { crearRouter } from './router';
import { configurarSesionHttp } from './services/http';
import { useAuthStore } from './stores/auth';
import './style.css';

const app = createApp(App);
app.use(createPinia());

const router = crearRouter();
app.use(router);

// Conecta el cliente HTTP con el store de sesión.
const auth = useAuthStore();
configurarSesionHttp({
  obtenerToken: () => auth.accessToken,
  refrescar: () => auth.refrescar(),
  alExpirar: () => {
    auth.limpiarSesion('expirada');
    router.push({ name: 'login' });
  },
});

app.mount('#app');
