# F458 — Política segura de identidad de IP para login

**Estado:** código en \`development\`, flag OFF por defecto. Issue #65.

- El nombre \`RENDER_SERVICE_NAME\` identifica el servicio, **no autentica** cabeceras de entrada. El cliente puede intentar forjar \`CF-Connecting-IP\` o \`X-Forwarded-For\`.
- \`DeploymentHttpSecurity.LoginRateLimitPartition\` utiliza la dirección \`RemoteIpAddress\` del peer de conexión salvo que \`Auth:TrustRenderCfConnectingIp\` esté expresamente habilitado. La opción por defecto es **false**, también en Render. No confundir esta IP con la del navegador cuando existe Vercel/proxy.
- Si se habilita manualmente la excepción, primero deben obtenerse evidencias de que **cada ingreso público** pasa por un proxy conocido que sobrescribe la cabecera sin permitir acceso directo alternativo y que la procedencia de esta identidad está controlada. Validación de rango, formato y dominio Render conocidos sigue siendo defensa complementaria, no prueba de autenticidad.
- El limitador de ASP.NET por peer es local a la instancia. El throttle por cuenta en PostgreSQL tiene su propio control de activación; ninguno de estos cambios convierte los límites por IP en un sistema distribuido de identidad de usuario.
- La comprobación CI prueba header público válido con opt-in, headers privados/malformados y que **sin opt-in** ni un header aparentemente válido cambia la partición. No se actualiza la configuración efectiva de Production ni Development en Render.
- **Gate #65 pendiente:** certificar la topología real del proxy, cookies HttpOnly/CSRF/roles, observabilidad, respaldo/restore y autorización separada para producción. No activar flag por deducción.

## F492 — Límite distribuido por peer de red, sin confiar en cabeceras

- Para activar se requieren Auth:EnableDistributedPeerLoginThrottle=true y Auth:VerifiedIngressPeerIdentity=true. **Ambos false por defecto**. Si falta atestación, IP o PostgreSQL, login devuelve 503.
- Usa HttpContext.Connection.RemoteIpAddress (peer TCP), canoniza IPv4-mapped IPv6 y IPv6 /64. Ignora X-Forwarded-For y CF-Connecting-IP.
- Reutiliza la tabla PostgreSQL core.login_throttle: 20 intentos por peer/60 segundos con INSERT ON CONFLICT atómico, además de 5 por cuenta/60 segundos de F369; claves HMAC SHA256 con dominios separados, sin IP ni correo en texto claro.
- **Riesgo NAT/proxy:** un ingress compartido acumula peticiones de todos los usuarios. Requiere topología real, pruebas de carga, decisión del operador y revisión de privacidad antes de encender; el flag de atestación no prueba por sí solo la ruta de red.
- La prueba F492 usa 21 peticiones concurrentes desde igual peer con 21 cabeceras falsas distintas: 20 normales y 1 HTTP 429, otro peer independiente, 503 por falta de attestation/IP y HMAC anónimo. PostgreSQL local CI; Production permanece sin cambios.
