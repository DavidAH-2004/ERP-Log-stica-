# Compras: una PO con hasta 10 productos

Base: `development@66c932d`. Rama de trabajo: `feat/compras-multiples-items`.

## Comportamiento

- Proveedor, contado/crédito con días editables y fecha estimada en el mismo modal.
- Agregar a la lista solo modifica el borrador local. La lista admite edición y eliminación, rechaza SKU duplicados y permite entre 1 y 10 productos.
- El total suma todas las líneas, muestra subtotal e IVA 13% y aplica el mismo redondeo al par que el backend.
- Crear Orden envía una única solicitud con encabezado y todos los detalles. Un ítem pendiente debe guardarse o cancelarse. Durante el envío se bloquean cambios y envíos repetidos.
- La tabla de órdenes muestra la cantidad de productos. La recepción conserva su procesamiento por línea.
- El registro de factura captura cantidades y precios del documento por producto; no supone que sean iguales a los de la PO. Envía el detalle requerido por el 3-Way Match existente.

## Corrección del error 500

El formulario envía `fechaEntregaEsperada` como `yyyy-MM-dd`. ASP.NET lo deserializa como `DateTimeKind.Unspecified`; Npgsql rechaza ese valor en `timestamp with time zone`. Se normaliza la fecha civil a medianoche UTC antes de persistir. Se aplica el mismo tratamiento a emisión/vencimiento de factura.

La prueba PostgreSQL reproduce el rechazo del valor sin zona con un parámetro `TimestampTz` y verifica después el recorrido de creación, recepción, factura y conciliación con la corrección. Esto reproduce una causa de 500 en el código; no se consultaron logs del servicio desplegado.

## Datos y compatibilidad

`OrdenCompra.Items`, `ItemOrdenCompra` y `finance.items_orden_compra` ya implementan la relación uno a muchos. No hace falta otra tabla ni una migración para esta mejora. Se mantiene el guardado atómico del encabezado y detalles mediante un único `SaveChangesAsync`.

El endpoint añade el límite de 10 líneas y valida que la suma de cantidades no desborde las operaciones de recepción/conciliación. Conserva controles de proveedor activo, catálogo, duplicados y precisión monetaria. Las órdenes históricas no se alteran.

## Validación

- Build TypeScript/Vite.
- 74 pruebas seleccionadas de Compras, facturas y conciliación aprobadas sobre PostgreSQL 18.1 local, incluyendo concurrencia y migraciones relacionadas.
- Después de añadir la reproducción explícita de Npgsql: 10 pruebas de `ComprasSelectorFase1Tests` y `P0ComprasPostgresTests` aprobadas.
- `tests/compras-multi-items.mjs`: Chromium con API simulada, edición/eliminación, duplicados, límite, cantidades inválidas, IVA con medio centavo, envío único, recuperación tras error, detalle de factura y ancho móvil. Integrado en CI.

Ejecución del navegador desde `frontend/marketplace-ui`, después de instalar las dependencias y Playwright:

```sh
npm run build
node tests/compras-multi-items.mjs
```

Para los tests .NET usar SDK 8.0.426 y `TEST_PG_CONNECTION` apuntando exclusivamente al PostgreSQL local desechable requerido por las pruebas; necesitan permiso para crear/eliminar sus propias bases de QA.

## Integración

Destino del PR: `development`. Tras revisión e integración, verificar Vercel Desarrollo y actualizar manualmente Render Desarrollo para que aplique la corrección UTC y las validaciones del API. La validación local no certifica el despliegue remoto. No se promueve a producción como parte de este cambio.
