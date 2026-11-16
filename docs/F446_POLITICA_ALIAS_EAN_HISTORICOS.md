# F446 — Diseño de alias y reserva irreversible de EAN históricos

**Alcance:** issue #70, requisitos transferidos de #71. Diseño y código F451/F470/F481/F476 implementados en development con pruebas PostgreSQL/Chromium CI; NO aplicados a Neon cloud, NI habilitados en Render/Production. Definición inicial: 15-nov-2026.

## Invariantes de negocio

- Un código EAN-13 escaneable debe referirse a **un único ProductoId para siempre**, incluidas etiquetas ya impresas y cualquier vendedor. El SKU puede cambiar sin transferir la propiedad de un EAN.
- El EAN vigente del producto y los EAN anteriores alias deben resolver el **mismo ProductoId**; la consulta debe validar el vendedor autenticado y no revelar un producto de otro vendedor.
- La operación de rotación será siempre explícita, auditada, idempotente y exclusiva de Admin autorizado, con justificación y posibilidad de segunda aprobación; prohibida por import CSV, PUT de producto y scripts de arranque.
- Los EAN externos que pertenecen al espacio GS1 requieren verificación contractual externa de titularidad; un checksum correcto no acredita propiedad. El prefijo 20 es para uso **interno**, no para comercialización externa.
- Los EAN históricos inválidos se conservan como dato maestro y evidencia de procedencia, pero **no** se emiten como etiquetas escaneables ni se convierten silenciosamente en aliases activos.
- El borrado lógico de Producto no libera EAN ni alias para otro producto. Las entradas de auditoría no se borran con el producto comercial.

## Esquema PostgreSQL 15 implementado en código (NO migrado a Neon)

`catalog.reservas_ean` — registro único de cada EAN-13 jamás emitido:
- `CodigoEAN varchar(13) PRIMARY KEY`, checksum EAN-13 validado a nivel aplicación y constraint de patrón SQL; no se confía exclusivamente en regex SQL para el dígito verificador.
- `ProductoId uuid NOT NULL` con FK restrictiva a `catalog.productos(Id)`, incluso si producto está inactivo.
- `Estado varchar(16) NOT NULL`: `Actual`, `Alias`, `Retirado`. `CreadoUtc timestamptz NOT NULL`, `UltimaTransicionUtc timestamptz`.
- El PK de `CodigoEAN` impide reutilizar un alias como código actual o alias de otro producto. Restricción parcial UNIQUE(`ProductoId`) WHERE `Estado='Actual'` permite solo un actual por producto y no limita aliases históricos.
- Trigger de control de asignación/rotación centralizada, conectado con la operación actual `CodigoBarrasAsignacionService`. Antes de activarlo, hay que actualizar el camino de alta y generador de EAN; nunca crear la reserva y olvidarse de mantenerla en sincronía.

`catalog.rotaciones_ean` — bitácora append-only de transiciones:
- `Id uuid PRIMARY KEY`, `SolicitudId uuid NOT NULL` (idempotencia), `ProductoId uuid NOT NULL`, `CodigoAnterior varchar(50) NOT NULL`, `CodigoNuevo varchar(13) NOT NULL`.
- `UNIQUE(SolicitudId, Version)` para registrar eventos consecutivos Solicitud/Approval/Execution sin sobrescribir estados, con idempotencia por versión. El último evento define el estado; no actualizar registros anteriores.
- `Motivo text NOT NULL` (limitado, sin credenciales), `SolicitadoPor uuid NOT NULL`, `AprobadoPor uuid NULL`, `SolicitadoUtc timestamptz NOT NULL`, `AprobadoUtc timestamptz NULL`, `EjecutadoUtc timestamptz NULL`, `Version integer NOT NULL`.
- `Estado varchar(24) NOT NULL`: `Solicitada`, `Aprobada`, `Ejecutada`, `Rechazada`, `Fallida`. El resultado de aplicación debe distinguir solicitud registrada de rotación realmente ejecutada; jamás imprimir código nuevo por tener solicitud pendiente.
- Restricción de coherencia entre estado/fechas; FK a producto sin cascada; índices (`ProductoId`, `SolicitadoUtc`) y (`CodigoAnterior`) para trazabilidad. Registro histórico de «EAN inválido legado» solo como texto, **nunca** como clave escaneable activa.
- Conservación mínima propuesta: **indefinida durante la vida del ERP** y respaldos, salvo una política legal de retención aprobada por responsable. No almacenar PII adicional en motivo.

## Migración por fases sin renumeración histórica

1. Preflight: respaldo independiente, totales y hash ordenado de (`ProductoId`, `SKU`, `CodigoEAN`, vendedor, stock), duplicados, códigos inválidos y esquema EF. Si hay dos productos con el mismo EAN válido, **abortar sin modificar**.
2. Expand: crear tablas, índices y restricciones aditivas, sin UPDATE a `catalog.productos` ni recomputar checksums. Backfill transaccional de EAN actuales **válidos y únicos**, los inválidos quedan solo registrados en reporte de preflight.
3. Sincronización: adaptar alta, asignación Admin y el trigger de reserva para registrar EAN válido atómicamente; validar con dos escritores concurrentes sobre PostgreSQL efímero. Fallar cerrado si la reserva no coincide con el producto.
4. Dual-read controlado: consultar `CodigoEAN` actual y aliases habilitados únicamente tras confirmar que no hay colisión cross-table; preservar filtro por `VendedorId`; registrar qué EAN se leyó sin exponerlo en logs HTTP.
5. Operación de rotación: transacción única que bloquea ProductoId y códigos en orden canónico, inserta el nuevo EAN en `reservas_ean` (conflicto = 409), cambia el anterior a Alias y actualiza `productos.CodigoEAN`, registra `rotaciones_ean`. `SolicitudId` y `Version` repetidos devuelven el mismo evento durable; la progresión autorizada agrega una versión nueva. Si cualquier paso falla, rollback completo.
6. Gate: feature flag por entorno apagada inicialmente. Habilitar solo después de pruebas de impresora, cámara/lector real, inventario, idempotencia, 3 roles, backup y rollback.

## Regla de reversión

- La migración aditiva solo admite Down sin pérdida **si nunca se ejecutó una rotación**. En presencia de un historial real, Down debe **fallar cerrado** y exigir recuperación planificada, no eliminar alias ni permitir reuso de códigos.
- Rollback de aplicación hacia versión que desconoce aliases no es seguro después de una rotación. Antes del primer uso, verificar compatibilidad de lectura o detener escrituras durante el cutover y mantener código nuevo.
- No compensar una rotación reasignando un EAN usado a otro SKU. Si se requiere volver a identificar un producto con un EAN anterior, se debe hacer por un flujo admin auditado que preserve la unicidad histórica.

## Pruebas obligatorias antes de implementar/activar

- PostgreSQL efímero: backfill EAN validos 49/49 actuales sin cambios, duplicados y EAN inválidos heredados; índices y trigger con carreras N escritores; snapshots antes/después de stock, reservas y productos; abort/rollback.
- Rotación `A→B→C` y reescaneo de `A`, `B`, `C` al mismo ProductoId. Reintento de la misma `SolicitudId`, uno de solicitud diferente y concurrencia simultánea.
- Rechazo 403 seller cruzado, 404 anónimo según regla de auth, 409 colisión, auditoría y retiro de producto. El mismo EAN **nunca** se resuelve a un ProductoId distinto.
- Impresora Avery 5160 a escala 100 %, pistola HID y cámara reales con etiquetas antiguas y nuevas; prueba sintética no sustituye este gate.
- Revisión de impacto fiscal/GS1, negocio y seguridad, aprobación explícita para migración Development y eventual Production bajo #65.

**Estado actualizado 16-nov-2026:** migraciones, triggers, funciones, API y UI ya implementados en development y pruebas CI. Pendientes: migración Neon DEV autorizada, aceptación física F398, política de retención firmada y release #65. PR #68 DRAFT y #70 abierto. No modificar EAN ni ejecutar DDL en Neon por esta definición.


## F457 — Consulta controlada de alias (en código, no habilitada)

- `ConsultaCodigoEan.BuscarAsync` permite lectura por `catalog.reservas_ean` con parámetro SQL seguro, `Actual` o `Alias`, hasta dos coincidencias, y aplica `VendedorId` en PostgreSQL antes de materializar datos.
- `GET /api/productos/por-codigo/{codigo}` conserva comportamiento anterior por defecto. La opción `Barcodes:EnableHistoricalAliasLookup` está **OFF** salvo activación explícita en entorno ya migrado y certificado. Si falta tabla, responde 503, sin fallback a resultados potencialmente inconsistentes.
- La respuesta incluye `CodigoLeido` y `EsAlias` para identificar el tipo de etiqueta sin cambiar `CodigoEAN` del producto. No registra lecturas WMS automáticamente.
- F457 prueba consulta de código vigente y alias sintético del mismo producto, filtrado del vendedor propietario y ausencia para otro vendedor **solo en PostgreSQL efímero CI**.
- **No habilitar el flag todavía:** F451/F470 solo han sido probadas en PostgreSQL efímero. Rotación transaccional y captura WMS están implementadas en código, pero falta migrar Neon con respaldo y certificar hardware, recuperación y autorización.

## F460 — Escaneo WMS de alias (bandera OFF)

- `SesionConteoService` reutiliza `ConsultaCodigoEan.ProductosPorCodigo` exclusivamente cuando `Barcodes:EnableHistoricalAliasLookup=true`; la ruta de conteos sigue restringida a Admin.
- El código físico original escaneado se guarda en `LecturaConteo.CodigoLeido` y conserva la clave de idempotencia `LecturaId`; el producto actual no cambia EAN.
- La prueba PostgreSQL desechable agrega un alias sintético a un producto con stock, registra una lectura de la etiqueta anterior y repite el mismo UUID; verifica una sola lectura, mismo ProductoId y EAN vigente intacto.
- **No activar en Neon DEV/Production** hasta completar la migración F451/F454, autorizaciones y pruebas con hardware real. La prueba sintética no demuestra rotación transaccional ni seguridad de lectores físicos.

## F487: política de EAN externo y prefijo interno

La nueva rotación admite exclusivamente EAN-13 de prefijo interno 20 con checksum válido. Un EAN externo GS1 debe rechazarse aunque su dígito verificador sea correcto: no se dispone de evidencia de titularidad contractual ni integración verificadora GS1. El uso del prefijo 20 queda restringido al inventario interno; no autoriza comercialización externa. Las pruebas F487 verifican rechazo de códigos externos e inválidos antes de consultar la base. Ninguna regla habilita reuso de códigos históricos ni reemplaza la certificación de titularidad por vendedor.
