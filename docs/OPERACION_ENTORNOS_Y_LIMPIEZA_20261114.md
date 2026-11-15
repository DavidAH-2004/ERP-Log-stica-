# MarketplaceERP — Arquitectura operativa y cierre de higiene del repositorio

**Inventario verificado:** 14 de noviembre de 2026 (El Salvador). **Estado:** integración de código a `main` completada; liberación comercial de Production **NO autorizada / NO-GO**. Este documento reemplaza como inventario vigente las cifras históricas de Issue #63 (88 ramas, 560 blobs, `main` sin proteger).

## Entornos permanentes y promoción

| Capa | Producción | Desarrollo |
| --- | --- | --- |
| GitHub | `main` (protegida) | `development` (rama de trabajo; protección propia aún pendiente) |
| Render | `marketplaceerp-api` — `srv-dbl9stegekts73foqer0` | `marketplaceerp-api-staging` — `srv-dbmnd6jrjlhs73c0jung` |
| Neon | Proyecto candidato `square-lake-18241058` / `marketplaceerp-test`; conexión efectiva de Render **NO atestada** | `tiny-sound-11266101` / `marketplaceerp-development`, rama `development` |
| Vercel | Proyecto `erp-log-stica`, alias público `erp-log-stica.vercel.app` | Preview deployments de la misma aplicación |
| Pagos | **Disabled** hasta integración, revisión y autorización independiente del proveedor bancario | **Disabled**; exclusivamente datos sintéticos |

El servicio Render de Desarrollo ya está conectado a `development`, con AutoDeploy **OFF**, y ejecuta el commit `90c1b9c504aabc5ee0be284373edc6504821be4d` LIVE. La base aislada tiene 39 tablas (Core/Catalog/Finance). No mezclar variables o cadenas de conexión entre estos entornos.

### Flujo de cambios

1. Desarrollar en `development`, ejecutar CI (.NET, frontend, E2E Chromium, comparador) y probar contra **Neon Desarrollo**. No hacer push directo a `main`.
2. Abrir PR `development` → `main`, revisar diferencias/migraciones y superar checks obligatorios y revisión humana según reglas de protección.
3. No interpretar un merge de Git como permiso para migrar base, activar pagos, publicar frontend ni modificar endpoints de Production. Son puertas separadas.
4. Crear backup fresco de la **base efectiva identificada** y conservar mecanismo de recuperación de secretos/configuración **fuera del repositorio**. Probar restauración y compatibilidad 37→39, escritores/colas, recuperación post-backup y plan de rollback.
5. Coordinar Render Production (servicio todavía sobre `integracion/main-reconciliacion-20261102`) y la promoción manual de Vercel. Verificar `/health`, `/health/ready`, operaciones y permisos por rol. Tras promoción, revalidar la política de alias.
6. Solo después de verificar despliegue y recuperabilidad, eliminar la rama histórica que ya no sea fuente de servicios; mantener respaldos fuera del repositorio público.

## GitHub: inventario de ramas y recuperación histórica

**`main@90c1b9c`** fusionó PR #60 y superó 4/4 checks post-merge. Se auditaron **93 referencias remotas**; **90 referencias históricas fueron eliminadas** mediante lotes verificados F330–F338 (archivo de 93 referencias en un `git bundle` previo). La rama `staging/catalog-500-neon-test` se retiró únicamente después de cambiar Render a `main`, y luego Render Desarrollo a `development`.

Ramas operativas al crear este documento:

- `main`: código consolidado y protegido.
- `development`: trabajo futuro, creada desde el commit exacto de `main`; requiere protección apropiada.
- `integracion/main-reconciliacion-20261102`: **no borrar**, aún la utiliza Render Production.
- `demo-web-logimkt`: **no borrar** antes de comprobar si sigue conectada a una demo Railway externa (no conectada/certificada en este inventario).

**Respaldo Git:** `D:\MarketplaceERP-Backup-F321\MarketplaceERP_ALL_REFS_BEFORE_MAIN_ONLY_20261114.bundle`, SHA-256 `61A5A696CBEEC02BD66B05FF474C3B05CEC1737A351E813A03EF5B7BA5D972F1`; copia también en C:. D: es otra unidad **local**, no un backup geográficamente independiente ni off-device. No subir el bundle completo al repositorio público: el historial puede contener secretos antiguos. Los cambios sueltos de worktrees fueron preservados con parches reversibles y XLSX separado en `D:\MarketplaceERP-Backup-F321\LocalWorktree-20261114`; las diferencias de código rastreado fueron revisadas en F337 y no se identificaron mejoras funcionales por trasladar.

**Riesgo residual del repositorio público:** no consta un escaneo exhaustivo del historial completo buscando secretos; tratar esto como **pendiente**, no como aprobado. Si un scanner autorizado identifica secretos, revocarlos y rotarlos en el proveedor antes de considerar cualquier reescritura de historial público. Verificar también permisos de GitHub Actions, CODEOWNERS, releases/tags y referencias de workflows antiguos.

### Workflow histórico

`.github/workflows/logimkt-grupo-a-regresion.yml` conserva triggers de una rama histórica (`integracion-auditoria-grupo-a`) que ya no existe. **Requiere confirmación de propietario** antes de desactivarlo o eliminarlo; hasta entonces es un artefacto legado. Evitar tratar su presencia como una ejecución activa de CI.

## Neon y almacenamiento: inventario y limpieza segura

Se encontraron **tres proyectos Neon** en la organización:

1. `square-lake-18241058` / `marketplaceerp-test`: siete ramas PostgreSQL; root `main` con **37 tablas** y seis ramas históricas con datos. **NO borrar/migrar** hasta demostrar destino efectivo de Render PROD, backup fresco, restauración independiente y dependencias de cada rama.
2. `tiny-sound-11266101` / `marketplaceerp-development`: una rama default `development` con **39 tablas**; conservar como Desarrollo.
3. `round-star-34135497` / `marketplaceerp-p5-restore-verificacion`: laboratorio de restore con dos bases de 37 tablas, candidato a retirada **solo tras** completar recuperación Productiva y autorización específica.

**Objetivo operativo:** 2 proyectos Neon (Production + Development) y 2 servicios Render; no confundir reducción de proyectos activos con eliminación de respaldos verificables. En la cuenta Render consultada no existen instancias PostgreSQL nativas; los dos servicios web actuales son suficientes. El proyecto único de Vercel puede servir Production + Preview sin necesidad de duplicación.

## Riesgos y seguimiento

| Riesgo/tarea | Registro responsable |
| --- | --- |
| Puertas completas de producción, secretos/configuración, rollback y release | [Issue #65](https://github.com/Marzza01/ERP-Log-stica-/issues/65), [#57](https://github.com/Marzza01/ERP-Log-stica-/issues/57), [#61](https://github.com/Marzza01/ERP-Log-stica-/issues/61) |
| Autenticación `localStorage`, CSRF, rate limiting, idempotencia de pagos reales | [#49](https://github.com/Marzza01/ERP-Log-stica-/issues/49), [#53](https://github.com/Marzza01/ERP-Log-stica-/issues/53) |
| Dependencias frontend: 6 avisos **moderados de desarrollo**, runtime 0 en auditoría F341 | [#51](https://github.com/Marzza01/ERP-Log-stica-/issues/51) |
| Escaneo del historial Git público, CODEOWNERS, workflow legado, backups off-device, demostración Railway | [#63](https://github.com/Marzza01/ERP-Log-stica-/issues/63), integrados en plan de [#65](https://github.com/Marzza01/ERP-Log-stica-/issues/65) |

No alterar pruebas aprobadas sin necesidad; las futuras auditorías deben identificar commit, hora, entorno, scope de conexión y resultado reproducible.

**Evidencia:** [Issue #65, F330–F340](https://github.com/Marzza01/ERP-Log-stica-/issues/65); [Issue #63](https://github.com/Marzza01/ERP-Log-stica-/issues/63); [Issue #51, F341](https://github.com/Marzza01/ERP-Log-stica-/issues/51).
