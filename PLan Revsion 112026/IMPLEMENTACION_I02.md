# I02 — Implementación preparada y puerta de despliegue

**Fecha:** 02/11/2026. **Estado:** código y migración preparados, sin aplicar en PostgreSQL. El PO autorizó exclusivamente I02 y prohibió ejecutar migraciones, modificar datos existentes o correr pruebas de integración con escrituras. Esta entrega no habilita I03 ni otro incremento.

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

`20261102181956_ReserveTraceablePieces` agrega `Status`, `ReservedBySaleDraftId`, `ReservedAtUtc`, `StateChangedAtUtc`, tabla de eventos, índices, FK al borrador y restricciones de consistencia. En una sola transacción bloquea las tablas relevantes, aborta ante reclamaciones duplicadas, cruces con ventas, diferencias de empresa/sucursal/artículo/peso o stock histórico ambiguo; luego reconstruye `Sold` desde ventas confirmadas, `Reserved` desde borradores activos y `Available` solo para piezas no ambiguas. Inserta un evento de backfill por pieza. El script SQL se generó **sin conectarse a una base**, únicamente para revisar sintaxis y orden; no se ejecutó.

**Antes de aplicar, con autorización separada:** detener todas las API escritoras y cajas, comprobar en lectura los datos históricos afectados, obtener y verificar un respaldo de la base única `carnicerias_test_visual`, resolver cualquier ambigüedad de piezas sin inventar estados, aprobar ventana de corte, aplicar la migración y desplegar la nueva API junto con ella. El código nuevo necesita las columnas nuevas; la API vieja no debe seguir escribiendo durante ni después del corte. Si el prevuelo falla, la transacción revierte la migración. El `Down` se bloquea deliberadamente para no borrar estado y auditoría; una reversión posterior requiere reparación hacia adelante revisada o restaurar un respaldo con escritores detenidos, considerando las operaciones ocurridas desde el corte.

## Verificación y límites

- Compilación API .NET: correcta, sin advertencias ni errores.
- Modelo, transiciones y mapeo: 18 pruebas .NET superadas en el filtro focalizado, sin conexión a BD.
- Concurrencia de dos conexiones y recuperación desde una conexión nueva: prueba preparada y protegida por `CARNICERIAS_I02_WRITES_APPROVED=I02_APPROVED` y cadena de la base única; **omitida**, porque requiere escrituras temporales todavía no autorizadas.
- POS: 52 pruebas focalizadas y 220 pruebas Angular completas superadas; incluyen recarga tras liberación administrativa. La pantalla de piezas tiene prueba de auditoría y liberación con motivo.
- Lint Web: correcto. Build Web: correcto; conserva advertencias previas de presupuestos SCSS en Administración y del paquete CommonJS `qrcode`.
- Validación manual de I02 y la pendiente de I04/I05 en dos cajas Electron: pendiente de autorización de migración y de un entorno ejecutable con datos; no se presentó una simulación en memoria como prueba de exclusividad PostgreSQL.

## Riesgos restantes

1. La exclusividad SQL, el bloqueo entre caja y pago y la liberación administrativa aún requieren prueba real con dos conexiones sobre la base única, después de autorización específica de escrituras y de aplicar la migración.
2. El backfill es deliberadamente conservador: una venta genérica o un ajuste negativo posterior a la recepción puede exigir conciliación manual antes del despliegue.
3. Se necesita corte coordinado de escritores. Mezclar API vieja y nueva permitiría rutas antiguas sin transición de estado o intentos de inserción sin columnas obligatorias.
4. Los estados `Transferred` y `Consumed` son terminales en el modelo actual, pero no tienen flujo de transferencia/producción en I02; dichos módulos requieren diseño y autorización propios.

**Siguiente puerta:** autorización expresa para prevuelo de datos, respaldo, aplicación de migración y fixture de integración sobre la única base. Hasta entonces, I02 queda preparado en código y no se debe iniciar la API nueva sobre el esquema anterior.
