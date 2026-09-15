<script setup>
// "Mis mascotas" del cliente. El backend filtra por propietario en la
// consulta SQL: aunque este código se modificara, la API solo devuelve las
// mascotas de quien inició sesión.
import { onMounted } from 'vue';
import { usePeticion } from '@/composables/usePeticion';
import { buscarMascotas } from '@/services/api/mascotas';
import { calcularEdad, capitalizar } from '@/utils/formato';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';

const { datos, cargando, error, ejecutar } = usePeticion(buscarMascotas);
const cargar = () => ejecutar({ limite: 100 }).catch(() => {});
onMounted(cargar);
</script>

<template>
  <div>
    <EncabezadoPagina titulo="Mis mascotas" />
    <EstadoCarga
      :cargando="cargando"
      :error="error"
      :vacio="datos?.items.length === 0"
      texto-vacio="Aún no tienes mascotas registradas. Acércate a recepción para registrarlas."
      @reintentar="cargar"
    >
      <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <li v-for="m in datos?.items" :key="m.id">
          <RouterLink
            :to="{ name: 'mascota-detalle', params: { id: m.id } }"
            class="block rounded-xl bg-white p-5 shadow-sm ring-1 ring-slate-200 transition hover:ring-teal-400"
          >
            <p class="text-lg font-semibold text-slate-900">{{ m.nombre }}</p>
            <p class="text-sm text-slate-500">
              {{ capitalizar(m.especie) }} · {{ m.raza || 'Sin raza' }}
            </p>
            <p class="mt-3 text-xs text-slate-400">Edad: {{ calcularEdad(m.fechaNacimiento) }}</p>
          </RouterLink>
        </li>
      </ul>
    </EstadoCarga>
  </div>
</template>
