# Resultado de migración I13a — 04/11/2026

**Destino exclusivo:** `carnicerias_test_visual` en el contenedor existente `carnicerias-pos-test-20261007`, publicado solo en `127.0.0.1:55433`. Se mantuvieron el ID de contenedor `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0` y el volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d` en `/var/lib/postgresql/data`. La API del puerto 5197 se detuvo de forma controlada antes del respaldo; `pg_stat_activity` registró cero conexiones ajenas a la sesión de control. Angular y las dos ventanas Electron quedaron abiertas, sin usar la API durante el corte.

## Respaldo verificado

Se ejecutó `pg_dump -U postgres -d carnicerias_test_visual -Fc` con el **mismo rol PostgreSQL que configura `Start-LocalPosApi.ps1`**, sin exponer la contraseña. El archivo custom se copió a `C:\Proyectos\Carnicerias\.local-backups\i13a-before-20261104190945Z-4381e378.dump` (fuera de Git): **423.704 bytes**, creado el **04/11/2026 19:09:45 UTC**, SHA‑256 `C810D66C58C981A593227BFBC685A330B2BC7DC8F6991E90AC90ACAD2E46D42E`. El hash local coincidió con el del contenedor. `pg_restore --list` leyó 550 entradas e identificó `fiscal_documents`, `confirmed_sales`, `cash_ledger`, `branch_inventory` e historial EF; `pg_restore --file=/dev/null` leyó el archivo completo con código cero. **No se restauró** ninguna base.

## Prevuelo y aplicación

El [prevuelo de lectura](../../tools/sql/20261104_i13a_preflight_readonly.sql) confirmó `current_database() = carnicerias_test_visual`, usuario `postgres`, **59 migraciones aplicadas** y última `20261104133006_MakeProductCostOptional`. Había 60 migraciones locales: la única pendiente era `20261104184401_AddFiscalDocumentSnapshots`. Antes de aplicar había 53 ventas no fiscales, 10 tickets fiscales, 17 facturas electrónicas y 24 intentos fiscales. No había otras conexiones a la base.

Se aplicó exclusivamente el [SQL idempotente I13a](../../tools/sql/20261104_i13a_snapshot_migration.sql) dentro de una transacción con `psql -X -v ON_ERROR_STOP=1`, usando `docker exec ... -U postgres -d carnicerias_test_visual`. Inició el **04/11/2026 19:10:29 UTC** y terminó el **04/11/2026 19:10:30 UTC**, con `COMMIT` y código cero. No se ejecutó otra migración ni se alteraron filas comerciales.

## Postcheck y recuperación

El [postcheck de solo lectura](../../tools/sql/20261104_i13a_postcheck_readonly.sql) terminó sin errores: **60 migraciones**, última I13a; existen `pos_sales.fiscal_document_snapshots` y `pos_sales.fiscal_document_vat_lines` con dos PK, dos FK y dos CHECK. Ambas tablas nuevas contienen **0 filas**; persisten **24** intentos fiscales, **80** ventas confirmadas y **114** renglones confirmados. Los intentos anteriores permanecen sin snapshot fiscal, conforme a la decisión de no reconstruir históricos.

El respaldo local y su checksum quedan como referencia de recuperación en la **misma base**, pero restaurarlo después de nuevas operaciones perdería esas operaciones. Ante una incidencia posterior se detendrían escritores, se verificaría el hash y se decidiría una reparación hacia adelante o restauración con autorización específica. El `Down` preparado bloquea el borrado de tablas si contienen snapshots. No se ejecutó reversión ni prueba con escritura.

**Pendiente:** desplegar el API y la UI nuevos cuando terminen los cambios en curso; comprobar el flujo HTTP/ARCA con fixture autorizado y la lectura histórica en Electron. La migración acredita esquema y conservación de datos, no emisión efectiva de un comprobante nuevo.
