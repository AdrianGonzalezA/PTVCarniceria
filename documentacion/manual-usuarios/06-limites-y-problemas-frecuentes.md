# 06 · Límites actuales y problemas frecuentes

## Qué significa cada mensaje

| En pantalla | Qué hacer |
| --- | --- |
| **Caja no disponible** | Pedir a un administrador que revise el estado de la caja y la credencial del perfil Electron. |
| **Abrí el turno para comenzar a vender** | Identificarse como el cajero responsable y abrir turno con el fondo inicial real. |
| **Stock insuficiente** | Reducir cantidad, elegir otro artículo o pedir que se registre una entrada/ajuste autorizado. No forzar la venta. |
| **Guardando ticket…** | Esperar antes de cambiar de A–D o cobrar. Si aparece error, usar **Reintentar** y verificar el detalle persistido. |
| **Orden Mercado Pago pendiente** | Esperar la consulta automática o usar **Consultar pago**; no cobrar otra vez hasta conocer el estado. |
| **ARCA pendiente o error** | La venta puede estar ya registrada. Usar **Consultar ARCA / reintentar**; no repetir ticket y pago. |
| **No se pudo imprimir/guardar PDF** | Confirmar en **Venta confirmada** que la operación existe; reintentar solo la salida del comprobante. |

## Alcance que todavía no debe prometerse

- **Fiscalidad:** ARCA funciona en homologación. El ticket/factura fiscal producido allí es de prueba y carece de validez fiscal productiva. Las alícuotas reales y datos del emisor/receptor deben validarse antes de un despliegue.
- **Mercado Pago:** el POS consulta automáticamente órdenes pendientes mientras la ventana de cobro está abierta. Falta un webhook HTTPS público y conciliación de fondo cuando la ventana está cerrada. No hay flujo de reintegro integrado.
- **Otros medios:** débito, crédito, transferencia y cheque se anotan como medios declarados; no se liquidan automáticamente con adquirentes o bancos. No hay cartera de cheques, depósito, rechazo ni ECHEQ.
- **Inventario trazable:** se puede recibir manualmente una pieza y venderla en un renglón propio; el saldo se controla por artículo. Falta importación en lote/interfaz con ERP y un ciclo de estados físicos completo por pieza.
- **Hardware:** en la instalación local de prueba se usan puertos seriales para balanza e impresora, pero su parametrización administrativa por caja está pendiente. Un puerto virtual o dispositivo distinto requiere validación técnica.
- **Caja:** el saldo registrado no sustituye el conteo físico. Aún no existe un arqueo obligatorio ni una gestión de diferencias al cerrar turno.
- **Histórico:** la base local contiene datos ficticios de desarrollo y no constituye un histórico comercial real.

## Para transformar esta base en manual definitivo

1. Recorrer cada procedimiento con cajeros y administradores en la versión que se vaya a distribuir.
2. Incorporar capturas actuales del **POS tomadas dentro de Electron** y capturas de administración, indicando versión y resolución de pantalla.
3. Validar con la empresa las reglas de turno, permisos, descuentos, cuenta corriente, devoluciones y cheques.
4. Validar con la contadora los comprobantes, impuestos, datos obligatorios y salida ARCA antes de sustituir ejemplos de homologación.
5. Documentar instalación, impresoras/balanzas reales, contingencia sin red y soporte una vez definidos los dispositivos y el despliegue.

Para estado técnico y trabajo pendiente, consultar [README del proyecto](../../README.md) y los archivos `tasks/` del repositorio.
