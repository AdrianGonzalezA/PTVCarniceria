# I02 — homologación HTTP de venta trazable

Fecha: 04/11/2026. Base única autorizada: `carnicerias_test_visual`, contenedor `carnicerias-pos-test-20261007`, puerto local `55433` y volumen original verificados por `tools/Run-I02HttpVisualTest.ps1`. El runner exige 59 migraciones y la última `20261104133006_MakeProductCostOptional` antes de escribir. No aplicó migraciones ni alteró operaciones históricas.

## Prueba ejecutada

`TraceablePieceHttpVisualEndpointTests.TwoCashiersRecoverReleaseAndSellOnePieceOnlyOnce` pasó. Usó una empresa, sucursal, producto de 1,200 kg, pieza, lista de precios, dos cajeros y dos terminales con credenciales propias; todo con prefijo `I02HTTP-` y limpieza en `finally`.

1. La caja A buscó el código y reservó la pieza en el borrador A.
2. La caja B recibió `PIECE_ALREADY_USED` al intentar agregarla; la búsqueda por código recibió `PIECE_RESERVED`.
3. Una nueva sesión HTTP de la caja A recuperó el borrador y canceló la operación. La pieza volvió a estar disponible.
4. La caja B reservó y confirmó la venta de $120,00. El reintento de confirmación devolvió el mismo ID de venta.
5. Tras vender, la búsqueda devolvió `PIECE_ALREADY_SOLD` y otro borrador recibió `PIECE_ALREADY_USED`.
6. Se verificaron una sola venta, una sola línea vinculada a la pieza y eventos de dos reservas, una liberación y una confirmación. El saldo de prueba quedó con `OnHand=0`, `Reserved=0` y `Quarantined=0`.

Resultado del postcheck de lectura: `carnicerias_test_visual|0|3`, es decir, cero empresas fixture `I02HTTP-` residuales y las tres piezas `999002`, `250661` y `071000` conservadas en `HistoricalUnverified`. La prueba concurrente de dos conexiones PostgreSQL ya estaba aprobada y documentada en `documentacion/despliegue/resultados-pruebas-i02-20261103.md`.

## Incidencia controlada

La primera ejecución falló en la limpieza del fixture porque intentó borrar el borrador antes que la pieza que aún lo referenciaba. Se corrigió el orden, se eliminó exclusivamente esa empresa fixture y se verificó su ausencia. La siguiente ejecución falló por una aserción del test que leía `code` en vez de `error.code`; el fixture se limpió correctamente. Tras ajustar la aserción, el recorrido y el postcheck pasaron.

## Pendiente

Falta operación real en dos cajas Electron y recuperación ante interrupción real del proceso/equipo. Esta prueba recreó la sesión HTTP, no una caída de energía. Las validaciones manuales de I04/I05 en cajas abiertas también siguen pendientes.
