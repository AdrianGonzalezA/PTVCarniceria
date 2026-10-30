# 03 · Cobrar y entregar un comprobante

## Cerrar el ticket

Con el borrador guardado, pulsá **Finalizar compra**. En **Cobrar venta** revisá el total, el comprobante solicitado y el tipo de cliente. Podés combinar medios de pago: **efectivo, débito, crédito, transferencia, Mercado Pago y cheque**. Solo el efectivo admite un importe mayor al saldo y genera vuelto; los demás importes no pueden superar lo adeudado.

Débito, crédito, transferencia y cheque son hoy **registros manuales del medio declarado**; el POS no verifica esas operaciones con bancos o adquirentes. Mercado Pago sí exige una orden acreditada y verificada por la API. El cheque todavía no tiene cartera, depósito ni rechazo gestionados en la aplicación.

## Mercado Pago: QR dinámico, QR fijo o Point

1. Marcá **Mercado Pago** y verificá el importe asignado a ese medio.
2. Elegí **QR dinámico**, **QR fijo del puesto** o **Terminal Point** y pulsá **Solicitar cobro**. El dinámico genera un QR para esta venta; el fijo reutiliza el QR de la caja, pero crea una orden por el importe de esta venta; Point envía la orden a la terminal vinculada.
3. Esperá a que la orden figure **Acreditado**. Mientras el cierre permanezca abierto, el POS consulta automáticamente cada 5 segundos. **Consultar pago** permite verificar manualmente si hace falta.
4. Solo entonces pulsá **Confirmar venta**. La acreditación externa por sí sola **no cierra** el ticket ni descuenta stock.

Si la orden sigue **Pendiente**, no vuelvas a cobrar por otro medio ni generes otra orden para el mismo ticket sin aclarar su estado. **Cancelar orden** se usa únicamente para una orden no acreditada; un resultado incierto requiere nueva consulta. Si se cierra la ventana de cobro, la consulta automática se detiene; al volver a abrir el ticket se recupera la orden y se consulta nuevamente.

Mercado Pago aparece solo si la caja y el turno actuales tienen QR o Point configurado; el selector muestra únicamente esos modos. El QR fijo requiere que el `external_id` de la caja corresponda a un puesto QR activo e integrado de Mercado Pago; no es el ID numérico del puesto. Si falla la consulta de pasarelas, podés seguir con medios manuales, pero no iniciar un cobro integrado nuevo. Una orden previa permanece visible para consultar o cancelar aunque la configuración de la caja haya cambiado; no se puede quitar mientras esté activa o incierta. Clover sandbox puede probarse desde Administración, pero aún no se ofrece para cobrar en el POS sin terminal y confirmación del pago.

## Cuenta corriente y saldo a favor

Cuando el cajero tiene permiso y el cliente está activo con **cuenta corriente habilitada**, puede buscarlo en la ventana de cobro. Allí puede dejar **todo o parte** del ticket a cuenta y/o aplicar parcial o totalmente un saldo a favor existente. El importe cargado a cuenta **no entra a caja**; el saldo a favor aplicado **no ingresa por segunda vez**. El resto debe cubrirse con los medios de pago elegidos.

Para cobrar deuda fuera de una venta, pulsá **Cobrar cuenta** en la cabecera del POS. Elegí cliente, importe y uno o varios medios con sus importes. Se usan los mismos medios de la venta; solo el efectivo puede generar vuelto. La propuesta aplica primero a ventas más antiguas, pero podés activar **Asignar importes a ventas manualmente**. Un excedente del importe cobrado frente a la deuda queda como saldo a favor. Al confirmar se emite un **recibo interno no fiscal** con el desglose de medios; no es una factura. Si el resultado de red es incierto, usá **Reintentar cobro** sobre la misma operación, no cargues un cobro nuevo.

Para usar **Mercado Pago** en una cobranza, asignale un importe, elegí uno de los modos habilitados para esa caja y pulsá **Solicitar cobro**. Esperá **Acreditado** antes de confirmar el recibo; **Consultar pago** verifica el estado y la ventana lo consulta automáticamente cada 5 segundos. Si se cierra Electron, al volver a **Cobrar cuenta** se recupera la operación pendiente. Una orden pendiente o con cancelación incierta no se puede reemplazar por otro medio dentro de esa operación. Si Mercado Pago no confirma la cancelación, pulsá **Dejar pendiente y seguir**: la caja conserva la orden en **Mercado Pago · por conciliar**, sin generar recibo, y permite atender a otro cliente. **Retomar** vuelve a consultar la orden. No cobres de nuevo a ese mismo cliente hasta aclarar el pago; la caja bloquea otra cobranza de cuenta a su nombre en ese perfil. Esta lista depende del perfil local de Electron y del turno; la conciliación entre turnos/equipos está pendiente. Débito, crédito, transferencia y cheque siguen siendo registros manuales; el POS no consulta a sus proveedores.

## Tipo de comprobante y datos del receptor

El tipo se selecciona en la pantalla principal del ticket: **Ticket no fiscal**, **Ticket fiscal** o **Factura electrónica**. También se elige **Consumidor final**, **Responsable inscripto**, **Monotributista** o **Exento**. Para comprobantes fiscales, la ventana de cobro permite buscar un cliente con ficha fiscal; completa nombre/razón social, DNI o CUIT, domicilio, localidad y provincia. Podés corregir esos datos para la venta y añadir un número de pedido u orden de compra. El cierre valida los campos requeridos y guarda una copia de los datos fiscales en la venta para que futuros cambios del cliente no alteren el comprobante emitido.

Al pulsar **Confirmar venta**, primero se registra la venta con pagos y egreso de stock. Si se pidió un comprobante fiscal, después se consulta **ARCA homologación**. Solo cuando la pantalla informa CAE autorizado se habilita la salida fiscal de prueba. Si ARCA falla, **la venta ya quedó registrada**: usá **Consultar ARCA / reintentar**; no repitas la venta ni el cobro.

La instalación actual es de prueba: los comprobantes fiscales se identifican como **sin validez fiscal**. No entregar esas salidas como facturas productivas.

## Imprimir o guardar

Al cerrar una venta, el comprobante se envía automáticamente **solo si la caja tiene una impresora asignada**. La comandera ESC/POS de 58 u 80 mm puede imprimir ticket no fiscal, ticket fiscal de homologación o factura electrónica con CAE y QR después de la autorización de ARCA. La impresora de texto serial entrega un ticket textual. La Hasar SMH/P-441F de laboratorio imprime ticket no fiscal y, tras el CAE, ticket fiscal B para consumidor final; no recibe facturas electrónicas. En **Venta confirmada**, pulsá **Descargar comprobante** para guardar el PDF en Descargas; los comprobantes fiscales requieren autorización previa. La pantalla confirma la descarga sin mostrar la ruta. Una falla de impresión o descarga **no repite ni deshace** la venta; ante un resultado incierto, revisá el papel y el estado del equipo antes de cualquier nueva acción.

Base técnica: [cobro y turno](../../SPEC-pos-checkout.md), [Mercado Pago](../../SPEC-mercado-pago.md), [cuenta corriente](../../SPEC-customers-credit.md) y [dispositivos](../../SPEC-device-emulation.md).
