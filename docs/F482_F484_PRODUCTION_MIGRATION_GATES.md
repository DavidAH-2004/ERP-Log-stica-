# F482/F484 — Fingerprint de migración y evidencia de release

## F482: PostgreSQL desechable

La prueba F451 compara SHA-256 determinista de `ProductoId`, SKU, EAN histórico, vendedor y stock antes y después de F451/F470 Up, Down seguro sin emisiones y nuevo Up. Cuando una emisión histórica ya existe, el Down debe abortar sin cambiar la huella y preservando `catalog.f470_aprobar_rotacion` para recuperación. Solo CI `localhost:5432/postgres`. No valida restauración de una base Neon real.

## F484: contrato GO/NO-GO de publicación

`node scripts/f484-production-evidence-gate.mjs /ruta/evidencia-validada.json` es un comprobador **offline**. Exige SHA de `main` igual a backend Render y al deployment asignado al alias público Vercel, las ramas `main`, recuperación efectiva de datos y secretos, protección de ramas, ausencia de escritores sin cercar, pagos reales Disabled y autorización específica del propietario. Un dato no disponible => NO_GO.

La salida `READY_FOR_MANUAL_REVIEW` no despliega ni autoriza el cambio: la procedencia de la evidencia y credenciales/secretos deben revisarse por fuera. En 15-nov-2026 Render Production seguía ejecutando `77e9a16` desde la rama histórica, distinto de `main`; el gate daría NO_GO. No se ha modificado ninguna variable ni servicio productivo.

El CI solo ejecuta fixtures sintéticos. No genera falsos resultados de release Production.
