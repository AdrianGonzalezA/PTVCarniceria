# I02 — Resultado de aplicación de la migración

**Verificación posterior autorizada:** las pruebas controladas de concurrencia y su limpieza constan en [resultados-pruebas-i02-20261103.md](resultados-pruebas-i02-20261103.md).

**Fecha:** 03/11/2026 (Argentina, UTC−03:00). **Destino único:** `carnicerias_test_visual`, contenedor Docker existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. **Resultado:** `20261102181956_ReserveTraceablePieces` aplicada y comprobada. No se ejecutaron restauraciones, conciliaciones ni pruebas de integración con escritura.

## Puerta previa

| Control | Evidencia antes de aplicar |
| --- | --- |
| Contenedor y volumen | ID `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d` en `/var/lib/postgresql/data`; publicación `127.0.0.1:55433 → 5432`. `psql` confirmó `current_database() = carnicerias_test_visual`. |
| Escritores | Cero procesos de la API Carnicerías y cero conexiones ajenas en `pg_stat_activity`, comprobado nuevamente inmediatamente antes del comando de migración. No hubo procesos que detener ni se interrumpieron otros servicios. |
| Respaldo | [Respaldo previo](resultado-respaldo-prevuelo-i02-20261103.md): `.local-backups/i02-before-20261103-140552-7d9413d0.dump`, 394.910 bytes, SHA-256 `1223B32EFAEFD467FAC356C65447892B3EAC3B56781BF87CD01FFDD4FA156C30`. Coincidieron copia local y archivo del contenedor. `pg_restore --list` identificó formato custom, base correcta y objetos esperados; `pg_restore --file=/dev/null` leyó el archivo íntegro con salida cero. |
| Migraciones | Se compararon los 57 archivos de migración POS locales con `"__EFMigrationsHistory"`: 56 aplicadas, sin divergencias; única pendiente `20261102181956_ReserveTraceablePieces`. |
| Guardas | [Prevuelos SQL de solo lectura](../../tools/sql/20261102_i02_preflight_readonly.sql): `duplicate_active_claims=0`, `sold_in_active_draft=0`, `active_piece_scope_or_weight=0`, `sold_piece_scope_or_weight=0`, `inventory_reservation_mismatches=0`; `unassigned_ambiguous_stock=3`, clasificado conservadoramente. Los dos grupos de inmovilización dieron `READY` (0,750 kg y 103,202 kg). No se observaron conexiones de aplicación en la lectura. |

La medición previa de registros comerciales y saldos agregados fue: 4 piezas; 172 movimientos de inventario; 49 balances; `OnHand` total 384.350,952 kg; `Reserved` total 6,000 kg; 101 borradores con 140 renglones; 80 ventas con 114 renglones; 26 intentos de pago, 84 pagos y 108 asientos de caja.

## Aplicación

Se ejecutó **una sola vez** el procedimiento documentado:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Apply-LocalPosMigrations.ps1
```

El script fija el contenedor, el puerto y la base autorizados, obtiene la credencial local sin imprimirla y llama `dotnet ef database update --context PlatformAccessDbContext`. La salida fue `Build succeeded`, `Applying migration '20261102181956_ReserveTraceablePieces'`, `Done` y `Migraciones aplicadas en carnicerias_test_visual`; código de salida **0**. La ejecución del comando ocupó aproximadamente **21 segundos** según las esperas medidas por la herramienta; entre la marca del control final previo (14:21:28) y la confirmación posterior (14:22:13) transcurrieron **45 segundos**, incluyendo comprobación y comunicación. No se aplicó otra migración.

## Verificación posterior de solo lectura

Se ejecutó completo [20261102_i02_postcheck_readonly.sql](../../tools/sql/20261102_i02_postcheck_readonly.sql) dentro de `BEGIN TRANSACTION READ ONLY`, con `ON_ERROR_STOP=1`; terminó en `COMMIT` sin errores.

| Control | Resultado |
| --- | --- |
| Historial EF POS | 57 entradas; última `20261102181956_ReserveTraceablePieces` |
| Esquema | `inventory.piece_state_events` existe; las cuatro columnas nuevas de pieza y las seis de cuarentena/bloqueo de balance están presentes |
| Restricciones | Ocho restricciones relevantes presentes y `convalidated = true` |
| Índices | Cuatro índices nuevos revisados con `indisvalid = true` e `indisready = true` |
| Estados de pieza | 1 `Sold`, 3 `HistoricalUnverified`, 0 `Reserved`, 0 `ReconciledUnavailable` |
| Auditoría de backfill | 4 piezas y 4 eventos de migración, exactamente uno por pieza; `pieces_without_exactly_one_backfill = 0` |
| Propietarios de reserva inválidos | 0 |
| Balances inválidos o cuarentena divergente | 0 / 0 |
| Bloqueos de conciliación activos o vencidos | 0 |
| Inmovilización histórica total | 103,952 kg |
| Conexiones ajenas tras postcheck | 0 |

Verificación individual: `999002` (0,750 kg), `250661` (51,601 kg) y `071000` (51,601 kg) quedaron en `HistoricalUnverified`, sin propietario de reserva, sin venta enlazada y con un evento de migración cada una. La pieza `999001` quedó en `Sold`, con su venta enlazada y un evento de migración. No se atribuyó ninguna venta genérica a las tres piezas ambiguas.

| Empresa / sucursal | Artículo | `OnHand` antes y después | `Reserved` | `Quarantined` después | Disponible después |
| --- | --- | ---: | ---: | ---: | ---: |
| Empresa Visual / Sucursal Visual | `1002` Asado | 62,000 kg | 0,000 kg | 0,750 kg | **61,250 kg** |
| Carniceria Rustica SRL / Alta Cordoba | `71.000` AGUJA | 10.097,852 kg | 0,000 kg | 103,202 kg | **9.994,650 kg** |

`Quarantined` coincide exactamente con la suma de pesos de piezas `HistoricalUnverified` por artículo y sucursal. `OnHand` conserva los saldos contables previos; `disponible = OnHand − Reserved − Quarantined`, sin segundo egreso de inventario. Los conteos previos y posteriores de piezas, movimientos, balances, borradores, ventas, pagos y caja fueron **idénticos**; también lo fueron las sumas agregadas de `OnHand` y `Reserved`.

## Incidencias y límites

No hubo errores del procedimiento ni conflictos nuevos en las guardas. No se hizo una restauración de ensayo ni se ejecutó el `Down`; la [recuperación documentada](preflight-i02-20261102.md#reversión-riesgos-y-ventana) sigue requiriendo autorización específica y, si hubo nuevas ventas, una evaluación adicional para no perderlas. No se ejecutó `PieceReservationConcurrencyTests.TwoBoxesCannotReserveOrSellTheSamePiece` ni recorrido de dos cajas Electron: ambos requieren escrituras de prueba no autorizadas en este corte. Tampoco se realizó conciliación administrativa de las tres piezas.

**Puerta de salida:** migración I02 aplicada y verificaciones de lectura aprobadas. Se detiene el trabajo aquí, a la espera de nueva autorización para cualquier prueba con escritura u otro incremento.
