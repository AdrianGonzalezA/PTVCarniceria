# Sistema de Facturación y Gestión para Carnicerías

## Documento consolidado de requerimientos para MVP

**Estado:** borrador para validación funcional y técnica  
**Fecha de consolidación:** 28 de agosto de 2026  
**Iniciativa:** RI-P-529/25 — Carnicerías, Etapa 1  
**Objetivo del documento:** establecer una base única, trazable y suficientemente precisa para estimar, diseñar y desarrollar el MVP.

---

## 1. Resumen ejecutivo

Se requiere una solución de facturación y gestión para carnicerías que permita operar uno o más puntos de venta, administrar productos y precios, registrar ingresos y egresos de stock, vender productos con o sin trazabilidad individual, cobrar con uno o varios medios de pago, emitir comprobantes fiscales o tickets no fiscales, controlar la caja y obtener reportes de cierre y estadísticas de venta.

El diferencial del producto es la convivencia de:

- productos con trazabilidad individual, identificados por un correlativo único;
- productos a granel, controlados por peso o cantidad;
- mercadería de almacén identificada mediante códigos EAN;
- integración con sistemas preexistentes de frigorífico o abastecedor.

El MVP debe priorizar la continuidad y velocidad de la operación de caja. El proceso de desarme o transformación de piezas se documenta como evolución, pero queda expresamente fuera de esta versión.

## 2. Fuentes relevadas y criterio de prevalencia

Se analizaron las siguientes fuentes:

1. `Especificacion_Sistema_Carnicerias.pdf`, documento aportado por el Product Owner, fechado el 28/08/2026.
2. `RI-P-529_25_ ANÁLISIS FUNCIONAL_ CARNICERÍAS - Etapa 1.docx`.
3. Especificaciones parciales de autenticación, ABM de productos, selección de productos, detalle de venta y finalización de compra.
4. `protoripo.txt`, que referencia un prototipo de Figma del puesto de venta.
5. Once recursos PNG de `Documentos/maqueta`, de los cuales siete corresponden a pantallas funcionales y cuatro son recursos gráficos decorativos.

Ante diferencias entre fuentes se adopta, provisoriamente, este orden de precedencia:

1. definiciones explícitas y más recientes del PO;
2. alcance de Etapa 1 del análisis funcional;
3. especificaciones parciales, usadas para completar comportamiento y validaciones;
4. prototipo visual, una vez que pueda ser revisado y validado.

Las pantallas exportadas del prototipo fueron inspeccionadas el 28/08/2026. El análisis se limita a los estados estáticos disponibles: no permite confirmar transiciones, validaciones, mensajes, permisos ni comportamiento interactivo que no esté representado en una captura.

## 3. Objetivos de negocio

- Agilizar la venta en mostrador mediante pantalla táctil, búsqueda, botones rápidos, lector de códigos y balanza.
- Emitir comprobantes electrónicos válidos de acuerdo con ARCA y tickets en puestos no fiscales.
- Mantener stock exacto diferenciando productos trazables y productos a granel.
- Disponer de trazabilidad de ventas, pagos, caja, stock y acciones críticas de usuarios.
- Integrar la carnicería con los sistemas existentes de frigorífico o abastecedor sin acoplar sus modelos de datos.
- Soportar múltiples empresas y sucursales con correcta separación de información.
- Brindar información operativa de cierre y estadísticas básicas de venta.

## 4. Alcance del MVP

### 4.1 Incluido

- Autenticación, cierre de sesión y autorización por rol.
- Selección de empresa y sucursal habilitada para el usuario.
- Configuración de puntos de venta fiscales y manuales/no fiscales.
- Gestión de productos, categorías/familias, unidades de medida, impuestos y estado.
- Gestión de listas de precios y vigencias.
- Productos con correlativo/trazabilidad y productos sin trazabilidad por peso o unidad.
- Venta mediante búsqueda, categorías/favoritos, selección táctil o lectura de código.
- Lectura de etiquetas de trazabilidad, etiquetas de balanza y códigos EAN.
- Integración con balanza física y alternativa de ingreso manual.
- Armado, edición, eliminación y cancelación del detalle de venta.
- Clientes eventuales y clientes registrados, incluyendo cuenta corriente sin control de límite.
- Factura electrónica y ticket manual/no fiscal según el punto de venta.
- Impresión en comandera y envío de comprobante por correo electrónico.
- Cobro con múltiples medios de pago y cálculo de vuelto en efectivo.
- Registro manual de tarjetas, transferencias, Mercado Pago y otros medios; sin integración directa con adquirentes.
- Movimientos de caja, retiros y cierre de jornada.
- Ingreso de stock por remitos internos y por compras a proveedores externos.
- Egreso de stock por venta.
- Consulta de facturas con filtros y exportación.
- Emisión de notas de crédito vinculadas a comprobantes originales, con alcance total/parcial a confirmar.
- Reporte de cierre de caja, reporte de cierre de stock y estadísticas básicas de venta.
- Auditoría de operaciones críticas.
- Sincronización del detalle de facturación necesario para Libro IVA con frigorífico/abastecedor.

### 4.2 Fuera del MVP

- Desarme de medias reses, cuartos, cajas o piezas y generación de nuevos correlativos.
- Relación genealógica entre producto madre y productos resultantes del desarme.
- Límites de crédito y bloqueo automático de ventas por saldo.
- Integración directa con Mercado Pago, POSnet u otros adquirentes.
- Promociones, descuentos recurrentes y fidelización.
- Monitoreo administrativo desde una aplicación móvil específica.
- Órdenes de pago.
- Controlador fiscal físico.
- Devoluciones internas al frigorífico, hasta que se confirme el circuito.

## 5. Actores y permisos

| Rol | Responsabilidades del MVP |
|---|---|
| Cajero | Iniciar una venta, seleccionar cliente y comprobante, cargar productos, editar ítems dentro de sus permisos, cobrar, emitir y entregar comprobantes, cancelar operaciones. |
| Encargado de ingresos | Consultar remitos recibidos, confirmar ingresos automáticos y cargar remitos de proveedores externos. |
| Administrador | Mantener maestros y precios, supervisar stock y caja, registrar retiros, cerrar jornada, consultar facturas, estadísticas y auditoría. |
| Operador de planta/desarmador | Rol reservado para Etapa 2. En el MVP no posee un proceso de desarme operativo. |

Una persona puede tener más de un rol. Los permisos deben evaluarse dentro de la empresa y sucursal seleccionadas.

## 6. Requerimientos funcionales

### RF-01. Acceso, sesión y contexto de operación

1. El usuario debe autenticarse con nombre de usuario o correo y contraseña.
2. La contraseña se mostrará oculta y podrá revelarse de manera voluntaria.
3. Los campos son obligatorios. Ante credenciales inválidas se informará un mensaje genérico sin revelar cuál dato falló.
4. Una vez autenticado, el usuario debe seleccionar una empresa y, si corresponde, una sucursal entre las que tiene habilitadas.
5. La sesión conservará usuario, roles, empresa y sucursal activas.
6. El sistema mostrará únicamente módulos y acciones autorizadas.
7. El usuario podrá cerrar sesión; la sesión se invalidará y la aplicación volverá al acceso.

### RF-02. Empresas, sucursales y puntos de venta

1. Una empresa puede tener múltiples sucursales.
2. Cada operación transaccional debe quedar asociada como mínimo a empresa, sucursal, punto de venta y usuario.
3. Cada punto de venta se configurará como:
   - fiscal/electrónico, con emisión mediante ARCA;
   - manual/no fiscal, con emisión de ticket sin autorización fiscal.
4. La numeración y configuración fiscal deben estar separadas por punto de venta.
5. No debe existir acceso cruzado a información de empresas no autorizadas.
6. El prototipo muestra cuatro ventas o tickets accesibles en paralelo (`Ticket A` a `Ticket D`). El MVP deberá permitir múltiples ventas en borrador por terminal, con identificación inequívoca, persistencia independiente y una indicación visual cuando un ticket tenga contenido pendiente.
7. Cambiar de ticket no debe mezclar cliente, comprobante, ítems, precios, descuentos ni pagos entre ventas.

### RF-03. Maestro de productos

Cada producto deberá permitir configurar, como mínimo:

- código interno único e inmutable luego del alta;
- denominación;
- categoría o familia;
- unidad de medida: kilogramo, unidad u otras parametrizadas;
- modalidad de stock: con trazabilidad individual o sin trazabilidad;
- modalidad de venta: peso o unidad;
- códigos EAN y/o PLU asociados;
- alícuota de IVA: exento, 10,5 % o 21 %;
- percepciones aplicables;
- costo;
- estado activo/inactivo;
- fecha de alta y datos de auditoría.

Reglas:

1. Código, denominación, categoría, unidad, modalidad, IVA, costo y estado son obligatorios.
2. El código debe ser único dentro del alcance que se defina para el catálogo y no podrá editarse luego del alta.
3. El costo debe ser mayor que cero.
4. Solo los productos activos pueden agregarse a nuevas ventas.
5. Un producto con movimientos históricos no debe eliminarse físicamente. Se lo inactivará para preservar integridad y trazabilidad.
6. El sistema conservará historial de cambios de costo, estado y configuración relevante, con fecha y usuario.

### RF-04. Categorías, favoritos y listas de precios

1. El administrador podrá mantener categorías/familias y asignar productos.
2. Podrán definirse productos favoritos o accesos rápidos para el puesto de venta.
3. El administrador podrá crear listas de precios, definir su vigencia y asignarlas a empresa, sucursal o contexto comercial a confirmar.
4. Cada cambio de precio debe conservar producto, lista, precio anterior, precio nuevo, fecha de vigencia, fecha de modificación y usuario.
5. El precio de venta vigente debe ser mayor o igual al costo, salvo autorización o regla empresarial expresamente definida.
6. Una venta debe congelar el precio utilizado para que cambios posteriores no alteren comprobantes históricos.

### RF-05. Clientes y cuentas corrientes

1. El sistema contará con un cliente eventual/consumidor final para ventas que no requieran identificación.
2. Se podrá buscar un cliente ingresando al menos tres caracteres sobre sus datos identificatorios.
3. Si el cliente no existe, un usuario autorizado podrá realizar un alta simplificada desde la venta.
4. La información mínima deberá permitir validar y emitir el tipo de comprobante solicitado conforme a ARCA.
5. Para factura A será obligatorio seleccionar un cliente fiscalmente válido.
6. Una venta podrá imputarse a cuenta corriente y generar el movimiento correspondiente.
7. En el MVP no se controlará límite de crédito ni se bloqueará la venta por saldo acumulado.

### RF-06. Inicio de venta

1. El cajero podrá iniciar y alternar entre varias ventas en borrador por terminal. La cantidad máxima será parametrizable o se fijará al cerrar la pregunta funcional correspondiente.
2. Por defecto se propondrá Factura B a consumidor final, sujeto a validación fiscal y configuración del punto de venta.
3. El cajero podrá seleccionar otro comprobante y cliente antes de confirmar.
4. La venta activa debe persistir ante una navegación accidental o refresco de pantalla, dentro de los límites definidos para la sesión.
5. Cada borrador debe mostrar si está vacío, tiene productos o requiere atención; el cierre de sesión o caja no debe descartar borradores sin advertencia y resolución explícita.

### RF-07. Selección y lectura de productos

El cajero podrá agregar productos mediante:

- categoría/familia o favoritos;
- búsqueda parcial, iniciada a partir de tres caracteres;
- lectura de etiqueta de trazabilidad;
- lectura de etiqueta de balanza;
- lectura de código EAN;
- carga manual autorizada.

Comportamiento por tipo de lectura:

| Tipo | Identificación esperada | Resultado |
|---|---|---|
| Trazabilidad | Correlativo/ID único | Recupera artículo, peso y datos vinculados; valida que el ID esté disponible en la sucursal. |
| Balanza | PLU y peso y/o importe codificados | Decodifica y agrega artículo, peso y precio según la regla configurada. |
| EAN | Código comercial estándar | Identifica el producto y agrega una unidad o solicita peso, según su modalidad. |

Reglas:

1. La búsqueda y las categorías mostrarán solo productos activos y con precio vigente.
2. Si no hay resultados se informará “Producto no encontrado” o “Sin productos disponibles”.
3. Los formatos mínimos a soportar son EAN-13, EAN-8, UPC y códigos internos configurables, sujeto a confirmar modelos concretos de etiquetas.
4. Un correlativo ya vendido, no recibido o perteneciente a otra sucursal no podrá agregarse.
5. Si un producto sin trazabilidad ya está en el detalle, se acumulará cantidad o peso.
6. Los productos con correlativo se mantendrán como unidades identificables aun cuando compartan artículo.

### RF-08. Integración con balanza

1. Para productos vendidos por peso, el sistema podrá obtener el valor desde una balanza conectada.
2. Se mostrará el peso leído antes de confirmar la incorporación.
3. Si no hay conexión o lectura válida, se informará el problema y se habilitará ingreso manual a usuarios autorizados.
4. No se aceptarán pesos nulos, cero o negativos.
5. El peso registrado deberá conservar su origen: balanza, etiqueta o ingreso manual.
6. La integración concreta dependerá de los modelos, protocolos y forma de conexión que se definan.

### RF-09. Detalle de venta

La grilla de venta mostrará como mínimo producto, correlativo si aplica, peso/cantidad, precio unitario, descuento, impuestos relevantes y total del ítem.

1. Los ítems se mostrarán en orden de incorporación.
2. Cada alta, edición o eliminación recalculará subtotal, impuestos, percepciones, descuentos y total general.
3. Un usuario autorizado podrá editar peso/cantidad y aplicar un descuento dentro del máximo parametrizado.
4. Cantidad, peso y precio deben ser mayores que cero.
5. No se permitirá un descuento que deje un importe negativo.
6. Al cancelar una edición se conservará el valor previo.
7. Para eliminar un ítem se solicitará confirmación y se liberará cualquier reserva de correlativo o stock.
8. Para cancelar toda la venta se solicitará confirmación, se liberarán reservas y se registrará el evento.
9. La carga rápida de un artículo inexistente no se considera segura por defecto. Si se confirma su inclusión deberá limitarse a un “ítem genérico” autorizado, sin alta implícita de producto ni impacto de stock no definido.

### RF-10. Impuestos y cálculo fiscal

1. Cada ítem aplicará automáticamente la alícuota de IVA configurada: exento, 10,5 % o 21 %.
2. Se contemplarán percepciones nacionales, provinciales y municipales por ítem cuando correspondan al cliente, jurisdicción y operación.
3. El cálculo debe conservar bases imponibles, importes y redondeos necesarios para reproducir el comprobante y el Libro IVA.
4. El total de la venta debe coincidir con la suma de conceptos del comprobante dentro de la tolerancia fiscal permitida.
5. Las reglas fiscales deben versionarse o conservarse en el comprobante histórico.

### RF-11. Finalización y medios de pago

Antes del cobro, el sistema validará:

- existencia de una venta activa con al menos un ítem;
- cantidad/peso, precio y total válidos;
- cliente y datos fiscales requeridos;
- disponibilidad de stock o correlativos;
- total correctamente calculado.

El sistema permitirá:

1. seleccionar medios de pago activos y parametrizados;
2. combinar varios medios hasta cancelar el total;
3. ingresar datos adicionales cuando el medio lo requiera, por ejemplo referencia de transferencia o cupón;
4. eliminar un medio antes de confirmar;
5. visualizar total, saldo pendiente y vuelto;
6. habilitar Confirmar cuando el saldo sea cero o exista un excedente válido exclusivamente en efectivo;
7. calcular el vuelto cuando el efectivo entregado exceda el saldo;
8. registrar el vuelto como movimiento negativo de efectivo en caja.

Los medios iniciales contemplados son efectivo, débito, crédito, transferencia, Mercado Pago, cheques y cuenta corriente. Su disponibilidad será parametrizable. En el MVP, excepto efectivo y cuenta corriente, su aprobación se registra manualmente.

### RF-12. Confirmación, facturación y entrega

1. La confirmación debe ser atómica desde el punto de vista del negocio: no puede quedar una venta cobrada sin una situación de comprobante identificable y recuperable.
2. Al confirmar se deben registrar venta, detalle, impuestos, pagos, movimientos de caja, cuenta corriente si aplica, egresos de stock y auditoría.
3. En un punto de venta fiscal se solicitará a ARCA la autorización del comprobante y se almacenarán CAE, vencimiento, número y datos para QR cuando correspondan.
4. En un punto manual/no fiscal se emitirá el ticket configurado sin autorización de ARCA.
5. Si ARCA no responde o rechaza, la operación quedará en un estado explícito y recuperable; no deberá duplicarse al reintentar.
6. Finalizada la venta se mostrará un resumen con total, pagos y vuelto.
7. El usuario podrá imprimir, enviar o imprimir y enviar el comprobante.
8. El correo y teléfono podrán completarse o corregirse antes del envío, sin alterar los datos fiscales del comprobante.

### RF-13. Stock e ingresos

El stock se actualizará mediante:

- remitos internos recibidos del frigorífico o abastecedor;
- remitos de compra a proveedores externos;
- ventas confirmadas;
- ajustes autorizados, si se aprueba su inclusión para puesta en marcha.

Reglas:

1. Los productos trazables se controlan por correlativo individual y ubicación.
2. Los productos sin trazabilidad se controlan por cantidad o peso por producto y sucursal.
3. Cada movimiento conservará tipo, fecha, origen, documento, producto, cantidad/peso, correlativo si aplica, sucursal y usuario o proceso responsable.
4. Los remitos internos recibidos por integración no deben duplicarse al reprocesarse.
5. Los remitos externos podrán cargarse manualmente y pasar por estados borrador/confirmado.
6. Solo la confirmación del ingreso impactará stock.
7. El método de carga de stock inicial debe definirse antes de la salida productiva, aunque se implemente como herramienta de implantación.
8. Debe establecerse una política ante stock insuficiente; como criterio recomendado para MVP, la confirmación de la venta debe bloquearse salvo permiso excepcional y quedar auditada.

### RF-14. Caja y cierre de jornada

1. La caja registrará entradas y salidas por tipo de valor.
2. Cada movimiento tendrá empresa, sucursal, caja/punto de venta, turno o jornada, usuario, fecha, tipo, concepto, importe y referencia de origen.
3. El administrador podrá registrar retiros indicando tipo de valor, importe, motivo y destino.
4. El cierre informará, para cada tipo de valor:
   - saldo inicial;
   - ingresos de la jornada;
   - egresos, discriminando retiros, transferencias y vuelto;
   - saldo teórico final;
   - saldo contado y diferencia, si se aprueba cierre con arqueo.
5. El cierre quedará identificado por usuario y timestamp y no podrá alterarse sin una reapertura o ajuste auditado.
6. Debe definirse si la caja opera por sucursal, punto de venta, terminal, cajero o turno.

### RF-15. Consultas y reportes

1. La consulta de comprobantes permitirá filtrar como mínimo por fecha, sucursal, punto de venta, número, cliente, producto, cajero, estado y tipo de comprobante.
2. Desde el resultado se podrá ver el detalle y reimprimir o reenviar cuando corresponda.
3. Los resultados podrán exportarse, como mínimo, a CSV o XLSX; el formato definitivo debe acordarse.
4. El reporte de cierre de stock mostrará por producto y subtotal de familia, expresado en su unidad base y, cuando aplique, kilos:
   - stock inicial;
   - ingresos por remito interno;
   - ingresos por compra externa;
   - egresos por venta;
   - ajustes aprobados;
   - stock final.
5. Las estadísticas iniciales de venta deben definirse. Como propuesta mínima: ventas por período, sucursal, familia/producto, medio de pago y cajero; unidades/kilos vendidos, importe neto, impuestos, total y ticket promedio.

### RF-16. Auditoría

Se auditarán, como mínimo:

- inicio y cierre de sesión;
- altas y modificaciones de productos, precios y parámetros;
- apertura, cancelación y confirmación de ventas;
- cambios de cantidad, peso, precio y descuento;
- eliminación de ítems;
- medios de pago y vuelto;
- emisión, rechazo, reintento y envío de comprobantes;
- ingresos, ajustes y egresos de stock;
- retiros, cierres y reaperturas de caja;
- errores y reintentos de sincronización.

Cada evento debe registrar usuario o proceso, timestamp con zona horaria, empresa, sucursal, entidad afectada, acción, resultado y valores relevantes anteriores/nuevos cuando corresponda. Los registros de auditoría no podrán editarse desde la aplicación.

### RF-17. Integración con frigorífico/abastecedor

1. El sistema de carnicerías tendrá un modelo de datos propio e independiente.
2. La integración se realizará mediante contratos o tablas de intercambio versionados, no mediante dependencia directa del modelo interno legado.
3. Se recibirán artículos/remitos y datos de productos trazables necesarios para el ingreso de stock.
4. Se enviará el detalle completo de facturación necesario para Libro IVA.
5. La integración debe ser idempotente, trazable y capaz de reintentar sin duplicar movimientos.
6. El objetivo indicado por el PO es sincronización en tiempo real. La semántica exacta, los SLA y el comportamiento sin conexión requieren definición de arquitectura.
7. Permanecen abiertos:
   - actualización del estado `epr-código` o “salido de cámara”;
   - sincronización de todos los movimientos de caja o solo retiros/destinos específicos.

### RF-18. Notas de crédito, devoluciones y ajustes de ventas

El prototipo incorpora una pantalla específica de nota de crédito, por lo que este circuito se considera necesario para que el MVP pueda corregir operaciones productivas.

1. Un usuario autorizado podrá buscar un comprobante emitido por número, cliente y fecha.
2. La nota de crédito deberá vincularse obligatoriamente con el comprobante original y respetar las reglas fiscales de ARCA.
3. Debe definirse si el MVP admite anulación total, devolución parcial por ítems y/o ajuste de importes.
4. La pantalla debe mostrar el detalle del comprobante y permitir seleccionar los ítems, cantidades o importes a acreditar; no debe depender únicamente de un importe total editable.
5. El importe acreditado acumulado no podrá superar lo facturado para cada ítem ni el total pendiente de acreditar.
6. La confirmación debe registrar comprobante fiscal, conceptos tributarios, motivo, usuario, auditoría e impacto correspondiente en caja/cuenta corriente.
7. El reingreso de mercadería a stock no será automático en todos los casos: deberá indicarse si el producto fue devuelto y si está en condiciones de reincorporarse. Para correlativos, se validará el ID exacto y su estado.
8. La nota podrá imprimirse y enviarse por correo, conservando los datos del destinatario sin modificar el comprobante original.
9. Una nota autorizada no podrá borrarse; cualquier corrección posterior deberá realizarse mediante el documento fiscal que corresponda.

### RF-19. Configuración operativa de balanzas

El prototipo muestra administración de varias balanzas por puesto de venta.

1. Un administrador podrá registrar una o más balanzas y asignarlas a empresa, sucursal y terminal/punto de venta.
2. La configuración mínima incluirá nombre, marca/modelo, protocolo, tipo de conexión, puerto o dirección, parámetros técnicos requeridos y estado activo/inactivo.
3. Se deberá poder editar, probar conexión y consultar el último resultado de comunicación.
4. Una balanza utilizada en operaciones no se eliminará físicamente; se desactivará.
5. Si hay más de una balanza disponible, deberá definirse cuál se usa por defecto y cómo la selecciona el cajero.
6. Las credenciales o parámetros sensibles no se mostrarán completos a usuarios no autorizados.

## 7. Flujo principal de venta

1. El usuario inicia sesión y selecciona empresa/sucursal.
2. El sistema identifica punto de venta, caja y lista de precios.
3. El cajero inicia una venta con comprobante y cliente propuestos.
4. Agrega productos por botones, búsqueda, lector o balanza.
5. El sistema valida productos, correlativos, pesos y precios y actualiza el total.
6. El cajero ajusta cantidades o descuentos autorizados si es necesario.
7. Selecciona o carga cliente cuando el comprobante lo exige.
8. Finaliza la carga; el sistema valida venta, fiscalidad y stock.
9. Registra uno o varios medios de pago hasta cubrir el total y calcula vuelto.
10. Confirma la operación.
11. El sistema registra en forma consistente venta, pago, caja, stock y auditoría y emite el comprobante.
12. El cajero imprime y/o envía el comprobante.
13. El puesto queda disponible para una nueva venta.

## 8. Estados mínimos sugeridos

### Venta

`BORRADOR` → `PENDIENTE_PAGO` → `CONFIRMANDO` → `CONFIRMADA`

Estados alternativos: `CANCELADA`, `ERROR_FISCAL`, `PENDIENTE_EMISION` y `ANULADA`, esta última únicamente mediante documento fiscal o proceso válido, no por borrado.

### Remito de ingreso

`BORRADOR`/`RECIBIDO` → `VALIDADO` → `CONFIRMADO` → `ANULADO`

### Sincronización

`PENDIENTE` → `EN_PROCESO` → `ENVIADO`, con alternativas `ERROR_REINTENTABLE` y `ERROR_DEFINITIVO`.

### Caja/jornada

`ABIERTA` → `EN_CIERRE` → `CERRADA`, con `REABIERTA` solo mediante permiso y auditoría.

## 9. Modelo conceptual mínimo

- Empresa, sucursal, terminal/punto de venta y caja/jornada.
- Usuario, rol, permiso y asignación de empresa/sucursal.
- Producto, categoría/familia, unidad, código alternativo, configuración tributaria y modalidad de trazabilidad.
- Lista de precios, vigencia y detalle de precio.
- Unidad trazable/correlativo, estado, peso, origen y ubicación.
- Stock agregado y movimiento de stock.
- Cliente, condición fiscal y cuenta corriente.
- Venta, ítem, concepto fiscal, comprobante y estado fiscal.
- Medio de pago, pago y movimiento de caja.
- Remito de ingreso y detalle.
- Evento de auditoría.
- Mensaje de integración, intento, resultado e identificador idempotente.

## 10. Requerimientos no funcionales

### RNF-01. Plataforma y experiencia

- Interfaz optimizada para pantalla táctil y operación rápida en caja.
- Administración y consultas disponibles vía web.
- La necesidad de Electron para el puesto de venta debe confirmarse según la estrategia de integración con periféricos y operación offline.
- Las acciones frecuentes deben requerir la menor cantidad razonable de interacciones y admitir uso de teclado/lector.

### RNF-02. Rendimiento

- La búsqueda local de productos y la actualización del detalle deberían responder en menos de 500 ms en condiciones normales.
- Las lecturas de código o balanza deberían reflejarse en pantalla en menos de 1 segundo, sin contar latencia de hardware externo.
- Los tiempos y volúmenes objetivo deben validarse mediante pruebas con un catálogo y carga representativos.

### RNF-03. Disponibilidad y contingencia

- Una caída de una integración externa no debe producir ventas duplicadas ni pérdida silenciosa de información.
- Las operaciones pendientes deben conservarse y reintentarse de manera controlada.
- Debe definirse si el MVP permitirá facturar y cobrar completamente sin conectividad o si solo encolará integraciones después de registrar localmente una operación autorizada.

### RNF-04. Seguridad

- Contraseñas almacenadas con un algoritmo de hash robusto y salado; nunca en texto plano.
- Comunicación cifrada mediante TLS.
- Autorización del lado servidor para toda acción sensible.
- Separación estricta de datos entre empresas.
- Sesiones con expiración y revocación en logout.
- Protección de secretos y certificados fiscales fuera del código fuente.
- Política de retención, respaldo y restauración acorde con información fiscal y comercial.

### RNF-05. Integridad e idempotencia

- Las confirmaciones de venta, emisión fiscal y sincronización deben usar identificadores idempotentes.
- Los totales monetarios deben manejarse con tipos decimales, reglas explícitas de redondeo y sin punto flotante binario.
- Toda fecha transaccional debe almacenarse con zona horaria o en UTC y mostrarse en la zona de la sucursal.

### RNF-06. Observabilidad y soporte

- Registro estructurado de errores y correlación de una operación a través de venta, ARCA, stock y sincronización.
- Panel o consulta de operaciones pendientes y fallidas con posibilidad de reintento autorizado.
- Alertas para errores fiscales o de sincronización que requieran intervención.

### RNF-07. Tecnologías indicadas

- Backend: .NET.
- Base de datos: PostgreSQL.
- Frontend administrativo: web.
- Puesto de venta: web o Electron, pendiente de decisión técnica.
- Impresión: comandera desde el puesto de venta.
- Hardware: balanzas compatibles con modelos/protocolos a relevar y lectores que operen como teclado o mediante interfaz definida.

## 11. Criterios de aceptación del MVP

El MVP estará funcionalmente aceptado cuando, en un entorno de prueba representativo:

1. Un usuario solo pueda operar empresas, sucursales y funciones autorizadas.
2. Se pueda dar de alta y parametrizar un producto trazable, uno a granel y uno EAN, asignar precios e impuestos y venderlos.
3. Se pueda cargar una venta desde botonera/búsqueda y desde cada uno de los tres tipos de etiqueta acordados.
4. Se pueda obtener peso desde al menos un modelo de balanza objetivo y continuar manualmente ante desconexión.
5. La venta descuente el correlativo exacto o los kilos/unidades correctos sin duplicidad.
6. Se pueda cobrar con un medio y con medios combinados, incluyendo vuelto de efectivo, y los movimientos coincidan con caja.
7. Se emita una factura electrónica autorizada en homologación ARCA y se controle un rechazo o indisponibilidad sin duplicar el comprobante.
8. Se emita un ticket desde un punto de venta no fiscal.
9. El comprobante pueda imprimirse y enviarse por correo.
10. Un remito interno integrado y uno externo manual actualicen stock una sola vez.
11. Se pueda registrar un retiro y cerrar la caja, obteniendo saldos trazables por tipo de valor.
12. El cierre de stock cuadre con stock inicial + ingresos − egresos ± ajustes.
13. Se puedan consultar y exportar comprobantes y obtener las estadísticas mínimas acordadas.
14. La facturación requerida para Libro IVA se sincronice o quede en una cola visible y reintentable.
15. Todas las operaciones críticas queden identificadas por usuario, fecha, empresa y sucursal.
16. Se pueda alternar entre ventas en borrador sin mezclar ni perder su información.
17. Se pueda emitir en homologación una nota de crédito vinculada al comprobante original, con impactos consistentes en fiscalidad, caja y stock.
18. Se pueda configurar, probar y desactivar una balanza asociada a un puesto de venta.

## 12. Contradicciones y decisiones adoptadas provisionalmente

| Tema | Fuentes | Decisión provisional |
|---|---|---|
| Plataforma POS | Un análisis indica Electron; el PO describe aplicación web. | Mantener backend y UI web; decidir Electron solo por periféricos/offline. |
| Eliminación de productos | ERS propone borrado físico; el resto exige historia y stock trazable. | No borrar productos con movimientos; usar inactivación. |
| Precio dentro del producto | ERS incluye precio de venta; alcance general exige listas de precios. | El precio pertenece a una lista con vigencia; no es un único campo mutable del producto. |
| Categoría/unidad obligatorias | La descripción las presenta sin asterisco, pero aceptación las exige. | Se consideran obligatorias. |
| Sobrepago | Una ERS dice que el monto no debe exceder el total y también exige vuelto. | Solo efectivo puede exceder el saldo y generar vuelto. |
| Factura/ticket | Se mencionan Factura A/B, ticket y “tipo factura B por defecto”. | El tipo válido depende de punto de venta, cliente y reglas ARCA; B/consumidor final será solo una propuesta. |
| Carga rápida de producto | ERS permite cargar precio y peso para un producto inexistente. | No crear productos implícitamente; evaluar ítem genérico controlado y auditado. |
| Descuentos | ERS de detalle los incluye; PO los ubica en Etapa 3 como recurrentes/promociones. | Se admite descuento manual por ítem con permiso y tope; promociones automáticas quedan fuera. |
| Operación offline | PO exige cola ante falta de conectividad pero no define qué operaciones continúan. | Requisito abierto de arquitectura; no prometer emisión fiscal offline hasta definir contingencia. |

## 13. Preguntas abiertas para cerrar el MVP

### Prioridad bloqueante — necesarias antes de arquitectura o estimación final

1. **Tenencia y despliegue:** ¿el sistema será SaaS centralizado, una instalación por cliente o una instalación local sincronizada? ¿Base compartida con tenant o base independiente por empresa?
2. **Offline:** cuando una carnicería pierde Internet, ¿debe poder seguir cobrando y emitiendo tickets? Para puntos fiscales, ¿qué contingencia aprobada se utilizará si ARCA no está disponible?
3. **Caja:** ¿se abre y cierra por sucursal, punto de venta, terminal, cajero o turno? ¿Habrá fondo inicial, arqueo físico y control de diferencias?
4. **Integración frigorífico:** ¿qué sistema es dueño del stock trazable? ¿Debe actualizarse `epr-código` al vender? ¿Qué movimientos de caja deben sincronizarse?
5. **Contratos de integración:** ¿existen APIs o solo acceso a tablas? ¿Quién provee diccionario, credenciales, ambientes, identificadores y reglas de reintento?
6. **Facturación:** ¿qué comprobantes exactos entran (A, B, C, notas de crédito/débito)? ¿Qué condiciones fiscales y topes debe validar el MVP?
7. **Anulación/devolución:** ¿cómo se corrige una venta confirmada y cómo se restituyen stock, caja y cuenta corriente? Este circuito no aparece definido y es indispensable para producción.
8. **Hardware:** ¿qué marcas/modelos y protocolos de balanza, lector y comandera deben certificarse? ¿Conexión USB/serie/red y drivers disponibles?
9. **Stock inicial:** ¿se cargará por importación, conteo manual o sincronización? Debe existir un método antes de la puesta en marcha aunque sea una herramienta de implantación.
10. **Stock negativo:** ¿se bloquea, se permite con autorización o se admite siempre para productos a granel?

### Prioridad alta — necesarias antes de desarrollar cada módulo

11. ¿Las empresas comparten catálogo de productos o cada empresa tiene catálogo, categorías, impuestos y códigos propios?
12. ¿Cómo se asignan listas de precios: por empresa, sucursal, cliente, canal o fecha? ¿Se necesita más de una lista vigente en una venta?
13. ¿El precio leído desde etiqueta de balanza debe respetarse o recalcularse con peso × lista vigente? ¿Cómo se resuelven diferencias?
14. ¿Cuál es la estructura exacta de códigos SYSTEL/balanza y de las etiquetas de trazabilidad del frigorífico?
15. ¿Un correlativo trae peso fijo? ¿Puede venderse parcialmente o siempre sale completo?
16. ¿Qué datos mínimos se requieren en el alta rápida de cliente y quién puede modificar clientes existentes?
17. ¿Cómo funciona la cuenta corriente: es un medio de pago, admite pagos parciales, recibos y consulta de saldo dentro del MVP?
18. ¿Qué descuentos manuales se permiten, cuál es el máximo y qué rol puede superar el tope?
19. ¿Las percepciones se calculan con parámetros propios o se reciben desde el sistema existente? ¿Cuáles son las reglas y padrones necesarios?
20. ¿Qué estados y acciones se permiten ante rechazo, timeout o autorización tardía de ARCA?
21. ¿Qué formatos de impresión y tamaños de comandera se requieren? ¿Se necesita copia, logo, QR y corte automático?
22. ¿Qué servicio enviará correos y qué política de reintentos, remitentes y adjuntos se requiere?

### Prioridad de producto — necesarias para aceptar reportes y experiencia

23. ¿Cuáles son las métricas de Etapa 1 y su nivel de desglose? Confirmar propuesta: período, sucursal, familia/producto, medio de pago, cajero, kilos/unidades, total y ticket promedio.
24. ¿Qué exportaciones se requieren: CSV, XLSX, PDF? ¿Deben respetar permisos y volumen máximo?
25. ¿Cuántos productos se muestran por categoría y qué criterio de orden se usa?
26. ¿Cómo se configuran favoritos: globales, por sucursal o por cajero?
27. ¿Se requiere apertura automática del cajón de dinero?
28. ¿Cuál es el volumen esperado de empresas, sucursales, terminales, productos, correlativos, ventas diarias y usuarios concurrentes?
29. ¿Cuál es el tiempo máximo aceptable de recuperación ante una caída y cuánto historial debe conservarse en línea?
30. ¿Quién validará que el prototipo de Figma y este documento representen el mismo flujo y qué versión del prototipo se congelará para desarrollo?
31. ¿Los cuatro tickets del prototipo representan ventas simultáneas? ¿El límite es cuatro, se pueden crear más y deben sobrevivir a cierre de sesión o reinicio de terminal?
32. Para notas de crédito, ¿se requiere devolución total, parcial por ítems o ambas? ¿Qué motivos, permisos y reglas de reposición de stock aplican?
33. ¿La pantalla de cierre es solo un resumen o debe permitir arqueo, confirmación y cierre definitivo? ¿El correo/teléfono corresponden al destinatario del reporte de cierre?
34. ¿“Agregar producto” en el POS abre el alta completa de un artículo, carga un ítem genérico o solo permite una búsqueda manual avanzada?
35. ¿Por qué el prototipo permite combinar `Factura A` con `Consumidor final`? Debe definirse si el cliente se limpia/cambia automáticamente o si la combinación se bloquea.

## 14. Análisis del prototipo visual

### 14.1 Pantallas funcionales identificadas

| Pantalla | Capacidades observadas | Cobertura documental |
|---|---|---|
| Puesto de venta vacío | Categorías laterales, búsqueda, alta/agregado manual, tipo de facturación, cliente, cancelación, total, finalización y cuatro tickets. | Cubre parcialmente RF-05 a RF-09; agrega multiticket. |
| Puesto de venta con productos | Botonera de artículos con precio, detalle con peso, cantidad, precio, descuento, total y eliminación. | Alineada con selección táctil y detalle de venta. |
| Alta/edición de artículo | Fecha, código, denominación, categoría, unidad, estado, costo, precio e historial. | Alineada con la ERS parcial, pero insuficiente para RF-03/RF-04. |
| Configuración de balanzas | Lista de balanzas, alta y eliminación. | Confirma configuración de múltiples balanzas; origina RF-19. |
| Retiro de caja | Fecha/hora, importe, comentarios, guardar/cancelar. | Cubre solo una parte de RF-14. |
| Cierre de caja | Ventas por medio, retiros, notas de crédito, saldos, correo/teléfono e impresión/envío. | Cubre el resumen, pero no representa apertura, arqueo ni confirmación. |
| Nota de crédito | Búsqueda/selección de factura, datos del cliente/comprobante, comentario, impresión y envío. | Funcionalidad no desarrollada en las ERS originales; origina RF-18. |

Los cuatro archivos denominados `Recurso 4PNG` son recursos decorativos y no aportan comportamiento funcional.

### 14.2 Hallazgos positivos

- El POS concentra categorías, productos y detalle en una sola vista, adecuado para una operación táctil rápida.
- Los botones de producto muestran denominación y precio antes de seleccionar.
- El detalle diferencia peso y cantidad, coherente con la naturaleza mixta del catálogo.
- La cancelación de venta y eliminación por ítem están visibles.
- La existencia de tickets paralelos puede resolver atención intercalada de varios clientes.
- Caja dispone de vistas diferenciadas para retiro y cierre.
- El cierre resume ventas por medio de pago e incluye notas de crédito.
- La nota de crédito parte de un comprobante existente, lo cual favorece trazabilidad.
- La configuración contempla más de una balanza.

### 14.3 Brechas funcionales o riesgos del diseño

#### Puesto de venta

- La combinación visible `Factura A` + `Consumidor final` es fiscalmente inconsistente y debe impedirse o corregirse automáticamente.
- `Ticket A/B/C/D` puede confundirse con tipos de comprobante fiscal. Se recomienda denominarlos `Venta 1`, `Venta 2` o usar nombre/número de pedido.
- No se ve el estado de conexión de balanza, lector, ARCA ni sincronización; son indicadores operativos necesarios.
- No se distingue visualmente producto por peso, unidad o correlativo antes de seleccionarlo.
- El detalle no muestra correlativo, estado/reserva de stock ni origen del peso.
- No se observa paginación, desplazamiento o indicador de más productos en la botonera.
- El botón Finalizar aparece habilitado incluso en la pantalla vacía y se muestra un total distinto de cero; debe depender del estado real de la venta.
- La grilla usa columnas de peso y cantidad simultáneamente; debe definirse cómo se representa un campo no aplicable para evitar valores engañosos como peso `0`.
- No hay pantalla exportada de captura de peso, lectura de etiqueta, cliente nuevo, medios combinados, autorización fiscal o resultado de venta.
- “Agregar producto” es ambiguo y podría permitir eludir catálogo, impuestos, lista de precios o stock.

#### Artículos y precios

- Faltan modalidad de stock/trazabilidad, modalidad de venta, EAN/PLU, IVA, percepciones y códigos alternativos.
- El precio aparece como atributo directo del artículo; no representa listas, vigencia ni asignación por sucursal.
- El historial solo prevé fecha, precio y costo; debería cubrir al menos usuario y valores anterior/nuevo.
- No se observan acciones de inactivación, permisos ni validaciones.

#### Balanzas

- Las tarjetas no permiten identificar claramente modelo, conexión, terminal asignada, estado o última comunicación.
- Solo se observa alta y eliminación; faltan editar, probar conexión y activar/desactivar.
- El ícono de papelera sugiere borrado físico de configuración con historia operativa.

#### Retiro y cierre de caja

- El retiro no solicita tipo de valor, motivo estructurado, destino, caja ni usuario responsable visibles.
- “Comentarios” no reemplaza un motivo parametrizado si se requieren reportes confiables.
- El cierre no muestra saldo inicial, egresos discriminados, saldo teórico vs. contado ni diferencia.
- No se ve una acción de confirmar cierre ni el estado de la jornada.
- El panel “Saldo” agrupa `Otros`, perdiendo detalle por tipo de valor solicitado por el PO.
- Los controles de enviar/imprimir parecen incompletos o inconsistentes: “Enviar e imprimir” se presenta como texto y no como acción clara.
- No queda claro por qué el cierre solicita correo y teléfono, ni qué canal utiliza el teléfono.

#### Nota de crédito

- La selección mediante casillas sugiere selección múltiple, pero el formulario representa una sola factura.
- No se muestran ítems ni cantidades a devolver, por lo que no soporta de forma clara una devolución parcial.
- No aparece motivo estructurado, impacto en stock, devolución del pago ni medio de reintegro.
- Se usa “Recibo” donde el título indica factura/nota de crédito; debe unificarse la terminología fiscal.
- La fecha de vencimiento requiere definición: puede ser vencimiento del CAE, no una fecha libre del documento.

### 14.4 Ajustes UX recomendados para el MVP

1. Validar automáticamente la compatibilidad entre comprobante, condición fiscal y cliente.
2. Mostrar estados de periféricos y servicios externos sin ocupar el área principal de venta.
3. Identificar en cada producto si se vende por peso, unidad o correlativo.
4. Mostrar un único campo operativo `Cantidad/Peso` con unidad explícita, manteniendo el detalle técnico internamente.
5. Renombrar los tickets paralelos para que no se confundan con comprobantes fiscales y mostrar producto/cliente/resumen en cada pestaña.
6. Deshabilitar Finalizar compra cuando el borrador no sea válido y mostrar claramente el motivo.
7. Separar “buscar/agregar un producto existente” de “crear artículo” y restringir la segunda acción por rol.
8. Incorporar arqueo y confirmación explícita al cierre de caja.
9. Diseñar la nota de crédito desde los ítems del comprobante original y mostrar sus efectos antes de confirmar.
10. Agregar estados vacíos, de carga, error, desconexión, rechazo y reintento para todos los procesos externos.

## 15. Propuesta de cortes de entrega

La siguiente secuencia reduce riesgos sin cambiar el alcance funcional:

1. **Base operativa:** tenancy/empresas, usuarios, permisos, sucursales, maestros, listas de precios y auditoría.
2. **Venta local:** interfaz táctil, búsqueda, detalle, clientes, impuestos, pagos y caja sin integración fiscal final.
3. **Stock y periféricos:** correlativos, stock a granel, remitos, lector, balanza e impresión.
4. **Fiscalidad:** ARCA, comprobantes, contingencias, envío y consulta.
5. **Integración y resiliencia:** intercambio con frigorífico, colas, idempotencia, monitoreo y recuperación.
6. **Cierre del MVP:** cuenta corriente acordada, reportes, estadísticas, pruebas integrales, seguridad, rendimiento y salida controlada.

## 16. Definition of Ready sugerida

Una historia estará lista para desarrollo cuando tenga:

- actor y empresa/sucursal afectados;
- flujo principal y alternativas;
- reglas y validaciones sin ambigüedad;
- permisos requeridos;
- impacto en stock, caja, fiscalidad y auditoría;
- datos de entrada/salida y estados;
- comportamiento ante error, reintento y cancelación;
- criterios de aceptación verificables;
- diseño de interfaz validado cuando corresponda;
- dependencias externas y datos de prueba disponibles.

## 17. Próximo paso recomendado

Realizar una sesión de definición con PO, referente fiscal/contable, arquitectura y un usuario operativo de carnicería. La sesión debería resolver primero las diez preguntas bloqueantes, validar el flujo completo de una venta normal y sus excepciones, y cerrar un acta de decisiones. Con esas respuestas se puede convertir este documento en una versión 1.0 aprobada y descomponerlo en épicas e historias estimables.
