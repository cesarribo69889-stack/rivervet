<script setup>
// Gestión del personal (HU-001: alta de usuario por el admin).
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useAuthStore } from '@/stores/auth';
import { useNotificacionesStore } from '@/stores/notificaciones';
import { useConfirmacion } from '@/composables/useConfirmacion';
import { usePeticion } from '@/composables/usePeticion';
import { actualizarUsuario, crearUsuario, listarUsuarios } from '@/services/api/usuarios';
import { ETIQUETAS_ROL } from '@/utils/roles';
import { erroresPorCampo, mensajeError } from '@/utils/errores';
import {
  requisitosPassword,
  validarEmail,
  validarNombre,
  validarPassword,
} from '@/utils/validaciones';
import { formatearFechaHora } from '@/utils/formato';
import EncabezadoPagina from '@/components/EncabezadoPagina.vue';
import EstadoCarga from '@/components/base/EstadoCarga.vue';
import TablaPaginada from '@/components/base/TablaPaginada.vue';
import BadgeEstado from '@/components/base/BadgeEstado.vue';
import BaseButton from '@/components/base/BaseButton.vue';
import BaseModal from '@/components/base/BaseModal.vue';
import BaseInput from '@/components/base/BaseInput.vue';
import BaseSelect from '@/components/base/BaseSelect.vue';
import BaseAlert from '@/components/base/BaseAlert.vue';

const auth = useAuthStore();
const notificaciones = useNotificacionesStore();
const { confirmar } = useConfirmacion();

const ROLES_PERSONAL = [
  { valor: 'admin', etiqueta: 'Administrador' },
  { valor: 'veterinario', etiqueta: 'Veterinario' },
  { valor: 'recepcionista', etiqueta: 'Recepcionista' },
];
const FILTROS_ROL = [...ROLES_PERSONAL, { valor: 'cliente', etiqueta: 'Cliente' }];

const filtroRol = ref(null);
const pagina = ref(1);
const { datos, cargando, error, ejecutar } = usePeticion(listarUsuarios);
const cargar = () => ejecutar({ pagina: pagina.value, rol: filtroRol.value }).catch(() => {});
onMounted(cargar);

watch(filtroRol, () => {
  pagina.value = 1;
  cargar();
});
function cambiarPagina(nueva) {
  pagina.value = nueva;
  cargar();
}

// ── Modal de alta / edición ──────────────────────────────────────────
const modal = reactive({ abierto: false, usuario: null });
const formulario = reactive({ nombre: '', email: '', password: '', rol: null, activo: true });
const errores = ref({});
const errorGeneral = ref(null);
const guardando = ref(false);
const esEdicion = computed(() => Boolean(modal.usuario));
const esYoMismo = computed(() => modal.usuario?.id === auth.usuario?.id);
const esCuentaCliente = computed(() => modal.usuario?.rol === 'cliente');

function abrir(usuario = null) {
  modal.usuario = usuario;
  Object.assign(formulario, {
    nombre: usuario?.nombre ?? '',
    email: usuario?.email ?? '',
    password: '',
    rol: usuario?.rol ?? null,
    activo: usuario?.activo ?? true,
  });
  errores.value = {};
  errorGeneral.value = null;
  modal.abierto = true;
}

function validar() {
  const e = { nombre: validarNombre(formulario.nombre, { etiqueta: 'El nombre' }) };
  if (!esEdicion.value) {
    e.email = validarEmail(formulario.email);
    e.password = validarPassword(formulario.password);
  }
  e.rol = formulario.rol ? null : 'Selecciona el rol';
  errores.value = Object.fromEntries(Object.entries(e).filter(([, v]) => v));
  return Object.keys(errores.value).length === 0;
}

async function guardar() {
  errorGeneral.value = null;
  if (!validar()) return;

  if (esEdicion.value && modal.usuario.activo && !formulario.activo) {
    const ok = await confirmar({
      titulo: 'Desactivar usuario',
      mensaje: `${formulario.nombre} perderá el acceso de inmediato y se cerrarán sus sesiones. ¿Continuar?`,
      textoConfirmar: 'Desactivar',
    });
    if (!ok) return;
  }

  guardando.value = true;
  try {
    if (esEdicion.value) {
      await actualizarUsuario(modal.usuario.id, {
        nombre: formulario.nombre.trim(),
        rol: formulario.rol,
        activo: formulario.activo,
      });
      notificaciones.exito('Usuario actualizado');
    } else {
      await crearUsuario({
        nombre: formulario.nombre.trim(),
        email: formulario.email.trim(),
        password: formulario.password,
        rol: formulario.rol,
      });
      notificaciones.exito('Usuario creado');
    }
    modal.abierto = false;
    cargar();
  } catch (err) {
    errores.value = erroresPorCampo(err);
    errorGeneral.value = mensajeError(err);
  } finally {
    guardando.value = false;
  }
}

const columnas = [
  { clave: 'nombre', titulo: 'Nombre' },
  { clave: 'email', titulo: 'Email' },
  { clave: 'rol', titulo: 'Rol' },
  { clave: 'activo', titulo: 'Estado' },
  { clave: 'ultimoLoginEn', titulo: 'Último ingreso' },
  { clave: 'acciones', titulo: '', clase: 'text-right' },
];
</script>

<template>
  <div>
    <EncabezadoPagina titulo="Usuarios" subtitulo="Personal de la clínica y cuentas de clientes.">
      <template #acciones>
        <BaseButton @click="abrir()">Nuevo usuario</BaseButton>
      </template>
    </EncabezadoPagina>

    <div class="mb-4 max-w-xs">
      <BaseSelect
        v-model="filtroRol"
        etiqueta="Filtrar por rol"
        :opciones="FILTROS_ROL"
        texto-vacio="Todos"
      />
    </div>

    <EstadoCarga
      :cargando="cargando && !datos"
      :error="error"
      :vacio="datos?.items.length === 0"
      @reintentar="cargar"
    >
      <TablaPaginada
        v-if="datos"
        :columnas="columnas"
        :filas="datos.items"
        :paginacion="datos.paginacion"
        descripcion="Listado de usuarios"
        @cambiar-pagina="cambiarPagina"
      >
        <template #celda-nombre="{ fila }">
          <span class="font-medium text-slate-900">{{ fila.nombre }}</span>
        </template>
        <template #celda-rol="{ fila }">{{ ETIQUETAS_ROL[fila.rol] }}</template>
        <template #celda-activo="{ fila }">
          <BadgeEstado :color="fila.activo ? 'verde' : 'rojo'">{{
            fila.activo ? 'Activo' : 'Inactivo'
          }}</BadgeEstado>
        </template>
        <template #celda-ultimoLoginEn="{ fila }">{{
          formatearFechaHora(fila.ultimoLoginEn)
        }}</template>
        <template #celda-acciones="{ fila }">
          <button
            type="button"
            class="font-medium text-teal-700 hover:text-teal-900"
            @click="abrir(fila)"
          >
            Editar<span class="sr-only"> a {{ fila.nombre }}</span>
          </button>
        </template>
      </TablaPaginada>
    </EstadoCarga>

    <BaseModal
      :abierto="modal.abierto"
      :titulo="esEdicion ? 'Editar usuario' : 'Nuevo usuario'"
      @cerrar="modal.abierto = false"
    >
      <form id="form-usuario" class="space-y-4" novalidate @submit.prevent="guardar">
        <BaseAlert v-if="errorGeneral" tipo="error">{{ errorGeneral }}</BaseAlert>
        <BaseInput
          v-model="formulario.nombre"
          etiqueta="Nombre completo"
          :error="errores.nombre"
          obligatorio
        />
        <BaseInput
          v-if="!esEdicion"
          v-model="formulario.email"
          etiqueta="Email"
          tipo="email"
          autocomplete="off"
          :error="errores.email"
          obligatorio
        />
        <BaseInput
          v-if="!esEdicion"
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
        <p v-if="esEdicion" class="text-sm text-slate-500">Email: {{ modal.usuario.email }}</p>
        <BaseSelect
          v-if="!esCuentaCliente"
          v-model="formulario.rol"
          etiqueta="Rol"
          :opciones="ROLES_PERSONAL"
          :error="errores.rol"
          obligatorio
          :deshabilitado="esYoMismo"
        />
        <label v-if="esEdicion" class="flex items-center gap-3 text-sm font-medium text-slate-700">
          <input
            v-model="formulario.activo"
            type="checkbox"
            class="h-4 w-4 rounded border-slate-300 text-teal-700"
            :disabled="esYoMismo"
          />
          Cuenta activa
        </label>
        <p v-if="esYoMismo" class="text-xs text-slate-500">
          No puedes cambiar tu propio rol ni desactivar tu cuenta.
        </p>
      </form>
      <template #pie>
        <BaseButton variante="secundario" @click="modal.abierto = false">Cancelar</BaseButton>
        <BaseButton tipo="submit" form="form-usuario" :cargando="guardando">Guardar</BaseButton>
      </template>
    </BaseModal>
  </div>
</template>
