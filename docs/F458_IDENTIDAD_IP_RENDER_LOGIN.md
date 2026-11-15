# F458 — Política segura de identidad de IP para login

**Estado:** código en \`development\`, flag OFF por defecto. Issue #65.

- El nombre \`RENDER_SERVICE_NAME\` identifica el servicio, **no autentica** cabeceras de entrada. El cliente puede intentar forjar \`CF-Connecting-IP\` o \`X-Forwarded-For\`.
- \`DeploymentHttpSecurity.LoginRateLimitPartition\` utiliza la dirección \`RemoteIpAddress\` del peer de conexión salvo que \`Auth:TrustRenderCfConnectingIp\` esté expresamente habilitado. La opción por defecto es **false**, también en Render. No confundir esta IP con la del navegador cuando existe Vercel/proxy.
- Si se habilita manualmente la excepción, primero deben obtenerse evidencias de que **cada ingreso público** pasa por un proxy conocido que sobrescribe la cabecera sin permitir acceso directo alternativo y que la procedencia de esta identidad está controlada. Validación de rango, formato y dominio Render conocidos sigue siendo defensa complementaria, no prueba de autenticidad.
- El limitador de ASP.NET por peer es local a la instancia. El throttle por cuenta en PostgreSQL tiene su propio control de activación; ninguno de estos cambios convierte los límites por IP en un sistema distribuido de identidad de usuario.
- La comprobación CI prueba header público válido con opt-in, headers privados/malformados y que **sin opt-in** ni un header aparentemente válido cambia la partición. No se actualiza la configuración efectiva de Production ni Development en Render.
- **Gate #65 pendiente:** certificar la topología real del proxy, cookies HttpOnly/CSRF/roles, observabilidad, respaldo/restore y autorización separada para producción. No activar flag por deducción.
