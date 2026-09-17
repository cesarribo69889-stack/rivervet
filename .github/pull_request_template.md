## ¿Qué hace este PR?

<!-- Historia de usuario: HU-XXX. Explica el cambio en 2-3 líneas. -->

## Checklist general

- [ ] La rama se llama `feature/HU-XXX-descripcion`
- [ ] Los commits siguen Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`)
- [ ] No hay secretos, contraseñas ni cadenas de conexión en el diff
- [ ] Agregué o actualicé pruebas

## Si toca el backend (`backend/`)

- [ ] `dotnet build` sin advertencias y `dotnet test` en verde
- [ ] Si cambié la API, actualicé `backend/docs/API.md`
- [ ] Si cambié la base de datos, agregué una migración NUEVA (no edité una existente)
- [ ] Toda consulta nueva usa parámetros y, si es de un cliente, filtra por propietario en el SQL
- [ ] Todo endpoint nuevo declara sus roles con `[Authorize(Roles = ...)]`
