<script setup>
import { computed, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import { mensajeError } from '@/utils/errores';
import { validarEmail } from '@/utils/validaciones';
import BaseInput from '@/components/base/BaseInput.vue';
import BaseButton from '@/components/base/BaseButton.vue';
import BaseAlert from '@/components/base/BaseAlert.vue';
import LogoRiverVet from '@/components/LogoRiverVet.vue';

const auth = useAuthStore();
const router = useRouter();

const formulario = reactive({ email: '', password: '' });
const errores = reactive({ email: null, password: null });
const errorGeneral = ref(null);
const enviando = ref(false);

const avisoCierre = computed(
  () =>
    ({
      expirada: 'Tu sesión expiró. Vuelve a iniciar sesión.',
      inactividad: 'Cerramos tu sesión por inactividad. Vuelve a iniciar sesión.',
      password: 'Actualizaste tu contraseña. Inicia sesión con la nueva.',
    })[auth.motivoCierre] ?? null,
);

function validar() {
  errores.email = validarEmail(formulario.email);
  errores.password = formulario.password ? null : 'La contraseña es obligatoria';
  return !errores.email && !errores.password;
}

async function ingresar() {
  errorGeneral.value = null;
  if (!validar()) return;
  enviando.value = true;
  try {
    await auth.login(formulario.email.trim(), formulario.password);
    const destino = auth.rutaPendiente || { name: 'inicio' };
    auth.rutaPendiente = null;
    router.push(destino);
  } catch (err) {
    // El backend responde lo mismo si el email no existe o si la contraseña
    // está mal (anti-enumeración): mostramos su mensaje tal cual.
    errorGeneral.value = mensajeError(err);
    formulario.password = '';
  } finally {
    enviando.value = false;
  }
}
</script>

<template>
  <div
    class="flex min-h-screen items-center justify-center bg-gradient-to-br from-teal-800 to-teal-950 px-4 py-12"
  >
    <div class="w-full max-w-md">
      <div class="mb-8 text-center text-white">
        <LogoRiverVet class="mx-auto h-14 w-14" />
        <h1 class="mt-4 text-3xl font-bold">RiverVet</h1>
        <p class="mt-1 text-teal-100">Gestión de la clínica veterinaria</p>
      </div>

      <div class="rounded-2xl bg-white p-6 shadow-xl sm:p-8">
        <h2 class="text-lg font-semibold text-slate-900">Iniciar sesión</h2>

        <BaseAlert v-if="avisoCierre && !errorGeneral" tipo="info" class="mt-4">
          {{ avisoCierre }}
        </BaseAlert>
        <BaseAlert v-if="errorGeneral" tipo="error" class="mt-4">{{ errorGeneral }}</BaseAlert>

        <form class="mt-5 space-y-4" novalidate @submit.prevent="ingresar">
          <BaseInput
            v-model="formulario.email"
            etiqueta="Email"
            tipo="email"
            autocomplete="username"
            inputmode="email"
            :error="errores.email"
            obligatorio
          />
          <BaseInput
            v-model="formulario.password"
            etiqueta="Contraseña"
            tipo="password"
            autocomplete="current-password"
            :error="errores.password"
            obligatorio
          />
          <BaseButton tipo="submit" :cargando="enviando" bloque>Ingresar</BaseButton>
        </form>
      </div>
    </div>
  </div>
</template>
