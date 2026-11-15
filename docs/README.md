# Índice vigente de documentación — MarketplaceERP

**Última revisión:** 14 de noviembre de 2026. **Ramas operativas:** `main` (producción) y `development` (mejoras futuras). El código de `main` todavía **no** representa un backend/BD productivos migrados; verificar servicios antes de cualquier publicación.

## Documentos de referencia

| Documento | Uso |
| --- | --- |
| [Arquitectura de los dos entornos](OPERACION_ENTORNOS_Y_LIMPIEZA_20261114.md) | Inventario actual de GitHub, Render, Neon y Vercel, protección de información, ramas vigentes y camino de despliegue |
| [Gate operativo D de release](PR60_D_RELEASE_GATE_OPERATIVO_20261114.md) | Requisitos de secreto/configuración, backup fresco, escritura y rollback Productivos; **el registro original es histórico** (PR #60 ya se fusionó, pero Production sigue pendiente) |
| [Decisiones P1 de seguridad](P1-DECISIONES-SEGURIDAD.md) | Deuda de autenticación, cookies HttpOnly, CSRF y política de acceso |
| [Regresión P4](P4-INTEGRACION-REGRESION-20261108.md) | Evidencias y pruebas históricas de STAGING; no equivalen a la verificación actual de Production |

## Estado real y registros de aceptación

- [Issue #65 — Checklist maestro de Producción y Desarrollo](https://github.com/Marzza01/ERP-Log-stica-/issues/65): fuente de verdad del cierre operativo; conservar controles abiertos hasta cumplirlos de verdad.
- [Issue #63 — Auditoría e higiene del repositorio](https://github.com/Marzza01/ERP-Log-stica-/issues/63): historial de 93 referencias, limpieza de 90 ramas antiguas, hallazgos de scanner y remanentes por certificación de dependencias.
- [Issue #51 — Auditoría npm resuelta](https://github.com/Marzza01/ERP-Log-stica-/issues/51): `minimatch` parcheado y comprobado contra CI de `main` después de PR #66.
- [PR #66](https://github.com/Marzza01/ERP-Log-stica-/pull/66): actualiza workflows CI de `develop` a `development` y exige auditoría completa de npm.

## Reglas para nuevas mejoras

1. Trabajar en `development`, no directamente sobre `main`. Mantener Render Desarrollo `autoDeploy=off`; probar código con la base Neon aislada.
2. Usar PR hacia `main` con CI aprobado. Exigir revisión explícita para cambios de seguridad, pagos y esquema.
3. Confirmar que Vercel no haya reasignado su alias público. Fusionar código no implica publicar Production ni ejecutar migraciones.
4. No introducir secretos, `.env`, dumps, bundles históricos, capturas de credenciales ni tokens en GitHub. Los respaldos sensibles deben residir en un depósito privado.
5. Conservar las ramas temporales usadas por Render Production o Railway hasta demostrar que sus servicios ya no las consumen. Las ramas de PostgreSQL Neon son independientes de las ramas Git.

## Material histórico

El README principal y los documentos antiguos contienen fotografías de fases, commits y nombres de ramas ya superados; sirven para auditoría, pero sus instrucciones NO sustituyen el estado actual en #65 ni este índice.

El workflow `.github/workflows/logimkt-grupo-a-regresion.yml` pertenece a un grupo histórico y referenciaba una rama Git eliminada; no es el CI canónico. La CI vigente es `.github/workflows/ci.yml`.
