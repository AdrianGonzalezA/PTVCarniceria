# 03 · Cobrar y entregar un comprobante

## Cerrar el ticket

Con el borrador guardado, pulsá **Finalizar compra**. En **Cobrar venta** revisá el total, el comprobante solicitado y el tipo de cliente. Podés combinar medios de pago: **efectivo, débito, crédito, transferencia, Mercado Pago y cheque**. Solo el efectivo admite un importe mayor al saldo y genera vuelto; los demás importes no pueden superar lo adeudado.

Débito, crédito, transferencia y cheque son hoy **registros manuales del medio declarado**; el POS no verifica esas operaciones con bancos o adquirentes. Mercado Pago sí exige una orden acreditada y verificada por la API. El cheque todavía no tiene cartera, depósito ni rechazo gestionados en la aplicación.

## Mercado Pago: QR o Point

1. Marcá **Mercado Pago** y verificá el importe asignado a ese medio.
2. Elegí **QR dinámico** o **Terminal Point** y pulsá **Solicitar cobro**. QR presenta un código en pantalla; Point envía la orden a la terminal vinculada a la caja.
3. Esperá a que la orden figure **Acreditado**. Mientras el cierre permanezca abierto, el POS consulta automáticamente cada 5 segundos. **Consultar pago** permite verificar manualmente si hace falta.
4. Solo entonces pulsá **Confirmar venta**. La acreditación externa por sí sola **no cierra** el ticket ni descuenta stock.

Si la orden sigue **Pendiente**, no vuelvas a cobrar por otro medio ni generes otra orden para el mismo ticket sin aclarar su estado. **Cancelar orden** se usa únicamente para una orden no acreditada; un resultado incierto requiere nueva consulta. Si se cierra la ventana de cobro, la consulta automática se detiene; al volver a abrir el ticket se recupera la orden y se consulta nuevamente.

## Cuenta corriente y saldo a favor

Cuando el cajero tiene permiso y el cliente está activo con **cuenta corriente habilitada**, puede buscarlo en la ventana de cobro. Allí puede dejar **todo o parte** del ticket a cuenta y/o aplicar parcial o totalmente un saldo a favor existente. El importe cargado a cuenta **no entra a caja**; el saldo a favor aplicado **no ingresa por segunda vez**. El resto debe cubrirse con los medios de pago elegidos.

Para cobrar deuda fuera de una venta, pulsá **Cobrar cuenta** en la cabecera del POS. Elegí cliente, importe y medio; la propuesta aplica primero a ventas más antiguas, pero podés activar **Asignar importes a ventas manualmente**. Un excedente queda como saldo a favor. Al confirmar se emite un **recibo interno no fiscal**; no es una factura. Si el resultado de red es incierto, usá **Reintentar cobro** sobre la misma operación, no cargues un cobro nuevo.

## Tipo de comprobante y datos del receptor

El tipo se selecciona en la pantalla principal del ticket: **Ticket no fiscal**, **Ticket fiscal** o **Factura electrónica**. También se elige **Consumidor final**, **Responsable inscripto**, **Monotributista** o **Exento**. Para comprobantes fiscales, la ventana de cobro solicita nombre/razón social, DNI o CUIT y domicilio según la condición elegida y valida los campos requeridos.

Al pulsar **Confirmar venta**, primero se registra la venta con pagos y egreso de stock. Si se pidió un comprobante fiscal, después se consulta **ARCA homologación**. Solo cuando la pantalla informa CAE autorizado se habilita la salida fiscal de prueba. Si ARCA falla, **la venta ya quedó registrada**: usá **Consultar ARCA / reintentar**; no repitas la venta ni el cobro.

La instalación actual es de prueba: los comprobantes fiscales se identifican como **sin validez fiscal**. No entregar esas salidas como facturas productivas.

## Imprimir o guardar

En **Venta confirmada** podés usar **Imprimir en COM1** o **Guardar ticket PDF / Guardar factura PDF**, según el comprobante. La impresión serial requiere el dispositivo configurado en ese puerto; el PDF se guarda en el equipo del puesto. Un fallo de impresión o guardado **no repite ni deshace** la venta. Verificá el comprobante antes de reenviarlo para evitar duplicar papel.

Base técnica: [cobro y turno](../../SPEC-pos-checkout.md), [Mercado Pago](../../SPEC-mercado-pago.md), [cuenta corriente](../../SPEC-customers-credit.md) y [dispositivos](../../SPEC-device-emulation.md).
