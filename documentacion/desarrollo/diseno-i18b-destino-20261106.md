# I18b — recepción de remito en destino

**Estado:** diseño de implementación, pendiente de la elección de estructura documental del PO. No se modificó el esquema ni el código de destino. Origen, despacho definitivo y exportación JSON opcional ya están operativos; ver [diseño y avance de I18b](diseno-i18b-20261105.md).

## Alcance funcional confirmado

1. El origen confirma el despacho y descuenta su stock sin esperar al destino. El destino puede no usar el POS; en ese caso no se genera recepción digital.
2. Si el destino usa el POS, el operador puede cargar el remito manualmente o importar el JSON exportado. El archivo no tiene firma y se trata como **datos declarados**; la revisión humana precede a la aprobación. No se promete detectar una alteración del archivo sin evidencia externa.
3. El identificador interno de la sucursal/pieza de origen es opcional. Si el origen usa nuestro POS y aporta identificadores, se conservan. La interfaz distingue identificador declarado de vínculo comprobado; una coincidencia de código, nombre o peso no prueba por sí sola el origen.
4. Se muestran enviado, recibido y diferencia por renglón. La aprobación suma en destino **solo la cantidad física recibida**; faltantes o sobrantes no bloquean la aprobación y quedan para resolución administrativa auditada. La ubicación del saldo es la sucursal; no hay depósitos internos ni estado en tránsito.
5. El permiso de recepción se configura por rol y empresa. Las cajas de otras sucursales y el POS de venta conservan su operatoria.

## Decisión de estructura solicitada al PO

**Recomendación: documento de recepción de transferencia propio**, con cabecera de origen/remito y renglones de cantidad enviada/recibida. Reutilizar la transacción serializable, el ledger, la protección de piezas, el lease de conciliación y el patrón de aprobación de I18a mediante un servicio común. Esto evita registrar una sucursal como proveedor y evita que la carga de proveedor pueda editar o aprobar un remito con reglas distintas. La alternativa es extender `InventoryReceipt` con origen de transferencia y restringir cada mutación existente por tipo; requiere ampliar su `CHECK` de proveedor, sus consultas, su importador y su interfaz. Ambas necesitan migración aditiva y prueba de regresión de I18a.

## Contrato propuesto, independiente de la estructura

- Cabecera: empresa/destino fijados por sesión, operación idempotente, origen declarado (nombre y, si existe, código/ID), número y fecha de remito, ID de transferencia de origen opcional, fuente manual o archivo, usuario/fecha, estado y versión. Unicidad por destino + origen normalizado + remito; si se proporciona ID de transferencia, unicidad adicional por destino + ID. Cancelar un borrador conserva auditoría y no mueve stock.
- Renglón: artículo de destino resuelto por **código** y validado en la empresa; cantidad enviada, cantidad recibida, unidad, diferencia, ID de renglón del origen opcional y referencia de pieza opcional. Una pieza recibida conserva código físico, fuente, identificador, peso, perfil local usado para interpretar la lectura y vínculo de origen declarado/comprobado. No se asigna una pieza histórica por similitud.
- Importación JSON: aceptar únicamente versión soportada, empresa por código, destino por nombre o ID conocido, cantidades y tipos de venta válidos, máximo de renglones, artículos resolubles y ausencia de duplicado conocido. Crear **borrador**, nunca stock. Conservar snapshot del remito importado para cotejar con lo escaneado. Un JSON manipulado que todavía cumple el esquema no puede autenticarse; mostrar esa limitación al aprobar.
- Carga manual: mismos campos y reglas, sin exigir IDs internos de otra instalación. El usuario registra lo observado; la aprobación deja constancia de quién validó el remito y qué se recibió.
- Confirmación: transacción serializable, versión y operación idempotente, guardas de conciliación y disponibilidad, crédito agrupado por artículo, movimiento `TransferIn` por renglón positivo, sin recontabilizar al reintentar. Diferencias y resolución administrativa no cambian movimientos cerrados; cualquier corrección posterior es otra operación auditada.

## Tenencia de piezas y compatibilidad

El índice único actual de `inventory.pieces` sobre `(CompanyId, NormalizedSourceSystem, ExternalIdentifier)` impide recibir en otra sucursal la misma pieza ya marcada `Transferred` en origen. La migración deberá permitir una **nueva tenencia** cuando la anterior está terminal por transferencia, manteniendo unicidad de la identidad física para estados no transferidos y unicidad de la referencia a la tenencia de origen cuando exista. Los estados `Sold`, `Consumed`, `HistoricalUnverified` y `ReconciledUnavailable` no se reabren. El origen local, si puede consultarse, debe estar `Transferred` y corresponder al remito; si otra instalación no es consultable, el vínculo queda declarado y no verificado. Los eventos de estado y movimientos anteriores permanecen íntegros.

## Migración, respaldo y reversión

Migración aditiva de documento/renglones o de campos de transferencia, según la decisión PO; ampliar `InventoryMovementKind` y `CK_inventory_movements_kind` para `TransferIn`; campos de linaje de pieza nullable; revisar el índice de identidad con prevuelo de duplicados y estados. Mantener contratos de lectura de recepciones existentes. Antes de aplicar en la única base `carnicerias_test_visual:55433`: confirmar contenedor/volumen, migraciones, cero escritores, respaldo custom verificado y guardas de datos. La reversión operativa suspende nuevas aprobaciones y conserva documentos, piezas y movimientos; `Down` se detiene si ya existen datos de destino. No restaurar sin autorización específica.

## Pruebas de aceptación

1. Despacho a destino sin POS termina sin recepción ni espera. Con destino POS, carga manual e importación generan borrador y stock intacto hasta aprobar.
2. Enviado 12, recibido 10: destino suma 10 una vez, diferencia 2 visible y resoluble por admin; reintento no suma otra vez. Enviado 12, recibido 14: suma 14 y audita excedente.
3. Código de artículo ausente, empresa/destino incorrectos, versión inválida, remito duplicado y cantidad fuera de unidad se rechazan antes de afectar stock. Un archivo sin firma se presenta como declarado; no se etiqueta falsamente como auténtico.
4. Pieza transferida en origen no puede volver a venderse allí. Destino registra nueva tenencia vinculada cuando hay ID; sin ID conserva origen/remito/código declarado. Dos aprobaciones concurrentes o dos ingresos del mismo remito no crean dos tenencias disponibles.
5. Regresión I02/I18a/I19/I20 y saldo/ledger por sucursal. Prueba HTTP con dos sesiones lógicas en la única base autorizada, fixture aislado y limpieza cotejada contra el saldo inicial; recorridos de Administración y dos cajas Electron pendientes de homologación manual.
