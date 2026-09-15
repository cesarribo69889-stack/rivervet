# Prueba de integración local: las tres capas conectadas

```
Navegador ──► http://localhost:5173 (Vite)
                 │  /api/*  ──proxy──►  http://localhost:3000 (IIS Express · RiverVet.Api)
                 │                              │
                 └─ resto: la SPA de Vue        └──► SQL Server .\SQLEXPRESS (rivervet_db)
```

## 1. Levantar todo

1. **SQL Server** corriendo (servicio "SQL Server (SQLEXPRESS)" en `services.msc`).
2. **API**: en Visual Studio abre `backend\RiverVet.slnx`, perfil **IIS Express**, ▶.
   (Sin Visual Studio: `dotnet run --project src\RiverVet.Api --launch-profile http`.)
3. **Frontend**:
   ```powershell
   cd frontend
   npm run dev
   ```

## 2. Verificar que se hablan entre sí

### Desde la terminal (a través del proxy de Vite, como el navegador)

```powershell
# a) La SPA responde
curl.exe -s http://localhost:5173/ | Select-String 'id="app"'

# b) La API responde A TRAVÉS DEL PROXY y llega a la base de datos
curl.exe -s http://localhost:5173/api/health
#   -> {"data":{"estado":"ok","baseDatos":"ok",...},"success":true}

# c) Login: el access token viene en el body, el refresh token en una cookie
curl.exe -s -c cookies.txt -H "Content-Type: application/json" `
  -d '{\"email\":\"ana.torres@correo.pe\",\"password\":\"RiverVet#2026\"}' `
  http://localhost:5173/api/auth/login
type cookies.txt      # debe listar rv_refresh (#HttpOnly_) y rv_csrf
del cookies.txt
```

### Desde el navegador

1. Abre `http://localhost:5173`, inicia sesión con `recepcion@rivervet.pe` / `RiverVet#2026`.
2. **F12 → Red (Network)**: las peticiones van a `localhost:5173/api/...` (mismo origen) y responden 200.
3. **F12 → Aplicación → Cookies → http://localhost:5173**: están `rv_refresh` (*HttpOnly*, *SameSite Strict*, ruta `/api/auth`) y `rv_csrf`.
4. **F12 → Aplicación → Almacenamiento local**: **vacío**. El token no se guarda ahí.
5. Recarga (F5): sigues con la sesión iniciada; por detrás se llamó a `POST /api/auth/refresh`.
6. Inicia sesión como `ana.torres@correo.pe` y escribe `http://localhost:5173/mascotas/3` (Rocky, de otro cliente): verás "no encontrada" y en Red un **404** del backend.

### Resultado de esta verificación (Sprint 1, API en IIS Express)

```
OK   Vite sirve la SPA
OK   health vía proxy
OK   login vía proxy (token en el body)
OK   cookie rv_refresh guardada para localhost, HttpOnly
OK   cookie rv_csrf legible
OK   GET /api/mascotas con token -> 200 (Ana ve: Firulais, Mishi)
OK   IDOR: mascota ajena (Rocky) -> 404
OK   RBAC: cliente en /api/clientes -> 403
OK   refresh SIN cabecera CSRF -> 403
OK   refresh con CSRF -> 200 y el refresh token rotó
OK   logout -> 200; el refresh token copiado ya no sirve -> 401
OK   CORS: origen no autorizado no recibe Allow-Origin
```

## 3. Errores típicos de CORS y cookies (y por qué NO los tenemos)

La causa de casi todos: el frontend en `localhost:5173` y la API en
`localhost:3000` son **orígenes distintos** (cambia el puerto). Nuestra solución
es no llamar nunca a `:3000` desde el navegador: **Vite hace de proxy** y todo es
mismo origen. En producción, `vercel.json` hace lo mismo con un *rewrite*.

| Error en la consola | Causa | Solución |
|---|---|---|
| `blocked by CORS policy: No 'Access-Control-Allow-Origin'` | El frontend llama directo a `http://localhost:3000` o a `https://localhost:44330` | `VITE_API_URL=/api`. Reinicia `npm run dev` |
| `... must not be the wildcard '*' when credentials mode is 'include'` | Alguien puso `*` en los orígenes | Nunca. `Seguridad:OrigenesCors` con la lista exacta (la API no arranca con `*`) |
| El login funciona pero al recargar se pierde la sesión | La cookie `rv_refresh` no se guardó o no se envía | Abre `http://localhost:5173`, **no** `127.0.0.1:5173` (son sitios distintos para las cookies) |
| `POST /api/auth/refresh` → **403** | Falta la cabecera `X-CSRF-Token` o la cookie `rv_csrf` | El frontend la toma de `document.cookie`: solo es posible si es mismo origen (proxy) |
| `ECONNREFUSED` en la terminal de Vite | La API no está corriendo en el puerto 3000 | Arranca IIS Express (paso 1). Si usas otro puerto, `API_PROXY_DESTINO` en `frontend/.env.local` |
| Cookies no se guardan con la API en HTTPS (44330) desde el proxy | El proxy usa `http://localhost:3000`; en desarrollo `Seguridad:CookiesSeguras=false` | No cambies el proxy a 44330 |
| `429 Too Many Requests` al probar mucho | Límite global (100 solicitudes / 15 min por IP) | Espera, o sube `Seguridad:GlobalMax` en `appsettings.Development.json` |

### ¿Por qué no cookies entre dominios en producción?

`rivervet.vercel.app` y `rivervet-api.azurewebsites.net` son **sitios distintos**.
Para enviar una cookie de uno a otro haría falta `SameSite=None; Secure`, y los
navegadores bloquean cada vez más las cookies de terceros (Safari ya lo hace).
Con el rewrite de Vercel el navegador solo habla con `rivervet.vercel.app`, y
podemos usar `SameSite=Strict`, que es lo más seguro.
