# Cuenta corriente de clientes — decisiones provisionales para revisar

**Estado:** aprobado para iniciar el desarrollo el 8 de octubre de 2026; todas las reglas de este documento pueden modificarse durante las pruebas. No equivale a aprobación fiscal ni a salida a producción.

## Objetivo y alcance

Permitir identificar clientes registrados habilitados para cuenta corriente, dejar a deber el saldo de una venta, cobrar deuda en forma parcial o total, conservar anticipos y corregir cobranzas erróneas con trazabilidad. Se parte de las ventas y turnos reales del POS. Las ventas actuales todavía no son comprobantes fiscales.

El relevamiento original (`Documentos/Especificacion_Sistema_Carnicerias.pdf`, §4.9 y etapas) contempla clientes eventuales o registrados con cuenta corriente y deja los límites de crédito y bloqueos para una etapa posterior. Este documento resuelve provisionalmente las preguntas operativas surgidas después; no reemplaza la revisión contable/fiscal.

## Preguntas y respuestas del relevamiento conversado

Las preguntas están redactadas para conservar el sentido de la conversación; las respuestas se registran en forma resumida, no como citas literales. Todas quedan **a revisar**, aunque habilitan el desarrollo actual.

1. **¿Una venta puede combinar pagos inmediatos y cuenta corriente por el saldo, o debe ir completa a cuenta?** Sí, combinar. El usuario prefirió el modelo más amplio porque luego se podrá restringir a una sola modalidad sin fraccionamiento si se desea.
2. **¿Quién puede autorizar pasar el saldo a cuenta corriente?** El cajero solo cuando su **rol tenga el permiso** correspondiente. La acción requiere confirmación adicional y auditoría de usuario, fecha/hora e importe; no se exige otra persona autorizante en cada operación.
3. **¿La cobranza de deuda debe ser total o admite pagos parciales?** Admite parciales, incluso si en la práctica termina siendo un solo pago. Se emite/registrará un recibo por cada cobranza y no se reescribe la venta original.
4. **¿La cobranza se hace desde el POS y afecta la caja?** Sí: la realiza un cajero identificado con turno abierto. El dinero recibido ingresa al turno/caja según el medio de cobro.
5. **¿Qué clientes pueden comprar a cuenta?** Solo los clientes con cuenta corriente expresamente habilitada desde administración. El límite de crédito y su bloqueo quedan para una etapa posterior.
6. **¿Cómo se imputan los pagos a las ventas pendientes?** El sistema propone las más antiguas, pero el cajero puede elegir una o varias ventas y distribuir montos parciales; el recibo conserva el detalle de imputación.
7. **¿Se puede cobrar más de la deuda y dejar saldo a favor?** Sí. El excedente queda como crédito/anticipo del cliente, no se pierde ni se atribuye arbitrariamente a otra venta.
8. **¿Cómo se usa el saldo a favor en otra compra?** El cajero decide aplicar nada, una parte o todo el saldo disponible. El remanente sigue a favor del cliente.
9. **¿Una cobranza errónea se corrige con nota de crédito?** Inicialmente se propuso una NC. Tras distinguir venta/facturación de cobranza, el usuario aceptó que la NC corresponde al ajuste de una operación facturada, no a borrar una cobranza equivocada. Para una cobranza mal asentada: constancia interna de anulación de recibo, numerada, vinculada al original; original marcado anulado, nunca eliminado, y nuevo recibo si corresponde. Se revierte caja **solo si el dinero no entró o fue devuelto**. Si el dinero sí ingresó pero se imputó al cliente equivocado, se corrige la imputación de cuenta sin sacar dinero de caja. El usuario aprobó provisionalmente este circuito.
10. **¿Qué documento respalda la cobranza y qué pasa si la operación ya fue facturada?** La propuesta provisoria es un Recibo X para pago total/parcial de operación ya facturada; no es factura. Una corrección de un comprobante fiscal electrónico con CAE requiere el documento fiscal asociado que corresponda (NC/ND). Antes de uso productivo se validará con asesor contable y las reglas vigentes de ARCA. Referencias: [situaciones especiales de facturación](https://www.arca.gob.ar/facturacion/comprobantes/situaciones-especiales.asp) y [consulta de comprobantes electrónicos](https://servicioscf.afip.gob.ar/publico/abc/consultas_detalle.aspx?id=6490326).

## Reglas del modelo y límites

- El cliente eventual no tiene cuenta corriente. El cliente registrado pertenece a la empresa y puede operar en sus sucursales; la habilitación de cuenta es explícita y se verifica en el servidor al confirmar cada venta.
- Una venta con saldo a cuenta conserva la venta original, sus pagos inmediatos, el importe adeudado y la identidad del cliente. La suma de pagos inmediatos + crédito aplicado + deuda nueva debe igualar exactamente el total, salvo vuelto de efectivo calculado por servidor. La operación completa (venta, stock, caja y cuenta) es atómica e idempotente.
- La deuda y el anticipo se reconstruyen desde movimientos inmutables de cuenta. No se edita un saldo agregado como fuente de verdad. Cada imputación de cobranza refiere una venta y no supera su saldo pendiente. Los importes son decimales monetarios con dos posiciones y nunca negativos.
- No se registra un ingreso de caja ficticio al crear deuda ni al consumir saldo a favor. El ingreso de caja ocurre al cobrar dinero real; una corrección distingue asiento de cuenta de movimiento físico de caja.
- Las escrituras exigen sesión, contexto de empresa/sucursal y, en POS, caja y turno abiertos. El servidor verifica pertenencia y permiso del rol; no confía en la interfaz para autorizar crédito ni acepta identificadores de empresa/cajero/caja del body.
- En esta fase no se implementa facturación ARCA, validación de CAE, NC/ND, límites automáticos de crédito, intereses, vencimientos ni integración bancaria. Los documentos internos no deben presentarse como comprobantes fiscales.
- No se crean otras bases de datos: desarrollo y migraciones solo sobre `carnicerias_test_visual` en PostgreSQL local, previa verificación del destino. No ejecutar pruebas de integración que creen o reinicien bases.

## Contratos previstos (a confirmar por incremento)

- Administración: alta/listado/edición de clientes de la empresa, incluida la habilitación de cuenta corriente. Un cliente inactivo o sin habilitación no admite deuda nueva; el historial previo se conserva.
- El primer corte de administración reutiliza `organization.manage` para el ABM de clientes; **no** concede por ello permiso para cargar una venta a cuenta. Ese permiso operativo específico se implementará en el corte de ventas.
- POS: búsqueda/selección de cliente para la venta; visualización de deuda y saldo a favor; elección explícita del importe a cuenta o del crédito a aplicar; autorización por permiso del rol.
- Cobranzas: consulta paginada de deuda por cliente y venta, propuesta de imputación por antigüedad, confirmación con clave de idempotencia, recibo interno y movimientos de caja/cuenta en una transacción.
- Correcciones: anulación enlazada e idempotente, sin eliminar recibos ni ventas, con motivo y actor obligatorios. La reversión de caja depende de la realidad del dinero, no solo de la imputación contable.
- Errores API mantienen `{ error: { code, message, details } }`; recursos ajenos a la empresa no se exponen. Los importes se recalculan y validan en el servidor.

## Criterios de aceptación generales

1. Un cliente no habilitado no puede generar deuda, aunque el cliente HTTP envíe la solicitud directamente.
2. Un cajero sin permiso no puede confirmar una venta con saldo a cuenta; un pago mixto válido cierra venta, stock y caja una sola vez.
3. Dos cobranzas o aplicaciones concurrentes no gastan dos veces el mismo saldo. Un reintento idéntico no duplica movimientos ni recibos.
4. El recibo conserva importes e imputaciones; un anticipo queda visible y es aplicable en parte o en total por decisión explícita.
5. Una anulación conserva originales y muestra el motivo; la caja solo se ajusta cuando corresponde al movimiento efectivo real.
6. Las pruebas unitarias y de frontend pueden correr sin otra base. La verificación manual del POS se hace en Electron.

## Puntos que siguen a revisión

- Identificación y datos fiscales obligatorios de cada tipo de cliente, duplicados, edición y anonimización.
- Alcance del saldo entre sucursales de la misma empresa y exposición al cajero de otras sucursales. La propuesta inicial es cuenta única por empresa, con cada movimiento atribuido a su sucursal.
- Numeración y formato definitivo de recibos internos, aprobación de anulaciones, arqueo y tratamiento de reembolsos.
- Vigencia fiscal argentina y circuito ARCA antes de activar comprobantes reales.
- Límite de crédito, mora, reglas de bloqueo, plazos y permisos más finos en una siguiente etapa.

## Estrategia de desarrollo y verificación

Implementar en cortes pequeños según `tasks/customers-credit-plan.md`. Cada corte incluye pruebas unitarias de invariantes y autorización cuando sea posible, compilación backend/frontend afectado, revisión del diff y estado de migración. La integración que cree o reinicie otra base queda expresamente pendiente; las migraciones se aplicarán únicamente a la base actual, después de verificar nombre y puerto. No publicar como terminado el flujo de cuenta hasta probar confirmación, cobranza, anticipo y corrección de punta a punta.
