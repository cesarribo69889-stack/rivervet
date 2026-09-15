<script setup>
// Alta (HU-002) y edición (HU-003) de clientes. Si recibe `id`, edita.
import { computed, onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useNotificacionesStore } from '@/stores/notificaciones';
import { actualizarCliente, crearCliente, obtenerCliente } from '@/services/api/clientes';
import { erroresPorCampo, mensajeError } from '@/utils/errores';
import { requisitosPassword, validarCliente } from '@/utils/validaciones';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import BaseInput from '@/components/base/BaseInput.vue';
import BaseCheckbox from '@/components/base/BaseCheckbox.vue';
import BaseButton from '@/components/base/BaseButton.vue';
import BaseAlert from '@/components/base/BaseAlert.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';

const props = defineProps({ id: { type: String, default: null } });

const router = useRouter();
const notificaciones = useNotificacionesStore();
const esEdicion = computed(() => Boolean(props.id));

const formulario = reactive({
  dni: '',
  nombres: '',
  apellidos: '',
  telefono: '',
  email: '',
  direccion: '',
  consentimientoDatos: false,
  crearCuenta: false,
  password: '',
});
const tieneCuenta = ref(false);
const errores = ref({});
const errorGeneral = ref(null);
const cargando = ref(false);
const errorCarga = ref(null);
const guardando = ref(false);

async function cargar() {
  if (!esEdicion.value) return;
  cargando.value = true;
  errorCarga.value = null;
  try {
    const cliente = await obtenerCliente(props.id);
    Object.assign(formulario, {
      dni: cliente.dni,
      nombres: cliente.nombres,
      apellidos: cliente.apellidos,
      telefono: cliente.telefono,
      email: cliente.email || '',
      direccion: cliente.direccion || '',
    });
    tieneCuenta.value = cliente.tieneCuenta;
  } catch (err) {
    errorCarga.value = mensajeError(err);
  } finally {
    cargando.value = false;
  }
}
onMounted(cargar);

// Solo se envían los campos que el backend espera: su validación es de lista
// blanca y rechazaría cualquier campo extra.
function armarCuerpo() {
  const comunes = {
    dni: formulario.dni.trim(),
    nombres: formulario.nombres.trim(),
    apellidos: formulario.apellidos.trim(),
    telefono: formulario.telefono.trim(),
    email: formulario.email.trim() || null,
    direccion: formulario.direccion.trim() || null,
  };
  if (esEdicion.value) return comunes;
  return {
    ...comunes,
    consentimientoDatos: formulario.consentimientoDatos,
    crearCuenta: formulario.crearCuenta,
    ...(formulario.crearCuenta ? { password: formulario.password } : {}),
  };
}

async function guardar() {
  errorGeneral.value = null;
  const creaCuenta = !esEdicion.value && formulario.crearCuenta;
  errores.value = validarCliente(formulario, {
    emailObligatorio: creaCuenta || tieneCuenta.value,
    conPassword: creaCuenta,
  });
  if (!esEdicion.value && !formulario.consentimientoDatos) {
    errores.value.consentimientoDatos = 'Se requiere el consentimiento del cliente';
  }
  if (Object.keys(errores.value).length) return;

  guardando.value = true;
  try {
    const cliente = esEdicion.value
      ? await actualizarCliente(props.id, armarCuerpo())
      : await crearCliente(armarCuerpo());
    notificaciones.exito(esEdicion.value ? 'Cliente actualizado' : 'Cliente registrado');
    router.push({ name: 'cliente-detalle', params: { id: cliente.id } });
  } catch (err) {
    errores.value = erroresPorCampo(err);
    errorGeneral.value = mensajeError(err);
  } finally {
    guardando.value = false;
  }
}
</script>

<template>
  <div class="max-w-3xl">
    <EncabezadoPagina :titulo="esEdicion ? 'Editar cliente' : 'Nuevo cliente'">
      <template #migas>
        <RouterLink :to="{ name: 'clientes' }" class="text-sm text-teal-700 hover:underline">
          ← Clientes
        </RouterLink>
      </template>
    </EncabezadoPagina>

    <EstadoCarga :cargando="cargando" :error="errorCarga" @reintentar="cargar">
      <form
        class="space-y-6 rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200"
        novalidate
        @submit.prevent="guardar"
      >
        <BaseAlert v-if="errorGeneral" tipo="error">{{ errorGeneral }}</BaseAlert>

        <fieldset class="grid gap-4 sm:grid-cols-2">
          <legend class="mb-2 text-base font-semibold text-slate-900">Datos personales</legend>
          <BaseInput
            v-model="formulario.nombres"
            etiqueta="Nombres"
            autocomplete="off"
            :error="errores.nombres"
            obligatorio
          />
          <BaseInput
            v-model="formulario.apellidos"
            etiqueta="Apellidos"
            autocomplete="off"
            :error="errores.apellidos"
            obligatorio
          />
          <BaseInput
            v-model="formulario.dni"
            etiqueta="DNI"
            inputmode="numeric"
            maxlength="8"
            autocomplete="off"
            :error="errores.dni"
            obligatorio
          />
          <BaseInput
            v-model="formulario.telefono"
            etiqueta="Celular"
            tipo="tel"
            inputmode="numeric"
            maxlength="9"
            autocomplete="off"
            :error="errores.telefono"
            obligatorio
          />
          <BaseInput
            v-model="formulario.email"
            etiqueta="Email"
            tipo="email"
            autocomplete="off"
            :error="errores.email"
            :obligatorio="formulario.crearCuenta || tieneCuenta"
            :ayuda="tieneCuenta ? 'Es también su email para iniciar sesión.' : null"
          />
          <BaseInput
            v-model="formulario.direccion"
            etiqueta="Dirección"
            maxlength="250"
            autocomplete="off"
            :error="errores.direccion"
          />
        </fieldset>

        <fieldset v-if="!esEdicion" class="space-y-4 border-t border-slate-100 pt-5">
          <legend class="sr-only">Consentimiento y cuenta web</legend>
          <BaseCheckbox
            v-model="formulario.consentimientoDatos"
            etiqueta="El cliente autoriza el tratamiento de sus datos personales"
            descripcion="Ley N° 29733. Finalidad: atención veterinaria, historial de sus mascotas, citas y ventas. Puede solicitar acceso o supresión de sus datos."
            :error="errores.consentimientoDatos"
          />
          <BaseCheckbox
            v-model="formulario.crearCuenta"
            etiqueta="Crear cuenta web para el cliente"
            descripcion="Podrá ver sus mascotas y citas desde internet."
          />
          <div v-if="formulario.crearCuenta" class="sm:max-w-sm">
            <BaseInput
              v-model="formulario.password"
              etiqueta="Contraseña inicial"
              tipo="password"
              autocomplete="new-password"
              :error="errores.password"
              :ayuda="
                requisitosPassword(formulario.password).length
                  ? `Falta: ${requisitosPassword(formulario.password).join(', ').toLowerCase()}`
                  : 'Cumple todos los requisitos'
              "
              obligatorio
            />
          </div>
        </fieldset>

        <div
          class="flex flex-col-reverse gap-2 border-t border-slate-100 pt-5 sm:flex-row sm:justify-end"
        >
          <BaseButton variante="secundario" @click="router.back()">Cancelar</BaseButton>
          <BaseButton tipo="submit" :cargando="guardando">
            {{ esEdicion ? 'Guardar cambios' : 'Registrar cliente' }}
          </BaseButton>
        </div>
      </form>
    </EstadoCarga>
  </div>
</template>
