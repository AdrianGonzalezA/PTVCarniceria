# 02 · Armar una venta en el POS

## Seleccionar lista y comprobante

Con turno abierto, elegí una **lista de precios** habilitada para la sucursal. El catálogo muestra los artículos con precio vigente y el stock disponible. Antes de cobrar podés dejar seleccionado **Tipo de comprobante** (ticket no fiscal, ticket fiscal o factura electrónica) y **Tipo de cliente**. Los datos fiscales que correspondan se piden al cerrar la venta.

La lista queda fija en cuanto el ticket tiene renglones; para usar otra, primero hay que cancelar ese ticket. El costo estadístico del artículo no determina el precio de venta: se aplica el valor de la lista elegida.

## Agregar artículos

Podés elegir una categoría, buscar por nombre/código o escribir/leer un código en **Producto / lector**. El campo vuelve a quedar disponible para nuevas lecturas durante la operación. Al elegir un artículo se abre una ventana donde se ingresa:

- **Peso en kilogramos**, hasta tres decimales, si el artículo se vende por peso. Puede escribirse manualmente o usarse **Leer balanza COM6** cuando hay una balanza serial configurada y una lectura estable reciente.
- **Cantidad entera** si el artículo se vende por unidad. La modalidad y la unidad se definen en el catálogo, no al cobrar.

El POS compara la cantidad con el disponible y avisa **Stock insuficiente** antes de agregar. Si el stock cambia mientras se guarda, el servidor puede rechazar la modificación y el POS vuelve al último detalle efectivamente guardado. Esperá el estado **Borrador guardado**; ante un error usá **Reintentar**.

## Piezas trazables

Una etiqueta de pieza previamente recibida en la sucursal puede identificar el artículo y traer su peso. Al leerla, el POS presenta la pieza y los kilos de la etiqueta; ese peso queda fijo. Cada pieza ocupa **un renglón separado**, aun si dos piezas pertenecen al mismo artículo. El identificador de pieza aparece también en el detalle del comprobante de prueba.

La lectura en venta **no crea stock**. La pieza debe haber ingresado antes por **Negocio → Recepción de piezas**. Un código no reconocido o una pieza ya usada se rechaza. El saldo físico se administra por artículo; la trazabilidad individual todavía tiene límites descritos en [06 · Límites](06-limites-y-problemas-frecuentes.md).

## Editar, descontar o cancelar

En el detalle podés cambiar la cantidad de un renglón común o quitarlo con **×**. El peso de una pieza trazable no se modifica: quitá el renglón si fue una lectura equivocada. Si tu permiso lo permite, podés aplicar un **descuento del ticket en pesos** e indicar un motivo. El descuento se refleja en el total; no es un descuento individual por artículo.

**Cancelar venta** libera la reserva de ese ticket y conserva su cancelación para auditoría. Un ticket borrador reserva stock disponible, pero **no descuenta existencia física**; el egreso ocurre al confirmar la venta. No confundas cancelar el ticket con anular una venta ya confirmada.

## Trabajar con tickets A–D

Las pestañas inferiores permiten mantener hasta cuatro ventas en curso por caja, cajero y turno. Esperá a que termine **Guardando ticket…** antes de cambiar de pestaña. Cada pestaña marcada **Guardado** se recupera desde la base de datos al reiniciar Electron, junto con su reserva. Una edición que no llegó a guardarse antes de un corte de energía no se considera recuperada.

Confirmar o cancelar una venta afecta solo el ticket activo; los otros tickets continúan separados. Para cerrar el turno hay que resolverlos todos.

Cuando el detalle esté completo y guardado, pulsá **Finalizar compra**. El procedimiento continúa en [03 · Cobros y comprobantes](03-cobros-y-comprobantes.md).

Base técnica: [borradores y stock](../../SPEC-pos-sales.md) y [piezas trazables](../../SPEC-inventory-traceability.md).
