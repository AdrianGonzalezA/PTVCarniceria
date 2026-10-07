# Especificación: borradores de venta del POS

## Objetivo y alcance del primer corte

Guardar automáticamente el único ticket activo habilitado en el POS, recuperarlo al volver a abrir Electron y permitir cancelarlo explícitamente. Una venta real debe provenir del catálogo del servidor y de una lista habilitada para la sucursal. Los renglones congelan descripción, unidad, modalidad, cantidad y precio aplicado. Este contrato trata el borrador; el cobro y el egreso de stock ya implementados se describen en `SPEC-pos-checkout.md`. No hay facturación fiscal.

## Contrato y reglas

- La empresa, sucursal y usuario se obtienen de la sesión autorizada; nunca del body del cliente.
- El servidor valida productos activos, modalidad y cantidades, lista asignada a sucursal y precio vigente. Ignora precios enviados por el cliente.
- Un ticket en borrador no descuenta stock. El egreso ocurre al confirmar la venta, en la misma transacción que venta, pagos y caja.
- El ticket sí reserva cantidades disponibles en la sucursal. La reserva se actualiza atómicamente con los renglones; quitar líneas o cancelar libera exactamente esas cantidades.
- Guardar una lista distinta a la ya asociada al borrador devuelve conflicto; primero se debe cancelar.
- El ticket de demostración no se persiste ni modifica existencias.
- En este incremento se admite un borrador por usuario y sucursal. Persistencia por terminal y varios tickets requieren resolver e implementar identidad de terminal.

## API

- `GET /api/sales/draft`: obtiene el borrador del usuario y contexto actuales, o `204` si no existe.
- `PUT /api/sales/draft`: reemplaza las cantidades de hasta 100 productos; los precios se consultan en el servidor dentro de la lista elegida.
- `DELETE /api/sales/draft`: cancela el borrador, conserva el detalle para auditoría y libera la reserva.
- `GET /api/inventory/stock`: consulta existencia física, reservada y disponible por producto en la sucursal activa.
- `POST /api/inventory/adjustments`: aplica un ingreso/ajuste con identificador idempotente y motivo; requiere `inventory.stock.manage`.

Todas las rutas requieren sesión autenticada y contexto operativo vigente. El borrador y cada renglón quedan ligados a empresa; la sucursal y usuario quedan ligados al encabezado. Restricciones de clave foránea protegen la integridad del catálogo.

## Stock y relación con el cobro

Este primer corte controla stock agregado por sucursal (los correlativos individuales quedan fuera). Los saldos tienen un libro de movimientos y los ajustes/ingresos requieren `inventory.stock.manage`; se asigna al rol administrador inicial. La API bloquea todo ajuste que reduzca el saldo por debajo de reservas. El borrador reserva disponible con una actualización atómica y rechaza falta de stock; quitar/cancelar libera la reserva. La confirmación convierte la reserva en egreso dentro de una sola transacción con venta, pagos y caja. El criterio recomendado por `REQUERIMIENTOS_MVP.md` es bloquear la venta por falta de stock salvo excepción autorizada y auditada.

## Criterios de aceptación

- El ticket real se recupera después de recargar o reiniciar Electron.
- Las rutas Angular `/pos` y `/users` vuelven a cargar `index.html` en el protocolo `app://bundle`, sin ampliar el acceso a archivos empaquetados ni a otras rutas.
- Un usuario no puede consultar ni modificar tickets de otra empresa, sucursal o usuario.
- Cambios de precio en catálogo no modifican los precios ya guardados en el borrador.
- Los productos inactivos, precios vencidos, cantidades inválidas y listas no asignadas se rechazan.
- Cancelar conserva el ticket con fecha/usuario de cancelación y libera la reserva sin generar egreso físico.
- El flujo de demostración sigue explícitamente separado del registro real.
