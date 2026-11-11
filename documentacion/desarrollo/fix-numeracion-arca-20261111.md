# Corrección de numeración fiscal y conciliación WSFE — 11/11/2026

## Incidente observado

La venta confirmada `2fb7a659-14c0-4b49-9c52-29072f35a9d5` de La Rústica no tenía intento fiscal. El POST devolvía `FISCAL_NUMBER_CONFLICT`. Un intento anterior de otra venta (`f8c403bd-b942-4349-81ef-675d225b862a`) conservaba el número A/99/37 en `NeedsReconciliation`. La consulta de solo lectura a WSFE homologación devolvió último autorizado A/99 = 36 y `FECompConsultar` para A/99/37 devolvió código 602. El índice local reservaba el número 37 incluso para intentos rechazados, y el flujo anterior trataba el 602 como incertidumbre perpetua.

ARCA documenta `FECompUltimoAutorizado`, `FECompConsultar` y el código 602 en [el manual WSFEv1](https://www.afip.gob.ar/fe/ayuda/documentos/wsfev1-COMPG.pdf). Las dos consultas deben concordar antes de liberar un número: 602 por sí solo no basta.

## Corrección implementada

1. La emisión toma un bloqueo consultivo PostgreSQL por CUIT emisor, punto de venta y tipo de comprobante. Dos instancias locales no envían simultáneamente el mismo número. Si el bloqueo está ocupado, se devuelve `FISCAL_REQUEST_IN_PROGRESS` para reintentar.
2. Antes de crear un intento, se revisa el intento activo del número calculado. Los intentos `Prepared` recientes y los `NeedsReconciliation` se conservan mientras ARCA no resuelva su estado.
3. Un intento incierto solo se marca como no emitido si transcurrieron dos minutos, `FECompConsultar` devuelve exclusivamente 602 y `FECompUltimoAutorizado` es menor que el número intentado. El intento se conserva con estado `Rejected` y `ErrorCodes=602`. La historia no se borra ni se vincula a otra venta.
4. Si la consulta encuentra un comprobante autorizado con otros datos, el intento se conserva como colisión conciliada (`Rejected`, código 10016). Si coincide en datos, sigue pendiente: en un punto compartido la coincidencia no prueba que el CAE sea de esta venta.
5. El índice único `(IssuerCuit, PointOfSale, VoucherType, Number)` ahora aplica a intentos no rechazados. Esto permite reutilizar un número confirmado como no emitido y mantiene la exclusividad de los intentos activos y autorizados. La restricción por venta ya excluía los rechazados.
6. Las transiciones de conciliación y los errores de bloqueo se registran con ID de intento o venta, sin credenciales.

El adaptador de esta instalación sigue apuntando a **homologación**. El cambio resuelve la lógica que podría causar el mismo incidente en una instalación productiva; habilitar el endpoint de producción de ARCA es una tarea de despliegue distinta.

## Base autorizada y despliegue

- Única base utilizada: `carnicerias_test_visual`, contenedor existente `carnicerias-pos-test-20261007`, volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`, puerto local `55433`.
- Se detuvieron las APIs escritoras `5197` y `5249` antes del respaldo y la migración. No se detuvo ni recreó PostgreSQL.
- Respaldo custom validado: `.local-backups/fiscal-number-reconcile-before-20261111-171205Z.dump`, 552.696 bytes, 788 objetos en TOC, SHA-256 `fd7911592caa523978e598d7dc223236a18dc636677f74a709b66a39b1d75c8a`.
- El prevuelo mostró 76 migraciones aplicadas y únicamente `20261111170601_ReconcileFiscalNumberConflicts` pendiente. Su SQL, revisado antes de aplicar, solo reemplaza el índice y agrega una fila al historial EF.
- Migración aplicada: 77 migraciones, última `20261111170601_ReconcileFiscalNumberConflicts`; el índice físico tiene filtro `"Status" <> 2`.
- APIs `5197` y `5249` recompiladas, iniciadas y saludables. La venta y el intento histórico conservaban su estado previo al cerrar esta verificación; el reintento fiscal real queda pendiente desde la caja.

## Pruebas y límites de verificación

- 22 pruebas fiscales focalizadas aprobadas: clasificación de ausencia, demora de seguridad, secuencia fiscal correcta, errores WSFE distintos de 602, historia del intento, modelo de índice y regresiones WSFE/documento.
- Una prueba contra **dos conexiones independientes a la misma base autorizada**, sin escrituras comerciales, confirmó que el bloqueo impide la segunda adquisición y se libera correctamente.
- Compilación Debug y publicación Release aprobadas; ambas APIs respondieron `healthy` después del despliegue.
- La verificación visual y el POST real desde Caja A Electron no se ejecutaron: la sesión de Windows estaba bloqueada y el controlador de interfaz no pudo inicializarse. No se emitió CAE nuevo durante este despliegue.

## Verificación operativa pendiente

Al desbloquear la caja, reintentar la emisión fiscal **de la venta ya confirmada**, sin repetir el cobro ni la venta. Verificar en la respuesta y por consulta posterior que exista un solo CAE para esa venta. Si ARCA sigue informando A/99/36 y 602 para el número 37, el flujo debe conservar el intento anterior como `Rejected`/602 y solicitar el número disponible. Si otro emisor ocupa el 37 entre consultas, WSFE debe rechazarlo con 10016 y el sistema debe consultar nuevamente el último autorizado.

La reversión de código puede hacerse sin borrar comprobantes. La migración tiene una guarda en `Down`: si un número fue reutilizado y hay dos intentos históricos para él, el índice anterior no puede recrearse sin perder historia. Una restauración de respaldo requeriría autorización y análisis de ventas posteriores; no se ejecutó ninguna.
