# F486 · Evidencia de desalineación productiva (lectura únicamente)

**Tomada en el ciclo de revisión del 15–16 noviembre de 2026.** Es una fotografía parcial de proveedores conectados y puede volverse obsoleta; no certifica secretos, BD efectiva ni recuperación.

| Recurso | Referencia actual observada | Interpretación |
|---|---|---|
| GitHub main | `8325ccec0bee1eaf30511f4c190804fcb073c769`, protegida | Fuente candidata de producción |
| Render Production | servicio `marketplaceerp-api`, `integracion/main-reconciliacion-20261102`, live `77e9a16ea2b9d7d7cdd3c83c65bf9bb1b4e81d1d`, autoDeploy ON | **NO coincide** con main |
| Render DEV | `marketplaceerp-api-staging`, rama `development`, autoDeploy OFF | No tocar en promoción |
| Vercel alias público | `erp-log-stica.vercel.app` → `dpl_4C15u8Ai87uGNuuVyfMyfsZSgvmv`, `main@e04c90696e59d86390216a8b51bf00e876cfebc5`, deployment READY | **NO coincide** con main actual |
| Protección ramas GitHub | `main.protected=true`, `development.protected=false` | Protección global sí existe; detalles de ruleset aún requieren certificación |
| Neon Production, restore de secretos, rollback y escritores | **Sin evidencia suficiente** | NO_GO |

El archivo `F486_PRODUCTION_NO_GO_PUBLIC_EVIDENCE.json` está compuesto solo de datos no secretos; flags desconocidos se declaran **false**, no se inventan valores positivos. El test F484 valida expresamente que el estado NO_GO no puede convertirse en aprobado por ausencia de datos.

Este documento **no** es autorización para cambiar servicios o aplicar migraciones. Para pasar a una revisión manual: inventariar y respaldar Neon Production con verificación independiente, comprobar recuperación de configuración y secretos Render/Vercel, asegurar compatibilidad de migraciones bajo escritores, obtener permiso explícito del propietario, después efectuar despliegue coordinado `main` y validar health, readiness, autenticación y rollback. No borrar la rama Render histórica antes de finalizar la promoción.

Pagos reales permanecen aplazados por #69; los registros de auditoría F483 no activan cobros. PR #68 sigue DRAFT.
