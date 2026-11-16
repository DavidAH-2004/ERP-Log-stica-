# F353 — Base de sesiones HttpOnly (prototipo aislado para Preview)

**Fecha:** 14 de noviembre de 2026, El Salvador. **Issues:** [#49](https://github.com/Marzza01/ERP-Log-stica-/issues/49), [#53](https://github.com/Marzza01/ERP-Log-stica-/issues/53). **Estado: NO-GO para activar en Production o Preview de evaluación.**

## Contexto real

El ERP actualmente guarda `erp_token`, `erp_usuario` y `erp_expires_at` en `localStorage` y usa `Authorization: Bearer` desde React. Numerosas pruebas E2E reales de Chromium leen ese valor para comprobar permisos y operaciones. El proxy actual de Vercel descarta cookies entrantes y `Set-Cookie` del backend para no confundir JWT Bearer con sesiones; no puede convertirse en BFF completo sin migrar esos consumidores y probarlos.

No cambiar el almacenamiento del frontend de una sola vez. **El código F353 se incorpora como infraestructura experimental apagada por defecto para continuar de forma controlada.**

## Implementación F353 (solo infraestructura)

- `frontend/marketplace-ui/bffCookieMode.js`: adaptador para el proxy Vercel con **dos cookies host-only `__Host-...`, `Secure`, `HttpOnly`, `SameSite=Strict` y máximo de 15 minutos**, sin exponer el JWT al cliente en el login opt-in; genera un token aleatorio CSRF distinto, exige `Origin` exacto y `x-erp-csrf` para operaciones de escritura, y descarta el `Authorization` enviado desde el navegador.
- `frontend/marketplace-ui/middleware.js`: el adaptador solo se usa cuando **ambas** condiciones se cumplen: `VERCEL_ENV=preview` y `ERP_COOKIE_BFF_MODE=enabled`. En cualquier otro caso conserva el comportamiento anterior.
- `src/MarketplaceERP.API/Controllers/AuthController.cs`: nuevo `GET /api/auth/session` con `[Authorize]`, que consulta usuario activo y rol vigente en Core, y devuelve únicamente identidad, rol y vencimiento (sin JWT). Permite recuperar la sesión desde una cookie del proxy tras recargar la página.
- `frontend/marketplace-ui/tests/bff-cookie.test.cjs`: pruebas negativas/positivas del login, cookies, token CSRF, ruta de sesión, encabezados peligrosos, logout y protección de producción. Se incorporan al script `npm run test:auth` obligatorio en CI.
- No hay migración de base de datos, no se crea cuenta ni usuario y no se habilitan pagos.

## F354 — Cliente experimental opt-in incorporado (sin activar en Vercel)

El backend y el proxy F353 se complementan ahora con un cliente React experimental:

- `VITE_BFF_AUTH_MODE=enabled` es un **flag de compilación del frontend**, separado del flag del middleware `ERP_COOKIE_BFF_MODE=enabled`. Se necesitan **ambos** y `VERCEL_ENV=preview` para el recorrido. **Ninguno fue configurado en Vercel/Render**.
- `AuthContext.tsx`: si el flag está compilado, limpia los antiguos valores de autenticación localStorage, rehidrata la sesión por `GET /api/auth/session`, mantiene únicamente identidad y vencimiento, y verifica la respuesta del backend antes de conceder acceso a la UI. No expone ni almacena JWT.
- `api/client.ts`: en modo BFF no consulta `erp_token` ni emite Bearer desde el navegador; usa cookies same-origin y envía `X-ERP-CSRF` en operaciones mutadoras, con el token solamente en memoria de módulo.
- `LoginPage.tsx`: selecciona ruta por el rol que acaba de verificar `AuthProvider`, sin leer `erp_usuario` de localStorage.
- `App.tsx`: espera el resultado de rehidratación en modo cookie, evitando redirecciones prematuras.
- `tests/bff-client.test.cjs`: añade comprobaciones del cliente para no leer JWT, enviar CSRF y usar cookies same-origin. Se ejecuta con `npm run test:auth`.

**Este código sigue desactivado por defecto**. Los flujos Chromium heredados prueban Bearer/localStorage, no un navegador real con los dos flags BFF. No habilitar Preview ni Production hasta completar las pruebas y la revisión que aparecen abajo.

## F355–F356 — Validación aislada adicional

- `tests/e2e-bff-cookie-browser.mjs`: test Chromium sobre **HTTPS local con certificado efímero** y un upstream simulado en memoria; comprueba cookies `Secure`/`HttpOnly`, ausencia de JWT en `localStorage`, recarga/identidad, CSRF 403/204, logout y rutas Admin, Vendedor, Comprador. No se conecta a Neon, Render ni pasarelas de pago.
- `.github/workflows/ci.yml`: ejecuta este test en un paso separado del job Chromium, con `VITE_BFF_AUTH_MODE=enabled` **solo en ese paso**. El CI normal y los despliegues no reciben el flag.
- `tests/MarketplaceERP.Finance.Tests/AuthCookieSessionControllerTests.cs`: pruebas de la acción de sesión con `CoreDbContext` InMemory (sin datos externos) para usuario activo y rol válido, frente a usuario inactivo, JWT expirado, identidad ausente, cambio de rol y rol desconocido.
- Estas comprobaciones **no certifican** la combinación real Vercel Preview → Render Desarrollo → Neon Desarrollo, ni revocación JWT en todas las instancias. El merge sigue sujeto a revisión del CI final, y la activación sigue bloqueada.

## F369 — Rate limiting distribuido por cuenta (opt-in, NO-GO para activar)

**Qué se implementa:** una ventana PostgreSQL atómica de **5 intentos por 60 segundos por identificador de correo**, compartida por todas las instancias Render que utilicen la misma base. Para evitar filtrar si una cuenta existe, se aplica **antes de buscar usuarios** y se cuenta igual con emails existentes y desconocidos. La tabla `core.login_throttle` almacena **solo** `AccountKey` (HMAC-SHA256 del email normalizado, con separación de dominio y clave privada), `WindowStartUtc` y `Requests`. No almacena IP, email en texto plano, token, cookie ni contraseña. La actualización `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` es atómica incluso entre procesos y evita que la concurrencia supere el límite en una ventana.

**Configuración:** `Auth:EnableDistributedAccountLoginThrottle=true` activa el cambio únicamente después de desplegar y certificar la migración EF `20261115042500_AgregarThrottleLoginCuenta`. **Flag desactivado por defecto** en todo entorno. Si la base o tabla no está disponible en modo estricto, el login **falla cerrado 503**, nunca cae a protección permisiva. La política IP anterior `LoginLimiter` sigue existiendo pero es **in-memory/per-instancia y puede agrupar clientes tras el proxy**: este cambio no certifica todavía un WAF ni rate limiting IP distribuido/identidad de edge confiable.

**Pruebas F369 (PostgreSQL desechable CI):** 5 fallos/401 y sexto 429 para una cuenta existente y otra inexistente; 20 intentos paralelos de un tercero, exactamente 5 rechazados normalmente (401) y 15 bloqueados (429); la tabla no contiene correos o identificadores sin HMAC; ventana expirada se reinicia y un login correcto vuelve a 200; flag OFF conserva el login previo.

**Pendientes operativos antes de habilitar:** 
- Backup y ensayo sobre Neon Desarrollo, aplicar nuevo historial Core (tercera→cuarta migración), revisar 39→40 tablas, y verificar recuperación/despliegue Render DEV. El código no debe desplegarse sin DDL aplicado.
- Probar el comportamiento con un servicio real mediante un procedimiento autorizado que no exponga credenciales a herramientas. Una prueba CI no equivale a un E2E del edge.
- Establecer una tarea de purga/retención de identificadores HMAC antiguos para controlar crecimiento y un límite de solicitudes en edge por IP fiable (o proveedor WAF), evitar bloqueo masivo intencionado de cuentas y alertar sobre 429/503 agregados.
- No activar en Production, ni cerrar #49 por este avance: faltan pagos reales o decisión formal de diferirlos y la validación de sesiones HttpOnly.

## F360 — Revocación persistente de sesión (código en Desarrollo, NO desplegado)

- `core.usuarios.VersionSesion` (nuevo entero no negativo, valor inicial 0) y migración EF `20261115032000_AgregarVersionSesionJWT`. No ejecutar esta migración en producción ni en Neon Desarrollo sin preflight y backup/rehearsal.
- JWT de login añade claim firmado `sv` con la generación vigente de la cuenta. La verificación `OnTokenValidated` consulta PostgreSQL compartido en cada request y exige que `sv`, estado activo y rol coincidan **solo cuando** `Auth:EnableSessionVersionCheck=true`. En el comportamiento por defecto la opción está **OFF**.
- `POST /api/auth/logout`: exige rol explícito Admin/Vendedor/Comprador y solo responde 204 después de un incremento **atómico** de generación en PostgreSQL. Invalida así todas las sesiones anteriores de esa cuenta, incluso en otras instancias. Si la función está desactivada o la BD no confirma el cambio, responde 503/401; nunca simula éxito.
- El BFF ya no borra cookies por logout si el servidor no confirma 204. La prueba negativa exige 503 sin `Set-Cookie` de borrado; el simulador Chromium comprueba revocación.
- `JwtSessionGenerationTests` usa **exclusivamente** PostgreSQL efímero `postgres@localhost:5432/postgres` dentro de GitHub CI para verificar versión 0→1, 204 inicial, rechazo de segundo logout y login posterior con nueva versión. **La compilación local no verifica la prueba PostgreSQL de CI; esperar evidencia del run final**.

**Puertas de operación F360:**
1. No desplegar la versión F360 en Render Desarrollo hasta tener migración aplicada sobre Neon Development 39→39 tablas, con huellas/respaldo y migraciones EF verificadas. Aunque el flag esté OFF, la columna nueva forma parte del modelo EF de login.
2. No activar `Auth:EnableSessionVersionCheck` hasta que *todas* las instancias usen el mismo esquema y el nuevo JWT; los tokens previos sin `sv` serán rechazados al activarlo. Esto exige coordinación de usuarios, recuperación y rollback probado.
3. El esquema de versión invalida **todas** las sesiones de una cuenta, no solo el dispositivo actual. Es una propiedad intencionada de la primera fase de revocación. Rotación automática de refresh tokens por dispositivo y revocación selectiva quedan **pendientes**.
4. Cada request autenticado en modo estricto agrega lectura a PostgreSQL; comprobar latencia, límite de conexiones, fallos y resiliencia distribuida antes de Production. No añadir caché permisiva que acepte tokens revocados.

## Límites de seguridad y por qué no está habilitado

1. **Frontend experimental, NO validado end-to-end:** F354 añade un modo cookie opt-in a `AuthContext`, `api/client.ts` y `LoginPage`, pero el comportamiento por defecto continúa siendo Bearer/localStorage. Las pruebas Chromium heredadas **todavía dependen** de `erp_token`; falta una matriz E2E en navegador con **ambos flags** compilados y configurados, para roles Admin/Vendedor/Comprador, reinicios, logout y datos reales sintéticos. **No activar aún en Vercel.**
2. **Logout no revoca el JWT en Render:** únicamente borra la cookie del navegador. Un JWT ya robado antes de migrar sigue válido hasta su vencimiento. Hace falta revocación/rotación respaldada por un almacén confiable y pruebas de reinicios.
3. **Middleware solo en Vercel:** el acceso directo al backend sigue soportando Bearer y debe mantener su propia autorización, rate limit, registros y comprobación de identidad/roles. El proxy no es una frontera que sustituya la seguridad en API.
4. **CSRF necesita migración:** cuando el frontend sea compatible, pedirá el token del endpoint de sesión y lo añadirá a **cada** mutación. El prototipo rechaza todas las peticiones sin `Origin` o CSRF correcto, incluidas las pruebas antiguas.
5. **Límites distribuidos y proxy confiable:** el limitador login ASP.NET es local a proceso y usa IP observada por Render. No equivale a límite distribuido por IP real + cuenta. No aceptar `X-Forwarded-For` de clientes.
6. **Controles externos:** no se han probado en una Preview desplegada autenticada con Admin/Vendedor/Comprador, ni se han ensayado cookies en navegadores reales, interferencias de nombres de dominio, recuperación de sesiones tras despliegues, ni DataProtection. 
7. **Riesgo del flag**: la función está intencionadamente *inoperante para usuarios* mientras falta la migración React. Ningún proveedor debe habilitar la variable de entorno como si fuera una característica lista.

## Secuencia de cierre prevista

1. Mantener las pruebas locales y CI del adaptador y revisar diseño de cookies/origen; cualquier error debe hacer fallar el cambio.
2. El cliente BFF experimental de F354 ya está codificado en `development` con opt-in; **pendiente** adaptar las pruebas e2e-auth-browser, e2e-live-local-api, e2e-staging-preview y F105 para verificar identidad/CSRF sin leer JWT en una Preview real. La ruta Bearer heredada sigue como comportamiento por defecto.
3. Verificar que la sesión en Vercel Preview es independiente del backend Production y que Render STAGING está conectado a Neon Desarrollo aislado.
4. Probar login, permisos y caducidad Admin/Vendedor/Comprador, rotación/revocación, CSRF y consultas/escrituras reales con BD temporal; añadir controles distribuidos y auditoría de login.
5. Solo después de CI, revisión de seguridad, rollback y autorización explícita, activar mediante variable en un entorno de evaluación; **Production permanece en Bearer legado hasta una migración aprobada y coordinada**.

**Definición de terminado de #49/#53:** no basta con que existan las cookies experimentales. Se requieren frontend sin JWT en almacenamiento accesible a JS, controles de origen/CSRF, revocación, rate limiting distribuido, pruebas de navegador y acceso/producto, y decisiones separadas de pagos/Production. Ambos issues permanecen abiertos.
