# Emulación externa de impresora y balanza (desarrollo)

## Objetivo

Probar desde el POS en Electron un circuito físico sin hardware: una aplicación separada simula la balanza y la impresora. El cajero puede leer un peso estable para un artículo vendido por kilo y enviar una venta ya confirmada a la impresora virtual. El simulador muestra lo recibido y guarda un archivo de ticket; no emite un comprobante fiscal ni modifica ventas o stock.

## Decisión y alcance

- La primera conexión usa un *named pipe* local de Windows entre dos procesos. La interfaz del POS se limita a `leerPeso` y `imprimirTicket`; luego se podrá sustituir el transporte por USB/serie o un controlador de impresora sin alterar las reglas de venta.
- La balanza responde con kilos decimales positivos de hasta tres posiciones y un estado de estabilidad. Sólo un peso estable y válido completa el campo manual; nunca reemplaza el peso fijo de una pieza trazable.
- La impresora recibe el mismo detalle validado que el PDF: artículo, pieza cuando corresponda, cantidad, precio, pagos, total, caja y fecha. Cada envío devuelve acuse o error. El simulador guarda una copia HTML no fiscal fuera de PostgreSQL y la muestra en una ventana independiente de Electron.
- El simulador es exclusivo de desarrollo. El servidor PostgreSQL y las API de venta no conocen el transporte. No se escucha en red ni se exponen credenciales de terminal. Una falla de dispositivo no deshace una venta confirmada.
- El PDF existente se conserva como salida alternativa. Su puente nativo debe funcionar con `sandbox: true` y `contextIsolation: true`.

## Contrato y seguridad

- Petición/respuesta JSON delimitada por salto de línea, con límite de tamaño, tiempo máximo de espera y acciones permitidas `read-scale` y `print-receipt`.
- Electron valida el origen del IPC y el contenido del ticket antes de escribir o enviar. El emulador valida nuevamente los datos externos y nunca acepta rutas de archivo del cliente.
- El archivo del ticket se genera en el directorio de datos del emulador con un nombre interno; no se confunde con una factura fiscal ni se envía a ARCA.
- No se usa el renderer para abrir sockets, acceder a archivos o invocar canales IPC arbitrarios.

## Verificación

1. Sin emulador, la lectura de balanza y el envío de impresión informan conexión ausente sin cambiar el ticket ni la venta.
2. Con emulador abierto, inyectar `0,750 kg` estable y leerlo desde el diálogo de un producto por peso; no permitirlo en una pieza de etiqueta fija o artículo por unidad.
3. Confirmar una venta, enviar el ticket al emulador y comprobar acuse, archivo no fiscal y renglones individuales.
4. Reiniciar el POS no duplica la venta ni borra los tickets ya impresos. Reintentar una impresión es explícito.

## Pendiente

Controladores reales USB/serial, protocolo específico de balanza, cola durable de impresión, selección/configuración por caja, impresora fiscal y ARCA. Estos requieren datos del hardware y reglas de implementación.
