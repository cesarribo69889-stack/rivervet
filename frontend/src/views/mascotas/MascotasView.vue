<script setup>
// Listado de mascotas para el personal (HU-005).
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { usePeticion } from '@/composables/usePeticion';
import { buscarMascotas } from '@/services/api/mascotas';
import { calcularEdad, capitalizar } from '@/utils/formato';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import TablaPaginada from '@/components/base/TablaPaginada.vue';
import BaseInput from '@/components/base/BaseInput.vue';

const texto = ref('');
const pagina = ref(1);
const { datos, cargando, error, ejecutar } = usePeticion(buscarMascotas);

const cargar = () => ejecutar({ texto: texto.value, pagina: pagina.value }).catch(() => {});

let temporizador;
watch(texto, () => {
  clearTimeout(temporizador);
  temporizador = setTimeout(() => {
    pagina.value = 1;
    cargar();
  }, 350);
});
onBeforeUnmount(() => clearTimeout(temporizador));
onMounted(cargar);

function cambiarPagina(nueva) {
  pagina.value = nueva;
  cargar();
}

const columnas = [
  { clave: 'nombre', titulo: 'Mascota' },
  { clave: 'especie', titulo: 'Especie' },
  { clave: 'edad', titulo: 'Edad' },
  { clave: 'clienteNombre', titulo: 'Dueño' },
  { clave: 'acciones', titulo: '', clase: 'text-right' },
];
</script>

<template>
  <div>
    <EncabezadoPagina
      titulo="Mascotas"
      subtitulo="Para registrar una mascota, entra a la ficha de su dueño."
    />

    <div class="mb-4 max-w-md">
      <BaseInput
        v-model="texto"
        etiqueta="Buscar mascota"
        tipo="search"
        placeholder="Nombre de la mascota, del dueño o DNI"
      />
    </div>

    <EstadoCarga
      :cargando="cargando && !datos"
      :error="error"
      :vacio="datos?.items.length === 0"
      :texto-vacio="
        texto ? 'Ninguna mascota coincide con la búsqueda.' : 'Todavía no hay mascotas registradas.'
      "
      @reintentar="cargar"
    >
      <TablaPaginada
        v-if="datos"
        :columnas="columnas"
        :filas="datos.items"
        :paginacion="datos.paginacion"
        descripcion="Listado de mascotas"
        :class="cargando ? 'opacity-60' : ''"
        @cambiar-pagina="cambiarPagina"
      >
        <template #celda-nombre="{ fila }">
          <span class="font-medium text-slate-900">{{ fila.nombre }}</span>
        </template>
        <template #celda-especie="{ fila }">
          {{ capitalizar(fila.especie)
          }}<span v-if="fila.raza" class="text-slate-400"> · {{ fila.raza }}</span>
        </template>
        <template #celda-edad="{ fila }">{{ calcularEdad(fila.fechaNacimiento) }}</template>
        <template #celda-clienteNombre="{ fila }">
          <RouterLink
            :to="{ name: 'cliente-detalle', params: { id: fila.clienteId } }"
            class="text-teal-700 hover:underline"
          >
            {{ fila.clienteNombre }}
          </RouterLink>
        </template>
        <template #celda-acciones="{ fila }">
          <RouterLink
            :to="{ name: 'mascota-detalle', params: { id: fila.id } }"
            class="font-medium text-teal-700 hover:text-teal-900"
          >
            Ver<span class="sr-only"> ficha de {{ fila.nombre }}</span>
          </RouterLink>
        </template>
      </TablaPaginada>
    </EstadoCarga>
  </div>
</template>
