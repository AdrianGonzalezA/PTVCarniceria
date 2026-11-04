# I03b — Cobros Mercado Pago, reintento y conciliación

**Fecha:** 04/11/2026. **Base de la prueba HTTP:** exclusivamente `carnicerias_test_visual` en el contenedor existente `carnicerias-pos-test-20261007` (`127.0.0.1:55433`). El proveedor Mercado Pago se sustituyó por un servidor HTTP simulado dentro del proceso de prueba; no se enviaron cargos al sandbox real. No hubo migración I03b.

## Cambio implementado

`PointPaymentIntent.TryClaimProviderCreate` registra `Pending` antes de salir de la transacción serializable que bloquea el borrador. Una segunda petición sobre el mismo instrumento no puede emitir simultáneamente otra orden. Si la respuesta se pierde, el reintento conserva el `IdempotencyKey` original y consulta `ExternalReference` antes de repetir el POST. Un intento sin orden identificada que superó 23 horas requiere conciliación; no se genera una clave nueva silenciosamente. `SaleCheckout.LinkMercadoPago` acepta la vinculación ya existente en estado incierto.

Este límite conservador respeta la [clave idempotente requerida para crear órdenes Point](https://www.mercadopago.com.ar/developers/es/reference/in-person-payments/point/orders/create-order/post), la [reutilización de clave en órdenes QR](https://www.mercadopago.com.ar/developers/es/docs/qr-code/resources/migrate-dynamic-qr-model-to-orders) y la [búsqueda de órdenes](https://www.mercadopago.com.ar/developers/es/reference/online-payments/checkout-pro/search-orders/get). La confirmación del estado del proveedor se hace por consulta; las [notificaciones QR pueden ser asíncronas](https://www.mercadopago.com.ar/developers/es/docs/qr-code/notifications?scope=prod).

## Resultados

| Escenario HTTP sobre fixture `I03B-...` | Resultado |
| --- | --- |
| Dos conexiones ejecutan el mismo instrumento mientras el proveedor retiene la primera respuesta | Una creación HTTP; una respuesta aceptada y otra `409`. Ninguna venta antes de acreditación. |
| La primera venta se acredita y se confirma dos veces | Una venta, un movimiento de cobro y stock `10 → 8`, reserva final cero. |
| El proveedor crea la orden, pero se pierde la respuesta | Estado `Uncertain`; el reintento encuentra la orden por referencia y acredita sin segundo POST. Segunda venta única; stock `8 → 7`. |
| Dos operaciones MP, una acreditada y otra rechazada | El aprobado de $100 persiste, el saldo pendiente es $100; una transferencia de reemplazo completa el plan. Tercera venta; stock `7 → 5`. |
| Auditoría y aislamiento | Eventos `Uncertain`, `Approved` y `Rejected` persistidos. Tres ventas y cuatro movimientos de cobro solo dentro del fixture. Limpieza limitada a la empresa de prueba. |

`tools/Run-I03AVisualTest.ps1 -MercadoPago`: **1 prueba HTTP aprobada**, con varios recorridos; 41 pruebas .NET focalizadas aprobadas y una omitida por configuración; 104 pruebas de arquitectura aprobadas. `tools/sql/20261104_i03a_postcheck_readonly.sql` posterior: **58 migraciones**, 101 borradores, 80 ventas, 26 intentos MP y 84 pagos; cero procesos/instrumentos/eventos I03, cero empresas `I03A-...` o `I03B-...` residuales. No se alteraron operaciones comerciales anteriores.

## Pendientes de homologación

1. Ejecutar cobros reales en Mercado Pago sandbox, incluidos webhook tardío, timeout y conciliación. El proveedor simulado prueba el contrato de nuestra API y el control de concurrencia, no el comportamiento externo real.
2. Recorrer el cobro y la recuperación en dos cajas Electron. Conservar las comprobaciones operativas pendientes de I02, I04 e I05.
3. Definir el procedimiento administrativo para un intento sin orden identificada cuando ya venció la ventana segura de reintento. No cerrar ni volver a cobrar la venta automáticamente.

El respaldo anterior a I03a está en `.local-backups/i03a-before-20261104-080901-37f95ba0.dump`. La reversión de código necesita conciliar los pagos pendientes; no ejecutar `Down` con procesos existentes.
