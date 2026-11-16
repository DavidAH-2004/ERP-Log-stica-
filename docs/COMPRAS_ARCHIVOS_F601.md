# F601 · Compras Archivos (PDF duradero) — rama aislada

Estado: IMPLEMENTACIÓN EN REVISIÓN (no desplegada). Rama temporal
`feat/compras-archivos-pdf`, basada originalmente en `development@791b76c`.
No fusionar con `main` ni con la PR #68 sin el gate productivo #65.

## Contrato

- `POST /api/compras/po` genera el PDF durante el mismo cambio de `FinanceDbContext` que
  inserta la PO. Si el generador falla, no se alcanza `SaveChangesAsync`.
- `finance.archivos_compras` usa `OrdenCompraId UNIQUE` y FK a `finance.ordenes_compra`.
  El PDF es `bytea` en Neon; no usa rutas efímeras en Render.
- Un PDF **vigente** por PO. Cambiar estado al enviar, recibir o conciliar 3-Way actualiza
  el mismo registro y su versión. No se modifica inventario ni se habilitan pagos.
- Admin solamente, en backend y frontend. Listado paginado, búsquedas de PO/proveedor,
  rango UTC de fechas, previsualizar, descargar, imprimir desde visor PDF.
- `POST /api/compras/archivos/reconciliar?limite=50` es backfill acotado, idempotente
  para PO anteriores; se lanza desde la página con **Archivar compras anteriores**.
- El botón **Descargar PDF** en Compras utiliza el mismo endpoint del archivo.
- `PdfSha256` se verifica antes de servir el archivo. `Cache-Control: private, no-store`.

## Migraciones y seguridad

- EF Core F601 `20261116062428_F601ComprasArchivosDurable` en esquema Finance.
- `Down` falla cerrado: no se puede borrar automáticamente la evidencia PDF.
  Para recuperar, restaurar un `pg_dump` recuperable y verificado.
- Al desplegar DEV, migrar primero Neon Development **tras respaldo/ensayo** y solo después
  reiniciar la API. Nunca ejecutar sobre Neon Production sin autorización y gate #65.
- No subir PDF privados ni credenciales a GitHub, Vercel o registros.
- Package QuestPDF `2026.9.2`, .NET 8. Uso de licencia Community únicamente si
  LOGIMKT cumple las condiciones aplicables para educación/evaluación/comercial.
  Revisar licencia oficial: https://www.questpdf.com/license/community.html
- No había en el repositorio una imagen oficial del logotipo LOGIMKT;
  por ahora el PDF lleva un encabezado tipográfico **LOGIMKT**. El logotipo final
  está pendiente de disponer del archivo oficial de marca.

## Pruebas de aceptación

1. Crear PO con 1, 3 y 10 ítems válidos; comparar subtotal, IVA 13% y total.
2. Rechazar PO inválida y comprobar **0 PDFs** y **0 PO** persistidas.
3. Confirmar `bytea` con cabecera `%PDF`, SHA256 correcto, un solo registro por PO.
4. Enviar PO, recibir parcialmente/completamente y ejecutar 3-Way Match;
   verificar versión e integridad y no duplicados del archivo.
5. Comprobar roles Admin 200 / Vendedor y Comprador 403 / anónimo 401.
6. Backfill de PO históricas y reintentos concurrentes: no duplicados.
7. PDF carta, impresora normal, encabezado/paginación, firmas y nombres de 10 SKU.
8. Reiniciar Render Development y verificar que los PDF siguen disponibles desde Neon.
9. Ensayo de restauración del nuevo esquema junto a `finance.archivos_compras` y los PDF.

## Estado real

Implementados backend, entidad, migración EF e interfaz React. F611: pruebas de
PDF real con PO de 1, 3 y 10 ítems, PDF íntegro por SHA256 y regeneración sin
registros duplicados. F612: reconciliación histórica protegida ante escrituras
simultáneas mediante el índice UNIQUE de PostgreSQL y tratamiento de colisión.
F613: ensayo en PostgreSQL local efímero: el documento sobrevive a un DbContext
nuevo, la segunda inserción del mismo OrdenCompraId es rechazada y el Down de
la migración se bloquea cuando existen PDFs reales. La base de prueba se destruye.
Compilación .NET y Vite aprobadas. F615: 33/33 pruebas frontend de autenticación
aprobadas, incluidas respuestas PDF binarias sin alteraciones por el proxy normal
y por el BFF HttpOnly (JWT añadido solo en el servidor; cookies externas suprimidas).
Todavía NO están validadas las pruebas end-to-end en Render/Neon Development ni todas
las transiciones de PO. NO publicar en Production antes de esas pruebas.
