# I17 — rendición de cierre de turno

**Fecha:** 05/11/2026. **Base única:** `carnicerias_test_visual`, contenedor existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. Se usó el rol `postgres` obtenido de la misma configuración de Docker que usa el script de conexión de la API. No se creó, clonó ni reinició ninguna base.

## Prevuelo y respaldo

- Identidad: contenedor `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`, puerto local `55433`, cero conexiones activas ajenas al prevuelo.
- `tools/sql/20261105_i17_preflight_readonly.sql`: 64 migraciones aplicadas, última `20261105135907_AddProtectedStockI10`; 33 turnos, 30 cerrados; tabla de rendiciones inexistente. El modelo EF coincidió con el snapshot preparado y solo I17 estaba pendiente.
- Respaldo custom `.local-backups/i17-before-20261105-150437Z.dump`, 454.801 bytes, 618 entradas legibles por `pg_restore -l` y tabla de turnos incluida. SHA-256 local y en contenedor: `716F31F6B4CBF839B6790AF00C4BA58397F9A8CBB97D7BA7127C25889C951154`. No se ejecutó restauración. El procedimiento verificable quedó en `tools/Backup-I17.ps1`; el archivo local está excluido de Git por contener datos de prueba y credenciales hasheadas.

## Migración y verificación

- Se aplicó únicamente `20261105144324_AddShiftClosingReportsI17` mediante `tools/Apply-LocalPosMigrations.ps1` después del respaldo, con cero escritores y usando la única base permitida. EF adquirió el bloqueo exclusivo y finalizó correctamente.
- `tools/sql/20261105_i17_postcheck_readonly.sql`: 65 migraciones, tabla `payments_cash.shift_closing_reports`, columnas JSONB/versionadas mediante snapshot, PK por turno, FK compuesta por empresa/turno y dos índices. Cero reportes antes de las nuevas operaciones; permanecieron 33 turnos y 30 cerrados.
- La prueba HTTP controlada `ShiftClosingReportVisualEndpointTests.CloseStoresSnapshotAndAdminDownloadsTheSameReport` pasó: rechazo de arqueo incompleto, preview de $100, cierre con $95 contados y $90 entregados, diferencia de -$5, persistencia única, indicador en historial y descarga PDF válida. Usó una empresa/caja temporal aislada y eliminó su fixture. La primera invocación del script de prueba terminó con error **solo en el postcheck** por referir `public.companies`; la prueba ya había pasado. Se corrigió el script a `platform_access.companies` y se repitió el postcheck de lectura: cero empresas de fixture, cero reportes, 33 turnos y 30 cerrados. No hubo corrección comercial ni migración repetida.

## Recuperación y pendientes

Ante un problema, detener escritores y conservar la tabla I17: el `Down` lanza excepción para impedir pérdida de reportes. Se puede revertir el binario de API con el esquema expandido solo si se evita cerrar turnos en la versión anterior. Restaurar el dump requeriría una decisión separada porque descartaría cambios posteriores a I17. El respaldo se encuentra en `.local-backups/` y no debe publicarse.

Pendientes: verificación visual de caja en Electron cuando el PO revise la interfaz completa; prueba con impresora física asignada de cada tipo. La prueba HTTP aislada no sustituye la operación manual del cajero.
