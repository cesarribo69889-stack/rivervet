<script setup>
// Registro de auditoría (solo admin): quién hizo qué, sobre qué y cuándo.
import { onMounted, ref, watch } from 'vue';
import { usePeticion } from '@/composables/usePeticion';
import { listarAuditoria } from '@/services/api/auditoria';
import { formatearFechaHora } from '@/utils/formato';
import { ETIQUETAS_ROL } from '@/utils/roles';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import TablaPaginada from '@/components/base/TablaPaginada.vue';
import BadgeEstado from '@/components/base/BadgeEstado.vue';
import BaseSelect from '@/components/base/BaseSelect.vue';

const ACCIONES = [
  { valor: 'LOGIN', etiqueta: 'Inicio de sesión', color: 'verde' },
  { valor: 'LOGIN_FALLIDO', etiqueta: 'Login fallido', color: 'ambar' },
  { valor: 'CUENTA_BLOQUEADA', etiqueta: 'Cuenta bloqueada', color: 'rojo' },
  { valor: 'LOGOUT', etiqueta: 'Cierre de sesión', color: 'gris' },
  { valor: 'REUSO_REFRESH_TOKEN', etiqueta: 'Posible robo de sesión', color: 'rojo' },
  { valor: 'CAMBIO_PASSWORD', etiqueta: 'Cambio de contraseña', color: 'azul' },
  { valor: 'CREAR', etiqueta: 'Alta', color: 'teal' },
  { valor: 'ACTUALIZAR', etiqueta: 'Modificación', color: 'azul' },
  { valor: 'ELIMINAR', etiqueta: 'Baja', color: 'rojo' },
];
const porValor = Object.fromEntries(ACCIONES.map((a) => [a.valor, a]));

const accion = ref(null);
const pagina = ref(1);
const { datos, cargando, error, ejecutar } = usePeticion(listarAuditoria);
const cargar = () => ejecutar({ pagina: pagina.value, accion: accion.value }).catch(() => {});
onMounted(cargar);

watch(accion, () => {
  pagina.value = 1;
  cargar();
});
function cambiarPagina(nueva) {
  pagina.value = nueva;
  cargar();
}

// El detalle viene como objeto JSON; se muestra como texto plano (Vue lo
// escapa), nunca como HTML.
function resumirDetalle(detalle) {
  if (!detalle) return '—';
  if (Array.isArray(detalle.campos)) return `Campos: ${detalle.campos.join(', ')}`;
  return Object.entries(detalle)
    .map(([k, v]) => `${k}: ${typeof v === 'object' ? JSON.stringify(v) : v}`)
    .join(' · ');
}

const columnas = [
  { clave: 'creadoEn', titulo: 'Fecha' },
  { clave: 'usuario', titulo: 'Usuario' },
  { clave: 'accion', titulo: 'Acción' },
  { clave: 'recurso', titulo: 'Recurso' },
  { clave: 'detalle', titulo: 'Detalle' },
  { clave: 'ip', titulo: 'IP' },
];
</script>

<template>
  <div>
    <EncabezadoPagina
      titulo="Auditoría"
      subtitulo="Registro inalterable de las acciones del sistema."
    />

    <div class="mb-4 max-w-xs">
      <BaseSelect
        v-model="accion"
        etiqueta="Filtrar por acción"
        :opciones="ACCIONES"
        texto-vacio="Todas"
      />
    </div>

    <EstadoCarga
      :cargando="cargando && !datos"
      :error="error"
      :vacio="datos?.items.length === 0"
      texto-vacio="No hay registros con ese filtro."
      @reintentar="cargar"
    >
      <TablaPaginada
        v-if="datos"
        :columnas="columnas"
        :filas="datos.items"
        :paginacion="datos.paginacion"
        descripcion="Registros de auditoría"
        @cambiar-pagina="cambiarPagina"
      >
        <template #celda-creadoEn="{ fila }">
          <span class="whitespace-nowrap">{{ formatearFechaHora(fila.creadoEn) }}</span>
        </template>
        <template #celda-usuario="{ fila }">
          <span v-if="fila.usuarioNombre">
            {{ fila.usuarioNombre }}
            <span class="block text-xs text-slate-400">{{ ETIQUETAS_ROL[fila.usuarioRol] }}</span>
          </span>
          <span v-else class="text-slate-400">Anónimo</span>
        </template>
        <template #celda-accion="{ fila }">
          <BadgeEstado :color="porValor[fila.accion]?.color || 'gris'">
            {{ porValor[fila.accion]?.etiqueta || fila.accion }}
          </BadgeEstado>
        </template>
        <template #celda-recurso="{ fila }">
          {{ fila.recurso
          }}<span v-if="fila.recursoId" class="text-slate-400"> #{{ fila.recursoId }}</span>
        </template>
        <template #celda-detalle="{ fila }">
          <span class="text-xs text-slate-500">{{ resumirDetalle(fila.detalle) }}</span>
        </template>
      </TablaPaginada>
    </EstadoCarga>
  </div>
</template>
