# Resultado de migración I16 — 04/11/2026

**Destino exclusivo:** contenedor existente `carnicerias-pos-test-20261007`, ID `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`, `127.0.0.1:55433`, base `carnicerias_test_visual`, rol `postgres` configurado por `Start-LocalPosApi.ps1`. La API estaba detenida y el prevuelo no detectó otras conexiones.

## Respaldo

Se generó `C:\Proyectos\Carnicerias\.local-backups\i16-before-20261104193201Z-4b2d930f.dump` con `pg_dump -Fc` el 04/11/2026 a las 19:32:12 UTC. Tamaño: **428.107 bytes**. SHA-256 local y del contenedor: `3C4B45792B31BDD9F0A6B29D799D9C3522B49777163A9DC4F8F48A9738A1B01F`. `pg_restore --list` leyó 558 entradas y `pg_restore --file=/dev/null` verificó el archivo completo con salida cero. El respaldo está fuera de Git. No se ejecutó restauración.

## Prevuelo y aplicación

El [prevuelo](../../tools/sql/20261104_i16_preflight_readonly.sql) confirmó 60 migraciones, última I13a; tabla de retiros inexistente; CHECK del ledger con tipos 0–5; 108 movimientos, 33 turnos, cero permisos `cash.withdrawal.approve` y cero conexiones ajenas. El contenedor, puerto y volumen coincidieron con el laboratorio autorizado. La migración I16 era la única pendiente y EF no detectó cambios de modelo posteriores.

Se aplicó exclusivamente [I16](../../tools/sql/20261104_i16_withdrawal_migration.sql) mediante `psql -X -v ON_ERROR_STOP=1 -U postgres -d carnicerias_test_visual`, con transacción y `COMMIT` a las 19:33:43 UTC. No se aplicó otra migración ni se editó ningún registro comercial histórico.

## Postcheck, prueba y recuperación

El [postcheck](../../tools/sql/20261104_i16_postcheck_readonly.sql) confirmó **61 migraciones**, nueva tabla con 10 restricciones y 8 índices, CHECK del ledger ampliado a tipo 6, tabla nueva sin filas, 108 movimientos y 33 turnos. El permiso existe una vez; `administrator` lo tiene y `cashier` no. La prueba HTTP con fixture aislado pasó y la limpieza devolvió los mismos conteos.

El respaldo anterior permitiría recuperar **esta misma base** únicamente con una decisión explícita sobre pérdida de escrituras posteriores. El `Down` preparado se niega a borrar la tabla si existen solicitudes; no se ejecutó. Un rollback operativo conserva la tabla y sus datos y evita volver a una API anterior que desconozca el bloqueo por solicitudes pendientes.
