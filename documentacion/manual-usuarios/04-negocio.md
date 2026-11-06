# 04 · Administración: Negocio

Ingresá en **Administración → Negocio** con un usuario autorizado. Esta sección reúne consultas y operaciones comerciales; las opciones visibles dependen de los permisos. El contexto de empresa y sucursal aparece arriba. Solo en un servidor compartido puede cambiarse la sucursal desde el inicio; la instalación de una sucursal queda vinculada a ella.

## Resumen de hoy

El panel permite ver todas las sucursales o filtrar una. Muestra ventas confirmadas, importe vendido, cobrado en ventas, nuevos cargos a cuenta, saldo a favor aplicado, cobranzas/devoluciones de cuenta, cajas abiertas, efectivo de turnos abiertos y cobros por medio.

**Importe vendido** incluye lo cargado a cuenta; **cobrado en ventas** refleja pagos aplicados. **Efectivo en cajas abiertas** es saldo contable, no arqueo físico. El panel no es un reporte de facturación fiscal.

## Ventas e historial

En **Ventas e historial** podés consultar **Ventas**, **Turnos**, **Caja y pagos** y **Movimientos de stock**. Usá los filtros de sucursal, caja y fechas, y **Ver detalle** para inspeccionar una operación. Es una vista de consulta: no modifica una venta ya confirmada.

## Estados de cuenta

Buscá un cliente por código o nombre y abrí **Ver estado**. Se muestran deuda pendiente, saldo a favor, ventas a cuenta, recibos internos y anticipos aplicados a ventas. Si un recibo se registró erróneamente, **Corregir** distingue dos casos:

- **Reasignar cuenta:** el dinero efectivamente ingresó, pero debe imputarse a otro cliente. No produce una salida de caja.
- **Revertir caja:** el dinero no ingresó o fue devuelto. Requiere registrar el motivo y afecta el ingreso contable.

La corrección interna no ejecuta por sí sola un reintegro en un banco, tarjeta o pasarela. Revisá el hecho real antes de elegir el tipo.

Cada recibo muestra los medios aplicados y el vuelto en efectivo. Una **reasignación** conserva ese desglose. Si el recibo incluye Mercado Pago, la opción **Revertir caja** queda deshabilitada: primero debe implementarse y confirmarse la devolución en la pasarela. La reasignación contable sigue disponible cuando corresponde.

## Existencias y recepción

**Existencias** consulta por artículo la cantidad física, la reservada por tickets abiertos y la disponible en la sucursal. **Ajustar** abre un modal para ese artículo y registra una variación positiva o negativa. La descripción es opcional; si se deja vacía, el movimiento conserva «Ajuste manual» como motivo de auditoría. El sistema no permite dejar la existencia por debajo de lo reservado.

Para cargar varios artículos, usá **Descargar plantilla** e **Importar Excel** en Existencias. Completá `CodigoArticulo` con el código principal y `Variacion` con un número: positivo suma y negativo resta al saldo actual de la sucursal seleccionada. `Descripcion` es opcional. La hoja **Ejemplos** no se importa. Primero pulsá **Previsualizar** para revisar los saldos resultantes; después **Confirmar importación**. Si una fila es inválida o deja el stock por debajo de lo reservado, no se aplica ninguna. Esta carga no crea piezas trazables; ver [reglas de importación](../../SPEC-stock-import.md).

**Recepción de piezas** registra una pieza por vez a partir de un perfil de lectura guardado, artículo vendido por peso, origen y código de barras. La recepción suma kilos al stock de la sucursal y conserva el identificador de la pieza; la lista muestra las últimas recibidas. Para probar o crear un formato de etiqueta se usa **Configuración → Lectura de etiquetas**. La importación masiva desde frigorífico/abastecedor todavía no está disponible.

## Conteo y agrupador anatómico

En **Conteo físico** creá un borrador, registrá cada artículo observado y envialo a revisión. Un administrador aprueba o rechaza el documento con motivo. Si el saldo cambia mientras se cuenta, se exige un conteo nuevo. Los artículos configurados para **Solo medición de masa** informan kilos observados sin crear un saldo teórico por corte. El detalle muestra los kilos por agrupador anatómico cuando el artículo lo tenía definido al momento de registrar la línea. Cambiar después el agrupador del artículo no reescribe ese conteo.

El agrupador es opcional en **Configuración → Artículos** y en su plantilla Excel (`AgrupadorAnatomico`). Sirve para consulta y agrupación de masa; no reemplaza el artículo, la pieza ni su identificador individual.

## Producción

En **Negocio → Producción**, un usuario con permiso de producción abre una orden eligiendo un tipo configurado en **Configuración → Tipos de producción**. Puede agregar varios insumos y varios productos obtenidos, por peso o unidad. Un insumo trazable se elige por su identificador y queda reservado para esa orden mientras esté abierta; otra orden no puede usarlo. Al cancelar la orden o quitar la línea se libera.

Para una salida trazable, registrar cada pieza por separado con su código, identificador y kilos reales, y preparar su etiqueta antes del cierre. El detalle informa ingreso, salida, merma y diferencia de masa. Al cerrar, se registran juntos el consumo, las salidas y los movimientos de stock. La orden cerrada no se edita; **Crear corrección** abre otra orden vinculada. El balance por tipo reúne las órdenes cerradas de la sucursal. La impresión de etiquetas desde el navegador debe verificarse con la impresora real del puesto.

Base técnica: [reportes](../../SPEC-reporting.md), [cuentas](../../SPEC-customers-credit.md) y [piezas](../../SPEC-inventory-traceability.md).
