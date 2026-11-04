# Resultado de migración I03a — 04/11/2026

**Destino exclusivo:** `carnicerias_test_visual`, contenedor existente `carnicerias-pos-test-20261007`, PostgreSQL 16.10 publicado en `127.0.0.1:55433`. Se conservó el contenedor `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0` y el volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`. No se creó, clonó ni reinició una base.

## Prevuelo y respaldo

- La base respondió como `carnicerias_test_visual`; `pg_stat_activity` no mostró otras sesiones de aplicación durante el corte. No había APIs locales escuchando en `5197`, `5249`, `5251`, `4200` ni `4302`.
- EF Core enumeró 57 migraciones aplicadas y solamente `20261103193100_AddSaleCheckoutProcess` pendiente. El SQL generado desde I02 a I03a se revisó en `.local-backups/i03a-migration-20261104.sql`: agrega tres tablas, dos columnas opcionales, claves e índices; sustituye dos índices de intentos MP. No borra ni reescribe registros comerciales.
- Se creó un respaldo `pg_dump -Fc` de la base completa en [`.local-backups/i03a-before-20261104-080901-37f95ba0.dump`](../../.local-backups/i03a-before-20261104-080901-37f95ba0.dump), fuera de Git. Tamaño: **405.715 bytes**. Marca del archivo: **2026-11-04 11:09:02 UTC**. SHA-256 local y del contenedor: `BBE68EBFB967648F3C2CB0103B1E6C8627276B33835406D1ABAA0CD6D3CFAF63`. `pg_restore --list` leyó 528 entradas y `pg_restore --file=/dev/null` leyó el contenido completo con salida cero. No se realizó restauración.

## Aplicación y postcheck

Se ejecutó `tools/Apply-LocalPosMigrations.ps1` después del prevuelo y respaldo. EF aplicó **solo** `20261103193100_AddSaleCheckoutProcess`, obtuvo el bloqueo exclusivo de migraciones y terminó con salida cero. No hubo incidencias. El comando completo, incluida la compilación, duró aproximadamente **18 segundos**.

El [postcheck de solo lectura](../../tools/sql/20261104_i03a_postcheck_readonly.sql) terminó con salida cero:

| Verificación | Resultado |
| --- | --- |
| Historial EF | **58** migraciones; última `20261103193100_AddSaleCheckoutProcess`. |
| Esquema | Existen `payments_cash.sale_payment_processes`, `sale_payment_instruments` y `sale_payment_events`. |
| Índices y restricciones | Índice único por borrador, clave de commit y vínculos de instrumento; índices MP de compatibilidad; 18 restricciones de las tablas nuevas presentes. |
| Registros comerciales antes y después | **101** borradores, **80** ventas confirmadas, **26** intentos MP, **84** pagos de venta: sin variación. |
| Datos I03a iniciales | **0** procesos, **0** instrumentos, **0** eventos y **0** vínculos nuevos a pagos históricos. |

## Recuperación y siguiente puerta técnica

El respaldo custom está disponible para una recuperación **solo en la misma base autorizada**, previa detención de escritores, verificación de checksum y evaluación de operaciones posteriores. No ejecutar `Down` si existen procesos I03a: la migración impide retirar el esquema con datos de cobro. Si ya hubo ventas o pagos nuevos, restaurar el snapshot perdería esas operaciones; corresponde conciliar y reparar hacia adelante.

El esquema quedó listo para homologación HTTP y Electron con datos de prueba identificados, seguimiento de saldos y limpieza segura. La aplicación real de cobros y proveedores todavía debe validarse; este postcheck acredita estructura y conservación de datos, no el flujo funcional.

## Verificación HTTP posterior — 04/11/2026

Se ejecutó `tools/Run-I03AVisualTest.ps1` sobre la única base permitida. El fixture creó una empresa `I03A-...` con sucursal, caja, cajero, artículo, lista y saldo propios. La prueba recorrió login, apertura de turno, borrador de 2 unidades por $200, dos transferencias independientes de $80 y $120, commit repetido con la misma clave, aprobación parcial, bloqueo de cierre y de confirmación, recuperación en otra sesión, aprobación final y confirmación repetida. Comprobó una venta, dos pagos y dos movimientos de caja por $200, stock 10→8 y reserva 2→0. La limpieza se limitó al fixture.

El postcheck de solo lectura posterior confirmó **58 migraciones, 101 borradores, 80 ventas, 26 intentos MP y 84 pagos**, sin alteración de los conteos previos; **0 procesos, 0 instrumentos, 0 eventos y 0 empresas `I03A-...` residuales**. La prueba reveló tres defectos de código corregidos: campos opcionales rechazados en preparación, borrador sin renglones cargados en commit y eventos nuevos con GUID interpretados como existentes por EF. El cambio de mapeo de GUID no altera el esquema: `has-pending-model-changes` terminó sin diferencias. Pasaron además 5 pruebas del modelo de checkout, 104 de arquitectura, 224 Angular, lint y build .NET.

Permanecen pendientes la validación operativa en Electron, MP sandbox y recuperación ante una interrupción real. El respaldo anterior a la migración sigue siendo el punto de recuperación documentado; no se ejecutó restauración.
