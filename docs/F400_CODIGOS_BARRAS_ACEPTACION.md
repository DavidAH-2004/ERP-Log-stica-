# F400 — Puerta de aceptación del sistema de códigos de barras (MarketplaceERP)

**Actualización F493/F494 (16-nov-2026):** recepción **solo de código en `development`** en PR #68 [review 5914115693](https://github.com/Marzza01/ERP-Log-stica-/pull/68) `COMMENTED`, sin aprobar merge. CI head `58f8daa`: [Actions 43786038161](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43786038161) 4/4 PASS (PG15 .NET, frontend, PG18 Chromium, Windows). Issue #70 registra F400 de código/CI revisado; **la aceptación física y publicación en main siguen abiertas**. Los hitos históricos abajo describen la evolución y pueden contener pendientes que después se implementaron; usar el estado actual de #70 para la decisión de release.


**Fecha:** 2026-11-15  
**Fuente fijada:** `alxmariotejada-byte/erp-sistema-de-codigo-de-barras-@523a82ddca72450e51640d8491dfd3ae0ef1b1a4`  
**Destino aprobado para desarrollo:** `Marzza01/ERP-Log-stica-:development`  
**Fuera de alcance de esta aceptación:** `main`, Render/Neon/Vercel Production, cobros reales, eliminación de ramas históricas.

## 1. Matriz de recepción técnica

| Componente | Estado y evidencia | Restricción |
|---|---|---|
| Validación de EAN-13 interno | Integrado; tests C# y JS | Prefijo de circulación interna `20` no equivale a GS1 comercial |
| Asignar EAN a producto sin código | API solo Admin; PostgreSQL `nextval`; CI F402 PASS | No toca EAN no vacío, ni siquiera histórico inválido |
| Buscar por código y consultar etiquetas | Admin/Vendedor y alcance por vendedor; F401 PASS | Comprador y anónimo denegados |
| Hojas Avery 5160, 30 por carta | F405 PASS (Chromium: 3×10 y PDF de una página) | **Falta impresión en papel a escala 100 %** |
| Generar SVG y decodificar ópticamente | F406 CI de JsBarcode→ZXing | **No equivale a uso con cámara o lector físico** |
| Escáner por cámara | UI integrada y permisos de HTTPS | **Falta cámara real autorizada, distintos navegadores** |
| Pistola USB/Bluetooth | Campo de entrada interpreta 13 dígitos + Enter | **Falta lector físico y comprobación de terminador/idioma** |
| Conteos de inventario por escáner | F399 E2E PostgreSQL PASS (Admin y dos solicitudes concurrentes) | Vendedor denegado hasta separación completa por almacén |
| Idempotencia y hallazgos | F404 PASS; rechazo de UUID reutilizado con cantidad alterada, un hallazgo por lectura | Sin mutación de stock directa por escaneo |
| Rotar EAN existente (operación opt-in, no normalización masiva) | **F451/F470/F481 integrados en código `development`**, Admin solicita, otro Admin aprueba, solicitante cancela pendiente, alias histórico inmutable y lectura WMS de etiquetas anteriores; CI PostgreSQL 15 + navegador PG18 | **OFF por defecto** y F451/F470 aún no migradas a Neon DEV; hardware, backup/restore y aprobación productiva pendientes |
| Normalización masiva del catálogo del fork | **Rechazada por riesgo de pérdida de identificadores** | Nunca ejecutar su `UPDATE` o limpieza de EAN en arranque |

## 2. Evidencia automatizada

- [CI #43689207510](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43689207510): 4/4, prueba F399 de conteo con lectura concurrente, F401 acceso, F402 EAN.
- [CI #43690403236](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43690403236): 4/4, F404 sin hallazgos duplicados.
- [CI #43690717252](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43690717252): 4/4, F405 30 etiquetas virtuales en una página carta, F399/F390.
- [CI #43691966469](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43691966469): **4/4 SUCCESS**, F406 tres EAN-13 JsBarcode→ZXing y pruebas de sesión/stock concurrentes.
- El rollback de migraciones F397 se ensayó en PostgreSQL desechable con EAN históricos preservados. **F408:** las migraciones Up aditivas se ensayaron después en una copia de Neon Desarrollo y luego se aplicaron a la rama principal de Neon Desarrollo (no Producción), con huella EAN idéntica antes/después.

## F408/F409 — Despliegue seguro de Desarrollo (15-nov-2026)

Se creó una rama de recuperación Neon sin compute `br-delicate-sea-bbcw0ene` desde el LSN `0/2E7CC80`, con vencimiento 29-nov-2026; también una rama temporal para ensayo `br-polished-darkness-bbp358o9`, con vencimiento 18-nov. El snapshot manual anterior se conservó porque el proyecto rechazó uno nuevo por cuota agotada. Ambas ramas temporales provienen del mismo origen y se verificó por SELECT la huella de productos/EAN en la rama de ensayo.

Se generó el SQL de **exactamente las dos migraciones EF** (sin DROP, DELETE FROM ni UPDATE de productos), se aplicó primero en la rama de ensayo y se comparó la estructura con la rama Development. Tras aprobar, se aplicó transaccionalmente en Neon **Development** por el conector nativo. La verificación obtuvo **Core4 / Catalog9 / Finance10**, 18 tablas Catalog, 500 productos, 451 sin EAN y 49 EAN de 13 dígitos; la huella EAN permaneció `de0d29ab45a87632b06d362424b51141`. Sesiones, lecturas y hallazgos nuevos: cero filas. No se tocó Neon Production.

El despliegue manual de Render Desarrollo `dep-dbsmoobbc2fs73c0h8j0` alcanzó estado **LIVE** en `development@cb843e8`; Vercel Preview protegida `dpl_5A1A1dwTKpwf9CcGvTztw8cxNgNn` **READY** con el mismo commit. Se probaron GET `/health`, `/health/ready` (**200**) y GET anónimos a sesión, consulta de etiquetas, sesiones de conteo y búsqueda EAN (**401**) en ambas rutas. Los logs Render posteriores no registraron errores en la ventana consultada; readiness tardó alrededor de 12 segundos en una muestra, insuficiente para medir SLO.

**Esta es evidencia de migración y rutas protegidas, no de E2E autenticado con tres roles en la Preview ni de impresora, cámara o lector físico.** La decisión de publicación sigue siendo NO-GO a Producción.

## 3. Prueba física indispensable (F398)

Registrar **fecha, equipo y evidencia observable** de cada escenario; detener si se modifican códigos o stock inesperadamente.

1. **Impresora:** imprimir una hoja de prueba compatible Avery 5160 en papel carta al **100 %**, sin «Ajustar a la página». Verificar primera, central y última celda, límites, quiet zones y lectura del código en las 30 posiciones.
2. **Pistola USB/Bluetooth:** escanear EAN válido con sufijo Enter, y sin sufijo; confirmar que el operador puede introducir cantidad y seleccionar ubicación; verificar que el escaneo nunca duplica el conteo ante reintento de red.
3. **Cámara real:** HTTPS y consentimiento de cámara, permiso denegado, cámara ocupada, cambio de orientación, EAN inválido y EAN válido. No habilitar cámara remotamente sin consentimiento explícito.
4. **Roles:** Admin puede imprimir y contar; Vendedor solo consulta sus productos/etiquetas; Comprador no ve conteos ni impresión interna.
5. **Datos históricos:** cotejar EAN antes/después de la prueba y comprobar que stock total/reservado no cambia por un escaneo.
6. **Incidencias:** registrar fallos en #70 con pasos y versión, corregir en `development`, repetir CI.

No marcar F398 como completo porque un dispositivo aparece enumerado en Windows: eso no demuestra impresión, lectura ni accesibilidad real.

## 4. Condiciones para F400 y la promoción

- [x] F406 y último CI con cambios funcionales: 4/4 PASS, [run #43691966469](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43691966469).
- [ ] F398 verificado con dispositivos físicos o aceptación explícita **documentada del alcance sin hardware**; nunca llamar «hardware probado» a una simulación.
- [ ] Aceptación expresa de que **cambio/regeneración** de EAN permanecen excluidos hasta diseñar auditoría, alias y compatibilidad de etiquetas históricas, o implementación segura por separado con pruebas.
- [ ] Revisar PR #68 por módulos y permisos; no tratar 76 archivos/92 commits como exclusivamente códigos de barras.
- [ ] Para `main`: cumplir issue #65 (respaldos de la base productiva, rollback coordinado, variables reales seguras), obtener autorización productiva independiente y *después* considerar promoción.
- [x] Mantener pagos reales del issue #69 en modo Disabled; no se han ejecutado cobros.

**Decisión de liberación actual: NO-GO a Producción.** El resultado automatizado respalda `development`; la prueba física y recuperación productiva siguen abiertas.


## F412–F414 — Aislamiento y listado de etiquetas desplegado (15-nov-2026)

- [CI #43700941751](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43700941751): **4/4 SUCCESS**, F412 comprobó que seleccionar un ID ajeno no devuelve etiquetas al Vendedor, mientras Admin sí ve la etiqueta; Comprador recibe 403 y UUID inválido 400, todo sobre PostgreSQL desechable.
- [CI #43701322502](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43701322502): **4/4 SUCCESS**, F413 confirmó que un Vendedor propietario puede seleccionar su propia etiqueta y resolver su EAN por la API, mientras los productos del otro propietario quedan ocultos.
- [CI #43701999752](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43701999752): **4/4 SUCCESS**, F414 corrigió `ConsultaEtiquetas.ObtenerAsync`. El predicado EF `CodigoEAN != ""` incluía NULL por compensación de nulos. Se cambió a `CodigoEAN != null && CodigoEAN != ""`; la selección explícita por `ids` conserva la opción de informar que un producto no tiene código.
- [Render DEV deploy `dep-dbso87u1egvs73edi34g`](https://dashboard.render.com/web/srv-dbmnd6jrjlhs73c0jung/deploys/dep-dbso87u1egvs73edi34g) **LIVE** en `development@7dbd0d2`; última Preview Vercel `dpl_AEQznaqX2VXZsgv22LrzCwMnYWuT` READY en el mismo SHA. Health/ready GET 200 y endpoint de etiquetas sin sesión GET 401.
- El ensayo positivo **real** `F412_BARCODE_LIVE_POSITIVE_READONLY.ps1` corrió con la cuenta Vendedor QA en la Preview protegida y Render DEV; 0 escrituras. La API anterior devolvía **488 productos publicados, solo 47 con EAN válido**. Después de F414 devolvió **47 registros y los 47 con EAN válido**, todos publicables como etiquetas. Búsqueda por EAN de producto propio en Preview y Render devolvió 200 con **ID, SKU y EAN idénticos**, y código válido inexistente devolvió 404.
- Los EAN históricos en Neon DEV siguen inalterados: 500 productos, 451 sin EAN, 49 EAN-13 válidos (47 publicados), 0 EAN duplicados y huella original conservada. No se modificó Neon PROD/DEV mediante las pruebas de lectura, no se generaron pagos, y Render PROD/`main` permanecen intactos.

**Pendientes de aceptación final:** Admin real en Preview, validación con cámara/lector USB-Bluetooth/impresora físicos y aceptación explícita del comportamiento de cambio/regeneración de EAN existentes. No declarar F398/F400 ni el gate #65 completos por pruebas sintéticas o verificaciones solo de Vendedor.


## F415–F416 — Importaciones CSV no pueden renumerar EAN (15-nov-2026)

**Hallazgo y corrección.** El importador del catálogo `CatalogImportService` sobrescribía `CodigoEAN` durante el UPDATE de un SKU existente, mientras la edición normal del producto ya lo protegía. Esto podía invalidar etiquetas impresas al cargar nuevamente el mismo CSV. Se eliminó esa asignación, conservando el código existente aunque el CSV proponga otro EAN, lo deje vacío o contenga un identificador heredado no estándar. Se escribe una advertencia de auditoría con el **SKU**, sin volcar el código, si el archivo intenta cambiarlo. Los datos maestros (p. ej. precio y stock mínimo) pueden seguir actualizándose y la importación no mueve inventario WMS.

**Evidencia reproducible:**

- [Commit F415 `06874f1`](https://github.com/Marzza01/ERP-Log-stica-/commit/06874f175c48074c9cf9395b1e543da166395623) y [tests F415 `79cd5fe`](https://github.com/Marzza01/ERP-Log-stica-/commit/79cd5feb86f36f97ef18d7b27bcb0cc87cf1a482): 4/4 regresiones locales .NET 8 (EAN original válido + CSV vacío; EAN original válido + diferente; EAN heredado no estándar + diferente; SKU vacío + EAN entrante).
- [F416 `04d9894`](https://github.com/Marzza01/ERP-Log-stica-/commit/04d989496b8956674058803b0a06dd9677fefd34), [CI #43736241426](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43736241426): **4/4 jobs SUCCESS**; test con PostgreSQL 15 desechable y dos reimportaciones (código distinto y vacío), manteniendo el EAN original y el stock WMS. El job .NET obtuvo **171 pruebas Catalog, 715 Finance y 10 Core sin fallos**, con `TEST_PG_CONNECTION` explícita al Postgres efímero.
- [Render DEV deploy `dep-dbsu0r8ae00c73938td0`](https://dashboard.render.com/web/srv-dbmnd6jrjlhs73c0jung/deploys/dep-dbsu0r8ae00c73938td0): **LIVE** sobre `development@04d9894`, AutoDeploy OFF. Vercel Preview protegida `dpl_6uHf2D5NkS8gC3DNvdJkWWje3osR`: READY en el mismo SHA.
- Reejecutado `F412_BARCODE_LIVE_POSITIVE_READONLY.ps1` con cuenta Vendedor DPAPI sobre la Preview más reciente y Render DEV: **47/47 etiquetas con EAN**, búsqueda por EAN propio **200 mismo ID/SKU/EAN**, EAN válido inexistente **404**, ninguna escritura de negocio. GET `/health` y `/health/ready` **200**.
- Neon DEV posterior (SELECT): **500 productos, 451 sin EAN, 49 EAN de 13 dígitos, huella `de0d29ab45a87632b06d362424b51141`**, cero sesiones y cero lecturas de conteo. No se ejecutó la importación de prueba contra Neon.

**Observación de calidad del CSV versionado:** el archivo de origen `src/MarketplaceERP.Catalog/SeedData/Catalogo_500_Productos.csv` contiene 500 valores de la columna EAN de 13 caracteres, pero solo **49** pasan el dígito verificador y **451** son no conformes. La base Neon DEV ya tiene esos 451 productos sin EAN. F415 evita que reimportar el CSV vuelva a escribir códigos inválidos sobre los SKUs **existentes**, pero crear un catálogo desde cero requiere una decisión de tratamiento seguro de esos 451 valores fuente. No se modificó el archivo versionado ni se ejecutó normalización masiva.

**Regla vigente:** los EAN existentes son inmutables desde las rutas de reimportación y de edición general. Asignar internos solo a productos sin EAN mediante el servicio Admin dedicado. **Modificar/regenerar identificadores ya emitidos continúa bloqueado hasta decidir alias de etiquetas antiguas y auditoría histórica.** F398 (hardware), aceptación F400 (Admin real/criterio de regeneración) y gate #65 hacia `main` continúan abiertos.

## F417–F419 — Altas CSV, alta individual y unicidad global de EAN (15-nov-2026)

**F417** [commit `0bd770d`](https://github.com/Marzza01/ERP-Log-stica-/commit/0bd770d8b90c355c685cc2f93248cd03ec25d066) evita incorporar EAN de 13 dígitos con checksum inválido al crear un SKU nuevo desde CSV; los códigos no conformes quedan `NULL` con aviso por SKU y asignación interna Admin posterior. El generador de datos demo ya no inventa códigos `741...`, y el archivo histórico original de 500 productos NO se modificó.

**F418** [commit final `08a312c`](https://github.com/Marzza01/ERP-Log-stica-/commit/08a312c8d580ed6811cdb28529ea3f91cb983523) valida `POST /api/productos` de Admin/Vendedor: devuelve 400 para EAN inválidos o de prefijo interno `20` introducidos manualmente, convierte texto vacío/espacios en `NULL` y mantiene intactos los datos WMS. [CI #43742704954](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43742704954) y [PR CI #43742710074](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43742710074): 4/4 PASS, 900/900 .NET. Render DEV `dep-dbsv13l6e07s73avm2u0` LIVE y Preview protegida Vercel READY; comprobación read-only: health/ready 200, etiquetas anónimas 401.

**F419** [commit `1576f9f`](https://github.com/Marzza01/ERP-Log-stica-/commit/1576f9ffa8bfa628c0e9aa3601acc89c75d1a644) evita que dos SKUs compartan un EAN no vacío: API rechaza alta duplicada con HTTP 409; importación CSV aborta el lote entero ante colisión de código (interna o con producto preexistente), sin guardar stock/productos parciales; tampoco acepta asignar el prefijo interno `20` por CSV. Migración **aditiva y no destructiva** `20261115183000_EanNoVacioUnico`: consulta y bloquea si detecta EAN históricos duplicados; si no existen, crea índice único parcial sobre EAN no vacíos; rollback solo retira ese índice, sin renumerar ni modificar productos. El índice es protección adicional frente a carreras entre instancias, independientemente del preflight API.

**Evidencia:** [CI #43744339990](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43744339990) y [PR CI #43744343886](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43744343886): **4/4 SUCCESS**, .NET **904/904** (Core 10, Catalog 177, Finance 717), frontend Node24, Chromium E2E y comparador Windows; la migración bloquea duplicados históricos en PostgreSQL 15 descartable, permite reconciliar SOLO en fixture QA, crea índice, rechaza colisión posterior y revierte el DDL conservando identificadores. Neon DEV por SELECT: **500 productos, 49 EAN no vacíos, 49 distintos, 0 códigos duplicados**, historial Catalog hasta `20261115075500`; no se ejecutó migración F419 sobre Neon ni se alteró su catálogo.

**Puerta F419 a Neon Development PENDIENTE:** copia recuperable independiente/plan de rollback, ensayo y revisión de SQL `Up` y `Down` sobre copia controlada, autorización explícita para aplicar al destino Neon Desarrollo, después versión Render DEV exactamente alineada. No aplicar la migración directamente a Production ni usar `CREATE UNIQUE INDEX` sin preflight de duplicados históricos. El despliegue vigente en DEV sigue en F418 hasta aprobar release de F419. **F398 físico, Admin real en Preview, aceptación de política de rotación/alias F400 y release #65 a main continúan NO-GO.**

## F420 — conflictos CSV se devuelven como 409 sin cambios parciales (15-nov-2026)

En F419 el importador PostgreSQL ya abortaba el lote completo al detectar un EAN duplicado, pero `ImportarCsv` no capturaba el error de negocio y podía devolver HTTP 500. **F420** introduce una excepción específica que no divulga el EAN en el texto, preserva el rollback y convierte el conflicto en **HTTP 409** desde `POST /api/productos/importar-csv` y el seeding exclusivo de Development. La protección de carreras por el índice único F419 continúa siendo requisito previo para garantizar unicidad concurrente cloud; no se ha aplicado aún a Neon.

**Evidencia CI F420:** [push #43745248110](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43745248110) y [PR #43745253908](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43745253908), ambos **4/4 SUCCESS**, .NET **905/905** (Core 10, Catalog 177, Finance 718), frontend Node 24, Chromium E2E y Windows. La prueba F420 confirma HTTP 409 sin escrituras; las F419 PostgreSQL esperan la excepción tipada y retienen rollback atómico. Las puertas F398/F400 y recuperación de #65 siguen abiertas.


## F421 — Migración F419 aplicada y verificada en Neon Desarrollo (15-nov-2026)

**Alcance autorizado:** exclusivamente proyecto Neon `marketplaceerp-development` (`tiny-sound-11266101`), rama `development` (`br-aged-morning-bbordmzw`), base `marketplaceerp_test`; Render DEV `srv-dbmnd6jrjlhs73c0jung` rama `development`; Vercel Preview. **NO** se tocó `main`, Render/Neon Production, pagos reales ni ramas GitHub históricas.

**Respaldo y ensayo:**
- Snapshot nuevo rechazado por Neon: `HTTP 422 snapshots limit exceeded`; **no se borró** ningún snapshot existente para liberar cuota.
- En sustitución, rama-clon de respaldo **prístina** `f421-pre-f419-unique-ean-copy-20261115`, Neon `br-winter-recipe-bbj4988e`, copiada antes de migrar, expira automáticamente **2026-11-29 23:00 UTC**. Conteo y huella verificados: 500 productos, 49 EAN distintos, `de0d29ab45a87632b06d362424b51141`.
- Rama separada de rehearsal `f421-rehearsal-unique-ean-20261115` (`br-raspy-thunder-bbhn9gfr`) con expiración **2026-11-17 23:00 UTC**: ejecutado preflight de duplicados + `CREATE UNIQUE INDEX "UX_productos_CodigoEAN_no_vacio"` sobre catálogo clonado; PostgreSQL confirma `indisunique=true`, `indisvalid=true`, `indisready=true`, preservación de 500 registros/49 EAN y huella original. Los tests CI F419 sobre PostgreSQL 15 descartable ya habían probado el fallo cerrado ante duplicados históricos, colisiones y rollback de DDL.

**Aplicación real solo en Neon DEV:** tras confirmar 0 EAN duplicados, migración prerrequisito F399 presente, F419 ausente y 0 conexiones `idle in transaction`, se aplicó una **sola transacción PostgreSQL**: (1) validar prerequisitos + duplicados, (2) crear el índice parcial único `UX_productos_CodigoEAN_no_vacio`, (3) registrar `20261115183000_EanNoVacioUnico` / `ProductVersion=8.0.8` en `catalog."__EFMigrationsHistory"`. Se devolvió `f419_aplicaciones=1` y `indice_creado=true`. Verificación post-migración: índice `valid/ready/unique=true`, **500 productos, 49 EAN no vacíos y distintos**, misma huella `de0d29ab45a87632b06d362424b51141`, 3 registros de `stock_fisico`, 0 sesiones/lecturas. **Sin UPDATE/DELETE masivo, renumeración ni importación CSV cloud.** El respaldo previo permanece independiente.

**Despliegue y smoke de Desarrollo:**
- GitHub `development@038b73b` [CI push #43745837069](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43745837069) y [PR CI #43745841470](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43745841470), **4/4 SUCCESS** ambos; commit funcional F420 `a640fd8` también 4/4 SUCCESS y 905/905 .NET. El cambio más reciente F421 es solo documentación.
- Render DEV [`dep-dbsvlspsrm7s739q3dsg`](https://dashboard.render.com/web/srv-dbmnd6jrjlhs73c0jung/deploys/dep-dbsvlspsrm7s739q3dsg), **LIVE** en SHA exacto `038b73b` (autoDeploy OFF). Vercel Preview protegida `dpl_e8xcEQtKiHcaE1G2BAneYwKTej1D`, READY mismo SHA.
- Desde equipo autorizado: GET `/health` **200**, `/health/ready` **200**, `/api/productos/etiquetas` anónimo **401**.
- Test positivo F412 read-only reutilizado con Preview nueva y credencial DPAPI **Vendedor QA** sin exportar ni mostrar secreto: **47/47** etiquetas publicadas válidas, mismo identificador/SKU/EAN del producto encontrado en Preview y Render, código válido desconocido **404**, `BUSINESS_WRITES=0`, limpiados archivos temporales. La ruta de inicio de sesión QA no equivale a aceptar el rol Admin.

**Pendiente no simulado:** F398 con cámara/lector/impresora reales; Admin positivo en Preview (el cofre QA local inspeccionado contiene Vendedor y Comprador, **no Admin**); decisión explícita de política de cambio/regeneración y alias de EAN históricos F400; gates transversales #65/recovery antes de PR #68 hacia `main`. Sin cerrar artificialmente #70 (4/7) ni #65 (116/123). Las dos ramas temporales Neon tienen caducidad configurada, sin borrados manuales en esta sesión.

## F422 — video sintético EAN-13 a través de React, sin cámara física (15-nov-2026)

- [Commit `9df40a1`](https://github.com/Marzza01/ERP-Log-stica-/commit/9df40a19da67b63630708e51afad4211e267ed49) incorpora una ruta E2E adicional en `frontend/marketplace-ui/tests/f411-scanner-camera-ui.mjs`. El navegador recibe un `MediaStream` de `canvas.captureStream`, con barras EAN-13 construidas a partir de las tablas L/G/R y de paridad, y utiliza el lector real `@zxing/browser` del componente de cámara. El formulario muestra **«¿Cuántas unidades hay?»** después de reconocer el código y comprueba **cero POST de inventario antes de confirmar cantidad**. La aplicación de prueba intercepta las rutas API en `127.0.0.1`; no abre cámara física ni llama a Render/Neon.
- Paso `F411/F422 scanner keyboard and synthetic camera decode (local Chromium)`: **`completed/success`** en [CI push #43749862314](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43749862314) y [PR CI #43749866346](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43749866346). Esta certificación se refiere al **paso concreto**, no presupone el estado final de los cuatro jobs hasta su cierre.
- **Diagnóstico físico, solo lectura:** laptop `DESKTOP-VA9BGV3` con cámara `HP TrueVision HD Camera` presente/OK; sin periférico de escaneo de códigos identificado por nombre PnP; `Spooler` Windows `Stopped`, enumeración `Win32_Printer` `CimException` y cero impresoras PnP listadas. No se activó cámara, no se encendió Spooler y no se imprimió.

### Protocolo de aceptación F398 pendiente, listo para prueba real

1. **Impresora física:** utilizar únicamente papel carta Avery 5160/compatible (30 posiciones) en entorno QA, comprobar escala 100 % y márgenes, imprimir una hoja de prueba sin datos sensibles, medir alineación y escanear al menos una etiqueta impresa. Registrar impresora, tamaño de papel, resultado y evidencia. Si Spooler está detenido, solicitar autorización para iniciarlo; no cambiar configuración del usuario silenciosamente.
2. **Cámara real:** en HTTPS de Vercel Preview con cuenta QA autorizada, habilitar la cámara de forma consciente, escanear una etiqueta EAN-13 real y verificar que el mismo SKU/código aparece en la interfaz; comprobar también «Permiso de cámara denegado». No apuntar la cámara a documentos privados, personas ni pantallas con contraseñas.
3. **Lector USB/Bluetooth físico:** conectar un lector tipo teclado, verificar 13 dígitos + Enter, leer un EAN impreso válido y uno no conforme, comprobar rechazo del segundo. Para conteos, usar **solo entorno Development** y cantidades de QA bajo rol Admin; verificar que la lectura no registra inventario sin confirmar unidades y que un retry no duplica conteos.
4. **Aceptación funcional de Admin:** iniciar sesión **realmente** en Preview con cuenta Admin QA autorizada y demostrar consulta de etiquetas, sesión/lectura/cierre de conteos y aislamiento de Comprador/Vendedor. No almacenar passwords/tokens ni volcar cabeceras de autorización en tickets; documentar únicamente códigos HTTP y resultados de negocio.
5. **Criterio de EAN:** se mantiene por defecto la inmutabilidad de identificadores históricos y se **excluye** la operación destructiva de normalización masiva del fork. Antes de permitir rotación/alias se requiere diseño independiente con auditoría, compatibilidad de etiquetas impresas y aprobación de negocio. La aceptación expresa de esa política queda pendiente.

**No confundir:** las pruebas ópticas de software, PDF virtual, cámara virtual y 47/47 etiquetas de Vendedor **no completan las pruebas físicas ni la aceptación Admin**. Dejar F398/F400 sin marcar y PR #68 DRAFT hasta contar con resultados reales y gate #65 para `main`.

## F423 — Revisión delimitada del alcance de PR #68 (15-nov-2026)

**Esto es inventario técnico de la diferencia de PR y revisión preliminar por permisos/migraciones, no un APPROVE ni aceptación productiva.** Base `main@8325cce`, origen `development@4ed07f8`; [PR #68](https://github.com/Marzza01/ERP-Log-stica-/pull/68) **DRAFT**, no fusionada. GitHub REST `/pulls/68/files` y `/pulls/68/reviews`: **91 archivos** (60 nuevos / 31 modificados), **120 commits**. Única revisión de PR previa: `COMMENTED`, no `APPROVED`. `mergeable_state=clean` no acredita aceptación ni producción segura.

| Subárea del diff | Archivos | Comprobación de alcance / riesgo |
|---|---:|---|
| Autenticación BFF y frontend | 6 | `bffCookieMode.js`, `middleware.js`, `AuthContext`, API cliente, login. **El prototipo de cookie solo se activa** en Preview mediante dos flags explícitos; no habilitarlo automáticamente en Production |
| Catálogo, etiquetas y cámara frontend | 7 | Lectura EAN/etiquetas y cámara; no modifica inventario. Avery/Cámara F405/F406/F411/F422 PASS solo en simulación |
| UI de conteos y escaneo | 7 | Flujo Admin con confirmación de cantidad, lectura con UUID; comprador/vendedor no deben acceder al inventario global |
| Catálogo backend, índices y sesiones | 16 | **3 migraciones de Catalog** F402/F399/F419. La última se aplicó a Neon Development con respaldo/huella de 500 productos preservada; **no aplicadas a Production** |
| Core/autenticación | 6 | **2 migraciones de Core** para versión de sesión JWT y limitación distribuida de login. Flags de activación requieren gates y recuperación real |
| API de seguridad, observabilidad y auth | 5 | `AuthController`, `ApiSecurity`, `Program`, cabeceras y auditoría: **alcance fuera de solo códigos de barras**, revisar releases reversibles |
| API de catálogo y conteos | 3 | `ProductosCodigoBarrasController` restringe Admin/Vendedor y filtra por vendedor; `SesionesConteoController` es **Admin-only**. El PUT general conserva `CodigoEAN` y CSV no renumera SKU existentes |
| Tests .NET/Node/Chromium | 32 | CI F422 real **4/4 success** en [push #43749862314](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43749862314) y [PR #43749866346](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43749866346): 905/905 .NET; cámara sintética, impresión virtual y PostgreSQL local. CI documental de `4ed07f8` **4/4 success** en [push #43750404405](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43750404405) y [PR #43750410892](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43750410892) |
| Documentación, CI y monitores | 6 | No reemplazan recuperación efectiva de secretos/roles y rollback de Production |
| Dependencias y enrutador de App | 3 | `package.json/lock`, `App.tsx`: superficie adicional, requiere smoke tras deploy |

**Controles confirmados con fuentes/CI:** `Get etiquetas` anon 401, Comprador 403, Vendedor dueño 200, Admin 200 **en CI local PostgreSQL**; Vendedor real en Vercel Preview/Render Desarrollo: 47/47 etiquetas, EAN propio 200 e inexistente 404; matriz real Comprador/Vendedor: 8 casos × 2 backends PASS. `F381_REAL_API_BFF_PASS` ejercita tres roles y guardas CSRF/cookie con API real y DB desechable, sin activar cookies en Preview real. Rutas `POST /{id}/codigo-barras/asignar` solo Admin; sesiones de conteo admin-only. Sin contraseñas, datos personales ni escrituras Productivas en esta revisión.

**Gates precisos que siguen pendientes:**
- **F398:** impresión Avery papel a escala 100 %, cámara real autorizada y lector USB/Bluetooth físico. Windows reportó `Spooler=Stopped` y sin impresora/lector enumerados; no se alteró configuración.
- **F400:** inicio de sesión positivo de **Admin QA real en Vercel Preview** y aceptación formal de conservación de EAN históricos; las credenciales Admin no se hallaron en el cofre DPAPI QA consultado, y el script local F290 solicita contraseña interactiva. Los tests Admin contra PostgreSQL CI **no reemplazan** ese login real.
- **PR/release:** este diff **no es solo barcode**; incluye autenticación, BFF, throttling, migrations Core y Catalog. Requiere revisión/aprobación independiente del conjunto, plan de secuencia de migraciones/flags/rollback y los **7 pendientes de #65**. Ninguna aprobación de merge ni autorización de Render/Neon/Vercel Production queda implícita; `main` se mantiene protegido, PR sigue DRAFT.

**Conclusión F423:** alcance de archivos y riesgos de permisos/migración identificados, con pruebas automatizadas y Desarrollo verdes; **no se declara F398, F400 ni promoción completa**.

## F425 — aceptación real Admin en Preview protegida (15-nov-2026)

**Evidencia de ejecución proporcionada por el operador desde su propio equipo Windows**, con el script `F425_ADMIN_PREVIEW_VISUAL_RUN.ps1`, usando contraseña Admin ingresada **oculta y localmente**. La cuenta utilizada corresponde a STAGING/QA y esta ejecución **no revela credenciales, JWT ni datos privados**.

Resultado observado en la salida original:

| Comprobación | Evidencia | Resultado |
|---|---|---|
| Autenticación Admin | `F288_LOGIN_ROLE=Admin_UI=PASS` | **PASS** |
| Rutas Admin | `/dashboard`, `/ordenes`, `/compras`, `/contabilidad` | **PASS**, visibles |
| Productos / etiquetas | `/productos`, `/productos/etiquetas?todos=1` | **PASS**, visibles |
| UI de escáner de conteos | `/conteos/escaner` | **PASS**, visible |
| Acceso API autenticado de códigos y sesiones | `F425_ADMIN_BARCODE_AND_COUNTS_PREVIEW_GET=200_200` | **PASS**, dos GET 200 |
| Restricción de roles de UI | `F290_ADMIN_CHECKOUT_COMPRADOR_BLOQUEADO=PASS` | **PASS** |
| Ausencia de operaciones de negocio | `F290_ADMIN_QA_WRITES=0` | **PASS**, sin escrituras |
| Resultado final | `F290_ADMIN_GATED_VISUAL=PASS` | **PASS** |
| Limpieza de metadatos | `F288_CLI_METADATA_CLEANED=OK` | **PASS** |

**Conclusión precisa:** la aceptación de **autenticación y visualización Admin real en Vercel Preview** está **COMPLETA** y no necesita repetirse. La evidencia confirma consultas de solo lectura; **no significa** que se haya iniciado, registrado o cerrado una sesión de conteo con cambios reales, ni que el lector USB, la impresora Avery o la cámara físicos hayan sido probados. Tampoco equivale a aprobar todos los módulos de PR #68 para Producción.

**Criterios aún abiertos:** F398 con hardware real, aceptación expresa de la política de inmutabilidad/rotación/alias de EAN (o exclusión acordada), revisión formal y puertas del release maestro #65 antes de `main`. PR #68 continúa en DRAFT y no se autoriza merge ni migración productiva a partir de este PASS. Mantener #70 **4/7** hasta completar sus condiciones integrales.

La Preview donde se ejecutó F425 fue el despliegue protegido `dpl_9VVqscPZRXHbxTPP4Dyp8ASBB8QH` (commit de aplicación `636c8d1`); los commits siguientes hasta `377b18b` solo modificaron el script de QA, no el código de la web ni API. La ejecución fue realizada por el operador, no por CI ni por la automatización.

## F427 — cierre preventivo de ediciones EAN por PUT (15-nov-2026)

**Control de seguridad añadido a `development`:** `PUT /api/productos/{id}` acepta el dato `codigoEAN` únicamente como comprobación de compatibilidad, **sin cambiar el EAN**. Si el cliente envía un código distinto o intenta vaciar el existente, responde **HTTP 409** y no modifica datos maestros. Se distingue de forma explícita entre **campo omitido**, `codigoEAN: null`, cadena vacía y EAN diferente; el `null` explícito sobre EAN existente también devuelve 409. Omitir `codigoEAN` o enviar exactamente el valor histórico existente conserva el comportamiento anterior de edición. Esta decisión no cambia el endpoint Admin dedicado para asignar EAN únicamente a productos sin código.

La medida evita que una aplicación cliente interprete erróneamente un PUT 200 como aprobación de rotación cuando el backend estaba ignorando el campo en silencio. Se añadieron pruebas unitarias con EF InMemory para Admin/Vendedor, con y sin `codigoEAN` y solicitando reemplazo o borrado. El EAN histórico debe permanecer vinculado al producto original. La migración de aliases y auditoría de rotación está separada en [issue #71](https://github.com/Marzza01/ERP-Log-stica-/issues/71); **no es funcionalidad implementada aquí**.

**Puerta F400 pendiente de aceptación expresa:** el equipo debe aceptar formalmente que la capacidad de *cambiar/regenerar* EAN históricos del fork se entrega aparte bajo #71. No declarar probado hardware ni autorizar `main`/Production por este cambio.

## F428–F429 — escaneo sin verificación y resultados asíncronos obsoletos (15-nov-2026)

**Se corrigieron dos fallos reales de la UI del escáner** únicamente en `development`:

1. **F428 (fail closed):** la pantalla de conteo creaba un formulario de cantidad en cuanto se escaneaba un EAN-13 válido, antes de recibir una respuesta positiva de `GET /api/productos/por-codigo/{ean}`. Los fallos de transporte, HTTP 403/429/5xx no descartaban la lectura pendiente, por lo que podían ofrecer un botón de guardar para un producto no verificado. Ahora el botón, las opciones de ubicación y el campo de cantidad permanecen deshabilitados hasta tener un producto confirmado por la API; ante error se descarta la entrada sin POST de inventario, con un mensaje al operador.
2. **F429 (respuesta obsoleta):** si se descartaba una lectura pendiente y se repetía el mismo EAN, un error tardío del intento anterior podía borrar el nuevo formulario. Cada intento tiene una identificación `lecturaId` y una referencia activa de lookup, y solo la respuesta asociada al intento actual puede confirmar o descartar su formulario. No se modificó la idempotencia del POST de conteo ni se alteró el backend.

**Evidencia técnica:**

- [Commit F428 `01d27ab`](https://github.com/Marzza01/ERP-Log-stica-/commit/01d27ab9c4ab39583ddab3b0b4739503fff6f2df) y [commit F429 `f86ecc8`](https://github.com/Marzza01/ERP-Log-stica-/commit/f86ecc813b1dae7471788124ba803eea884a42ad).
- [CI push #43760928437](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43760928437) y [PR CI #43760932560](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43760932560): **4/4 SUCCESS** en ambos. .NET: **912/912** (Core 10, Catalog 177, Finance 725). Frontend/Chromium/Windows PASS.
- Paso real de Chromium `F411_SCANNER_CAMERA_UI_PASS`: `f428LookupFailClosed:true`, `f428UnverifiedCannotSave:true`, `f429StaleLookupCannotClearNewScan:true`, `offlineRetrySameUuid:true`, `remoteApiCalls:0`. Se simuló HTTP 503, desconexión de red y respuestas fuera de orden; no se realizaron POST de inventario antes de cantidad confirmada.
- Vercel Preview `dpl_3Z8qNefdkyWo1JSgEG11wPpSCHzq` reporta **READY** para `development@f86ecc8`. Render Desarrollo permanece en `ce5bc3c`, la última versión con cambios backend, y no necesita redeploy por estos cambios exclusivos de React/tests.

**Límites de certificación:** F428/F429 mejoran la confiabilidad funcional, pero **F398 permanece abierto** porque el lector, la cámara y la impresora físicos no fueron probados. F400 aún exige aceptación del alcance sin rotación histórica (issue #71) y revisión/aceptación final de la PR #68; #65 sigue gobernando la integración productiva.
