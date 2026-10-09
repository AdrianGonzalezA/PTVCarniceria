# 04 · Administración: Negocio

Ingresá en **Administración → Negocio** con un usuario autorizado. Esta sección reúne consultas y operaciones comerciales; las opciones visibles dependen de los permisos. El contexto de empresa y sucursal aparece arriba y puede cambiarse desde el inicio.

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

## Existencias y recepción

**Existencias** consulta por artículo la cantidad física, la reservada por tickets abiertos y la disponible en la sucursal. **Ajustar** registra una variación positiva o negativa con motivo; el sistema no permite dejar la existencia por debajo de lo reservado.

**Recepción de piezas** registra una pieza por vez a partir de un perfil de lectura guardado, artículo vendido por peso, origen y código de barras. La recepción suma kilos al stock de la sucursal y conserva el identificador de la pieza; la lista muestra las últimas recibidas. Para probar o crear un formato de etiqueta se usa **Configuración → Lectura de etiquetas**. La importación masiva desde frigorífico/abastecedor todavía no está disponible.

Base técnica: [reportes](../../SPEC-reporting.md), [cuentas](../../SPEC-customers-credit.md) y [piezas](../../SPEC-inventory-traceability.md).
