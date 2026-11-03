# I02 — Pruebas de integración y concurrencia autorizadas

**Fecha:** 03/11/2026. **Base única:** `carnicerias_test_visual`, contenedor existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. No se ejecutaron migraciones, restauraciones, conciliaciones ni cambios de esquema.

## Controles y fixture

Antes de escribir se reconfirmaron el ID del contenedor y su volumen original, el nombre de la base y el puerto. `"__EFMigrationsHistory"` contenía **57 migraciones**, con `20261102181956_ReserveTraceablePieces` como última. El respaldo `i02-before-20261103-140552-7d9413d0.dump` continuaba disponible: **394.910 bytes** y SHA-256 `1223B32EFAEFD467FAC356C65447892B3EAC3B56781BF87CD01FFDD4FA156C30`. Las piezas `999002`, `250661` y `071000` estaban en `HistoricalUnverified` y no se utilizaron en la prueba.

Se ejecutó únicamente [PieceReservationConcurrencyTests.TwoBoxesCannotReserveOrSellTheSamePiece](../../tests/Carnicerias.IntegrationTests/PieceReservationConcurrencyTests.cs) con `CARNICERIAS_I02_WRITES_APPROVED=I02_APPROVED` y una conexión de proceso a la base y puerto anteriores; la contraseña no se imprimió ni se guardó en Git. El fixture creó una empresa `I02-...`, sucursal, dos cajeros, catálogo, lista de precio, tres piezas de 1 kg, movimientos, balance y dos borradores. Dos piezas se llevaron a estados terminales `Transferred` y `Consumed`, con egresos de prueba compensatorios; la tercera se utilizó para la carrera de reserva. El test hizo `DELETE` exclusivamente de filas de esa empresa aislada, dentro de una transacción, en su bloque `finally`.

En la ejecución ampliada, las identidades de prueba fueron:

| Entidad | Identificador |
| --- | --- |
| Empresa | `4b00c0e8-25fe-4974-b08b-06fad2e86e82`, código `I02-6EE69C834E` |
| Pieza de reserva/venta | `4082427e-f4b4-4ac4-8707-f142d979bfd9` |
| Pieza transferida | `42b9389a-e537-4c1e-b25e-d1503f3b43c8` |
| Pieza consumida | `14bc736b-8c62-4aa4-a61f-3aad46274f1b` |

## Resultados por escenario

| Escenario | Evidencia y resultado |
| --- | --- |
| Dos cajas reclaman la misma pieza | Dos `DbContext`/conexiones independientes, transacciones `Serializable` y barrera de inicio simultáneo. `Assert.Single(results, won => won)` pasó: una reserva se confirmó; la otra devolvió rechazo o conflicto de serialización. |
| Operación ajena intenta reservar o vender | Desde un contexto nuevo, `ReserveAsync` y `SellAsync` con el borrador perdedor devolvieron `false`; el propietario de reserva siguió siendo el ganador. |
| Interrupción simulada y recuperación | Se cerraron los contextos de la carrera después del commit. Un contexto nuevo leyó la misma pieza `Reserved`, el mismo propietario, un evento de reserva y el borrador activo persistido. No se simuló un apagado del sistema operativo. |
| Cancelación | `ReleaseAsync` y `SaleDraft.Cancel` se confirmaron en la misma transacción. Un contexto posterior encontró el borrador `Cancelled`; el segundo borrador pudo reservar la pieza liberada. |
| Confirmación del estado de venta | `SellAsync`, el componente transaccional invocado por la confirmación, pasó la pieza de `Reserved` a `Sold`, borró su propietario y registró el evento. Desde un contexto posterior, `ReserveAsync` devolvió `false`; se contaron cuatro eventos de estado de esa pieza en el fixture. |
| Transferida y consumida | Dos piezas aisladas persistieron con estados `Transferred` y `Consumed`; para ambas, `ReserveAsync` y `SellAsync` devolvieron `false` en PostgreSQL. El dominio también rechazó transferir o consumir una pieza reservada. |
| Piezas históricas inmovilizadas | Las tres piezas reales siguieron en estado `5` (`HistoricalUnverified`) con pesos 0,750 / 51,601 / 51,601 kg; ninguna fue objeto del fixture. |

La prueba de integración se ejecutó **dos veces**, antes y después de ampliar las aserciones, y pasó **1/1** en cada ejecución. Se ejecutaron además **20/20 pruebas puras** de `InventoryPieceTests` y `HistoricalPieceCompatibilityTests`, incluidas las que rechazan re-reservar piezas vendidas, transferidas, consumidas o históricas sin verificar. No se lanzó la suite de integración general porque contiene pruebas que requieren una base descartable, prohibida en este laboratorio.

## Saldos y limpieza

| Medida en toda la base | Antes | Después |
| --- | ---: | ---: |
| Migraciones aplicadas | 57 | 57 |
| Piezas | 4 | 4 |
| Movimientos de inventario | 172 | 172 |
| Borradores | 101 | 101 |
| Ventas confirmadas / renglones | 80 / 114 | 80 / 114 |
| Eventos de estado de pieza | 4 | 4 |
| Filas de balance | 49 | 49 |
| `OnHand` total | 384.350,952 kg | 384.350,952 kg |
| `Reserved` total | 6,000 kg | 6,000 kg |
| `Quarantined` total | 103,952 kg | 103,952 kg |

Asado en Sucursal Visual quedó en **62,000 kg contables, 0,750 kg inmovilizados y 61,250 kg disponibles**. AGUJA en Alta Córdoba quedó en **10.097,852 kg contables, 103,202 kg inmovilizados y 9.994,650 kg disponibles**. No hubo doble descuento. Una consulta de solo lectura posterior encontró **cero** empresas `I02-...`, piezas con origen `I02 fixture`, movimientos con motivo `Fixture I02...` y filas con el ID de empresa indicado en sucursales, usuarios, artículos, piezas, movimientos, eventos, balances o borradores. No se requirió limpieza manual.

## Incidencias y alcance de la evidencia

**Incidencias:** ninguna; no se encontraron defectos en el mecanismo de reserva, liberación y transición de pieza probado. La concurrencia y los estados se ejercitaron sobre transacciones y componentes reales de la API con PostgreSQL. El test no ejecuta el recorrido HTTP completo de cierre de venta, ni los futuros flujos de transferencia o consumo; `Transferred` y `Consumed` se preparan como estados de piezas aisladas para probar su rechazo de reserva. La recuperación comprobada corresponde a cierre y reapertura de conexiones, no a un corte real de energía. Estos límites no afectan los saldos ni la limpieza comprobados.

**Estado:** pruebas controladas de I02 terminadas; no se inicia otro incremento.
