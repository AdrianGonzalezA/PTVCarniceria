# I18a — resultado de migración del catálogo de proveedores

**Fecha:** 05/11/2026. **Alcance:** exclusivamente `20261105120458_AddSuppliersI18a` en la base existente `carnicerias_test_visual`, contenedor `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. Esta entrega aún no habilita recepciones documentadas ni altera movimientos de stock.

## Prevuelo y respaldo

- Contenedor original `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, volumen `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d` en `/var/lib/postgresql/data`, estado `running`, enlace local `55433`. No se creó ni reinició ninguna base.
- PostgreSQL informó `carnicerias_test_visual|postgres|61|20261104191635_AddCashWithdrawalApproval`. EF mostró únicamente `20261105120458_AddSuppliersI18a (Pending)`. Sin sesiones adicionales de la base y sin procesos API de Carnicerías activos; Angular en `4200` no escribía a la base.
- Respaldo custom: `C:\Proyectos\Carnicerias\.local-backups\i18a-suppliers-before-20261105-121234Z.dump`, 437.424 bytes, 585 entradas legibles por `pg_restore --list`, fecha UTC `2026-11-05T12:12:34Z`, SHA-256 `E0CECAF6F65AF897BC5E418524726BE3B186F1C2CE3B0A2A399C1070D97828EF`.
- SQL EF revisado: `CREATE TABLE inventory.suppliers`, índice único por `CompanyId, NormalizedCode` y entrada en `__EFMigrationsHistory`; ningún `UPDATE`, `DELETE` ni cambio de tablas comerciales. El script previo local está en `.local-backups/i18a-suppliers-preview.sql`.

## Aplicación y postcheck

`tools/Apply-LocalPosMigrations.ps1` confirmó identidad de contenedor, puerto y base, y utilizó el usuario `postgres` de `tools/Start-LocalPosApi.ps1`, sin mostrar contraseña. EF aplicó exclusivamente `AddSuppliersI18a` bajo su lock exclusivo. Duración medida: **6,84 s**. Salida: `Done`.

Postcheck de lectura: **62 migraciones** con `AddSuppliersI18a` como última; tabla, PK, clave alternativa `(CompanyId, Id)`, FK restrictiva a empresa e índice único `(CompanyId, NormalizedCode)` presentes. Antes del ensayo HTTP había **0 proveedores**, **49 balances** y **172 movimientos**; el respaldo conserva el checksum.

## Ensayo controlado

`SupplierVisualEndpointTests.CrudIsCompanyScopedAndInactiveSuppliersStayInHistory` pasó con una empresa, sucursal, terminal, usuario y proveedor de prueba identificados por `I18A-...`: alta, duplicado por código normalizado rechazado, búsqueda, edición, inactivación y exclusión de opciones activas. La prueba limpió sus registros en `finally`. Postcheck: **0 proveedores**, **0 empresas `I18A-%`**, **49 balances** y **172 movimientos**. No se tocaron datos comerciales históricos.

Pruebas adicionales: 7/7 pruebas puras de proveedor/recepción al desplegar proveedores, 255/255 Angular, lint y build Web aprobados; `dotnet ef migrations has-pending-model-changes` informó que el modelo coincide con el snapshot de esa migración. Luego se preparó una **segunda migración de documentos de recepción que permanece pendiente**; al revisar esa preparación pasaron 8/8 pruebas puras y de modelo. La revisión visual administrativa y la recepción documentada permanecen pendientes. El `Down` de la migración de proveedores bloquea su borrado; si hay que revertir, deshabilitar la nueva pantalla/API conservando la tabla y sus datos.
