# Integración continua (CI) y flujo de trabajo en GitHub

> El despliegue continuo (CD) está en [DESPLIEGUE.md](DESPLIEGUE.md). La
> explicación para no técnicos está en [CICD.md](CICD.md).
>
> **Un solo repositorio (monorepo):** `backend/` y `frontend/` viven en el mismo
> repositorio `rivervet`. GitHub solo lee los workflows de la carpeta
> `.github/workflows/` de la RAÍZ, por eso ahí están los cuatro:
> `backend-ci.yml`, `backend-cd.yml`, `frontend-ci.yml` y `frontend-cd.yml`.
> Cada uno ejecuta sus comandos dentro de su carpeta (`defaults.run.working-directory`).

## 1. Qué corre en cada push y pull request

### Backend — [`.github/workflows/backend-ci.yml`](../../.github/workflows/backend-ci.yml)

| Job | Qué hace | Falla si… |
|---|---|---|
| **backend** | `dotnet restore` → `dotnet format --verify-no-changes` → `dotnet build` (analizadores como errores) → `dotnet test` (unitarias + integración contra un SQL Server en contenedor) con cobertura | hay un problema de formato, una advertencia del compilador o de los analizadores (p. ej. SQL concatenado), una prueba falla, o la cobertura de líneas baja del 60 % |
| **seguridad** | `dotnet list package --vulnerable` → gitleaks (todo el historial) → CodeQL (C#) | hay un paquete NuGet con vulnerabilidad alta/crítica, un secreto en el código o un hallazgo de CodeQL |
| **base-de-datos** | Crea una base limpia → aplica las migraciones dos veces → verifica el esquema → carga el seed dos veces → comprueba que el migrador **rechaza** una migración editada | alguna migración no aplica, el esquema queda incompleto o el migrador no detecta la edición |

Equivalencias con el mundo Node, por si las piden en la sustentación:

| Node | .NET |
|---|---|
| ESLint | analizadores de .NET + `TreatWarningsAsErrors` |
| Prettier | `dotnet format` + `.editorconfig` |
| Jest + Supertest | xUnit + `WebApplicationFactory` |
| `npm audit` | `dotnet list package --vulnerable` |

### Frontend — [`.github/workflows/frontend-ci.yml`](../../.github/workflows/frontend-ci.yml)

| Job | Qué hace |
|---|---|
| **frontend** | `npm ci` → ESLint (prohíbe `v-html`, `localStorage`, `eval`) → Prettier → Vitest → `vite build` |
| **seguridad** | `npm audit` → gitleaks → CodeQL |

Los jobs corren **en paralelo**. Un commit nuevo en la misma rama cancela la corrida anterior.
Las dos CI corren en **cada** push y pull request, aunque el cambio toque solo una
carpeta: así los *status checks* obligatorios de la protección de ramas siempre
existen y ningún PR queda esperando un check que nunca corre.

> **CodeQL en repositorio privado:** GitHub solo permite publicar sus hallazgos en
> repositorios privados de cuentas personales con *GitHub Advanced Security*. Por
> eso, si el repositorio es privado, los pasos de CodeQL se omiten con un aviso en
> la corrida. Si lo haces público, corren solos, sin cambiar nada.

## 2. Ramas

```
main      ← producción. Protegida. Solo se toca por pull request desde develop.
develop   ← integración. Protegida. Aquí se juntan las features.
feature/HU-001-login, feature/HU-008-agendar-cita, ...   ← una rama por historia de usuario
```

```bash
git switch develop
git pull
git switch -c feature/HU-006-registrar-atencion
# ... trabajo, commits ...
git push -u origin feature/HU-006-registrar-atencion
# En GitHub: abrir Pull Request  feature/HU-006-...  →  develop
```

### Conventional Commits

```
feat: registrar atención en historial (HU-006)
fix: validar fecha de próxima dosis
test: prueba de concurrencia de doble agendamiento
docs: documentar /citas en API.md
chore(deps): actualizar Dapper
```

## 3. Proteger `main` y `develop` en GitHub

> Requisito: el repositorio debe ser **público**, o tu cuenta debe tener
> **GitHub Pro**. Con el **GitHub Student Developer Pack** (se valida con el
> correo de la UPN) tienes Pro gratis. CodeQL en repositorios privados requiere
> además GitHub Code Security (de pago): si el repo es privado y no lo tienes,
> los workflows ya omiten CodeQL automáticamente (ver arriba).

Hazlo después de que la CI haya corrido al menos una vez (así GitHub conoce
los nombres de los jobs):

1. **Settings → Rules → Rulesets → New ruleset → New branch ruleset**.
2. Nombre: `proteccion-principal`. **Enforcement status: Active**.
3. **Target branches → Add target → Include by pattern**: `main`, y otra vez `develop`.
4. Marca:
   - ☑ **Restrict deletions**
   - ☑ **Block force pushes**
   - ☑ **Require a pull request before merging** → *Required approvals*: **1** → ☑ *Dismiss stale pull request approvals when new commits are pushed*
   - ☑ **Require status checks to pass** → ☑ *Require branches to be up to date before merging* → **Add checks**:
     - backend: `backend (formato + build + pruebas)`, `seguridad (NuGet + gitleaks + CodeQL)`, `base de datos (migraciones)`
     - frontend: `frontend (lint + pruebas + build)`, `seguridad (audit + gitleaks + CodeQL)`
5. **Create**.

Resultado: nadie (ni el dueño del repositorio) puede empujar directo a `main` o
`develop`, y un PR no se mezcla hasta que la CI esté en verde y otra persona lo apruebe.

## 4. Cómo leer una CI en rojo

1. En el PR, clic en **Details** junto al job con ❌.
2. Abre el paso marcado con ❌ y lee **las últimas líneas**: ahí está el error real.

| Paso que falla | Qué significa | Qué hacer |
|---|---|---|
| Formato (`dotnet format`) | espacios, llaves o nombres fuera del estilo | `dotnet format RiverVet.slnx` y vuelve a hacer commit |
| Compilar | error o advertencia (son errores) | `dotnet build` en tu PC; el mensaje indica archivo, línea y regla (p. ej. `CA2100`) |
| Pruebas | una prueba no pasa | `dotnet test`; el log dice qué prueba y qué esperaba |
| `coverage ... is below the specified threshold` | la cobertura bajó del 60 % | agrega pruebas al código nuevo |
| Paquetes vulnerables | dependencia con vulnerabilidad | actualiza el paquete o espera el PR de Dependabot |
| gitleaks | **subiste un secreto** | **Rota el secreto de inmediato** (cámbialo en el servicio) y luego quítalo del código. Borrarlo en un commit nuevo **no basta**: sigue en el historial |
| CodeQL | posible vulnerabilidad | pestaña **Security → Code scanning** muestra archivo y línea |
| Migraciones | una migración no aplica o se editó una vieja | nunca edites una migración aplicada: crea `000N_nueva.sql` |
