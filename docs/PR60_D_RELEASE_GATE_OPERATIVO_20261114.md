# F317 — Plan operativo y puerta GO/NO-GO de D para PR #60

**Actualizado:** 14 de noviembre de 2026. **Estado de autorización: NO-GO para fusionar/desplegar Production.** Este documento prevalece sobre las referencias históricas del archivo `PR60_RELEASE_GATE_MAESTRO_20261113.md`; no altera ni sustituye los documentos de evidencia F220–F316.

**Repositorio:** `Marzza01/ERP-Log-stica-`. **Candidato congelado para esta evaluación:** `staging/catalog-500-neon-test@dbedb4e7061b83f202fb66803520ab591a1335b8` → `main@e04c90696e59d86390216a8b51bf00e876cfebc5`; PR #60 **OPEN/DRAFT**. No cambiar el criterio GO/NO-GO por avanzar una casilla del checklist.

## 1. Lo que está demostrado

| Área | Evidencia y límite |
| --- | --- |
| Consolidación histórica | F220: 93 referencias clasificadas (5/52/1/10/25). F302/F303: 11 worktrees locales y 29 rutas únicas con cambios; variantes históricas de importador 500-SKU y matriz XLSX preservadas pero no declaradas integradas. |
| Bloque C | Cierre funcional de **QA STAGING** F298 con `Payments:Provider=Disabled`. NO certifica cobros comerciales reales, pasarela de producción ni todos los sistemas externos. No volver a abrirlo por un cambio exclusivamente documental. |
| Calidad | [GitHub CI #43646032629](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43646032629) **4/4 SUCCESS** para `dbedb4e` (.NET, frontend, E2E Chromium, comparador PowerShell). F304: 18/18 escenarios del release gate (simulados), F315: 11/11 escenarios de política Vercel (simulados) y GET real `F315_POLICY_PASS=manual_promotion_only`. |
| GitHub `main` | Branch Protection **activa**, cuatro checks requeridos para todo usuario. Confirmado por API. No confundir protección de merge con protección de dominio. |
| STAGING | Render STAGING `dep-dbsd99jm8hqs73ce5nm0` LIVE, Vercel Preview `dpl_ECdw9WYFxRAE58E3mrC7gMCgMMA7` READY, ambos en fuente `8d531f3` (los cambios posteriores `dbedb4e` son scripts/CI de seguridad); Neon STAGING 39 tablas EF Core2/Catalog7/Finance10; 500 productos. |
| Vercel Production | Proyecto `prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82`; deployment actual `dpl_4C15u8Ai87uGNuuVyfMyfsZSgvmv` READY @ `e04c906`. Dominio `erp-log-stica.vercel.app` y otros dos alias intactos. `autoAssignCustomDomains=false` **confirmado con GET autenticado de API oficial** en F314 y F315. No hay prueba de un nuevo deployment Production **Staged**, ni Deployment Checks de Vercel activados. |
| Render Production | Servicio `srv-dbl9stegekts73foqer0`, branch histórica `integracion/main-reconciliacion-20261102`, AutoDeploy ON, deployment LIVE `dep-dbp5kgu7bikc73d68ajg@77e9a16e`. No se ha migrado el servicio a `main`. |
| Neon de referencia | `square-lake-18241058/main` (rama `br-broad-band-bb7at8kt`): 37 tablas y 546 filas, EF Core2/Catalog6/Finance6. **No está comprobado mediante valores efectivos de Render que esta sea exactamente la base utilizada por el servicio Productivo**; F312 es correlación de actividad, no prueba concluyente. |
| Respaldo y recuperación | F294 `production-20261114-113600.dump`, SHA256 `5AA835D6647032AC0967A56766579CB6E712C5F5D82B27E02C9B50FD68834853`; restauración separada 37/37 tablas/546 filas, F309 37/37 huellas en ese momento. F300 migración forward 37→39 en PostgreSQL local; F316 **37→39→37 por restauración de dump** en DB local efímera, 37/37 huellas e historiales recuperados, dump intacto, código 0, cero DB temporales. |

## 2. Bloqueos duros, no substituibles con CI

- **D2.A — Configuración Render recuperable:** comprobar en el entorno administrativo correcto (sin divulgar secretos) la identidad de variables efectivas, host/branch/DB Neon, valores de configuración cifrados en bóveda accesible al propietario y procedimiento real de restablecimiento. El conector de Render utilizado NO ofrece lectura de secretos. No guardar valores secretos en Issue, PR, logs, repositorio ni chat. La coincidencia de datos y correlación de CPU/actividad no bastan.
- **D2.B — Copia actual al instante de la liberación:** confirmar la rama Neon realmente conectada; hacer `pg_dump` justo antes del corte, guardar SHA-256 en depósito privado y validar `pg_restore` + fingerprints en proyecto/host independiente. El respaldo F294 del 14-nov es evidencia de ensayo, NO sustituto de un corte futuro tras posibles escrituras.
- **D2.C — Escrituras y compatibilidad:** diseñar y ensayar la ventana de mantenimiento con escritores y colas detenidos, permisos/roles, API anterior/nueva y la recuperación de cambios posteriores al backup. F316 demuestra recuperación **de un snapshot**, no RPO cero; nunca ejecutar migraciones EF `Down` destructivas en Neon Productivo por comodidad.
- **D1 — Barrera real del dominio:** la política manual `autoAssignCustomDomains=false` fue confirmada, pero faltan observaciones de un deployment `main` deliberadamente creado como **Staged**, sin reasignación de alias, y decisión de promoción separada. El workflow F299/F301 en STAGING no se recibe aún por `repository_dispatch` desde `main`; no llamar a ese check «activo». La incidencia del proveedor [vercel/vercel#15095](https://github.com/vercel/vercel/issues/15095) obliga a comprobar de nuevo la preferencia **después** de `vercel promote`.
- **GO/NO-GO final y autorización:** registrar expresamente tres autorizaciones distintas: merge a `main`, cambio/migración de infraestructura/datos y promoción del dominio; no inferirlas de «continúa». No habilitar proveedores de pago reales.

## 3. Orden operativo seguro para el corte autorizado

**FASE A — Antes del merge:**
1. Consultar GitHub: SHA candidato exacto, PR todavía DRAFT, CI 4/4 y los cuatro checks obligatorios en `main`; congelar nuevas modificaciones.
2. Ejecutar `scripts/f315-vercel-promotion-policy.ps1` en el CLI autenticado, en **modo predeterminado `Check`**. Requerir `F315_POLICY_PASS=manual_promotion_only`, sin ejecutar `RestorePolicy` salvo nueva autorización explícita. Preservar el deployment `Current` y sus alias.
3. En Render Production registrar sin imprimir secretos el **host/DB/branch que realmente usa** la aplicación; exportar configuración y secretos a bóveda cifrada, probar que el equipo autorizado puede restaurarlos. Mantener separados de GitHub y STAGING.
4. Preparar backup **fresco** de esa rama/DB con destino independiente; comparar inventarios y huellas. Conservar F294 y la instantánea Neon existente. El historial Neon del plan Free se limita a **6 horas**.
5. Ensayar sobre la nueva copia la secuencia de migración, restauración y reanudación de escritores. Certificar RPO/RTO realistas y ruta de compensación de operaciones posteriores al respaldo. Si no pasa, **NO-GO**.

**FASE B — Después de la autorización explícita del merge:**
6. Marcar PR #60 Ready y fusionarlo solo una vez respetadas reglas de GitHub; comprobar SHAs reales de `main` y conservar commit previo `e04c906`.
7. Confirmar que Vercel crea un despliegue **Staged**, NO promocionado, conservando `Current` y dominio original. Si cambia el alias o la política autoassign, **STOP** y contener publicación; no seguir con la base.
8. En ventana coordinada, aplicar migraciones a la **base Neon cuyo destino efectivo ya fue atestado**, desplegar backend exacto de `main` con configuración recuperable y comprobar `/health`, `/health/ready`, roles, WMS, OMS, Finance y pagos **Disabled**; no invertir el orden sin evidencia de compatibilidad.
9. Aprobar **por separado** la promoción del deployment Vercel exacto; monitorear funcionamiento real y ejecutar nuevamente el guard F315 `Check`. Si una promoción reactivó `autoAssignCustomDomains`, detener nuevos despliegues y reparar solo con aprobación expresa, seguida de GET verificable.
10. Conservar deployment anterior Render/Vercel, backups, ramas y logs hasta pasar la aceptación; luego cerrar E y retirar recursos obsoletos solo después de identificar dependencias.

**FASE C — En caso de fallo:**
- Contener escritores/checkout y nuevas promociones, preservar evidencia; no ejecutar pagos ni restablecer producción al azar.
- Si **solo falla el frontend** y la API/esquema son compatibles, restaurar el alias Vercel al deployment anterior verificado.
- Si falla la **API**, restaurar versión Render y configuración desde el conjunto de referencia, únicamente después de verificar compatibilidad con el esquema efectivo.
- Si falla el **esquema/datos**, restaurar desde backup nuevo en destino seguro; verificar integridad, roles e identidad de host antes de cambiar tráfico. **La restauración de una instantánea descarta escrituras posteriores** salvo reconciliación explícita previamente aprobada. No usar la copia F294 para sobrescribir producción con escrituras posteriores.
- Si no existe evidencia suficiente para restaurar sin perder transacciones, mantener el sistema en mantenimiento y detener la promoción en lugar de improvisar una reversión destructiva.

## 4. Dictamen actual y criterio de cierre

**NO-GO**: backup/restauración de esquema en laboratorio y controles de CI están probados; faltan **configuración/secrets Render recuperables**, atestación definitiva de conexión Neon, ensayo de escritores/rollback completo, confirmación del deployment `Staged` y autorización de operaciones productivas. Checklist Issue #65 **115/123 (8 controles compuestos abiertos)**; no cerrar las casillas D/E anticipadamente. Pagos reales continúan fuera del alcance QA.

**Fuentes:** [Issue #65](https://github.com/Marzza01/ERP-Log-stica-/issues/65) (F294, F300, F309, F312–F316), [PR #60](https://github.com/Marzza01/ERP-Log-stica-/pull/60), [CI verificada](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43646032629).
