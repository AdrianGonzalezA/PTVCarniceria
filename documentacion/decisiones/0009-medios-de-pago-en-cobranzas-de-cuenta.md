# ADR 0009: medios combinados en cobranzas de cuenta corriente

**Estado:** aceptada e implementada el 27/10/2026.

## Contexto

El cierre de una venta permite combinar efectivo, débito, crédito, transferencia, cheque y Mercado Pago. La cobranza de cuenta corriente recibía un solo medio; seleccionar `mercadoPago` solo lo asentaba como texto, sin crear ni verificar una orden en la pasarela. Esto permitía registrar como acreditado un importe que Mercado Pago no había confirmado.

## Decisión

- La cobranza reutiliza la regla de distribución de pagos de la venta. El cajero asigna importes a varios medios; solo el efectivo puede exceder el importe a cobrar y generar vuelto. El recibo conserva por medio el importe entregado y el aplicado. El libro de caja recibe un movimiento por medio y, si corresponde, otro de tipo **vuelto de cuenta**. El turno y el resumen comercial calculan las cobranzas netas de ese vuelto sin mezclarlo con el vuelto de una venta.
- Una cobranza con Mercado Pago crea una intención vinculada a **empresa, sucursal, cliente, operación de cobranza, turno, cajero, caja e importe**. La misma API de órdenes que usa la venta ofrece QR dinámico, QR fijo y Point según la configuración de la caja. El recibo solo se confirma cuando la orden está acreditada y esos datos coinciden. La intención acreditada se vincula una sola vez al recibo.
- El POS conserva localmente el identificador de operación y el reparto de medios antes de enviar la orden. Al reabrir Electron consulta primero si ya existe el recibo y luego recupera la orden para continuar la consulta; no inicia otro cobro automáticamente. Los reintentos de confirmación usan la misma clave idempotente.
- Una orden sin cancelación confirmada puede quedar **por conciliar** en el mismo perfil Electron: se guarda el borrador completo y se abre una operación nueva para otro cliente. Retomarla consulta su estado y conserva la identidad de operación. No registra recibo al posponerla, ni permite iniciar otro cobro de cuenta al mismo cliente en ese perfil. Es una salida para seguir atendiendo; la conciliación automática entre turnos y equipos aún requiere almacenamiento y bandeja en el servidor.
- La reasignación contable de un recibo conserva su desglose y no vuelve a ingresar dinero. La reversión de un recibo con Mercado Pago se bloquea hasta que exista un flujo de devolución confirmado por la pasarela; anular solo el asiento local dejaría el pago externo vigente.

## Consecuencias

`POST /api/customers/{customerId}/collections` acepta `payments: [{method, amount}]` y, cuando corresponde, `mercadoPagoIntentId`; el contrato anterior con `method` sigue aceptándose para reintentos históricos. La respuesta agrega `payments` y `changeAmount`. Las órdenes de cobranza usan `/api/customers/collections/operations/{operationId}/mercado-pago` con `GET`, `POST`, `POST /{intentId}/check` y `POST /{intentId}/cancel`. Todos exigen el permiso `pos.account.charge` y el contexto de caja; las operaciones de venta mantienen sus rutas previas.

Las migraciones `AddCustomerCollectionPaymentMethods` y `AddAccountCollectionChangeMovement` agregan `customers_credit.collection_payments`, conservan los recibos existentes con un registro de su medio original, admiten intenciones de Mercado Pago propias de una cobranza y distinguen el vuelto de cuenta en el libro de caja. Se aplicaron solo a `carnicerias_test_visual`; no se creó otra base. Las pruebas unitarias y de frontend no crean bases; la verificación manual del POS se hace en Electron.

En el sandbox, una orden QR de cobranza se creó y pudo consultarse con estado proveedor `created`, pero Mercado Pago respondió HTTP 404 al solicitar su cancelación el 27/10/2026. La [referencia oficial de cancelación QR](https://www.mercadopago.com.ar/developers/es/reference/in-person-payments/qr-code/orders/cancel-order/post) indica que `created` es cancelable. Se ajustó la solicitud para enviar el `Content-Type` de su ejemplo y la respuesta siguió siendo 404. La API no la marca cancelada por su cuenta. La acreditación de una cobranza mediante comprador de prueba queda por verificar con una persona que pueda completar el pago.
