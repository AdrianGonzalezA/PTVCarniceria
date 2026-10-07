# Especificación: cierre de venta y caja por cajero/turno

## Estado

Corte implementado para caja asociada al cajero y a su turno, dentro de la sucursal activa. La identidad de caja fue definida por el usuario el 6 de octubre de 2026. La verificación integrada y sus límites están registrados en `tasks/pos-checkout-plan.md`.

## Objetivo

Permitir que el cajero abra su turno, registre pagos combinados y confirme un borrador real como venta. La confirmación debe guardar venta, renglones, pagos, movimientos de caja y egreso de stock en una sola transacción. Los reintentos no deben duplicar venta, cobros ni movimientos.

## Reglas acordadas y supuestos visibles

- Empresa, sucursal y cajero provienen de la sesión autorizada; no se aceptan como identidad confiable desde el body.
- Existe como máximo un turno abierto por cajero y sucursal. Cada turno conserva actor, apertura, saldo inicial y cierre.
- El fondo de apertura se captura explícitamente y puede ser cero. El cierre con arqueo físico/diferencias queda fuera de este corte hasta acordar esa política.
- Una venta solo puede confirmarse desde un borrador persistido del cajero/sucursal actual y con una lista/precios ya congelados.
- El backend recalcula el total con los renglones y precios del borrador. No acepta total, precio ni vuelto calculado por el cliente.
- Se admiten pagos combinados. Débito, crédito, transferencia, Mercado Pago y cheque son registros manuales, sin integración con adquirentes.
- Solo efectivo puede exceder el saldo pendiente. El vuelto se calcula en servidor y se registra como egreso de efectivo; otros medios no pueden exceder el total.
- Cuenta corriente se excluye hasta definir cliente, autorización, pagos parciales y saldo.
- La venta real no emite comprobante fiscal en este corte. Impuestos, descuentos, correlativos de piezas, devolución/anulación y facturación ARCA quedan fuera.
- La venta de demostración nunca se confirma como operación comercial.

## Contrato funcional/API implementado

- `GET /api/cashier-shifts/current`: turno abierto del usuario/contexto actual o `204`.
- `GET /api/cashier-shifts/last-closed`: último turno cerrado del mismo cajero y sucursal, o `204`; permite recuperar el resumen después de reiniciar Electron.
- `POST /api/cashier-shifts`: abre turno con fondo inicial no negativo; rechaza segundo turno abierto para ese cajero/sucursal.
- `POST /api/cashier-shifts/current/close`: cierra el turno propio solo si no hay una confirmación en curso; arqueo físico no requerido en este corte.
- Las respuestas de turno incluyen `openingCash`, `cashSales`, `nonCashSales`, `salesTotal` y `cashBalance`. Se calculan desde el libro de movimientos: el efectivo recibido menos el vuelto integra `cashSales`; `cashBalance` agrega el fondo inicial. No equivalen a un arqueo físico.
- `POST /api/sales/drafts/{draftId}/confirmation`: requiere turno abierto y pagos. El borrador solo pasa a confirmado si se registran juntos venta/detalle, pagos, caja y egreso de stock.
- La confirmación usa `draftId` como intención idempotente: una repetición devuelve la venta resultante; un payload de pagos distinto para esa venta confirmada devuelve conflicto.
- Los métodos iniciales son efectivo, débito, crédito, transferencia, Mercado Pago y cheque. Solo se exponen los que estén activos; su administración configurable puede incorporarse sin romper el contrato.
- Rutas autenticadas y ligadas al contexto operativo; los recursos siempre se filtran por empresa/sucursal y las operaciones de caja por cajero/turno propio. El reintento de confirmación de un borrador ya cobrado solo devuelve la venta a su cajero; otro cajero recibe conflicto.
- Errores mantienen `{ error: { code, message, details } }`; códigos estables incluyen `CASHIER_SHIFT_REQUIRED`, `CASHIER_SHIFT_ALREADY_OPEN`, `SALE_NOT_CONFIRMABLE`, `PAYMENT_TOTAL_MISMATCH`, `INVALID_CHANGE` e `IDEMPOTENCY_CONFLICT`.

## Modelo y consistencia

- Turno de caja: empresa, sucursal, cajero, apertura UTC, saldo inicial, estado y cierre UTC.
- Venta confirmada: origen de borrador único, actor/contexto, hora, lista de precios, total calculado y snapshot de renglones.
- Pagos: venta, método, importe aplicado; efectivo recibido y vuelto se conservan diferenciados para auditoría.
- Caja: libro inmutable de apertura, cobros y vuelto; cada movimiento tiene clave de origen única.
- Stock: al confirmar, la cantidad reservada pasa a egreso físico sin liberar disponibilidad entre ambas operaciones.
- Una sola transacción serializable protege la transición de borrador, stock, venta, pagos y caja. Una restricción única por borrador confirmado y claves únicas de movimientos complementan el control concurrente.

## Criterios de aceptación

- El cajero abre un turno con fondo cero o positivo y no puede abrir otro mientras el suyo siga abierto en esa sucursal.
- El cierre muestra el efectivo registrado y el total vendido; el último cierre sigue consultable por el mismo cajero en la misma sucursal después de reiniciar el POS.
- No se confirma una venta sin turno abierto, borrador real y saldo de pagos válido.
- El servidor calcula saldo/vuelto; únicamente el efectivo produce vuelto.
- Un error o conflicto deja sin cambios venta, pagos, turno, saldo de caja y stock.
- Una confirmación repetida no duplica registros y devuelve el mismo resultado; un reintento con pagos diferentes se rechaza.
- El saldo físico baja exactamente una vez y la reserva correspondiente queda en cero.
- Otro cajero o sucursal no puede leer ni operar el turno o la venta.
- El resumen final muestra total, medios/importes y vuelto, sin afirmar que se emitió comprobante fiscal.

## Estrategia de pruebas y comandos

- Integración contra PostgreSQL real para autorización/contexto, doble apertura, concurrencia, idempotencia y rollback completo.
- Pruebas de dominio para pagos combinados, saldo pendiente, sobrepago no efectivo y cálculo de vuelto.
- Angular: `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web`, `npm run build --prefix src/Carnicerias.Web`.
- Backend: `dotnet test Carnicerias.sln --configuration Release`; compilar API/solución tras cada corte.
- Revisión visual obligatoria en Electron con flujo real; no usar navegador para sustituir la revisión acordada.

## Límites

- Siempre: validar importes y formas en API, resolver identidad desde sesión, controlar autorización/propiedad, usar transacción y restricciones únicas, conservar auditoría y snapshot.
- Requiere nueva definición antes de incluir: arqueo obligatorio, diferencias de caja, cuenta corriente, descuentos/impuestos, anulaciones/devoluciones, numeración fiscal o integración con adquirentes.
- Nunca: confiar en total/vuelto del cliente, aceptar pago en tarjeta/transferencia por encima del saldo, descontar stock antes de confirmar el resto, persistir una venta del catálogo de demostración o guardar datos completos de tarjetas. Las ventas de ensayo con la lista real sí se persisten en la base no productiva.

## Pregunta abierta

- ¿El arqueo físico y la diferencia deben ser obligatorios para cerrar el turno? No bloquea el registro inicial de ventas; queda fuera de este corte.
