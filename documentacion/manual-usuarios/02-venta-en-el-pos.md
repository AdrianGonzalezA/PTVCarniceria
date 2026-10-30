# 02 · Armar una venta en el POS

## Seleccionar lista y comprobante

Con turno abierto, elegí una **lista de precios** habilitada para la sucursal. El catálogo muestra todas las categorías activas y, dentro de cada una, los artículos con precio vigente y el stock disponible. Una categoría vacía sigue visible e indica que no tiene artículos con precio vigente en la lista seleccionada. Antes de cobrar podés dejar seleccionado **Tipo de comprobante** (ticket no fiscal, ticket fiscal o factura electrónica) y **Tipo de cliente**. Los datos fiscales que correspondan se piden al cerrar la venta.

La lista queda fija en cuanto el ticket tiene renglones; para usar otra, primero hay que cancelar ese ticket. El costo estadístico del artículo no determina el precio de venta: se aplica el valor de la lista elegida.

Con el turno abierto, la caja vuelve a consultar las existencias y las listas de la sucursal cada **5 segundos**, al recuperar la conexión y al volver a enfocar Electron. Un ajuste de stock hecho desde administración aparece sin reiniciar la caja. Si la consulta de stock o listas falla, se muestra un aviso y se detienen la carga de productos y el cobro hasta sincronizar; el ticket que ya estaba armado no se borra. Usá **Reintentar** o aguardá la próxima consulta automática.

Si administración deshabilita la lista seleccionada y el ticket está vacío, la caja elige la siguiente lista habilitada y avisa el cambio. Si el ticket ya tiene productos, conserva sus renglones y precios, pero no permite continuar ni recalcularlos con otra lista: hay que cancelar esa venta y elegir una lista vigente. La confirmación final y la reserva de stock siguen siendo validadas por el servidor.

## Agregar artículos

Podés elegir una categoría, buscar por nombre/código o escribir/leer un código en **Producto / lector**. El campo vuelve a quedar disponible para nuevas lecturas durante la operación. Al elegir un artículo se abre una ventana donde se ingresa:

- **Peso en kilogramos**, hasta tres decimales, si el artículo se vende por peso. Puede escribirse manualmente o usarse **Leer balanza** cuando hay una balanza serial asignada y una lectura estable reciente.
- **Piezas físicas**, opcional para artículos por peso. Es un entero independiente de los kilogramos; si no se conoce, dejalo vacío. Una pieza trazable leída del código registra una pieza automáticamente.
- **Cantidad entera** si el artículo se vende por unidad. La modalidad y la unidad se definen en el catálogo, no al cobrar.

El POS compara la cantidad con el disponible y avisa **Stock insuficiente** antes de agregar. Si el stock cambia mientras se guarda, el servidor puede rechazar la modificación y el POS vuelve al último detalle efectivamente guardado. Esperá el estado **Borrador guardado**; ante un error usá **Reintentar**.

## Piezas trazables

Una etiqueta de pieza previamente recibida en la sucursal puede identificar el artículo y traer su peso. Al leerla, el POS presenta la pieza y los kilos de la etiqueta; ese peso queda fijo. Cada pieza ocupa **un renglón separado**, aun si dos piezas pertenecen al mismo artículo. El identificador y la cantidad física de una pieza aparecen también en el comprobante, separados del peso.

La lectura en venta **no crea stock**. La pieza debe haber ingresado antes por **Negocio → Recepción de piezas**. Un código no reconocido o una pieza ya usada se rechaza. El saldo físico se administra por artículo; la trazabilidad individual todavía tiene límites descritos en [06 · Límites](06-limites-y-problemas-frecuentes.md).

## Editar, descontar o cancelar

El detalle muestra una tabla con producto, cantidad o peso, precio e importe. Podés cambiar la cantidad de un renglón común o quitarlo con **×**. El peso de una pieza trazable no se modifica: quitá el renglón si fue una lectura equivocada. Si tu permiso lo permite, podés aplicar un **descuento del ticket en pesos** e indicar un motivo. El descuento se refleja en el total; no es un descuento individual por artículo.

**Cancelar venta** libera la reserva de ese ticket y conserva su cancelación para auditoría. Un ticket borrador reserva stock disponible, pero **no descuenta existencia física**; el egreso ocurre al confirmar la venta. No confundas cancelar el ticket con anular una venta ya confirmada.

## Trabajar con tickets A–D

Las pestañas A–D están en la barra gris debajo del catálogo, a la izquierda de **Finalizar compra**. Permiten mantener hasta cuatro ventas en curso por caja, cajero y turno. Esperá a que termine **Guardando ticket…** antes de cambiar de pestaña. Cada pestaña marcada **Guardado** se recupera desde la base de datos al reiniciar Electron, junto con su reserva. Una edición que no llegó a guardarse antes de un corte de energía no se considera recuperada.

Confirmar o cancelar una venta afecta solo el ticket activo; los otros tickets continúan separados. Para cerrar el turno hay que resolverlos todos.

Cuando el detalle esté completo y guardado, pulsá **Finalizar compra**. El procedimiento continúa en [03 · Cobros y comprobantes](03-cobros-y-comprobantes.md).

Base técnica: [borradores y stock](../../SPEC-pos-sales.md) y [piezas trazables](../../SPEC-inventory-traceability.md).
