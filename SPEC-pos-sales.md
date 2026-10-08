# Especificación: borradores de venta del POS

## Objetivo y alcance

Guardar automáticamente hasta cuatro tickets A–D por caja y turno, alternar entre ellos sin mezclar ventas y recuperarlos al volver a abrir Electron. Cada ticket puede cancelarse explícitamente. Una venta real debe provenir del catálogo del servidor y de una lista habilitada para la sucursal. Los renglones congelan descripción, unidad, modalidad, cantidad y precio aplicado. Este contrato trata el borrador; el cobro y el egreso de stock se describen en `SPEC-pos-checkout.md`. No hay facturación fiscal.

## Contrato y reglas

- La empresa, sucursal y usuario se obtienen de la sesión autorizada; nunca del body del cliente.
- El servidor valida productos activos, modalidad y cantidades, lista asignada a sucursal y precio vigente. Ignora precios enviados por el cliente.
- Un ticket en borrador no descuenta stock. El egreso ocurre al confirmar la venta, en la misma transacción que venta, pagos y caja.
- El ticket sí reserva cantidades disponibles en la sucursal. La reserva se actualiza atómicamente con los renglones; quitar líneas o cancelar libera exactamente esas cantidades.
- El POS muestra la unidad de venta definida en el catálogo y advierte al ingresar una cantidad superior al disponible antes de agregar o aumentar un renglón. Al editar un renglón ya guardado, su propia reserva cuenta dentro del máximo permitido. El servidor sigue siendo la autoridad: si el stock cambia entre la consulta y el guardado, rechaza la operación y el POS vuelve al último detalle persistido, sin presentar como agregado el renglón rechazado.
- Guardar una lista distinta a la ya asociada al borrador devuelve conflicto; primero se debe cancelar.
- El ticket de demostración no se persiste ni modifica existencias.
- Los tickets A–D se identifican en servidor por una ranura inmutable. Cada borrador pertenece a empresa, sucursal, caja, cajero y turno; cada ranura admite como máximo un borrador activo para ese contexto. Las ranuras vacías no reservan stock ni crean borradores.
- El cambio de pestaña solo se admite cuando el último cambio del ticket actual terminó de guardarse. Ante un error de guardado se conserva el detalle visible y se exige reintentar o resolver el error antes de cambiar. Tras reiniciar Electron se recupera el último estado confirmado por PostgreSQL, no una edición que nunca llegó al servidor.
- Confirmar o cancelar actúa solo sobre la ranura activa. Las otras ventas y sus reservas no cambian. El cierre de turno sigue bloqueado mientras exista cualquier borrador activo de A–D.

## API

- `GET /api/sales/drafts`: devuelve los borradores activos A–D del turno/caja/cajero autorizados; las ranuras vacías no se incluyen.
- `GET /api/sales/drafts/{slot}`: obtiene el borrador de A, B, C o D, o `204` si la ranura está vacía.
- `PUT /api/sales/drafts/{slot}`: reemplaza hasta 100 renglones solo en esa ranura; los precios se consultan en el servidor dentro de la lista elegida.
- `DELETE /api/sales/drafts/{slot}`: cancela únicamente ese borrador, conserva el detalle para auditoría y libera su reserva.
- Las rutas heredadas `/api/sales/draft` siguen representando A para no romper clientes existentes durante la transición.
- `GET /api/inventory/stock`: consulta existencia física, reservada y disponible por producto en la sucursal activa.
- `POST /api/inventory/adjustments`: aplica un ingreso/ajuste con identificador idempotente y motivo; requiere `inventory.stock.manage`.

Todas las rutas requieren sesión autenticada y contexto operativo vigente. El borrador y cada renglón quedan ligados a empresa; la sucursal y usuario quedan ligados al encabezado. Restricciones de clave foránea protegen la integridad del catálogo.

## Stock y relación con el cobro

Este primer corte controla stock agregado por sucursal (los correlativos individuales quedan fuera). Los saldos tienen un libro de movimientos y los ajustes/ingresos requieren `inventory.stock.manage`; se asigna al rol administrador inicial. La API bloquea todo ajuste que reduzca el saldo por debajo de reservas. El borrador reserva disponible con una actualización atómica y rechaza falta de stock; quitar/cancelar libera la reserva. La confirmación convierte la reserva en egreso dentro de una sola transacción con venta, pagos y caja. El criterio recomendado por `REQUERIMIENTOS_MVP.md` es bloquear la venta por falta de stock salvo excepción autorizada y auditada.

## Criterios de aceptación

- El ticket real se recupera después de recargar o reiniciar Electron.
- Se pueden armar cuatro ventas A–D, cambiar entre ellas sin perder lista, detalle ni reserva, y recuperarlas después de reiniciar Electron. Cobrar o cancelar B no modifica A, C ni D.
- Las rutas Angular `/pos` y `/users` vuelven a cargar `index.html` en el protocolo `app://bundle`, sin ampliar el acceso a archivos empaquetados ni a otras rutas.
- Un usuario no puede consultar ni modificar tickets de otra empresa, sucursal o usuario.
- Cambios de precio en catálogo no modifican los precios ya guardados en el borrador.
- Los productos inactivos, precios vencidos, cantidades inválidas y listas no asignadas se rechazan.
- No se agrega un producto sin stock ni una cantidad superior al disponible; los productos por unidad admiten solo enteros y los vendidos por peso hasta tres decimales.
- Cancelar conserva el ticket con fecha/usuario de cancelación y libera la reserva sin generar egreso físico.
- El flujo de demostración sigue explícitamente separado del registro real.
