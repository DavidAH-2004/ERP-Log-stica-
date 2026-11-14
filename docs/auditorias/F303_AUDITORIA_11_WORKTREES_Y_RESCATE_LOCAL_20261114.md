# F303 — Revisión final de 11 worktrees locales para consolidar MarketplaceERP

Corte: 14 de noviembre de 2026. **Alcance:** solo las rutas versionadas o no rastreadas visibles con `git status --porcelain=v1 --untracked-files=normal`. No se leyó ni copió ningún secreto, no se alteró ningún archivo local, no se ejecutó merge ni despliegue.

## Inventario verificado

**11 worktrees registrados, 5 con archivos pendientes y 6 limpios**:

| Worktree | SHA local | Estado | Archivos con estado |
| --- | --- | --- | ---: |
| `MarketplaceERP` | `48d0f9c` | `staging/vercel-render-neon-test` | 13 |
| `MarketplaceERP-staging-500` | `6db2e7a` | `integracion/fase1-seguridad` | 2 |
| `MarketplaceERP-backend-integ` | `48e63d9` | `tmp/integracion-backend-dashboard` | 16 |
| `MarketplaceERP-Candidate-Work` | `d3e8e6d` | detached; limpio | 0 |
| `MarketplaceERP-frontend-integ` | `70a1445` | limpio | 0 |
| `MarketplaceERP-P0` | `e79ad46` | limpio | 0 |
| `MarketplaceERP-P1` | `f8b7c4e` | contiene `frontend/marketplace-ui/.gitignore` | 1 |
| `MarketplaceERP-P5-Backup` | `b3d8f1f` | contiene el mismo `.gitignore` | 1 |
| `MarketplaceERP-P5-Security` | `613e4fb` | limpio | 0 |
| `MarketplaceERP-vercel-patch` | `8c1934e` | limpio | 0 |
| `MarketplaceERP_main_check` | `45fcd8f` | limpio | 0 |

**Total 33 entradas, 29 rutas relativas únicas** (descontando repeticiones en varios worktrees).

### Resultado de contraste, por entrada

| Clasificación | Entradas | Evidencia y disposición |
|---|---:|---|
| **Byte-identical** contra la candidata | 7 | Seis archivos del principal (Topbar, KpiCard, KpiDetailModal, kpiDetails, index.css, KpisDashboardController) y `Producto.cs` de backend. No recuperar. |
| **Contenido idéntico normalizando BOM/CRLF** | 7 | CSV catálogo en tres worktrees; `ProductoCompradorResponse.cs`; `ValidarFase8GrupoA.ps1`; migración EAN y su Designer. Mantener candidata. |
| **Diferente** | 12 | 4 rutas del ERP principal (middleware, DashboardPage, tipos, tests proxy) + 8 del backend (productos.ts, tipos, ProductosController, CatalogServiceExtensions, CatalogDbContext, ModelSnapshot, CatalogLogisticsTests, AutorizacionU2Tests). Diferencias ya descritas por F220/F302: la candidata conserva controles de auth, WMS, readiness, tipos FIN09 y tests ampliados. No sobrescribir con la versión histórica. |
| **No existe ese archivo en la candidata** | 7 | Matriz XLSX (1), importador controlado histórico (4 rutas) y `frontend/marketplace-ui/.gitignore` duplicado en P1/P5 (2 entradas, una ruta única). Las decisiones se detallan abajo. |
| **Total** | **33** | **29 rutas únicas** |

### Seis rutas únicas sin homólogo directo en candidata

1. `docs/Matriz_Integracion_MarketplaceERP.xlsx`: está en el ERP principal; 33457 bytes y SHA256 `AC1C43EDB560D71D6F4996618436012F7C913FEEC99D3225563C372C933E0DC8`. Preservar como respaldo externo. El contenido de sus celdas **no pudo leerse ni certificarse**; no incluir automáticamente binarios no revisados.
2. `docs/Catalogo_500_Importacion_Controlada.md`: conservar como especificación histórica diferenciada, en su worktree y ZIP F220.
3. `src/MarketplaceERP.Catalog/Services/ControlledCatalogImportService.cs`: no sustituir servicio actual; hay diferencias de gestión de stock/WMS y rutas.
4. `src/MarketplaceERP.Catalog/Services/IControlledCatalogImportService.cs`: interfaz correspondiente; mantener con variante histórica.
5. `tests/MarketplaceERP.Catalog.Tests/ControlledCatalogImportTests.cs`: pruebas de esa variante; mantener históricas. La candidata prueba su propio importador con PostgreSQL y WMS.
6. `frontend/marketplace-ui/.gitignore`: las copias P1 y P5-Backup contienen exactamente `.vercel` y `.env*`. **Rescate selectivo completado:** el `.gitignore` raíz de la candidata ya ignoraba `.env*` y sus plantillas permitidas, pero no `.vercel`. Por ello se agregó **`.vercel/` al archivo raíz `.gitignore`** en STAGING con commit `f3496ccc458fbb49eb767e3a44872af3e3355036`, preservando el beneficio sin duplicar archivos anidados. No se borraron las dos copias históricas.

### Diferencias no idénticas y por qué no se trasplantan

- `middleware.js`: candidato añade `/api/health/ready`; el local no.
- `DashboardPage.tsx`: candidato protege respuestas asíncronas desfasadas, permite exportación KPI CSV y no presenta comisión registrada como cobro confirmado.
- `types/index.ts`: candidato contempla FIN09/splits y EAN nullable.
- `auth-proxy.test.cjs`: candidata verifica rutas/roles y reabastecimiento solo Admin.
- `productos.ts`: candidato evita GUID fijados y normaliza producto/stock del servidor.
- `ProductosController.cs`: el histórico protegería con Admin a nivel de clase, bloqueando Comprador/Vendedor; candidato valida pertenencia y roles.
- `CatalogServiceExtensions.cs`: registra en la versión vieja el servicio controlado omitido intencionalmente; no recuperar sin diseño de endpoint diferenciado.
- `CatalogDbContext.cs` y `CatalogDbContextModelSnapshot.cs`: candidato aporta restricciones y modelo más reciente; no reemplazar con snapshot histórico.
- `CatalogLogisticsTests.cs`: candidato evita sobrescribir CSV en pruebas y utiliza directorio temporal.
- `AutorizacionU2Tests.cs`: candidato incorpora rutas de comprador/vendedor y pruebas más amplias.

Las conclusiones anteriores se basan en el análisis semántico F220 y comparación local F302, **no** son autorización para borrar archivos. Específicamente, F220 clasifica **93 referencias remotas** (5/52/1/10/25), distinto de estos **11 worktrees locales**. El ZIP histórico de 23 archivos (20 rutas únicas) fue verificado y se conserva; sus 20 rutas no agotan las 29 rutas únicas del inventario local más reciente.

## Limitaciones y puerta D

- La reconciliación de **código** observado no identificó ninguna implementación más nueva que la candidata y que debamos incorporar por sustitución. La protección `.vercel/` sí era recuperable y se integró.
- La modalidad histórica estricta de 500 SKU **no está implementada** en la candidata; permanece como mejora diferida, preservada, no un requisito de ejecutar importación masiva para el MVP actual.
- La matriz XLSX permanece **sin revisión de celdas**; si se exige que `main` incluya todos los documentos, debe revisarse antes de publicar, sin asumir que su contenido es seguro o vigente.
- `git status` no incluye archivos ignorados; este inventario no constituye un escaneo de todo el disco.
- Quedan bloqueos no documentales: Vercel Deployment Checks aún no activado ni ensayado, secretos y configuración Render/Vercel sin respaldo recuperable probado, plan y simulacro de rollback Production y autorización explícita para merge/deploy/BD.
- No cerrar la puerta integral D, no fusionar PR #60 ni actualizar despliegues de Production como consecuencia de este documento. El checklist #65 permanece **115/123 (8 abiertos)**.

**Evidencia:** informes locales `_Codex_F220_Cierre_20261112/F220_BRANCH_MATRIX_93.csv`, `F220_BRANCH_DISPOSITION.md`, `F220_COMPARACION_23_RESULTADO_20261112.md`; informe versionado F302; inspección read-only de `git worktree list --porcelain`, `git status --porcelain=v1` y SHA-256/normalización contra `MarketplaceERP-Candidate-Work`. Ninguna rama o worktree fue borrada.
