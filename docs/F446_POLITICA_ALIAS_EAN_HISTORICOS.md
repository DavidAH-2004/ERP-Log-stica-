# F446 — Diseño de alias y reserva irreversible de EAN históricos

**Alcance:** issue #70, requisitos transferidos de #71. Diseño técnico cerrado; **funcionalidad no implementada**, sin migración aplicada, sin rotación ni normalización de etiquetas actuales. Fecha de definición: 15-nov-2026.

## Invariantes de negocio

- Un código EAN-13 escaneable debe referirse a **un único ProductoId para siempre**, incluidas etiquetas ya impresas y cualquier vendedor. El SKU puede cambiar sin transferir la propiedad de un EAN.
- El EAN vigente del producto y los EAN anteriores alias deben resolver el **mismo ProductoId**; la consulta debe validar el vendedor autenticado y no revelar un producto de otro vendedor.
- La operación de rotación será siempre explícita, auditada, idempotente y exclusiva de Admin autorizado, con justificación y posibilidad de segunda aprobación; prohibida por import CSV, PUT de producto y scripts de arranque.
- Los EAN externos que pertenecen al espacio GS1 requieren verificación contractual externa de titularidad; un checksum correcto no acredita propiedad. El prefijo 20 es para uso **interno**, no para comercialización externa.
- Los EAN históricos inválidos se conservan como dato maestro y evidencia de procedencia, pero **no** se emiten como etiquetas escaneables ni se convierten silenciosamente en aliases activos.
- El borrado lógico de Producto no libera EAN ni alias para otro producto. Las entradas de auditoría no se borran con el producto comercial.

## Propuesta de esquema PostgreSQL 15 (a migrar solo tras pruebas)

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

**Estado de aceptación:** definido el diseño de campos/cardinalidad/retención/unicidad y no reutilización; pendiente implementar tabla, migración, triggers, API/UX, pruebas físicas y aceptar política. La PR #68 continúa DRAFT, #70 abierto. No modificar EAN ni ejecutar DDL por esta definición.
