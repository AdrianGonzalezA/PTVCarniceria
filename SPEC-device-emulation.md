# Balanza e impresión serial (desarrollo)

## Objetivo

Probar desde el POS en Electron la recepción serial de una balanza y la impresión al cerrar ventas, solo cuando la caja tenga esos dispositivos asignados en Administración. En el laboratorio, PuTTY puede enviar peso por COM5 a una balanza asignada a COM6; una impresora de texto asignada a COM1 puede escucharse por COM2; la SMH/P-441F asignada a COM8 recibe los comandos de ticket después de confirmar la venta. Ninguno de esos puertos es una asignación predeterminada.

## Decisión y alcance

- El proceso principal del POS abre solo la balanza serial asignada a la caja, con velocidad configurada, 8N1 y sin control de flujo. Recibe líneas ASCII `ST,0.750,kg` o `US,0.750,kg`, terminadas en CR, LF o CRLF. `ST` significa estable; `US`, inestable. El número usa punto decimal y hasta tres decimales. Sin balanza asignada no abre COM ni ofrece lectura, pero permite peso manual.
- El cajero puede pedir la última lectura si hay balanza asignada; debe ser positiva, estable y tener menos de 30 segundos. Un dato mal formado, un puerto desconectado o una lectura vieja no completa el campo ni modifica el ticket. El peso fijo de una pieza trazable nunca se reemplaza. Si el COM asignado no está disponible, el POS reintenta abrirlo sin bloquear la venta ni el ingreso manual.
- Con una impresora serial de texto asignada, el proceso principal abre su COM al imprimir, a la velocidad configurada, con 8 bits, sin paridad, 1 bit de parada y sin control de flujo. Envía texto UTF-8 con CRLF, incluyendo artículo, pieza cuando corresponda, cantidad, precio, pagos, total, caja y fecha. Cada pieza trazable ocupa un bloque propio. El nombre de producto y demás texto se limpian de códigos de control antes de transmitir. Sin impresora asignada no hay intento de impresión; la venta y el PDF no dependen de ella.
- `serialport.write` confirma aceptación por el controlador, no lectura en PuTTY. Se intenta `drain` antes de cerrar; el controlador virtual HHD usado en esta instalación devuelve error de `FlushFileBuffers` pese a aceptar los datos. Solo en ese caso se espera el tiempo mínimo estimado de transmisión y se informa `write-only` para que el usuario verifique PuTTY. Un fallo o tiempo de espera no implica que no haya llegado nada; antes de reintentar se debe comprobar el receptor.
- El simulador es exclusivo de desarrollo. El servidor PostgreSQL y las API de venta no conocen el transporte. No se escucha en red ni se exponen credenciales de terminal. Una falla de dispositivo no deshace una venta confirmada.
- El PDF existente se conserva como salida alternativa. Su puente nativo debe funcionar con `sandbox: true` y `contextIsolation: true`.
- La biblioteca `serialport` usa el binario Node-API precompilado. Forge omite recompilar ese módulo en esta instalación sin MSVC y deja los archivos `.node` fuera del ASAR; el paquete resultante se verificó enviando un ticket por COM1. Referencias: [SerialPort en Electron](https://serialport.io/docs/guide-electron/), [opciones de reconstrucción de Forge](https://www.electronforge.io/config/configuration) y [módulos nativos en ASAR](https://www.electronjs.org/docs/latest/tutorial/asar-archives).

## Contrato y seguridad

- El analizador de balanza limita la trama a 64 bytes imprimibles y acepta únicamente el formato indicado; no ejecuta comandos recibidos por serial. El último dato se descarta si llega una trama inválida.
- Electron valida el origen del IPC y el contenido del ticket antes de escribir. Consulta la asignación de la caja autenticada en la API y valida puerto, velocidad y tipo; no acepta rutas seriales provistas por el renderer ni usa los campos de dispositivos del antiguo `pos-config.json`. Si la credencial o la API fallan, cierra/deshabilita los dispositivos. Administración guarda la asignación por caja en `pos_terminals`.
- El ticket lleva las leyendas `COMPROBANTE DE PRUEBA` y `NO FISCAL`; no se envía a ARCA. No se persiste un estado de impresión en PostgreSQL.
- No se usa el renderer para abrir sockets, acceder a archivos o invocar canales IPC arbitrarios.

## Verificación

1. Asignar balanza COM6 a 9600 en Administración para esa caja, abrir PuTTY en COM5 a 9600/8N1 y enviar `ST,0.750,kg` seguido de Enter. Abrir un artículo por peso en el POS y pulsar **Leer balanza**; debe mostrar `0,750 kg`. No se permite en una pieza de etiqueta fija o artículo por unidad.
2. Enviar `US,1.250,kg`, una trama inválida o dejar vencer 30 segundos; ninguno de esos casos debe completar el peso. Sin COM6 se puede ingresar el peso manualmente.
3. Asignar impresora de texto COM1 a 9600; confirmar una venta y verificar que PuTTY en COM2 muestre el texto completo. Luego quitar esa asignación y confirmar otra venta: no debe enviarse nada al COM ni mostrarse reintento de impresión.
4. Asignar Hasar COM8 a 9600; cerrar una venta no fiscal y verificar papel; para una fiscal B de consumidor final, comprobar CAE de homologación y número físico de la Hasar. Reiniciar Electron y comprobar que el mismo `saleId` no vuelve a imprimirse automáticamente.

## Pendiente

La asignación actual desde Administración cubre caja, tipo de impresora, puerto y velocidad. 8N1 permanece fijo en el protocolo y falta parametrizar bits, paridad y protocolos de otros modelos, además de probar conexión desde la interfaz. COM1/COM6 y el controlador virtual HHD son solamente una instalación de desarrollo con licencia de prueba de 14 días; no forman parte del contrato de despliegue. La Hasar SMH/P-441F de laboratorio se asigna desde Administración y recibe tickets en COM8 si ese puerto fue elegido. Sigue pendiente persistir centralmente el resultado de impresión. Su prueba y límites están en [tasks/hasar-441f-evaluacion.md](tasks/hasar-441f-evaluacion.md). No enviarle texto libre de COM1.

Faltan el protocolo real de la balanza física (esta trama es de simulación), el protocolo específico de impresora de texto física (por ejemplo ESC/POS), cola durable y auditoría central de impresión. La Hasar está enlazada a una venta persistida, pero su marca de no duplicación reside en el perfil local de Electron; resta conciliación guiada de resultados inciertos y un modelo fiscal independiente de ARCA para despliegues posteriores.
