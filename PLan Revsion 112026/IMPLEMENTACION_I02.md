# I02 — Implementación preparada y puerta de despliegue

**Actualización posterior, 03/11/2026:** I02 fue aplicada y validada técnicamente por el PO en la base única. Ver [resultado de migración](../documentacion/despliegue/resultado-migracion-i02-20261103.md), [pruebas de dos conexiones](../documentacion/despliegue/resultados-pruebas-i02-20261103.md) y los pendientes de homologación del [plan vigente](PLAN_EJECUCION_AJUSTADO.md). Los apartados siguientes conservan la evidencia y las restricciones de la **etapa de preparación previa**; sus menciones a «no aplicada» o «prueba omitida» ya no describen el estado actual. I03 requiere diseño y autorización separados.

## Alcance implementado

| Flujo | Implementación y protección |
| --- | --- |
| Recepción | Una pieza nueva nace `Available` y su evento inicial se guarda con el movimiento de entrada. Se conserva identidad, origen, código, peso e idempotencia de la recepción existente. |
| Guardado A/B/C/D | `SaleDraftEndpoints.SaveAsync` usa la transacción serializable existente, bloquea el borrador y aplica cambios condicionales por pieza `Available → Reserved` o `Reserved → Available`. Mantiene la reserva agregada de stock y el precio de líneas ya guardadas. Regrabar sin cambiar piezas no renueva la reserva ni agrega eventos. |
| Cancelación/venta | Cancelar libera solo las piezas del borrador dueño, además de la reserva agregada. Confirmar exige ese dueño y cambia `Reserved → Sold` en la misma transacción que venta, movimientos y stock. El índice único de línea vendida permanece como defensa adicional. |
| Recuperación | El propietario de cada reserva es el ID del borrador persistido. No existe caducidad automática: un cierre inesperado conserva pieza, línea, precio, slot y turno para la recuperación existente. |
| Lectura POS | El escaneo indica `PIECE_RESERVED`, `PIECE_ALREADY_SOLD` o no disponible según estado. Si una caja intenta guardar una versión obsoleta tras una liberación administrativa, el servidor impide readquirir la pieza en el mismo borrador y el POS recarga el ticket persistido. |
| Liberación excepcional | Solo un administrador de la misma empresa, con motivo, puede liberar. La transacción serializable bloquea el borrador y comprueba ausencia de venta confirmada y de intentos Mercado Pago preparados, pendientes, aprobados o por conciliar. Retira únicamente esa línea, ajusta `Reserved` agregado y registra actor, motivo y transición. El borrador permanece abierto aunque quede vacío; el cajero puede cancelarlo por el flujo normal. Si el descuento ya no es válido, se quita el descuento y se informa en la respuesta. |
| Transferencia/producción | La máquina de estados define `Transferred` y `Consumed` como no reservables. Sus escritores y operaciones de negocio corresponden a incrementos posteriores; I02 no creó rutas de transferencia ni de producción. |

## Archivos y contratos

- Estado, evento, mapeo y migración: `src/Carnicerias.Infrastructure/InventoryPiece.cs`, `InventoryPieceStateEvent.cs`, `PlatformAccessDbContext.cs`, `Migrations/20261102181956_ReserveTraceablePieces.cs` y su diseñador/snapshot.
- Transacciones/API: `src/Carnicerias.Api/Inventory/PieceReservationTransactions.cs`, `InventoryPieceEndpoints.cs`, `Sales/SaleDraftTransactionLock.cs`, `SaleDraftEndpoints.cs`, `SaleConfirmationEndpoints.cs`, `Payments/MercadoPagoPaymentEndpoints.cs`.
- UI/cliente: `src/Carnicerias.Web/src/app/core/inventory/inventory-piece-client.ts`, `features/admin/piece-receipt-page.*`, `features/pos/pos-page.ts`.
- Pruebas: `tests/Carnicerias.IntegrationTests/InventoryPieceTests.cs`, `PieceReservationConcurrencyTests.cs`; `piece-receipt-page.spec.ts`, `pos-page.spec.ts`. `src/Carnicerias.Api/AssemblyInfo.cs` expone los auxiliares internos solo al proyecto de pruebas.

Se mantuvieron las rutas existentes de borrador, confirmación, recepción y búsqueda. Se agregaron `GET /api/inventory/pieces/{id}/events` y `POST /api/inventory/pieces/{id}/release`, ambos bajo el alcance y permiso de stock, con comprobación adicional del rol administrador para liberar. Los contratos de lectura de pieza incorporan campos de estado; los consumidores actuales conservan los campos anteriores.

## Migración preparada

`20261102181956_ReserveTraceablePieces` agrega `Status`, propietario y fechas de reserva, `Quarantined`, campos del bloqueo temporal por artículo/sucursal, eventos, índices, FK y restricciones. En una transacción aborta ante reclamaciones duplicadas, cruces venta/borrador y diferencias de empresa/sucursal/artículo/peso. Reconstruye `Sold` y `Reserved` por vínculos explícitos; las piezas ambiguas pasan a `HistoricalUnverified = 5` y su peso completo a `Quarantined`. Solo el resto pasa a `Available`. Si falta un balance o `OnHand < Reserved + Quarantined`, aborta todo. Inserta un evento inicial por pieza, sin modificar ventas ni movimientos históricos. El SQL se regeneró **sin conectarse a una base**; no se ejecutó.

**Antes de aplicar, con autorización separada:** detener todas las API escritoras y cajas, repetir el prevuelo en lectura y el control de viabilidad de cuarentena, obtener y verificar un respaldo de la base única `carnicerias_test_visual`, aprobar la ventana de corte, aplicar la migración y desplegar la nueva API junto con ella. La ambigüedad permanece explícita y bloqueada hasta una conciliación física/documental posterior. El código nuevo necesita las columnas nuevas; la API vieja no debe seguir escribiendo durante ni después del corte. Si el prevuelo falla, la transacción revierte la migración. El `Down` se bloquea para no borrar estado y auditoría; una reversión posterior requiere reparación hacia adelante revisada o restaurar un respaldo con escritores detenidos, considerando las operaciones ocurridas desde el corte.

## Compatibilidad histórica aprobada y preparada en código

El PO aprobó `HistoricalUnverified` y `ReconciledUnavailable`, la inmovilización del peso y la conciliación administrativa auditada. No se asocia ninguna venta genérica a las piezas `999002`, `250661` o `071000`. El saldo contable sigue en `OnHand`; `Quarantined` retiene kilos de piezas indeterminadas; la venta común usa `OnHand - Reserved - Quarantined`. La pantalla de existencias muestra los tres componentes y la disponibilidad. POS, catálogo, borradores, confirmación, ajustes, recepción e importador Excel verifican la retención y el bloqueo temporal. Los renglones y pagos previos permanecen inalterados.

Un administrador de la empresa inicia un conteo para una pieza pendiente. El servidor coloca un lease de **3 minutos** en el saldo del artículo y la sucursal, renovable cada 45 segundos por la pantalla. Se rechaza el inicio si hay un ticket abierto con ese artículo. Durante el lease, el catálogo publica disponibilidad cero para el artículo y las escrituras de venta, cancelación, ajuste, recepción e importación se rechazan o fallan por concurrencia; una caída deja vencer el lease sin limpieza manual. La conciliación exige peso observado, conteo total del artículo, motivo y referencia estable al acta. El servidor registra la hora, actor, evento y un movimiento nuevo solo por la diferencia entre conteo físico y saldo contable. La pieza completa verificada pasa a `Available`; una pieza no disponible pasa a `ReconciledUnavailable`. En ambos casos se libera la retención de esa pieza, sin descontar automáticamente su peso de `OnHand` por segunda vez. Una referencia documental no prueba por sí sola la existencia física: el administrador debe conservar el acta y la evidencia bajo el procedimiento de la empresa.

Rutas nuevas bajo `inventory.stock.manage` y rol `administrator` de la misma empresa: `POST /api/inventory/pieces/{id}/reconciliation-lease`, `POST .../renew`, `DELETE .../{lockId}` y `POST /api/inventory/pieces/{id}/reconcile`. `GET /api/inventory/pieces` acepta `status` opcional sin cambiar las consultas anteriores. El resultado de conciliación es idempotente por `OperationId` de empresa; el reintento de red conserva el mismo cuerpo. La auditoría conserva estado anterior/final, actor, referencia de evidencia, peso observado, conteo, fecha y vínculo al movimiento nuevo si corresponde. No hay ruta para revertir un estado terminal en I02.

## Verificación y límites

- Compilación API .NET: correcta, sin advertencias ni errores. `dotnet ef migrations has-pending-model-changes --no-build` con conexión ficticia sin servidor: modelo y snapshot coinciden. SQL regenerado desde EF sin conectar a PostgreSQL.
- Modelo, saldos, transiciones e importador Excel: 31 pruebas .NET focalizadas superadas sin conexión a BD. Arquitectura .NET: 104 pruebas superadas.
- Concurrencia de dos conexiones y recuperación desde una conexión nueva: prueba extendida con lease de stock, protegida por `CARNICERIAS_I02_WRITES_APPROVED=I02_APPROVED` y cadena de la base única; **omitida**, porque requiere escrituras temporales todavía no autorizadas.
- Angular: 222 pruebas completas superadas. Once pruebas focalizadas de recepción, conciliación y existencias superadas; incluyen búsqueda de piezas pendientes, saldos separados, reintento idempotente ante corte de red y bloqueo de ajuste durante el conteo.
- Lint Web: correcto. Build Web: correcto; conserva advertencias previas de presupuestos SCSS en Administración y del paquete CommonJS `qrcode`.
- Prevuelo de solo lectura en `carnicerias_test_visual:55433`: tres piezas ambiguas, dos grupos con `quarantine_gate=READY`, cero conflictos fatales y cero diferencias de reserva agregada. No se ejecutó la migración ni el postcheck que requiere el nuevo esquema.
- Validación manual de I02 y la pendiente de I04/I05 en dos cajas Electron: pendiente de autorización de migración y de un entorno ejecutable con datos; no se presentó una simulación en memoria como prueba de exclusividad PostgreSQL.

## Riesgos restantes

1. La exclusividad SQL, el bloqueo entre caja y pago y la liberación administrativa aún requieren prueba real con dos conexiones sobre la base única, después de autorización específica de escrituras y de aplicar la migración.
2. El backfill es deliberadamente conservador: una venta genérica o un ajuste negativo posterior a la recepción deja la pieza inmovilizada hasta la conciliación. El prevuelo de lectura detectó tres casos y saldo suficiente, pero hay que repetirlo antes del corte.
3. Se necesita corte coordinado de escritores. Mezclar API vieja y nueva permitiría rutas antiguas sin transición de estado o intentos de inserción sin columnas obligatorias.
4. Los estados `Transferred` y `Consumed` son terminales en el modelo actual, pero no tienen flujo de transferencia/producción en I02; dichos módulos requieren diseño y autorización propios.

**Siguiente puerta:** el prevuelo de solo lectura se completó en `carnicerias_test_visual` y se repitió con la consulta de viabilidad de cuarentena; los dos grupos de artículos dieron `READY`. Ver `documentacion/despliegue/preflight-i02-20261102.md`. El respaldo, la aplicación de la migración y el fixture de integración requieren autorización posterior y separada. I02 sigue preparado en código; no se debe iniciar la API nueva sobre el esquema anterior.
