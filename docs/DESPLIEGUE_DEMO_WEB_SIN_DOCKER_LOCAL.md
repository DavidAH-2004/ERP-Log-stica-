# LOGIMKT — Demo web compartida (una cuenta Admin, sin Docker en los equipos)

> Candidato de despliegue basado en ZIP `ERP-Log-stica--main.zip` (5 nov 2026). **No desplegado ni probado en .NET/Railway en este entorno.** Debe aprobar build y arranque de PostgreSQL nuevo antes de compartir la URL.

## Modelo de acceso

- **Una URL HTTPS** para todos los integrantes; React se entrega desde el mismo contenedor ASP.NET Core que sirve `/api`.
- **Una única cuenta `Admin` inicial**, creada al arrancar en una BD nueva a partir de `ADMIN_EMAIL` y `ADMIN_PASSWORD`. Las credenciales **NO se guardan en GitHub**; configurarlas en el servicio.
- La demo alojada no permite crear más cuentas mediante `POST /api/usuarios`.
- Los pagos, el Checkout por POST, los reembolsos y la conciliación operativa están bloqueados. No configure `Payments__EnableMock=true`.
- **Se requiere Railway (u otro alojamiento)** con backend .NET y PostgreSQL. No hay Docker en los ordenadores de los visitantes. Railway puede utilizar Docker para **construir el servidor**.
- Compartan la URL y las credenciales **por canal privado**; todas las sesiones comparten permisos y datos. En una demo pública, usar datos sintéticos y renovar clave al finalizar.

## Despliegue recomendado: Railway

1. Subir estos archivos a una **rama nueva** del repositorio GitHub, sin tocar `main` ni las ramas de los otros grupos; primero validar el build .NET, las pruebas y el frontend.
2. En Railway, crear proyecto; añadir **Postgres** nuevo, sin importar/copiar los datos reales.
3. Añadir servicio **Deploy from GitHub repo**, seleccionar la rama de demo, y utilizar el Dockerfile `src/MarketplaceERP.API/Dockerfile` (hay `railway.json`). Fijar **Root directory** en raíz del repositorio, no en `src`.
4. Configurar en el servicio las variables siguientes (sin comillas y con valores reales):

| Variable | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` |
| `DATABASE_URL` | `${{Postgres.DATABASE_URL}}` (referencia al Postgres del proyecto; seleccionar por autocompletado) |
| `Demo__Hosted` | `true` |
| `Payments__Provider` | `Disabled` |
| `Payments__EnableMock` | `false` |
| `Payments__EnableRecoveryQA` | `false` |
| `Payments__EnableRefundsQA` | `false` |
| `Reservas__EnableExpirationQA` | `false` |
| `Seed__DemoData` | `true` (catálogo de demostración; verificar 500 SKU) |
| `ADMIN_EMAIL` | correo elegido para la **única** cuenta admin |
| `ADMIN_PASSWORD` | contraseña fuerte, privada, >=16 caracteres |
| `Jwt__Key` | cadena aleatoria segura de >=48 caracteres; conservar estable entre despliegues |
| `Jwt__Issuer` | `MarketplaceERP` |
| `Jwt__Audience` | `MarketplaceERP.Clients` |
| `Jwt__ExpirationMinutes` | `60` |
| `PORT` | `8080` si el servicio lo solicita |

**Nunca** subir `.env`, contraseñas o cadenas DB a Git. No habilitar público el puerto PostgreSQL ni conectarlo a bases anteriores.

5. En Railway, **Networking → Generate Domain**, apuntando al puerto `8080` si corresponde. Abrir `https://...up.railway.app/` y verificar pantalla login.
6. Antes de compartir, comprobar `/health/ready` = HTTP 200; ingresar con admin; verificar catálogo; probar recarga en una ruta del frontend; verificar que `POST /api/checkout` retorna 503 sin crear órdenes.
7. Si el arranque falla, consultar **Deploy Logs**. **No** intentar despliegues sucesivos en bases que ya tengan datos sin revisar migraciones; el inicializador de .NET aplica `Database.MigrateAsync()` al arrancar. Para la demo se exige una base PostgreSQL vacía y exclusiva.

## Costos y seguridad

Railway puede cobrar por ejecución, almacenamiento y transferencia, según plan y consumo. Configurar alertas de gasto. La base demo no debe contener datos reales ni contraseñas históricas; el acceso remoto usa HTTPS y JWT. El uso de una cuenta compartida impide atribuir acciones a una persona, así que es **solo para demostración temporal**, no una práctica recomendable para producción.

## Diferencia importante

**Subir el código a GitHub no sirve la aplicación en el navegador.** Se necesita un despliegue activo. Tampoco equivale a la evaluación académica completa: faltan verificaciones de KPIs e integración intergrupal con datos en ejecución.
