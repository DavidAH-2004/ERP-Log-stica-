# F400 — Puerta de aceptación del sistema de códigos de barras (MarketplaceERP)

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
| Substituir o regenerar un EAN existente | **No integrado**; se conserva código histórico | Requiere historia de alias, auditoría persistente e invalidación/reconciliación de etiquetas |
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
