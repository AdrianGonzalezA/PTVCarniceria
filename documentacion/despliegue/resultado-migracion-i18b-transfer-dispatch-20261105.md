# I18b — despliegue del despacho de origen en laboratorio

El 05/11/2026 se aplicó únicamente `20261105184137_AddTransferDispatchI18b` a `carnicerias_test_visual` mediante `tools/Apply-LocalPosMigrations.ps1`, con el usuario PostgreSQL `postgres` usado por la API. El contenedor existente `carnicerias-pos-test-20261007` conservó ID `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, volumen `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d` y publicación `127.0.0.1:55433`. No se creó, clonó ni reinició una base.

## Respaldo y prevuelo

`tools/Backup-AuthorizedLab.ps1` generó `.local-backups/i18b-transfer-before-20261105-185420Z.dump` en formato custom: 470.137 bytes, 649 entradas TOC, checksum SHA-256 `7b0acc2ac75703cc481b8b9a62cc3a50c045b7527044b8bcc35908cc17b9015d`, verificado contra el archivo dentro del contenedor. El prevuelo de solo lectura confirmó 67 migraciones, última `20261105175303_AddCompanyRolePermissionOverrides`, cero escritores activos, ausencia de tablas de transferencia, 4 empresas, 13 usuarios y 4 piezas.

## Resultado

La migración creó `inventory.transfer_documents` y `inventory.transfer_lines`, amplió el CHECK de tipos de movimiento para `TransferOut` y agregó `inventory.transfers.manage` con concesión inicial a `administrator`. Postcheck: 68 migraciones, 2 tablas nuevas, 1 permiso, 1 concesión global, cero remitos y movimientos de transferencia al salir de la migración. Los conteos de empresas, usuarios y piezas no cambiaron.

`tools/Run-I18BTransferVisualTest.ps1` ejecutó una prueba HTTP con empresa, usuario, terminal, artículos y pieza aislados. Confirmó despacho hacia destino no informatizado, reintento sin duplicar, rechazo de saldo genérico insuficiente, una sola confirmación al competir dos conexiones por la misma pieza y débito correcto de pieza protegida pese a déficit genérico previo. El primer intento de prueba reveló que EF envolvía el conflicto de serialización en `InvalidOperationException`; se corrigió la clasificación del error y la repetición pasó. El fixture dejó cero empresas de prueba y cero remitos; los conteos globales finales fueron 4 empresas, 13 usuarios, 4 piezas, 172 movimientos, `OnHand` 384350,952 y `ProtectedOnHand` 103,952, iguales al prevuelo de la prueba.

## Recuperación

Ante un defecto se deben suspender nuevas confirmaciones de despacho, conservar remitos y movimientos y revisar el estado. `Down` no elimina automáticamente documentos ni historial. El respaldo está disponible para un procedimiento de recuperación autorizado; no se ejecutó restauración. La exportación y recepción digital del destino requieren otra sección de I18b.
