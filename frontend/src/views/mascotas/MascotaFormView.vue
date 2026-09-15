<script setup>
// Registro (HU-004) y edición de mascotas.
//  - Con `clienteId` (ruta /clientes/:clienteId/mascotas/nueva): alta.
//  - Con `id` (ruta /mascotas/:id/editar): edición.
import { computed, onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useNotificacionesStore } from '@/stores/notificaciones';
import { obtenerCliente } from '@/services/api/clientes';
import { actualizarMascota, crearMascota, obtenerMascota } from '@/services/api/mascotas';
import { ESPECIES } from '@/utils/roles';
import { erroresPorCampo, mensajeError } from '@/utils/errores';
import { validarMascota } from '@/utils/validaciones';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import BaseInput from '@/components/base/BaseInput.vue';
import BaseSelect from '@/components/base/BaseSelect.vue';
import BaseButton from '@/components/base/BaseButton.vue';
import BaseAlert from '@/components/base/BaseAlert.vue';

const props = defineProps({
  id: { type: String, default: null },
  clienteId: { type: String, default: null },
});

const router = useRouter();
const notificaciones = useNotificacionesStore();
const esEdicion = computed(() => Boolean(props.id));

const formulario = reactive({
  clienteId: props.clienteId ? Number(props.clienteId) : null,
  nombre: '',
  especie: null,
  raza: '',
  sexo: null,
  fechaNacimiento: '',
  pesoKg: '',
  color: '',
  senasParticulares: '',
  esterilizado: null,
});
const nombreDueno = ref('');
const errores = ref({});
const errorGeneral = ref(null);
const cargando = ref(true);
const errorCarga = ref(null);
const guardando = ref(false);
const hoy = new Date().toISOString().slice(0, 10);

const OPCIONES_SEXO = [
  { valor: 'macho', etiqueta: 'Macho' },
  { valor: 'hembra', etiqueta: 'Hembra' },
];
const OPCIONES_ESTERILIZADO = [
  { valor: true, etiqueta: 'Sí' },
  { valor: false, etiqueta: 'No' },
];

async function cargar() {
  cargando.value = true;
  errorCarga.value = null;
  try {
    if (esEdicion.value) {
      const m = await obtenerMascota(props.id);
      Object.assign(formulario, {
        clienteId: m.clienteId,
        nombre: m.nombre,
        especie: m.especie,
        raza: m.raza || '',
        sexo: m.sexo,
        fechaNacimiento: m.fechaNacimiento || '',
        pesoKg: m.pesoKg ?? '',
        color: m.color || '',
        senasParticulares: m.senasParticulares || '',
        esterilizado: m.esterilizado,
      });
      nombreDueno.value = m.clienteNombre;
    } else {
      const c = await obtenerCliente(props.clienteId);
      nombreDueno.value = `${c.nombres} ${c.apellidos}`;
    }
  } catch (err) {
    errorCarga.value = mensajeError(err);
  } finally {
    cargando.value = false;
  }
}
onMounted(cargar);

const vacioANull = (v) => (typeof v === 'string' ? v.trim() || null : v);

// Solo los campos que el backend acepta (lista blanca). En edición NO se
// envía clienteId: el backend lo rechazaría (el dueño no se cambia editando).
function armarCuerpo() {
  const cuerpo = {
    nombre: formulario.nombre.trim(),
    especie: formulario.especie,
    raza: vacioANull(formulario.raza),
    sexo: formulario.sexo,
    fechaNacimiento: vacioANull(formulario.fechaNacimiento),
    pesoKg:
      formulario.pesoKg === '' || formulario.pesoKg === null ? null : Number(formulario.pesoKg),
    color: vacioANull(formulario.color),
    senasParticulares: vacioANull(formulario.senasParticulares),
    esterilizado: formulario.esterilizado,
  };
  return esEdicion.value ? cuerpo : { clienteId: formulario.clienteId, ...cuerpo };
}

async function guardar() {
  errorGeneral.value = null;
  errores.value = validarMascota(formulario);
  if (Object.keys(errores.value).length) return;

  guardando.value = true;
  try {
    const mascota = esEdicion.value
      ? await actualizarMascota(props.id, armarCuerpo())
      : await crearMascota(armarCuerpo());
    notificaciones.exito(esEdicion.value ? 'Mascota actualizada' : 'Mascota registrada');
    router.push({ name: 'mascota-detalle', params: { id: mascota.id } });
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
    <EncabezadoPagina
      :titulo="esEdicion ? 'Editar mascota' : 'Nueva mascota'"
      :subtitulo="nombreDueno ? `Dueño: ${nombreDueno}` : null"
    >
      <template #migas>
        <button type="button" class="text-sm text-teal-700 hover:underline" @click="router.back()">
          ← Volver
        </button>
      </template>
    </EncabezadoPagina>

    <EstadoCarga :cargando="cargando" :error="errorCarga" @reintentar="cargar">
      <form
        class="space-y-6 rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200"
        novalidate
        @submit.prevent="guardar"
      >
        <BaseAlert v-if="errorGeneral" tipo="error">{{ errorGeneral }}</BaseAlert>

        <div class="grid gap-4 sm:grid-cols-2">
          <BaseInput
            v-model="formulario.nombre"
            etiqueta="Nombre"
            maxlength="60"
            autocomplete="off"
            :error="errores.nombre"
            obligatorio
          />
          <BaseSelect
            v-model="formulario.especie"
            etiqueta="Especie"
            :opciones="ESPECIES"
            :error="errores.especie"
            obligatorio
          />
          <BaseInput
            v-model="formulario.raza"
            etiqueta="Raza"
            maxlength="60"
            autocomplete="off"
            :error="errores.raza"
          />
          <BaseSelect
            v-model="formulario.sexo"
            etiqueta="Sexo"
            :opciones="OPCIONES_SEXO"
            texto-vacio="Sin especificar"
            :error="errores.sexo"
          />
          <BaseInput
            v-model="formulario.fechaNacimiento"
            etiqueta="Fecha de nacimiento"
            tipo="date"
            :max="hoy"
            :error="errores.fechaNacimiento"
          />
          <BaseInput
            v-model="formulario.pesoKg"
            etiqueta="Peso (kg)"
            tipo="number"
            step="0.01"
            min="0.01"
            max="999.99"
            inputmode="decimal"
            :error="errores.pesoKg"
          />
          <BaseInput
            v-model="formulario.color"
            etiqueta="Color"
            maxlength="40"
            autocomplete="off"
            :error="errores.color"
          />
          <BaseSelect
            v-model="formulario.esterilizado"
            etiqueta="Esterilizado"
            :opciones="OPCIONES_ESTERILIZADO"
            texto-vacio="No se sabe"
          />
          <div class="sm:col-span-2">
            <BaseInput
              v-model="formulario.senasParticulares"
              etiqueta="Señas particulares"
              multilinea
              maxlength="250"
              :error="errores.senasParticulares"
              ayuda="Manchas, cicatrices, collar, etc."
            />
          </div>
        </div>

        <div
          class="flex flex-col-reverse gap-2 border-t border-slate-100 pt-5 sm:flex-row sm:justify-end"
        >
          <BaseButton variante="secundario" @click="router.back()">Cancelar</BaseButton>
          <BaseButton tipo="submit" :cargando="guardando">
            {{ esEdicion ? 'Guardar cambios' : 'Registrar mascota' }}
          </BaseButton>
        </div>
      </form>
    </EstadoCarga>
  </div>
</template>
