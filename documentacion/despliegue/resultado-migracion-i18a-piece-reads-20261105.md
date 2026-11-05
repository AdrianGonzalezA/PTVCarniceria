# I18a — lecturas de piezas en recepciones

**Fecha:** 05/11/2026. **Base única:** `carnicerias_test_visual`, contenedor existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. No se creó, clonó ni reinició ninguna base.

## Prevuelo y respaldo

- Se verificaron el contenedor `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, el volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d` y cero escritores activos. El script `tools/sql/20261105_i18a_piece_reads_preflight_readonly.sql` confirmó 65 migraciones, última I17, tabla nueva ausente, cero recepciones/líneas, cuatro piezas existentes y cero conflictos de identidad de perfiles.
- Se creó un respaldo custom con `tools/Backup-AuthorizedLab.ps1`: `.local-backups/i18a-pieces-before-20261105-153658Z.dump`, 457.468 bytes, 624 entradas legibles por `pg_restore -l`, con objetos de recepción. SHA-256 local y del contenedor: `84C6C99CFE48314BF0AE237E9F0F664229D03D6D024E16B5CF7BEE815394FDB0`. El archivo local queda excluido de Git.

## Migración y verificación

- Se aplicó únicamente `20261105152647_AddReceiptPieceReadsI18a` mediante `tools/Apply-LocalPosMigrations.ps1` después del respaldo. El modelo EF coincidía con su snapshot y no había otra migración pendiente. La migración agregó `inventory.receipt_piece_reads`, claves a documento, línea, perfil y pieza, índices de idempotencia/unicidad y una restricción de peso positivo. También agregó la clave compuesta de perfil por empresa. No alteró datos comerciales históricos.
- `tools/sql/20261105_i18a_piece_reads_postcheck_readonly.sql` confirmó 66 migraciones, tabla y restricciones presentes, cero lecturas/recepciones/líneas antes de las pruebas y las cuatro piezas previas intactas.
- `tools/Run-I18APieceReadsVisualTest.ps1` verificó nuevamente identidad, volumen, respaldo y esquema antes de ejecutar `InventoryReceiptVisualEndpointTests` en una empresa ficticia aislada. Pasó la prueba HTTP: dos lecturas del mismo artículo con pesos diferentes, reintento idempotente, rechazo de duplicado, edición genérica y Excel sin pérdida de piezas, aprobación única, rechazo de aprobación competidora y saldo `OnHand`/`ProtectedOnHand`/genérico consistente. El fixture se eliminó: cero empresas, documentos, líneas o lecturas de prueba; continúan cuatro piezas preexistentes.
- Pasaron siete pruebas puras `InventoryReceiptTests`, 263 pruebas Angular en 49 archivos, compilaciones .NET y Angular y lint Angular. La compilación Angular conserva avisos previos de presupuestos CSS y dependencia CommonJS `qrcode`.

## Recuperación y pendientes

El `Down` de la migración bloquea el borrado automático de lecturas trazables. Ante un incidente, detener escritores y conservar la tabla; revertir binarios requiere impedir nuevas recepciones por piezas hasta restaurar una versión compatible. Restaurar el dump descartaría los cambios posteriores y requiere decisión separada. El respaldo no se publicó.

Pendientes de I18a: asignación configurable de `inventory.receipts.manage` por empresa/rol tras resolver el alcance de roles globales; recorrido visual de administración y caja cuando el PO revise la interfaz completa. La plantilla Excel de esta fase importa cantidades genéricas y conserva las piezas escaneadas; no importa códigos individuales de piezas en lote.
