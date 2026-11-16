# F477-F479 — gates de seguridad para tres issues

- #65 F477: rotacion EAN solo si RenderServiceName=marketplaceerp-api-staging, Deployment:Target=NeonIsolated, Payments:Provider=Disabled y alias historico habilitado; nombres vacios, servicio Production, marcador antiguo o Mock bloqueados. No se cambiaron los flags de Render.
- #69 F478: parser Wompi SV rechaza campos de JSON con variantes de mayusculas, incluyendo Id de Aplicativo, aunque la firma HMAC sea valida. Sin endpoint ni credenciales bancarias.
- #70 F479: concurrencia real de dos aprobaciones distintas sobre un producto en PostgreSQL temporal del CI. Solo una rotacion puede ejecutar; la otra debe fallar sin liberar ni reasignar EAN y el stock permanece igual.
- Render PROD sigue en integracion/main-reconciliacion-20261102@77e9a16 (autoDeploy ON); DEV esta en development (autoDeploy OFF). Vercel confirma previews READY de development, no autorizacion del alias Production.
- CI, migraciones DEV, hardware real y rollback Production siguen sujetos a aceptacion. No ejecutar migraciones en Neon cloud ni habilitar pagos con estos tests.