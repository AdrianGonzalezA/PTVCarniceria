# Balanza e impresión serial (desarrollo)

## Objetivo

Probar desde el POS en Electron la recepción serial de una balanza y el envío serial de tickets. PuTTY envía el peso por COM5 y el POS escucha COM6; el POS transmite tickets por COM1 y PuTTY los recibe por COM2. Ninguna salida emite un comprobante fiscal ni modifica ventas o stock.

## Decisión y alcance

- El proceso principal del POS mantiene COM6 abierto a 9600 baudios, 8N1, sin control de flujo. Recibe líneas ASCII `ST,0.750,kg` o `US,0.750,kg`, terminadas en CR, LF o CRLF. `ST` significa estable; `US`, inestable. El número usa punto decimal y hasta tres decimales. No se necesita el segundo Electron.
- El cajero puede pedir la última lectura; debe ser positiva, estable y tener menos de 30 segundos. Un dato mal formado, un puerto desconectado o una lectura vieja no completa el campo ni modifica el ticket. El peso fijo de una pieza trazable nunca se reemplaza. Si COM6 no está disponible, el POS reintenta abrirlo sin bloquear la venta ni el ingreso manual.
- El proceso principal del POS abre COM1 a 9600 baudios, 8 bits, sin paridad, 1 bit de parada y sin control de flujo. Envía texto UTF-8 con CRLF, incluyendo artículo, pieza cuando corresponda, cantidad, precio, pagos, total, caja y fecha. Cada pieza trazable ocupa un bloque propio. El nombre de producto y demás texto se limpian de códigos de control antes de transmitir.
- `serialport.write` confirma aceptación por el controlador, no lectura en PuTTY. Se intenta `drain` antes de cerrar; el controlador virtual HHD usado en esta instalación devuelve error de `FlushFileBuffers` pese a aceptar los datos. Solo en ese caso se espera el tiempo mínimo estimado de transmisión y se informa `write-only` para que el usuario verifique PuTTY. Un fallo o tiempo de espera no implica que no haya llegado nada; antes de reintentar se debe comprobar el receptor.
- El simulador es exclusivo de desarrollo. El servidor PostgreSQL y las API de venta no conocen el transporte. No se escucha en red ni se exponen credenciales de terminal. Una falla de dispositivo no deshace una venta confirmada.
- El PDF existente se conserva como salida alternativa. Su puente nativo debe funcionar con `sandbox: true` y `contextIsolation: true`.
- La biblioteca `serialport` usa el binario Node-API precompilado. Forge omite recompilar ese módulo en esta instalación sin MSVC y deja los archivos `.node` fuera del ASAR; el paquete resultante se verificó enviando un ticket por COM1. Referencias: [SerialPort en Electron](https://serialport.io/docs/guide-electron/), [opciones de reconstrucción de Forge](https://www.electronforge.io/config/configuration) y [módulos nativos en ASAR](https://www.electronjs.org/docs/latest/tutorial/asar-archives).

## Contrato y seguridad

- El analizador de balanza limita la trama a 64 bytes imprimibles y acepta únicamente el formato indicado; no ejecuta comandos recibidos por serial. El último dato se descarta si llega una trama inválida.
- Electron valida el origen del IPC y el contenido del ticket antes de escribir. Las rutas seriales y sus parámetros son fijos en el proceso principal, nunca provienen del renderer.
- El ticket lleva las leyendas `COMPROBANTE DE PRUEBA` y `NO FISCAL`; no se envía a ARCA. No se persiste un estado de impresión en PostgreSQL.
- No se usa el renderer para abrir sockets, acceder a archivos o invocar canales IPC arbitrarios.

## Verificación

1. Abrir PuTTY en COM5 a 9600/8N1 y enviar `ST,0.750,kg` seguido de Enter. Abrir un artículo por peso en el POS y pulsar **Leer balanza COM6**; debe mostrar `0,750 kg`. No se permite en una pieza de etiqueta fija o artículo por unidad.
2. Enviar `US,1.250,kg`, una trama inválida o dejar vencer 30 segundos; ninguno de esos casos debe completar el peso. Sin COM6 se puede ingresar el peso manualmente.
3. Enviar un ticket de prueba sin registrar una venta y verificar que PuTTY en COM2 muestre el texto completo, incluidos renglones individuales, con velocidad 9600/8N1.
4. Confirmar una venta y usar **Imprimir en COM1**. El envío ocurre una sola vez por clic; reiniciar el POS no duplica la venta. Una reimpresión es explícita.

## Pendiente

Protocolo real de la balanza física (esta trama es de simulación), protocolo específico de impresora física (por ejemplo ESC/POS), cola durable y auditoría de impresión, selección/configuración de puerto por caja, impresora fiscal y ARCA. Estos requieren datos del hardware y reglas de implementación.
