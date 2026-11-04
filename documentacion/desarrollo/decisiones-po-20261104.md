# Decisiones del PO para los incrementos pendientes

**Fecha:** 04/11/2026. Estas respuestas complementan el plan H0 y los diseños I07–I18a. No cambian la restricción de base única `carnicerias_test_visual` ni autorizan inventar datos históricos.

| Incremento | Decisión confirmada | Aplicación |
| --- | --- | --- |
| I07 | Al ingresar **coma o punto**, el signo representa **decimal siempre**. Al mostrar, usar punto para miles y coma para decimal. Alinear todas las cifras a la derecha. | Un único separador se interpreta como decimal también cuando lo siguen tres cifras; el rango de decimales permitido depende del campo. Una entrada con varios separadores se rechaza como ambigua; el formato de visualización es `es-AR`. |
| I09 | Por ahora no se utilizará el costo en la operación. | Conservar costos y vigencias ya almacenados y la compatibilidad del costo opcional implementado. La respuesta no define borrar un costo conocido ni retirar campos históricos. No ampliar I09 por inferencia. |
| I10 | Sí a bloquear saldo negativo para la combinación artículo/sucursal que tenga piezas protegidas. | Aplicar la alternativa A del diseño. La venta común sin stock se habilita por defecto solo donde no exponga piezas trazables. |
| I12 | Sí al servicio `ws_sr_constancia_inscripcion` para consultar CUIT. | Implementar cliente local primero, consulta ARCA cuando falten datos y carga manual ante caída. La autorización WSASS efectiva del certificado de homologación aún debe verificarse; no se presume por el «sí» funcional. |
| I13a/b | Sí al snapshot fiscal inmutable por intento, sin reconstruir históricos; el ticket fiscal ingresa al Libro IVA con CAE autorizado aunque Hasar no confirme la impresión; otros gravámenes quedan para otro incremento. | Persistir bases/IVA emitidos y distinguir históricos incompletos. La impresión física se informa por separado. No aplicar ni calcular gravámenes adicionales como si estuvieran en la solicitud fiscal. |
| I16 | Sí: retiros extraordinarios solo en efectivo, aprobación por **otro** administrador y permiso específico. | El solicitante no puede aprobar su propio retiro aunque tenga permisos administrativos. El saldo retirado no puede exceder el efectivo registrado; solicitud, decisión y movimiento deben ser idempotentes y auditados. |
| I18a | Sí a catálogo de proveedores con snapshot histórico; el operador confirma lo efectivamente recibido; el administrador resuelve diferencias; el endpoint existente de pieza crea un documento unitario ya aprobado y auditado. | Reutilizar escaneo e importación existentes; la aprobación suma stock una sola vez. No cambiar la semántica de venta ni de ajustes previos. |

## Límite de la respuesta sobre costo

«No vamos a usar costo por este momento» descarta trabajo adicional de costo en esta etapa. No se interpreta como instrucción para borrar datos, vigencias o quitar el campo del esquema. Si más adelante se desea retirar el costo de la interfaz o permitir volver de valor conocido a desconocido, hará falta definir ese cambio de alcance.

## Consultas técnicas abiertas tras contrastar los contratos reales

1. I10: cuando ya existe déficit o reserva genérica sin respaldo, definir si la primera pieza trazable debe esperar regularización antes de recibirse, conservando la alternativa A, o si se separa el déficit en otro modelo.
2. I13a: confirmar tablas aditivas de snapshot e IVA por intento de emisión frente a columnas/JSON en `fiscal_documents`.
3. I16: confirmar solicitud persistente con estados y débito único de efectivo en el ledger al aprobar, bloqueando cierre con solicitudes pendientes.
4. I18a: el POST legado de pieza no envía proveedor. Definir si su documento unitario admite una excepción auditada sin ficha de catálogo, usando `sourceSystem` como origen, mientras las recepciones nuevas exigen proveedor del catálogo.

Estas consultas no reabren las siete decisiones funcionales; delimitan la estructura y la compatibilidad necesarias antes de preparar sus migraciones.
