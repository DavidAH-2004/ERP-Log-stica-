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

## Límites de seguridad y por qué no está habilitado

1. **Frontend no migrado:** `AuthContext`, `authSession.ts`, `api/client.ts`, `LoginPage` y pruebas Chromium siguen bajo el contrato Bearer/localStorage. Activar `ERP_COOKIE_BFF_MODE` sin migrarlos impediría iniciar sesión correctamente. **No activar el flag aún**, tampoco en Preview de pruebas normales.
2. **Logout no revoca el JWT en Render:** únicamente borra la cookie del navegador. Un JWT ya robado antes de migrar sigue válido hasta su vencimiento. Hace falta revocación/rotación respaldada por un almacén confiable y pruebas de reinicios.
3. **Middleware solo en Vercel:** el acceso directo al backend sigue soportando Bearer y debe mantener su propia autorización, rate limit, registros y comprobación de identidad/roles. El proxy no es una frontera que sustituya la seguridad en API.
4. **CSRF necesita migración:** cuando el frontend sea compatible, pedirá el token del endpoint de sesión y lo añadirá a **cada** mutación. El prototipo rechaza todas las peticiones sin `Origin` o CSRF correcto, incluidas las pruebas antiguas.
5. **Límites distribuidos y proxy confiable:** el limitador login ASP.NET es local a proceso y usa IP observada por Render. No equivale a límite distribuido por IP real + cuenta. No aceptar `X-Forwarded-For` de clientes.
6. **Controles externos:** no se han probado en una Preview desplegada autenticada con Admin/Vendedor/Comprador, ni se han ensayado cookies en navegadores reales, interferencias de nombres de dominio, recuperación de sesiones tras despliegues, ni DataProtection. 
7. **Riesgo del flag**: la función está intencionadamente *inoperante para usuarios* mientras falta la migración React. Ningún proveedor debe habilitar la variable de entorno como si fuera una característica lista.

## Secuencia de cierre prevista

1. Mantener las pruebas locales y CI del adaptador y revisar diseño de cookies/origen; cualquier error debe hacer fallar el cambio.
2. Implementar el cliente BFF en `development` y sustituir lectura de `localStorage`; adaptar e2e-auth-browser, e2e-live-local-api, e2e-staging-preview y F105 para pedir identidad/CSRF sin leer JWT.
3. Verificar que la sesión en Vercel Preview es independiente del backend Production y que Render STAGING está conectado a Neon Desarrollo aislado.
4. Probar login, permisos y caducidad Admin/Vendedor/Comprador, rotación/revocación, CSRF y consultas/escrituras reales con BD temporal; añadir controles distribuidos y auditoría de login.
5. Solo después de CI, revisión de seguridad, rollback y autorización explícita, activar mediante variable en un entorno de evaluación; **Production permanece en Bearer legado hasta una migración aprobada y coordinada**.

**Definición de terminado de #49/#53:** no basta con que existan las cookies experimentales. Se requieren frontend sin JWT en almacenamiento accesible a JS, controles de origen/CSRF, revocación, rate limiting distribuido, pruebas de navegador y acceso/producto, y decisiones separadas de pagos/Production. Ambos issues permanecen abiertos.
