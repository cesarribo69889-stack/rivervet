<script setup>
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useAuthStore } from '@/stores/auth';
import { usePeticion } from '@/composables/usePeticion';
import { buscarClientes } from '@/services/api/clientes';
import { ROLES } from '@/utils/roles';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import TablaPaginada from '@/components/base/TablaPaginada.vue';
import BadgeEstado from '@/components/base/BadgeEstado.vue';
import BaseInput from '@/components/base/BaseInput.vue';

const auth = useAuthStore();
// Solo decide si se MUESTRA el botón; el backend rechaza igual a quien no pueda.
const puedeRegistrar = auth.tieneRol(ROLES.ADMIN, ROLES.RECEPCIONISTA);

const texto = ref('');
const pagina = ref(1);
const { datos, cargando, error, ejecutar } = usePeticion(buscarClientes);

const cargar = () => ejecutar({ texto: texto.value, pagina: pagina.value }).catch(() => {});

// Espera 350 ms después de la última tecla antes de buscar, en lugar de
// consultar la API en cada letra.
let temporizador;
watch(texto, () => {
  clearTimeout(temporizador);
  temporizador = setTimeout(() => {
    pagina.value = 1;
    cargar();
  }, 350);
});
onBeforeUnmount(() => clearTimeout(temporizador));

function cambiarPagina(nueva) {
  pagina.value = nueva;
  cargar();
}

onMounted(cargar);

const columnas = [
  { clave: 'nombre', titulo: 'Cliente' },
  { clave: 'dni', titulo: 'DNI' },
  { clave: 'telefono', titulo: 'Celular' },
  { clave: 'totalMascotas', titulo: 'Mascotas', clase: 'text-center' },
  { clave: 'cuenta', titulo: 'Cuenta web' },
  { clave: 'acciones', titulo: '', clase: 'text-right' },
];
</script>

<template>
  <div>
    <EncabezadoPagina
      titulo="Clientes"
      subtitulo="Busca por nombre, apellido, DNI, email o celular."
    >
      <template #acciones>
        <RouterLink
          v-if="puedeRegistrar"
          :to="{ name: 'cliente-nuevo' }"
          class="inline-flex min-h-10 items-center rounded-lg bg-teal-700 px-4 py-2 text-sm font-semibold text-white hover:bg-teal-800"
        >
          Nuevo cliente
        </RouterLink>
      </template>
    </EncabezadoPagina>

    <div class="mb-4 max-w-md">
      <BaseInput
        v-model="texto"
        etiqueta="Buscar cliente"
        tipo="search"
        placeholder="Ej. 70123456 o Torres"
      />
    </div>

    <EstadoCarga
      :cargando="cargando && !datos"
      :error="error"
      :vacio="datos?.items.length === 0"
      :texto-vacio="
        texto ? 'Ningún cliente coincide con la búsqueda.' : 'Todavía no hay clientes registrados.'
      "
      @reintentar="cargar"
    >
      <TablaPaginada
        v-if="datos"
        :columnas="columnas"
        :filas="datos.items"
        :paginacion="datos.paginacion"
        descripcion="Listado de clientes"
        :class="cargando ? 'opacity-60' : ''"
        @cambiar-pagina="cambiarPagina"
      >
        <template #celda-nombre="{ fila }">
          <span class="font-medium text-slate-900">{{ fila.apellidos }}, {{ fila.nombres }}</span>
        </template>
        <template #celda-cuenta="{ fila }">
          <BadgeEstado :color="fila.tieneCuenta ? 'teal' : 'gris'">
            {{ fila.tieneCuenta ? 'Sí' : 'No' }}
          </BadgeEstado>
        </template>
        <template #celda-acciones="{ fila }">
          <RouterLink
            :to="{ name: 'cliente-detalle', params: { id: fila.id } }"
            class="font-medium text-teal-700 hover:text-teal-900"
          >
            Ver ficha<span class="sr-only"> de {{ fila.nombres }} {{ fila.apellidos }}</span>
          </RouterLink>
        </template>
      </TablaPaginada>
    </EstadoCarga>
  </div>
</template>
