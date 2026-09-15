<template>
  <div class="flex min-h-screen flex-col items-center justify-center px-4 text-center">
    <p class="text-6xl font-bold text-teal-700">404</p>
    <h1 class="mt-4 text-2xl font-semibold text-slate-900">Página no encontrada</h1>
    <p class="mt-2 text-slate-500">La dirección que buscas no existe o fue movida.</p>
    <RouterLink
      :to="{ name: 'inicio' }"
      class="mt-6 rounded-lg bg-teal-700 px-4 py-2 text-sm font-semibold text-white hover:bg-teal-800"
    >
      Ir al inicio
    </RouterLink>
  </div>
</template>
