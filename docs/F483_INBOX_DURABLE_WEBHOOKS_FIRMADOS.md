# F483 — Inbox durable de webhooks firmados (pagos reales aplazados)

- Se agrega la migración Finance `20261116060000_F483InboxWebhooksPagosDiferidos`: tabla `finance.webhooks_pago_inbox`, clave primaria `(Proveedor, CuentaId, AplicativoId, TransaccionId)`, hash SHA-256 del cuerpo original y estado fijo `RequiereConsulta`.
- `WompiSvWebhookInbox` comprueba HMAC-SHA256 y sobre de proveedor previo a insertar. Webhook de prueba o inválido: cero escrituras. Repetido idéntico: no-op. Misma identidad bancaria con cuerpo firmado distinto: `ConflictoBloqueado`, sin conciliación automática. Eventos son append-only; Down falla si existen registros.
- No almacena cuerpo original ni datos de tarjeta, CVV o secreto. No tiene endpoint, DI, cron ni instrucciones de cobro. Un evento en este inbox **NO** acredita dinero recibido.
- Prueba PostgreSQL 15 efímera valida inserción, replay, conflicto, entorno sandbox, firma incorrecta e inmutabilidad; también verifica migraciones de los tres DbContext.
- **Gate #69 sigue abierto:** falta proveedor contratado y sandbox, verificación de estado por API bancaria, correlación autorizada con orden y moneda, recuperación/reembolsos durables, monitoreo, conciliación y aprobación separada. `Payments:Provider=Disabled` obligatorio.
