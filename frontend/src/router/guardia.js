// Guard global de navegación.
//
// ⚠ IMPORTANTE: los guards del router son EXPERIENCIA DE USUARIO, no
// seguridad. Evitan que alguien vea una pantalla que no le sirve, pero el
// código del frontend se ejecuta en el navegador del usuario y se puede
// modificar a gusto: cualquiera puede saltarse un guard u ocultar/mostrar un
// botón. La protección REAL está en el backend, que verifica el token, el rol
// y la propiedad del recurso en CADA petición. Creer que ocultar un botón
// protege el endpoint es un error clásico.
export function crearGuardia(useAuth) {
  return async function guardia(to) {
    const auth = useAuth();

    // Primera navegación: intentar recuperar la sesión con la cookie.
    if (!auth.inicializado) await auth.restaurarSesion();

    if (to.meta.soloInvitados) {
      return auth.estaAutenticado ? { name: 'inicio' } : true;
    }
    if (to.meta.publica) return true;

    if (!auth.estaAutenticado) {
      auth.rutaPendiente = to.fullPath;
      return { name: 'login' };
    }

    // to.meta combina el meta de las rutas padre e hija; manda el más
    // específico (el de la hija).
    const roles = to.meta.roles;
    if (roles && !auth.tieneRol(roles)) return { name: 'sin-permisos' };

    return true;
  };
}
