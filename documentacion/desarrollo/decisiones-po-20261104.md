# Decisiones del PO para los incrementos pendientes

**Fecha:** 04/11/2026. Estas respuestas complementan el plan H0 y los diseños I07–I18a. No cambian la restricción de base única `carnicerias_test_visual` ni autorizan inventar datos históricos.

| Incremento | Decisión confirmada | Aplicación |
| --- | --- | --- |
| I07 | Al ingresar **coma o punto**, el signo representa **decimal siempre**. Al mostrar, usar punto para miles y coma para decimal. Alinear todas las cifras a la derecha. | Un único separador se interpreta como decimal también cuando lo siguen tres cifras; el rango de decimales permitido depende del campo. Una entrada con varios separadores se rechaza como ambigua; el formato de visualización es `es-AR`. |
| I09 | Por ahora no se utilizará el costo en la operación. | Conservar costos y vigencias ya almacenados y la compatibilidad del costo opcional implementado. La respuesta no define borrar un costo conocido ni retirar campos históricos. No ampliar I09 por inferencia. |
| I10 | Sí a bloquear saldo negativo para la combinación artículo/sucursal que tenga piezas protegidas. **No** exigir regularizar un déficit genérico anterior antes de recibir la primera pieza. | Aplicar la regla estricta desde la recepción de la pieza y mantener aislado el déficit anterior para que la pieza no lo cubra ni quede disponible para venta genérica. El esquema de separación y sus invariantes requieren diseño técnico antes de preparar la migración. |
| I12 | Sí al servicio `ws_sr_constancia_inscripcion` para consultar CUIT. | Implementar cliente local primero, consulta ARCA cuando falten datos y carga manual ante caída. La autorización WSASS efectiva del certificado de homologación aún debe verificarse; no se presume por el «sí» funcional. |
| I13a/b | Sí al snapshot fiscal inmutable por intento, sin reconstruir históricos; **sí** a las tablas aditivas de encabezado y desglose IVA por intento. El ticket fiscal ingresa al Libro IVA con CAE autorizado aunque Hasar no confirme la impresión; otros gravámenes quedan para otro incremento. | Persistir bases/IVA emitidos y distinguir históricos incompletos. La impresión física se informa por separado. No aplicar ni calcular gravámenes adicionales como si estuvieran en la solicitud fiscal. |
| I16 | Sí: retiros extraordinarios solo en efectivo, aprobación por **otro** administrador y permiso específico. **Sí** a solicitud persistente pendiente/aprobada/rechazada y débito único al aprobar; una solicitud pendiente bloquea el cierre del turno. | El solicitante no puede aprobar su propio retiro aunque tenga permisos administrativos. El saldo retirado no puede exceder el efectivo registrado; solicitud, decisión y movimiento deben ser idempotentes y auditados. |
| I18a | Sí a catálogo de proveedores con snapshot histórico; el operador confirma lo efectivamente recibido; el administrador resuelve diferencias; el endpoint existente de pieza crea un documento unitario ya aprobado y auditado. **Sí** a la excepción auditada sin `SupplierId` solo para ese POST legado, conservando `sourceSystem`; las recepciones nuevas exigen proveedor de catálogo. | Reutilizar escaneo e importación existentes; la aprobación suma stock una sola vez. No crear un proveedor ficticio ni cambiar la semántica de venta o ajustes previos. |

## Límite de la respuesta sobre costo

«No vamos a usar costo por este momento» descarta trabajo adicional de costo en esta etapa. No se interpreta como instrucción para borrar datos, vigencias o quitar el campo del esquema. Si más adelante se desea retirar el costo de la interfaz o permitir volver de valor conocido a desconocido, hará falta definir ese cambio de alcance.

## Arquitectura pendiente de I10

Las cuatro consultas del 04/11 están respondidas. En I10, la respuesta «No por el momento» descarta condicionar la recepción de la primera pieza a regularizar el déficit genérico previo. La alternativa A sigue rigiendo para ventas posteriores con piezas protegidas, pero requiere un mecanismo de transición que conserve separadas las obligaciones genéricas anteriores. No se puede quitar el CHECK de saldo ni compensar automáticamente el déficit con kilos de la nueva pieza. El diseño de columnas, movimiento e invariantes se revisará con el PO antes de crear la migración I10.

## Aclaraciones PO del 05/11 para I18a e I18b

1. **Piezas transferidas:** la pieza queda terminal en origen. Si el destino informatizado registra la recepción, se crea una nueva tenencia del mismo código vinculada al remito y a la pieza de origen. Un destino no informatizado no genera una tenencia digital; el despacho sigue siendo válido.
2. **Remito offline:** el origen puede exportar los datos para importación y validación en un destino informatizado. Ni la exportación ni la importación son obligatorias para completar el despacho. La carga manual sigue disponible.
3. **Permisos por empresa:** cada empresa configura los permisos de sus roles sin afectar a otra empresa que use el mismo rol. En I18a se habilita `inventory.receipts.manage` para administrador o cajero mediante una configuración específica de empresa, dejando los permisos globales existentes como valores iniciales.
