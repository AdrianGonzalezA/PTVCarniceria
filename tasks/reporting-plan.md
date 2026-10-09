# Plan aprobado: Negocio y Configuración del administrador

Origen: `SPEC-reporting.md`, clasificación confirmada por el usuario el 8/10/2026. No reemplaza `tasks/plan.md` ni `tasks/todo.md`, que contienen trabajo de otros módulos. Una sola base: `carnicerias_test_visual`.

## Cortes y verificaciones

### R1. Navegación con dos áreas

- [x] `/admin` abre Negocio; Configuración y Negocio son enlaces principales accesibles por teclado y con estado activo, también en páginas internas.
- [x] Los ABM y la lectura de etiquetas quedan en Configuración; Historial, Existencias y Recepción de piezas en Negocio. Los enlaces internos actuales siguen funcionando y conservan permisos.
- [x] Tests de navegación/autorización Angular; build/lint; revisión visual en Electron a 1280×720.

### R2. Resumen real del negocio

- [x] API de solo lectura autorizada por empresa, rango UTC y sucursal opcional. Rechaza filtros inválidos y no mezcla ventas, cobros de venta ni cargos a cuenta.
- [x] Tablero con cantidad/importe de ventas, cobros inmediatos y nuevos cargos a cuenta. Muestra cargos brutos acumulados, no deuda neta; estados vacío/error/carga y fecha local visible.
- [x] Tests unitarios del rango y de interfaz; consulta manual no destructiva sobre la única base, validación de sucursal e importes y revisión en Electron.

**Verificación del corte R1–R2 (8/10/2026, Argentina):** Angular 85 pruebas y lint/build correctos; .NET 7 pruebas del rango y compilación correctas; Electron 45 pruebas. Electron a 1280×720 confirmó 19 tickets, $531.215 vendidos = $479.715 de pagos aplicados + $51.500 de cargos a cuenta para el día local consultado. Sucursal desconocida da 404; GUID vacío, 400. El protocolo Electron ahora permite recargar directamente `/admin/pieces` y `/admin/configuracion`, confirmado en la aplicación. No se creó otra base ni se alteraron ventas, stock o cargos; la sesión autenticada sí actualiza su marca de actividad. La integración automatizada que crea o reinicia base continúa pendiente.

### R3. Estado de cuenta y siguientes indicadores

- [x] Vincular la lista y detalle de clientes al área Negocio; mostrar cargos, recibos, imputaciones, anticipos aplicados y correcciones persistidas, con paginación.
- [x] Agregar al resumen cajas abiertas, efectivo en turnos abiertos, cobranzas, devoluciones y deuda neta sin sumar fondo inicial a ventas.
- [ ] Pruebas de aislamiento, dobles conteos y período; revisión de reglas con el usuario antes de interpretar cifras como fiscales.

**Verificación R3 (9/10/2026):** resumen y estado de cuenta consultados en Electron desde la única base. Antes de una corrección se mostraron $51.500 de cargos acumulados, $50.500 de deuda, $1.500 de cobranzas y $0 de saldo a favor luego de aplicar $500 a una venta. Tras reintegrar $1.000, la deuda quedó en $51.500, devoluciones en $1.000 y cobranzas netas de efectivo en $500. Una reasignación previa conservó el ingreso original. Los importes son comerciales no fiscales y la vista de caja abierta incluye fondos iniciales de forma explícita. Las pruebas de aislamiento automatizadas que provisionan otra base no se ejecutan.

## Dependencias y límites

R1 precede a R2 y R3. R3 depende también del avance de cobranzas en `tasks/customers-credit-plan.md`. No se crean bases de prueba, tablas de totales editables ni datos ficticios para el dashboard. Las cifras actuales son comerciales no fiscales. Los tests de integración que provisionan otra base quedan pendientes.
