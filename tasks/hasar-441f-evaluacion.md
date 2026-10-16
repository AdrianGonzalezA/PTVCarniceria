# Hasar SMH/P-441F en COM8 — laboratorio

Estado al 14/10/2026: el enlace físico COM8 a 9600 baudios, 8N1 y la emisión de un ticket fiscal de prueba de $1 quedaron verificados en el controlador (número 5160). El equipo está fiscalizado, no en modo entrenamiento, pero fue desafectado y se usa exclusivamente en el laboratorio. Desde Electron se cerró después una venta ficticia **no fiscal** de $2.500: la Hasar confirmó `H/I/J`, el POS mostró impresión correcta y una consulta posterior encontró el documento cerrado. También se cerró una venta ficticia **fiscal B** de $2.400 con CAE autorizado en ARCA homologación (`PV 99`, comprobante 14). Antes de abrir el ticket físico, la Hasar informó `8004` (error entre controlador y mecanismo impresor); el POS conservó la venta y dejó el reintento disponible. Una consulta posterior informó `C0B0` (falta de papel de diario y de tickets), sin documento abierto. Requiere revisión física para completar esa prueba.

## Flujo al cerrar una venta

- El POS registra la venta, pago y movimiento de stock en `carnicerias_test_visual`. Después imprime automáticamente **solo si esta caja tiene impresora asignada**; una falla de la Hasar no deshace el cobro ni vuelve a confirmarlo.
- `nonFiscalTicket`: Electron consulta estado, abre comprobante no fiscal (`H`), imprime el detalle real con líneas `I` y cierra (`J`). El controlador agrega sus leyendas no fiscales. Incluye caja, cajero, artículos, identificador de pieza, cantidad, precio, descuento, pagos, total y vuelto.
- `fiscalTicket`: primero se obtiene CAE en **ARCA homologación**. Para consumidor final con comprobante B, Electron toma IVA por renglón del snapshot de la venta, abre ticket fiscal (`@`), imprime CAE con la leyenda `ARCA HOMO SIN VALIDEZ`, artículos (`B`), descuento general (`T`) si corresponde, pagos parciales (`D`) y cierre (`E`). El número físico de la Hasar es distinto del número de homologación ARCA. La interfaz muestra el número confirmado por el controlador.
- `electronicInvoice`: mantiene la factura PDF y no envía un ticket a la Hasar. Una impresora de texto asignada puede imprimir el detalle; sin ella queda solo el PDF.
- La Hasar se asigna en **Administración → Organización → Cajas → Dispositivos**, eligiendo el COM de esa caja y 9600 baudios. No hay COM8 predeterminado. La caja puede quedar «Sin impresora» o con una impresora de texto distinta; no enviar texto libre al COM de la Hasar. El antiguo campo local `hasarPrinter` se ignora.

## Seguridad de emisión y límites

El proceso principal valida el origen IPC, los campos del ticket, importes, descuentos, IVA, formas de pago y capacidad de cinco pagos **antes** de abrir el documento. Consulta papel, fallas y documento abierto. Comprueba ACK, estado de respuesta, BCC, número fiscal y cierre. Ante una falla con documento abierto intenta cancelarlo; ante un resultado incierto no envía un segundo ticket.

Cada intento de venta deja una marca por `saleId` en el perfil local de Electron (`hasar-sale-prints`). Un cierre confirmado queda registrado allí y otra invocación devuelve el mismo resultado sin reimprimir. Si el resultado quedó incierto, el mismo `saleId` queda pendiente para conciliación manual. Esta marca es local a la caja, no un historial central de impresión en PostgreSQL. No se guardan secretos en esos archivos.

Por ahora, el comando físico de ticket fiscal `T` se usa sólo para **consumidor final B**; otros receptores o factura A requieren implementar tique-factura `A/B` y `SetCustomerData`, y se rechazan antes de tocar el COM asignado. Una venta puede quedar registrada con su CAE aunque la impresión no se complete; la UI lo informa. Antes de habilitar un despliegue operativo faltan persistencia central del resultado y conciliación guiada.

## Diagnóstico y verificaciones

Desde `src/Carnicerias.Pos`:

```powershell
npm run hasar:status
npm run electron:test
```

`hasar:status` es **solo lectura**. El comando `hasar:lab-ticket -- --confirm-fiscalized` todavía existe como diagnóstico de bajo nivel, pero emite un ticket físico adicional de $1 y **no** prueba el cierre de una venta; no usarlo para la aceptación funcional. Las pruebas automatizadas usan puertos simulados, nunca COM8. Para completar la aceptación: asignar Hasar COM8 a esa caja desde Administración, resolver la falta de papel `C0B0` (y `8004` si reaparece), usar **Reintentar impresión** sobre la venta fiscal B que permanece abierta en Electron y cotejar en papel artículos, cantidad, pago, total, CAE de homologación y número Hasar. No repetir la venta ni el cobro. Si se cierra Electron antes de ese reintento, todavía falta una pantalla para recuperar comprobantes confirmados pendientes de impresión; la marca local de idempotencia por sí sola no provee esa recuperación. No ejecutar pruebas de integración que creen otra base.

Fuentes: [manual de comandos Hasar DSB-MCMD-2013AR025-EFH/00](https://es.scribd.com/document/449468037/Manual-de-Comandos-Tiquedoras), secciones 3.4, 3.5 y 3.7; [repositorio técnico de Hasar](https://github.com/grupohasar/HasarFiscalDriver). No se envían comandos de inicialización, cambio de configuración fiscal ni cierre Z.
