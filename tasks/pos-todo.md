# Desarrollo del punto de venta

**Prioridad:** acordada el 6 de octubre de 2026. El módulo de administración de usuarios queda en pausa para centrar el desarrollo en el POS y permitir probar el producto desde Electron.

## Estado del flujo de venta (7 de octubre de 2026)

- El catálogo real, el borrador, la reserva de stock, el turno de cajero y la confirmación de venta ya están conectados al backend. Una confirmación registra venta, pagos, caja y egreso de stock en una transacción.
- Al reabrir Electron, el ticket recuperado conserva su identificador y puede cobrarse. El POS no habilita el cobro mientras aún se guarda un cambio del detalle.
- El cobro muestra el saldo pendiente antes de confirmar y, tras una confirmación exitosa, presenta el total registrado, los medios aplicados, el vuelto y la referencia de la operación. No se emite comprobante fiscal.
- La prueba automatizada de Angular cubre restauración, guardados consecutivos, pago insuficiente y resumen final. En Electron se verificaron apertura, guardado/recuperación del ticket y cierre de turno; la venta persistida en la sesión se comprobó en PostgreSQL. Falta observar directamente el clic de confirmación y el resumen inmediato posterior al cobro, sin interacción concurrente.
- Recargar `/pos` en Electron ya conserva la aplicación y el ticket: el protocolo entrega `index.html` a esa ruta Angular. La prueba de regresión y las 6 pruebas de Electron pasan.
- La administración de usuarios y la facturación fiscal permanecen fuera del foco actual.

## Corte previo (referencia histórica)

- Pantalla de punto de venta alineada a `Documentos/maqueta/Landing Puesto de venta 1 - Desktop-1.png`.
- Acceso desde la sesión autenticada y el contexto operativo confirmado.
- Categorías, búsqueda desde tres caracteres, selección por peso o unidad, acumulación de productos repetidos, edición de cantidad/peso, eliminación confirmada y total calculado.
- Ingreso rápido del código de demostración desde el buscador: Enter agrega una unidad o abre la carga manual de peso, según el producto.
- Selector conectado a las listas habilitadas para la sucursal; las listas reales consultan categorías, productos, códigos y precios vigentes del servidor. Las opciones de prueba permiten recorrer el flujo y bloquean el cambio mientras el ticket tenga líneas.
- Electron abre maximizado para aprovechar el área útil de la pantalla; catálogo y detalle desplazan internamente cuando crecen.
- Los tickets del catálogo real se guardan como borrador en PostgreSQL, se recuperan al volver al POS y se cancelan desde el servidor. El backend valida lista, productos, modalidad, cantidades y precios; no acepta importes del cliente.
- El borrador congela descripción, código, unidad, modalidad y precio. Reserva stock agregado al guardar y lo libera al quitar/cancelar; el stock físico no baja hasta confirmar. Los productos de demostración continúan sin persistencia.
- Stock agregado por sucursal: consulta desde el catálogo y panel del administrador para ingresos/ajustes con motivo; movimientos idempotentes y protegidos por permiso. El saldo no puede quedar debajo de lo ya reservado por tickets.
- El botón de finalización ya conecta con el cobro real; no se emiten comprobantes fiscales.
- La balanza figura como no conectada; el peso se ingresa manualmente.
- Una confirmación protege contra salir de la pantalla con productos en la venta de demostración.

## Pendientes del POS

1. Resolver si el MVP admite cuatro tickets simultáneos por terminal. La maqueta muestra Ticket A–D, pero el relevamiento considera esta decisión pendiente; por ahora solo Ticket A está habilitado.
2. Continuar `catalog-pricing`: ya existen listas por sucursal, categorías, productos, códigos alternativos, historial de precios, `GET /api/catalog/price-lists`, `GET /api/catalog/categories` y `GET /api/catalog/products` conectados al POS. Falta la gestión para cargar y mantener esos datos. Contrato en `SPEC-catalog-pricing.md`.
3. Extender borradores a varios tickets por terminal y agregar identificación de terminal; el corte actual permite uno por usuario/sucursal. Contrato en `SPEC-pos-sales.md`.
4. Incorporar correlativos trazables y definir la excepción autorizada por falta de stock; la conversión de reserva en egreso junto al cobro ya funciona.
5. Completar edición del detalle, permisos de descuento, impuestos y cancelación auditada según `SPEC-pos-sales.md`.
6. Ampliar `payments-cash` con medios parametrizados y arqueo de cierre; la apertura, pagos combinados, saldo y vuelto ya funcionan.
7. Integrar periféricos y facturación fiscal solamente cuando estén definidos sus contratos y configuración.

## Límites restantes

- Las ventas confirmadas con una lista real se persisten. Los datos iniciales del catálogo son ficticios y deben reemplazarse antes de operar comercialmente.
- El catálogo de prueba permanece solo en memoria y se pierde al recargar Electron. Los borradores de ventas con lista real ya se guardan y recuperan.
- El cambio de sucursal queda bloqueado mientras haya un borrador o turno abierto.
- Clientes registrados, descuentos, impuestos, correlativos trazables, balanza, lector y ARCA aún no están integrados.
- La administración de usuarios permanece disponible como módulo previo, pero su desarrollo queda pausado por prioridad del POS.

## Cierre de venta y caja por cajero/turno

Alcance aprobado por el usuario el 6 de octubre de 2026; contrato en `SPEC-pos-checkout.md`, secuencia técnica en `tasks/pos-checkout-plan.md`.

- [x] Persistir apertura/consulta/cierre del turno propio y bloquear cambio de contexto mientras siga abierto.
- [x] Implementar y probar las reglas de pagos combinados y vuelto solo efectivo.
- [x] Confirmar borrador real en una transacción: venta, pagos, caja y egreso de stock con idempotencia.
- [x] Conectar apertura, pagos y resumen al POS sin habilitar ventas demo reales.
- [ ] Completar el recorrido integrado desde Electron: observar directamente la confirmación y el resumen final del cobro. Apertura, ticket recuperado y cierre de turno ya fueron comprobados; evidencia y límites en `tasks/pos-checkout-plan.md`.
