<script setup>
import { onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import { useNotificacionesStore } from '@/stores/notificaciones';
import { usePeticion } from '@/composables/usePeticion';
import { obtenerMiFicha } from '@/services/api/clientes';
import { cambiarPassword } from '@/services/api/auth';
import { ETIQUETAS_ROL, ROLES } from '@/utils/roles';
import { erroresPorCampo, mensajeError } from '@/utils/errores';
import { requisitosPassword, validarPassword } from '@/utils/validaciones';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import BaseInput from '@/components/base/BaseInput.vue';
import BaseButton from '@/components/base/BaseButton.vue';
import BaseAlert from '@/components/base/BaseAlert.vue';

const auth = useAuthStore();
const router = useRouter();
const notificaciones = useNotificacionesStore();
const esCliente = auth.rol === ROLES.CLIENTE;

const ficha = usePeticion(obtenerMiFicha);
onMounted(() => {
  if (esCliente) ficha.ejecutar().catch(() => {});
});

const clave = reactive({ passwordActual: '', passwordNueva: '', confirmacion: '' });
const errores = ref({});
const errorGeneral = ref(null);
const guardando = ref(false);

async function guardarClave() {
  errorGeneral.value = null;
  errores.value = {
    passwordActual: clave.passwordActual ? null : 'Ingresa tu contraseña actual',
    passwordNueva: validarPassword(clave.passwordNueva),
    confirmacion:
      clave.confirmacion === clave.passwordNueva ? null : 'Las contraseñas no coinciden',
  };
  if (Object.values(errores.value).some(Boolean)) return;

  guardando.value = true;
  try {
    await cambiarPassword(clave.passwordActual, clave.passwordNueva);
    // El backend cerró todas las sesiones: hay que volver a entrar.
    auth.limpiarSesion('password');
    notificaciones.exito('Contraseña actualizada');
    router.push({ name: 'login' });
  } catch (err) {
    errores.value = erroresPorCampo(err);
    errorGeneral.value = Object.keys(errores.value).length ? null : mensajeError(err);
  } finally {
    guardando.value = false;
  }
}
</script>

<template>
  <div class="max-w-3xl">
    <EncabezadoPagina titulo="Mi perfil" />

    <section class="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h2 class="text-base font-semibold text-slate-900">Mis datos</h2>
      <dl class="mt-4 grid gap-4 text-sm sm:grid-cols-2">
        <div>
          <dt class="text-slate-500">Nombre</dt>
          <dd class="font-medium text-slate-900">{{ auth.usuario?.nombre }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Email</dt>
          <dd class="font-medium text-slate-900">{{ auth.usuario?.email }}</dd>
        </div>
        <div>
          <dt class="text-slate-500">Rol</dt>
          <dd class="font-medium text-slate-900">{{ ETIQUETAS_ROL[auth.rol] }}</dd>
        </div>
      </dl>

      <div v-if="esCliente" class="mt-6 border-t border-slate-100 pt-4">
        <EstadoCarga
          :cargando="ficha.cargando.value"
          :error="ficha.error.value"
          @reintentar="ficha.ejecutar()"
        >
          <dl v-if="ficha.datos.value" class="grid gap-4 text-sm sm:grid-cols-2">
            <div>
              <dt class="text-slate-500">DNI</dt>
              <dd class="font-medium text-slate-900">{{ ficha.datos.value.dni }}</dd>
            </div>
            <div>
              <dt class="text-slate-500">Celular</dt>
              <dd class="font-medium text-slate-900">{{ ficha.datos.value.telefono }}</dd>
            </div>
            <div class="sm:col-span-2">
              <dt class="text-slate-500">Dirección</dt>
              <dd class="font-medium text-slate-900">{{ ficha.datos.value.direccion || '—' }}</dd>
            </div>
          </dl>
          <p class="mt-4 text-xs text-slate-500">
            Para corregir tus datos, acércate a recepción. Tus datos se tratan conforme a la Ley N°
            29733 de Protección de Datos Personales.
          </p>
        </EstadoCarga>
      </div>
    </section>

    <section class="mt-6 rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h2 class="text-base font-semibold text-slate-900">Cambiar contraseña</h2>
      <p class="mt-1 text-sm text-slate-500">
        Al cambiarla se cerrará tu sesión en todos los dispositivos.
      </p>
      <BaseAlert v-if="errorGeneral" tipo="error" class="mt-4">{{ errorGeneral }}</BaseAlert>
      <form class="mt-4 grid gap-4 sm:max-w-md" novalidate @submit.prevent="guardarClave">
        <BaseInput
          v-model="clave.passwordActual"
          etiqueta="Contraseña actual"
          tipo="password"
          autocomplete="current-password"
          :error="errores.passwordActual"
          obligatorio
        />
        <BaseInput
          v-model="clave.passwordNueva"
          etiqueta="Nueva contraseña"
          tipo="password"
          autocomplete="new-password"
          :error="errores.passwordNueva"
          :ayuda="
            requisitosPassword(clave.passwordNueva).length
              ? `Falta: ${requisitosPassword(clave.passwordNueva).join(', ').toLowerCase()}`
              : 'Cumple todos los requisitos'
          "
          obligatorio
        />
        <BaseInput
          v-model="clave.confirmacion"
          etiqueta="Repite la nueva contraseña"
          tipo="password"
          autocomplete="new-password"
          :error="errores.confirmacion"
          obligatorio
        />
        <div>
          <BaseButton tipo="submit" :cargando="guardando">Actualizar contraseña</BaseButton>
        </div>
      </form>
    </section>
  </div>
</template>
