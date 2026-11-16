# F436 — Preparación segura de una pasarela bancaria en sandbox

**Estado:** documento de evaluación para issue #69. No selecciona proveedor, no crea cuentas, no configura claves, no ejecuta cobros ni autoriza Production. Los requisitos bancarios del issue #69 siguen **7/7 pendientes** hasta pruebas y aceptación independiente. Fecha: 15-nov-2026.

## Candidatos comerciales a evaluar en El Salvador

| Candidato | Evidencia pública | Por confirmar con el comercio |
| --- | --- | --- |
| Wompi El Salvador | API REST, enlaces/botón de pago y documentación de webhooks; sitio oficial indica sandbox de pruebas | Contrato/afiliación, quién asume PCI y 3DS, comisión y calendario de liquidación, reembolsos, validación de firma webhook, límites del sandbox, disponibilidad legal |
| BAC Credomatic El Salvador | Oferta local «E-Commerce» para cobros web/app, además de Compra Click y servicios de ventas digitales | API/programa exacto para El Salvador, credenciales de sandbox, condiciones de afiliación, reembolsos/contracargos, comisiones, reglas de conciliación y disponibilidad |

**Fuentes oficiales consultadas, sin inferir acuerdos comerciales:**
- Wompi API: https://docs.wompi.sv/ (enlaces de pago, transacciones y webhooks); https://wompi.sv/NuestrosServicios/API y https://wompi.sv/nuestrosServicios
- BAC El Salvador: https://www.baccredomatic.com/es-sv/nuestra-empresa/medios-de-pago y https://www.baccredomatic.com/es-sv/pymes/ventas-digitales

No reutilizar ejemplos/credenciales de otros países; los documentos generales de BAC pueden no aplicar a El Salvador. Solicitar documentación contractual oficial del proveedor elegido antes de programar un adaptador específico.

## Contrato de backend propuesto (no implementado)

1. Mantener el checkout y los cobros reales en modo **Disabled** hasta autorización independiente. Jamás habilitar Mock en Production; el arranque ya rechaza proveedores distintos de Disabled/Mock DEV.
2. Elegir preferentemente flujo alojado/redirect del proveedor si el contrato lo permite, **sin capturar PAN/CVV ni datos de tarjeta** en la aplicación ERP, evitando ampliar innecesariamente el alcance PCI.
3. Registrar en `finance.intentos_pago` un identificador de negocio, clave idempotente y monto/moneda antes de enviar solicitud al proveedor; conservar estado Preparado/Enviado/Incierto para reintentos seguros.
4. Un webhook no debe adjudicar stock, facturar o liquidar basado solo en JSON recibido: verificar autenticidad de firma según documentación exacta del proveedor, marca de tiempo/replay, orden/monto/moneda y consultar al proveedor por servidor en situaciones inciertas.
5. La recepción repetida de webhooks debe ser idempotente por evento y referencia de transacción, con restricción única durable. Nunca registrar dos cobros, dos asientos o dobles reservas por reintentos/replay.
6. Rechazos confirmados liberan stock solo después de persistir el resultado; timeout/5xx/caída de red **no prueban rechazo**, por lo que no se reintenta un cobro con otra clave ni se libera automáticamente inventario mientras el resultado sea incierto.
7. Reembolsos/contracargos deben ejecutarse en flujo separado con permisos Admin, justificación, evidencia, conciliación y protección contra doble reembolso.
8. Credenciales sandbox y Production, secretos HMAC/webhook y scopes se administrarán fuera de GitHub y fuera de logs, cada entorno separado. No publicar en una PR archivos `.env`, claves o ejemplos reales.

## Pruebas mínimas exigidas antes de marcar #69

- Proveedor y términos aprobados comercialmente, incluyendo compatibilidad con El Salvador y liquidaciones en USD.
- Sandbox oficial con credenciales segregadas y sin tarjetas o fondos reales; capturar resultados en datos sintéticos.
- Un único checkout de prueba y su webhook **firmado auténticamente**; rechazos, timeouts, reenvíos, eventos duplicados/fuera de orden, conciliación después de reinicio, autorización Admin y comprador; rechazo de webhook falsificado.
- Escenarios B2B/B2C multi-SKU 2–20 y fallo parcial/compensación de reserva; reconciliación y reporte contable de reembolsos con autorización.
- Inspección de cumplimiento/privacidad del proveedor y prueba de deshabilitación, más backup y rollback del esquema y variables productivas.
- Aprobación formal de activación comercial **posterior** al release seguro de #65.

## Verificaciones automáticas disponibles

`ConfiguracionPasarelaI2B1Tests` protege el contrato fail-closed: `Payments:Provider=Disabled` registra exclusivamente `PagoServiceDisabled`, no envía cobros; `Mock` solo arranca en Development con flag explícita; proveedor bancario todavía no implementado impide el arranque. F436 añade escenarios de valores de proveedor no implementados y de `EnableMock` activo con modo Disabled.

**No convertir estos tests de configuración en afirmación de pagos reales implementados.** #69 continúa abierto, sin cobros, sin proveedor seleccionado y sin webhooks externos.

## F461 — Investigación oficial Wompi SV y verificador aislado (15-nov-2026)

**No hay proveedor contratado ni adaptador habilitado; cero llamadas bancarias.** La documentación oficial de **Wompi El Salvador** establece que el header `wompi_hash` representa un HMAC-SHA256 calculado sobre los **bytes exactos** del body con el API Secret del aplicativo. F461 agrega `WompiSvWebhookSignature.Verificar` como función pura, comparación en tiempo constante y rechazo de firmas/cuerpos inválidos; no se registró en DI ni se añadió un endpoint.

Documentación verificada (jurisdicción correcta):
- https://docs.wompi.sv/webhook/validar-webhook
- https://docs.wompi.sv/metodos-api/transaccion_prueba
- https://docs.wompi.sv/webhook/definicion-webhook
- https://docs.wompi.sv/redirect-url/parametros-de-url-de-redirect
- https://wompi.sv/NuestrosServicios/API

Controles que **siguen sin implementarse**: recepción autenticada de webhook, unicidad durable de eventos e idempotencia del reembolso, comprobación de cuenta/app/orden/monto/moneda y resultado final consultado al proveedor. `EsProductiva=false` distingue simulaciones documentadas pero **no sustituye verificar firma**, ni una firma válida prueba por sí sola un cargo confirmado. No usar parámetros de redirección del navegador como confirmación sin validarlos. Evitar cualquier manejo de PAN/CVV en MarketplaceERP; preferir flujo alojado tras contrato y revisión PCI.

**Advertencia sobre BAC:** la guía general https://ayuda.baccredomatic.com/para_empresas_o_negocios/comercios_afiliados/e-commerce?country=es señala expresamente que ese artículo no aplica a sus clientes de El Salvador. Por ello no asumir que sus requisitos ni APIs particulares están disponibles para el país sin información contractual.

**Nunca confundir** dominios, llaves ni SDKs de Wompi Colombia (`wompi.co`) con Wompi El Salvador (`wompi.sv`). Solicitar acceso de desarrollo al proveedor seleccionado y confirmar parámetros oficiales antes de integrar; `Payments:Provider=Disabled` permanece obligatorio.
