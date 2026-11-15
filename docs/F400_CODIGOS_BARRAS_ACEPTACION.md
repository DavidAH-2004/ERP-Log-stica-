# F400 — Puerta de aceptación del sistema de códigos de barras (MarketplaceERP)

**Fecha:** 2026-11-15  
**Fuente fijada:** `alxmariotejada-byte/erp-sistema-de-codigo-de-barras-@523a82ddca72450e51640d8491dfd3ae0ef1b1a4`  
**Destino aprobado para desarrollo:** `Marzza01/ERP-Log-stica-:development`  
**Fuera de alcance de esta aceptación:** `main`, Render/Neon/Vercel Production, cobros reales, eliminación de ramas históricas.

## 1. Matriz de recepción técnica

| Componente | Estado y evidencia | Restricción |
|---|---|---|
| Validación de EAN-13 interno | Integrado; tests C# y JS | Prefijo de circulación interna `20` no equivale a GS1 comercial |
| Asignar EAN a producto sin código | API solo Admin; PostgreSQL `nextval`; CI F402 PASS | No toca EAN no vacío, ni siquiera histórico inválido |
| Buscar por código y consultar etiquetas | Admin/Vendedor y alcance por vendedor; F401 PASS | Comprador y anónimo denegados |
| Hojas Avery 5160, 30 por carta | F405 PASS (Chromium: 3×10 y PDF de una página) | **Falta impresión en papel a escala 100 %** |
| Generar SVG y decodificar ópticamente | F406 CI de JsBarcode→ZXing | **No equivale a uso con cámara o lector físico** |
| Escáner por cámara | UI integrada y permisos de HTTPS | **Falta cámara real autorizada, distintos navegadores** |
| Pistola USB/Bluetooth | Campo de entrada interpreta 13 dígitos + Enter | **Falta lector físico y comprobación de terminador/idioma** |
| Conteos de inventario por escáner | F399 E2E PostgreSQL PASS (Admin y dos solicitudes concurrentes) | Vendedor denegado hasta separación completa por almacén |
| Idempotencia y hallazgos | F404 PASS; rechazo de UUID reutilizado con cantidad alterada, un hallazgo por lectura | Sin mutación de stock directa por escaneo |
| Substituir o regenerar un EAN existente | **No integrado**; se conserva código histórico | Requiere historia de alias, auditoría persistente e invalidación/reconciliación de etiquetas |
| Normalización masiva del catálogo del fork | **Rechazada por riesgo de pérdida de identificadores** | Nunca ejecutar su `UPDATE` o limpieza de EAN en arranque |

## 2. Evidencia automatizada

- [CI #43689207510](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43689207510): 4/4, prueba F399 de conteo con lectura concurrente, F401 acceso, F402 EAN.
- [CI #43690403236](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43690403236): 4/4, F404 sin hallazgos duplicados.
- [CI #43690717252](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43690717252): 4/4, F405 30 etiquetas virtuales en una página carta, F399/F390.
- [CI #43691966469](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43691966469): **4/4 SUCCESS**, F406 tres EAN-13 JsBarcode→ZXing y pruebas de sesión/stock concurrentes.
- El rollback de migraciones F397 se ensayó en PostgreSQL desechable con EAN históricos preservados. **F408:** las migraciones Up aditivas se ensayaron después en una copia de Neon Desarrollo y luego se aplicaron a la rama principal de Neon Desarrollo (no Producción), con huella EAN idéntica antes/después.

## F408/F409 — Despliegue seguro de Desarrollo (15-nov-2026)

Se creó una rama de recuperación Neon sin compute `br-delicate-sea-bbcw0ene` desde el LSN `0/2E7CC80`, con vencimiento 29-nov-2026; también una rama temporal para ensayo `br-polished-darkness-bbp358o9`, con vencimiento 18-nov. El snapshot manual anterior se conservó porque el proyecto rechazó uno nuevo por cuota agotada. Ambas ramas temporales provienen del mismo origen y se verificó por SELECT la huella de productos/EAN en la rama de ensayo.

Se generó el SQL de **exactamente las dos migraciones EF** (sin DROP, DELETE FROM ni UPDATE de productos), se aplicó primero en la rama de ensayo y se comparó la estructura con la rama Development. Tras aprobar, se aplicó transaccionalmente en Neon **Development** por el conector nativo. La verificación obtuvo **Core4 / Catalog9 / Finance10**, 18 tablas Catalog, 500 productos, 451 sin EAN y 49 EAN de 13 dígitos; la huella EAN permaneció `de0d29ab45a87632b06d362424b51141`. Sesiones, lecturas y hallazgos nuevos: cero filas. No se tocó Neon Production.

El despliegue manual de Render Desarrollo `dep-dbsmoobbc2fs73c0h8j0` alcanzó estado **LIVE** en `development@cb843e8`; Vercel Preview protegida `dpl_5A1A1dwTKpwf9CcGvTztw8cxNgNn` **READY** con el mismo commit. Se probaron GET `/health`, `/health/ready` (**200**) y GET anónimos a sesión, consulta de etiquetas, sesiones de conteo y búsqueda EAN (**401**) en ambas rutas. Los logs Render posteriores no registraron errores en la ventana consultada; readiness tardó alrededor de 12 segundos en una muestra, insuficiente para medir SLO.

**Esta es evidencia de migración y rutas protegidas, no de E2E autenticado con tres roles en la Preview ni de impresora, cámara o lector físico.** La decisión de publicación sigue siendo NO-GO a Producción.

## 3. Prueba física indispensable (F398)

Registrar **fecha, equipo y evidencia observable** de cada escenario; detener si se modifican códigos o stock inesperadamente.

1. **Impresora:** imprimir una hoja de prueba compatible Avery 5160 en papel carta al **100 %**, sin «Ajustar a la página». Verificar primera, central y última celda, límites, quiet zones y lectura del código en las 30 posiciones.
2. **Pistola USB/Bluetooth:** escanear EAN válido con sufijo Enter, y sin sufijo; confirmar que el operador puede introducir cantidad y seleccionar ubicación; verificar que el escaneo nunca duplica el conteo ante reintento de red.
3. **Cámara real:** HTTPS y consentimiento de cámara, permiso denegado, cámara ocupada, cambio de orientación, EAN inválido y EAN válido. No habilitar cámara remotamente sin consentimiento explícito.
4. **Roles:** Admin puede imprimir y contar; Vendedor solo consulta sus productos/etiquetas; Comprador no ve conteos ni impresión interna.
5. **Datos históricos:** cotejar EAN antes/después de la prueba y comprobar que stock total/reservado no cambia por un escaneo.
6. **Incidencias:** registrar fallos en #70 con pasos y versión, corregir en `development`, repetir CI.

No marcar F398 como completo porque un dispositivo aparece enumerado en Windows: eso no demuestra impresión, lectura ni accesibilidad real.

## 4. Condiciones para F400 y la promoción

- [x] F406 y último CI con cambios funcionales: 4/4 PASS, [run #43691966469](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/43691966469).
- [ ] F398 verificado con dispositivos físicos o aceptación explícita **documentada del alcance sin hardware**; nunca llamar «hardware probado» a una simulación.
- [ ] Aceptación expresa de que **cambio/regeneración** de EAN permanecen excluidos hasta diseñar auditoría, alias y compatibilidad de etiquetas históricas, o implementación segura por separado con pruebas.
- [ ] Revisar PR #68 por módulos y permisos; no tratar 76 archivos/92 commits como exclusivamente códigos de barras.
- [ ] Para `main`: cumplir issue #65 (respaldos de la base productiva, rollback coordinado, variables reales seguras), obtener autorización productiva independiente y *después* considerar promoción.
- [x] Mantener pagos reales del issue #69 en modo Disabled; no se han ejecutado cobros.

**Decisión de liberación actual: NO-GO a Producción.** El resultado automatizado respalda `development`; la prueba física y recuperación productiva siguen abiertas.
