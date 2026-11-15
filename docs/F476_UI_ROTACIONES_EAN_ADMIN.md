# F476 — UI Admin de rotaciones EAN, código sin activar

- Ruta /productos/rotaciones visible solo a Admin en catálogo y protegida por RoleRouteGuard. El backend exige [Authorize(Roles=Admin)] y un usuario auténtico; cliente no puede suplir identidad del aprobador.
- Admin A envía una solicitud idempotente con UUID, nuevo EAN interno prefijo 20 y motivo. Admin B usa otra cuenta para aprobar; PostgreSQL comprueba identidad distinta y ejecuta transición A→B con histórico append-only.
- Historial GET filtrado por UUID de producto devuelve máximo 100 eventos (sin correo, motivo u otras PII). La interfaz no imprime ni renumera códigos antiguos.
- Tanto frontend como API permanecen INACTIVOS si no se han autorizado las banderas EnableHistoricalAliasLookup + EnableEanRotation. En Render Production F472 impide incluso arrancar una rotación por configuración prematura.
- Pendiente: prueba con lectores e impresoras físicos, certificación migración/restore en Neon DEV, política de retención, seguridad GS1, autorización y release #65. No modifica pagos (#69) ni Production.
