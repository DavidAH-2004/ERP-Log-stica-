# F377 — Puertas de liberación y recuperación: dos entornos MarketplaceERP

**Corte de información:** 2026-11-15 UTC. **Ámbito:** evidencia consultada en GitHub, Render, Neon, Vercel, Railway y CLI Vercel autorizado. **NO-GO para cambiar Production.**

> Este archivo es un runbook de *preflight* y de recuperación, **no prueba que los secretos estén respaldados** ni autoriza ejecutar cambios productivos. Ver #49, #61, #65 y la postergación comercial #69.

## 1. Topología real y protecciones

| Recurso | Producción / main | Desarrollo / development |
|---|---|---|
| GitHub | `main`; protegido; PR #60 ya fusionada | `development` con CI 4/4 en `40017a1` |
| Render | `marketplaceerp-api` (servicio `srv-dbl9stegekts73foqer0`), **todavía** rama `integracion/main-reconciliacion-20261102`; AutoDeploy ON; SHA LIVE `77e9a16` | `marketplaceerp-api-staging` (`srv-dbmnd6jrjlhs73c0jung`), rama `development`; AutoDeploy OFF; deploy LIVE `dep-dbsjg8t6e07s73a20sl0`, SHA `40017a1` |
| Vercel | Dominio `erp-log-stica.vercel.app` todavía vinculado a deployment `dpl_4C15u8Ai87uGNuuVyfMyfsZSgvmv` (no al último build READY de `main`) | Preview protegida `dpl_Gnhgk4L85ekytxfGB96gnyC92Wbc` en SHA `40017a1`, `READY` |
| Neon | Proyecto histórico candidato `square-lake-18241058`, DB `marketplaceerp_test` en rama default `main`: 37 tablas; EF Core/Catalog/Finance 2/6/6. **No se ha certificado host efectivo de la API Render Production.** | Proyecto `tiny-sound-11266101`, DB `marketplaceerp_test` rama `development`: 40 tablas; EF 4/7/10 y respaldos/ramas de ensayo conservados |
| Pagos bancarios | **NO IMPLEMENTADOS / diferidos a #69**; mantener proveedor Disabled | Prototipos de QA; mantener proveedor Disabled |

Vercel Preview `development` usa exclusivamente `API_STAGING_ORIGIN=https://marketplaceerp-api-staging.onrender.com`; las banderas `ERP_COOKIE_BFF_MODE` y `VITE_BFF_AUTH_MODE` no están configuradas, por lo que el modo BFF experimental no está activado.

**F376 smoke autenticado a Deployment Protection (no a la aplicación):** `vc curl` contra Preview protegida respondió `GET /api/health` 200 JSON `status=healthy`, `GET /api/health/ready` 200 y `GET /api/auth/session` anónimo 401; **no** acredita E2E autenticado con tres roles.

## 2. Branch cleanup: exactitud antes que cantidad

GitHub remoto contiene **exactamente 4 ramas** al corte: `main`, `development`, `integracion/main-reconciliacion-20261102`, `demo-web-logimkt`.

La comparación contra `main` de los dos restos históricos mostró:
- `integracion/main-reconciliacion-20261102`: 1 commit único adelante y 330 detrás; 2 rutas en diff (`ProductosController.cs` y `Producto.cs`). **Render Production todavía está vinculado**: **PROHIBIDO borrar** antes del cutover y rollback.
- `demo-web-logimkt`: 1 commit único adelante y 339 detrás; 9 rutas de cambio incluyendo `railway.json`, Docker, `UsuariosController`, `Program`, finance y tests. El conector Railway de esta cuenta devolvió **0 proyectos accesibles**, lo cual **NO prueba** inexistencia de otro workspace, cuenta o dependencia externa. Revisar propiedad/último deploy antes de borrar.
- `development` está 66 commits por delante de `main` y ninguno por detrás (normal: mejoras experimentales en desarrollo; no fusionar de manera automática).

El paquete histórico de 93 referencias ya cuenta con respaldo Git F321 según #65, pero la eliminación de ramas remanentes requiere prueba externa de integridad del bundle y certificación de dependencias.

## 3. DataProtection, autenticación y alcance real

La API actual registra `JwtBearer`, no Cookie Authentication ni ASP.NET Antiforgery. El BFF HttpOnly/CSRF experimental vive en Vercel y permanece OFF; el gate estático `scripts/f372-dataprotection-consumers.mjs` detecta referencias explícitas a consumidores DataProtection en `src` y aprueba en CI.

Render DEV emite warnings `EphemeralXmlRepository[50]`, `XmlKeyManager[59]` y `[35]` tras reinicio. Se justifican **solo** para el flujo JWT Bearer actual, no para nuevos consumidores de DataProtection. Si el futuro backend utiliza cookies/antiforgery/session: implementar repositorio persistente cifrado para claves, compartirlo entre instancias y probar autenticación antes/después de reinicios.

La PR #68 está **DRAFT**: faltan E2E autenticados reales en Preview con Admin/Vendedor/Comprador, revocación, CSRF, protección fiable de IP y manejo de renovación. Los flags `Auth:EnableSessionVersionCheck` y `Auth:EnableDistributedAccountLoginThrottle` permanecen OFF por defecto. La prueba local/PostgreSQL CI no reemplaza esta certificación remota.

## 4. Respaldo y recuperación de secretos: puerta obligatoria

**NO almacenar credenciales, URL con password, claves JWT, variables privadas ni exportaciones `.env` en GitHub, tickets, logs, artifacts públicos o respuestas del asistente.**

Antes de cualquier cambio en Production, un operador autorizado debe:
1. Hacer inventario por **nombre y scope** de cada variable Render Prod y Vercel Prod/Preview, GitHub env secrets y rutas de resolución de `ConnectionStrings:DefaultConnection`, `Jwt:Key`, `Payments:Provider`, `Cors:AllowedOrigins`. Registrar solo nombres y fingerprints HMAC/huellas que no revelen valores.
2. Respaldar **valores efectivos** y procedencia en vault cifrado controlado por propietario, con control de acceso distinto de STAGING y una segunda copia protegida. Si un proveedor no permite exportar secretos, documentar proceso **probado** para reintroducir/rotar cada valor sin leerlos por la IA.
3. En instancia desechable **no productiva**, reconstruir las mismas claves y scopes con valores ficticios equivalentes, confirmar precedencia de variables y conexión al destino esperado y ejecutar restauración validada de configuración. No simular que esto reconstruye los secretos reales.
4. Certificar backup lógico **fresco e independiente** de la BD **efectivamente conectada** a Render Prod (el F294 anterior no acredita cambios posteriores). SHA-256, recuentos, tablas, historiales EF, roles/privilegios, inventario de escritores y restauración sobre destino aislado sin pagos.
5. Ensayar migración `37→39` (o esquema que corresponda al árbol objetivo) sobre copia actual y rollback coordinado de Vercel + Render + conexiones DB. Las migraciones Finance con `Down` fail-closed pueden impedir rollback físico: preferir recuperación restaurada bajo ventana de congelación y estrategia de *forward fix*, nunca revertir DB a ciegas.
6. No conmutar Render a `main` ni promover el alias de Vercel mientras no coincidan host efectivo, schema, credenciales, smoke JWT por roles, backups y rollback en el mismo release SHA.

**Barrera de publicación:** respaldo/restore PASS + claves/scopes PASS + migraciones PASS + E2E autorizados PASS + plan de corte/rollback PASS + autorización productiva explícita = GO. Cualquier `FAIL` o `NOT VERIFIED` = NO-GO.

## 5. Observabilidad y alertas

- `ERP_HTTP` emite rutas categorizadas, método, status y latencia sin credenciales. Observada latencia fría de readiness en Render DEV de aproximadamente 12 s y posteriores `<600ms` en muestras QA.
- `scripts/f371-readiness-monitor.mjs` permite prueba manual solo con GET a dos endpoints de Render DEV; CI F371/F372 pasó 4/4.
- Automatización horaria #1: health/ready DEV, activa y con primera ejecución HTTP 200/200. Automatización horaria #2: errores 5xx/503, volumen 429 y `elapsed_ms >= 15000` de logs DEV; **aún no está probada la entrega efectiva de alertas de anomalías**.
- Falta certificar retención por plan efectivo, export/log stream si es necesario, una alerta simulada o incidente real verificado, monitorización productiva autorizada y SLO representativo. No marcar #61 por la sola existencia de las tareas.

## 6. Orden para cerrar #61, #49, #65

1. **#61:** completar inventario efectivo de variables productivas sin revelar valores, detectar proveedor de pagos Disabled en entorno efectivo, probar retención/entrega de alertas y respaldo/rollback de configuración.
2. **#49:** aprobar E2E auténtico de las sesiones/CSRF/revocación y rate limiting distribuido con proxy/IP de confianza. La pasarela real quedó **administrativamente trasladada a #69**, no implementada.
3. **#65:** finalizar respaldo de secretos y DB efectiva, ensayo completo no productivo, aprobación explícita de corte, actualización Render Production→`main` + promoción Vercel con smoke y rollback, y finalmente podar ramas/resources al certificar dependencias.

**Ninguna operación en Production o pago real se ejecutó para redactar este documento.**
