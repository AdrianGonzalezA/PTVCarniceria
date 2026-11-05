# I18a — migración de documentos de recepción en la base de desarrollo

**Fecha:** 05/11/2026. **Base única:** `carnicerias_test_visual` en el contenedor existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. Se usó el rol PostgreSQL `postgres`, igual al configurado por `Start-LocalPosApi.ps1`. No se creó ni reinició ninguna base.

## Prevuelo y respaldo

- Contenedor original `9a9fb3d3bdbe`, volumen `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`, sin conexiones a la base excepto las consultas de prevuelo. No había API local escuchando en `5197`, `5249` ni `5251`; Angular en `4200` no escribe directo en PostgreSQL.
- Prevuelo `tools/sql/20261105_i18a_receipts_preflight_readonly.sql`: 62 migraciones aplicadas, última `20261105120458_AddSuppliersI18a`; 0 tablas de recepción, 0 proveedores, 172 movimientos y 49 balances. El código contenía 63 migraciones: únicamente `20261105122200_AddInventoryReceiptDocumentsI18a` pendiente.
- Respaldo custom `C:\Proyectos\Carnicerias\.local-backups\i18a-receipts-before-20261105-131652Z.dump`: 439.758 bytes, 576 objetos listados por `pg_restore`, creado a las 13:16:53 UTC. SHA-256 `53F57793279A786E0E0263FD2BDA21C4E921DB99F7DF0FF8A322BF521F9CA475`, idéntico en el archivo del equipo y en el contenedor. Incluye catálogo, inventario e historial EF. No se ejecutó restauración.
- SQL EF inspeccionado en `.local-backups/i18a-receipts-preview-final.sql`: dos tablas nuevas, índices/FK/CHECK, ampliación del CHECK de tipos de movimiento y alta de `inventory.receipts.manage`. Sin `UPDATE` ni `DELETE` de registros comerciales.

## Aplicación y resultado

`tools/Apply-LocalPosMigrations.ps1` aplicó únicamente `20261105122200_AddInventoryReceiptDocumentsI18a` y terminó correctamente en 8,4 segundos, incluida la compilación. El postcheck de solo lectura `tools/sql/20261105_i18a_receipts_postcheck_readonly.sql` confirmó 63 migraciones, tablas vacías, permiso presente y cuatro restricciones verificadas. Proveedores 0, movimientos 172 y balances 49, sin cambios respecto del prevuelo.

La prueba HTTP aislada `InventoryReceiptVisualEndpointTests` creó una empresa, sucursal, proveedor, artículos, rol receptor y terminal de laboratorio; verificó acceso con el permiso nuevo, alta idempotente de borrador, dos ediciones de líneas, rechazo de versión vieja, aprobación concurrente sin duplicar stock, diferencia pendiente y cancelación sin ingreso. También comprobó que el rol receptor obtiene opciones de artículos sin permiso de ajuste, que el Excel previsualiza y guarda en borrador sin sumar stock hasta la aprobación, y que el endpoint legado de pieza crea un documento unitario aprobado e idempotente. La primera ejecución detectó que EF Core trataba una línea nueva como modificada; se corrigió marcándola explícitamente como `Added` y se repitió la prueba con éxito. El bloque `finally` eliminó el fixture. Postcheck final: 0 recepciones/líneas/proveedores residuales, 172 movimientos y 49 balances.

## Reversión

El `Down` de la migración se bloquea para preservar futuros documentos aprobados. Ante un fallo de aplicación, detener escritores y evaluar el estado con el postcheck; no ejecutar una reversión automática ni restaurar sin una decisión explícita. El respaldo anterior está verificado y disponible. Si se revierte solo el binario, deshabilitar nuevos ingresos del endpoint de piezas hasta que el código vuelva a ser compatible con los documentos persistidos.

## Pendientes operativos

La prueba HTTP no sustituye el recorrido de la interfaz. Quedan pendientes la asignación configurable del permiso por rol según alcance de empresa, la carga de múltiples piezas trazables en un borrador, y la revisión visual posterior dentro de Electron cuando termine el conjunto de cambios solicitado por el PO. La importación Excel actual carga artículos y cantidades; no inventa piezas trazables ni convierte códigos de bulto de I20.
