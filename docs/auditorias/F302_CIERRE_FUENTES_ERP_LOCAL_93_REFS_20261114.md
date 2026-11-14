# F302 — Reconciliación de fuentes locales e históricas para integrar MarketplaceERP a main

Fecha de corte: 14-nov-2026. Fuente de aplicación candidata: `staging/catalog-500-neon-test` con el código de ERP probado en `d3e8e6d` y cambios posteriores de **solo workflow de GitHub**. Registro de evidencia local: `C:\Users\Marcelo\Documents\Logistica (ERP)\_Codex_F220_Cierre_20261112`. Este documento **no autoriza merge ni cambio de producción**.

## A. Alcance e inventario

- Inventario F220 `F220_BRANCH_MATRIX_93.csv`: **93 filas verificadas** en la laptop, columnas `branch,sha,inMain,inStaging,inCandidate,aheadCandidate,behindCandidate,filesVsCandidate,category,paths`. Categorías documentadas: 5 ancestros de `main`, 52 de STAGING, 1 de candidata, 10 sin patch propio pendiente y 25 con revisión semántica. La auditoría F220 tiene disposiciones para las 25 (23 de patch-id único y 2 equivalentes). No significa que 93 nombres de ramas deban fusionarse uno por uno.
- Respaldo de 23 archivos `ERP_Revision_23_Archivos_20261108_143610.zip`, SHA-256 `579B927FBC343617E4CE12C14D450C7B8AE41D01984D7C3681C83593E8A59881`: 23/23 huellas coincidentes, 20 rutas únicas. Informes F220 de comparación en el paquete local `_Codex_F220_Cierre_20261112`; se conservaron en ZIP las implementaciones no trasladadas.
- Worktree local principal `MarketplaceERP@48d0f9c` (`staging/vercel-render-neon-test`): **12 archivos versionados modificados sin commit + 1 libro Excel no versionado**. No sobrescribirlos ni asumir que son idénticos al HEAD remoto.
- Worktree candidato `MarketplaceERP-Candidate-Work@d3e8e6d` estaba limpio, **pero quedó por detrás de HEAD remoto** de STAGING tras F299/F301, que modificaron workflows. No hacer `git reset --hard` sobre worktrees de trabajo.

## B. Dictamen de las 13 rutas locales

| Origen local | Comparación contra candidato | Disposición |
|---|---|---|
| `docs/data/Catalogo_500_Productos.csv` | 501 líneas ambas versiones; **contenido idéntico** tras normalizar BOM y CR/LF, diferencias de tamaño 189700/189199 bytes | **Equivalente**, conservar la versión candidata y la copia local |
| `frontend/marketplace-ui/src/components/layout/Topbar.tsx` | SHA-256 igual | Ya representado |
| `frontend/marketplace-ui/src/components/ui/KpiCard.tsx` | SHA-256 igual | Ya representado |
| `frontend/marketplace-ui/src/components/ui/KpiDetailModal.tsx` | SHA-256 igual | Ya representado |
| `frontend/marketplace-ui/src/constants/kpiDetails.ts` | SHA-256 igual | Ya representado |
| `frontend/marketplace-ui/src/index.css` | SHA-256 igual | Ya representado |
| `src/MarketplaceERP.API/Controllers/KpisDashboardController.cs` | SHA-256 igual | Ya representado |
| `frontend/marketplace-ui/middleware.js` | Candidato mejora el proxy para `/api/health/ready` | Conservar candidato; no sustituir por versión local |
| `frontend/marketplace-ui/src/pages/dashboard/DashboardPage.tsx` | Candidato añade control `useRef` para respuestas asíncronas obsoletas, exportación KPI CSV, evita decir «100% cobrable» sin evidencia y solo muestra estadísticas con datos | Conservar candidato; no retroceder |
| `frontend/marketplace-ui/src/types/index.ts` | Candidato añade tipos de liquidaciones, compatibilidad FIN09 y EAN nullable | Conservar candidato |
| `frontend/marketplace-ui/tests/auth-proxy.test.cjs` | Candidato añade cobertura de rutas Admin/Vendedor/Comprador y reabastecimiento Admin-only | Conservar test candidato; autorización real debe certificarse en backend |
| `scripts/ValidarFase8GrupoA.ps1` | Diferencia inicial BOM/codificación en primera línea `param(` | Equivalente funcional; conservar candidato |
| `docs/Matriz_Integracion_MarketplaceERP.xlsx` | Libro **solo local, no presente en candidata**, 33457 bytes, SHA-256 `AC1C43EDB560D71D6F4996618436012F7C913FEEC99D3225563C372C933E0DC8` | **Preservar como evidencia local externa**. No se pudo leer/verificar contenido de las celdas ni justificar su publicación automática en GitHub. Revisión de contenido pendiente si se desea que forme parte del repositorio |

**Dictamen de código:** ninguna de las diferencias locales de código **observadas** exige sustituir la implementación más reciente de STAGING; seis rutas tienen bytes iguales, el CSV coincide normalizado y las restantes diferencias de implementación/tests favorecen al candidato. Esto no convierte el Excel en «revisado» ni garantiza que no existan archivos fuera de las rutas inventariadas.

## C. Importación controlada histórica de 500 productos

Los archivos históricos `ControlledCatalogImportService.cs`, `IControlledCatalogImportService.cs`, `ControlledCatalogImportTests.cs` y documentación `Catalogo_500_Importacion_Controlada.md` no están en la candidata. La funcionalidad histórica limita el proceso a 500 SKU, vendedor activo, seis categorías y `StockInicial=0`, sin crear movimientos WMS.

**Candidato actual inspeccionado:** `src/MarketplaceERP.Catalog/Services/CatalogImportService.cs` y `ICatalogImportService.cs`: importación CSV con 16 columnas exactas, máximo de 10 000 filas/8 MB, validación previa de SKU/valores, operación transaccional con bloqueo advisory PostgreSQL, `SaveChanges` al final y pruebas `CatalogImportAtomicPostgresTests` y `CatalogImportPreservesWmsTests`. Gestiona stock inicial y ubicación WMS en el alta, con protecciones para no alterar stock/ubicación existente en una actualización. Es un flujo diferente; **NO trasplantar el código histórico directamente**, ya que omite funciones logísticas vigentes y podría introducir rutas en conflicto.

**Disposición de integración:** mantener el importador candidato para esta liberación; **conservar intacta en el respaldo F220 la variante histórica para una futura mejora explícita** de importación masiva estricta 500-SKU. No declarar esa variante como feature integrada, ni eliminar su código original, ni ejecutarla en Neon Production. Añadirla al core exige diseño de endpoint diferenciado, verificaciones de roles, idempotencia, 500-SKU estrictos y pruebas WMS con PostgreSQL aislado. Es una mejora **diferida**, no dependencia indispensable del código que hoy opera en STAGING, salvo que el alcance comercial requiera esa modalidad exacta.

## D. Seguridad y verificaciones antes de merge

- CI para HEAD de F301 `28a2c6e`: GitHub Actions [run #43634807499](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43634807499) **4/4 SUCCESS**; pruebas de aplicación `769/769` ya certificadas antes de cambios solo en workflows. Preview Vercel nuevo READY.
- F294 restauración aislada de dump de Production **37/37 tablas y 546/546 filas**; F300 migración del dump actual **37→39** en PostgreSQL local PASS, huellas de 35 tablas de negocio idénticas. **No** equivale a rollback real de Neon con escritores.
- F299/F301: workflow de `ERP-Production-Release-Gate` preparado en STAGING y fijado por SHA; **regla de Deployment Checks NO activada en Vercel**, evento `repository_dispatch` no se ejecutará hasta existir en rama default `main`. El readiness antiguo no certifica esquema.
- **No-GO pendiente:** recuperar/probar configuración/secretos de Render/Vercel, protección obligatoria del alias Vercel antes de merge, simulacro de rollback coordinado API + DB + frontend, reconciliación del último dump productivo, autorización independiente de merge/migración/despliegue.
- Checkout comercial real seguirá deshabilitado en Production hasta contar con proveedor y conciliación; el alcance funcional QA del Bloque C fue **`Payments:Provider=Disabled`**, no cobranza real.

## E. Puerta de salida D

La reconciliación **del código local listado** y la clasificación histórica quedan documentalmente verificadas para este corte. NO cerrar la puerta integral de D ni mover PR #60 de Draft por este dictamen: **los controles de promoción y rollback siguen abiertos**. La reconciliación de la matriz Excel queda cubierta como **preservación de evidencia no publicada**, no como revisión de contenido. Al ejecutar la fusión, verificar nuevamente HEAD, CI, condición de Deployment Checks y compatibilidad de backend/BD antes de cualquier autoasignación de dominio.
