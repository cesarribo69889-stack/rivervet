<template>
  <div class="mx-auto max-w-lg py-16 text-center">
    <p class="text-5xl font-bold text-amber-500">403</p>
    <h1 class="mt-4 text-2xl font-semibold text-slate-900">
      No tienes permiso para ver esta página
    </h1>
    <p class="mt-2 text-slate-500">
      Si crees que deberías tener acceso, pídeselo al administrador de la clínica.
    </p>
    <RouterLink
      :to="{ name: 'inicio' }"
      class="mt-6 inline-block rounded-lg bg-teal-700 px-4 py-2 text-sm font-semibold text-white hover:bg-teal-800"
    >
      Volver al inicio
    </RouterLink>
  </div>
</template>
