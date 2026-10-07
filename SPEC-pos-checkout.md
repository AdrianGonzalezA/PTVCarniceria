# Especificación: cierre de venta y caja por cajero/turno

## Estado

Corte implementado para caja asociada al cajero y a su turno, dentro de la sucursal activa. La identidad de caja fue definida por el usuario el 6 de octubre de 2026. La verificación integrada y sus límites están registrados en `tasks/pos-checkout-plan.md`.

## Objetivo

Permitir que el cajero abra su turno, registre pagos combinados y confirme un borrador real como venta. La confirmación debe guardar venta, renglones, pagos, movimientos de caja y egreso de stock en una sola transacción. Los reintentos no deben duplicar venta, cobros ni movimientos.

## Reglas acordadas y supuestos visibles

- Empresa, sucursal y cajero provienen de la sesión autorizada; no se aceptan como identidad confiable desde el body.
- Existe como máximo un turno abierto por cajero y sucursal. Cada turno conserva actor, apertura, saldo inicial y cierre.
- El fondo de apertura se captura explícitamente y puede ser cero. El cierre con arqueo físico/diferencias queda fuera de este corte hasta acordar esa política.
- Una venta solo puede confirmarse desde un borrador persistido del cajero/sucursal actual y con una lista/precios ya congelados.
- El backend recalcula el total con los renglones y precios del borrador. No acepta total, precio ni vuelto calculado por el cliente.
- Se admiten pagos combinados. Débito, crédito, transferencia, Mercado Pago y cheque son registros manuales, sin integración con adquirentes.
- Solo efectivo puede exceder el saldo pendiente. El vuelto se calcula en servidor y se registra como egreso de efectivo; otros medios no pueden exceder el total.
- Cuenta corriente se excluye hasta definir cliente, autorización, pagos parciales y saldo.
- La venta real no emite comprobante fiscal en este corte. Impuestos, descuentos, correlativos de piezas, devolución/anulación y facturación ARCA quedan fuera.
- La venta de demostración nunca se confirma como operación comercial.

## Contrato funcional/API implementado

- `GET /api/cashier-shifts/current`: turno abierto del usuario/contexto actual o `204`.
- `GET /api/cashier-shifts/last-closed`: último turno cerrado del mismo cajero y sucursal, o `204`; permite recuperar el resumen después de reiniciar Electron.
- `POST /api/cashier-shifts`: abre turno con fondo inicial no negativo; rechaza segundo turno abierto para ese cajero/sucursal.
- `POST /api/cashier-shifts/current/close`: cierra el turno propio solo si no hay una confirmación en curso; arqueo físico no requerido en este corte.
- Las respuestas de turno incluyen `openingCash`, `cashSales`, `nonCashSales`, `salesTotal` y `cashBalance`. Se calculan desde el libro de movimientos: el efectivo recibido menos el vuelto integra `cashSales`; `cashBalance` agrega el fondo inicial. No equivalen a un arqueo físico.
- `POST /api/sales/drafts/{draftId}/confirmation`: requiere turno abierto y pagos. El borrador solo pasa a confirmado si se registran juntos venta/detalle, pagos, caja y egreso de stock.
- La confirmación usa `draftId` como intención idempotente: una repetición devuelve la venta resultante; un payload de pagos distinto para esa venta confirmada devuelve conflicto.
- Los métodos iniciales son efectivo, débito, crédito, transferencia, Mercado Pago y cheque. Solo se exponen los que estén activos; su administración configurable puede incorporarse sin romper el contrato.
- Rutas autenticadas y ligadas al contexto operativo; los recursos siempre se filtran por empresa/sucursal y las operaciones de caja por cajero/turno propio. El reintento de confirmación de un borrador ya cobrado solo devuelve la venta a su cajero; otro cajero recibe conflicto.
- Errores mantienen `{ error: { code, message, details } }`; códigos estables incluyen `CASHIER_SHIFT_REQUIRED`, `CASHIER_SHIFT_ALREADY_OPEN`, `SALE_NOT_CONFIRMABLE`, `PAYMENT_TOTAL_MISMATCH`, `INVALID_CHANGE` e `IDEMPOTENCY_CONFLICT`.

## Modelo y consistencia

- Turno de caja: empresa, sucursal, cajero, apertura UTC, saldo inicial, estado y cierre UTC.
- Venta confirmada: origen de borrador único, actor/contexto, hora, lista de precios, total calculado y snapshot de renglones.
- Pagos: venta, método, importe aplicado; efectivo recibido y vuelto se conservan diferenciados para auditoría.
- Caja: libro inmutable de apertura, cobros y vuelto; cada movimiento tiene clave de origen única.
- Stock: al confirmar, la cantidad reservada pasa a egreso físico sin liberar disponibilidad entre ambas operaciones.
- Una sola transacción serializable protege la transición de borrador, stock, venta, pagos y caja. Una restricción única por borrador confirmado y claves únicas de movimientos complementan el control concurrente.

## Criterios de aceptación

- El cajero abre un turno con fondo cero o positivo y no puede abrir otro mientras el suyo siga abierto en esa sucursal.
- El cierre muestra el efectivo registrado y el total vendido; el último cierre sigue consultable por el mismo cajero en la misma sucursal después de reiniciar el POS.
- No se confirma una venta sin turno abierto, borrador real y saldo de pagos válido.
- El servidor calcula saldo/vuelto; únicamente el efectivo produce vuelto.
- Un error o conflicto deja sin cambios venta, pagos, turno, saldo de caja y stock.
- Una confirmación repetida no duplica registros y devuelve el mismo resultado; un reintento con pagos diferentes se rechaza.
- El saldo físico baja exactamente una vez y la reserva correspondiente queda en cero.
- Otro cajero o sucursal no puede leer ni operar el turno o la venta.
- El resumen final muestra total, medios/importes y vuelto, sin afirmar que se emitió comprobante fiscal.

## Estrategia de pruebas y comandos

- Integración contra PostgreSQL real para autorización/contexto, doble apertura, concurrencia, idempotencia y rollback completo.
- Pruebas de dominio para pagos combinados, saldo pendiente, sobrepago no efectivo y cálculo de vuelto.
- Angular: `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web`, `npm run build --prefix src/Carnicerias.Web`.
- Backend: `dotnet test Carnicerias.sln --configuration Release`; compilar API/solución tras cada corte.
- Revisión visual obligatoria en Electron con flujo real; no usar navegador para sustituir la revisión acordada.

## Límites

- Siempre: validar importes y formas en API, resolver identidad desde sesión, controlar autorización/propiedad, usar transacción y restricciones únicas, conservar auditoría y snapshot.
- Requiere nueva definición antes de incluir: arqueo obligatorio, diferencias de caja, cuenta corriente, descuentos/impuestos, anulaciones/devoluciones, numeración fiscal o integración con adquirentes.
- Nunca: confiar en total/vuelto del cliente, aceptar pago en tarjeta/transferencia por encima del saldo, descontar stock antes de confirmar el resto, persistir una venta del catálogo de demostración o guardar datos completos de tarjetas. Las ventas de ensayo con la lista real sí se persisten en la base no productiva.

## Pregunta abierta

- ¿El arqueo físico y la diferencia deben ser obligatorios para cerrar el turno? No bloquea el registro inicial de ventas; queda fuera de este corte.

## Cambio propuesto para cajas simultáneas y relevo de cajero (pendiente de aprobación)

**Origen:** definición del usuario del 7 de octubre de 2026. Puede haber varias cajas en una sucursal. El cierre y la nueva apertura representan un relevo de responsabilidad entre personas. Esta sección reemplazará las reglas anteriores que limitan el turno solo por cajero/sucursal una vez aprobada e implementada; hasta entonces describe el comportamiento objetivo, no el estado actual.

### Objetivo y límites

- Cada caja o puesto de venta tiene una identidad estable, propia de una terminal Electron y asignada a una sucursal. Dos instancias de prueba (`Caja 1` y `Caja 2`) usan perfiles locales, sesiones y cookies independientes, aunque compartan la API y la base de datos.
- Un turno pertenece conjuntamente a empresa, sucursal, caja/terminal y cajero autenticado. La caja conserva el mismo identificador cuando cambia de cajero; cada relevo crea un turno nuevo y no mezcla movimientos ni saldos de ambos turnos.
- Puede haber turnos simultáneos en cajas distintas de la misma sucursal. Solo puede haber un turno abierto por caja; se mantiene también el límite de un turno abierto por cajero y sucursal para evitar una atribución ambigua.
- La identidad de la caja se configura/provisiona para Electron y se valida en el servidor contra la sucursal. Un identificador editable enviado por Angular no constituye prueba suficiente de identidad de terminal. El secreto de vinculación no se expone al renderizador ni se versiona.
- La administración general de usuarios/productos queda fuera del bloqueo operativo del POS. Arqueo obligatorio, reapertura y toma forzada de una caja por un supervisor requieren reglas separadas.

### Secuencia funcional

1. Al iniciar Electron se muestra la caja asignada. El operador se autentica con su propio usuario y contraseña y confirma la sucursal habilitada para esa caja.
2. Si la caja no tiene turno abierto, el POS no permite buscar/agregar productos, modificar o guardar borradores, ajustar stock desde el POS ni cobrar. Solo se puede consultar el estado necesario para abrir turno o salir. Las rutas de escritura de ventas aplican el mismo control en la API, sin confiar en el bloqueo visual.
3. El cajero identificado abre su turno con fondo inicial. El servidor rechaza la apertura si esa caja ya tiene turno abierto, si la caja no pertenece a la sucursal activa o si ese cajero ya tiene otro turno abierto allí.
4. Los borradores y sus reservas se aíslan por caja, cajero y turno, además de empresa y sucursal. No se puede cerrar el turno con un borrador pendiente: primero se confirma o cancela expresamente.
5. Al cerrar se registra el usuario y la hora, se conserva el resumen de ese turno y se invalida la sesión operativa de esa terminal. El siguiente responsable debe iniciar sesión con sus propias credenciales y abrir un turno nuevo; no basta con pulsar «Abrir turno» en la sesión anterior.
6. Ventas, pagos y movimientos de caja conservan caja, turno y cajero. El resumen de una caja no incluye movimientos de otra, aunque estén en la misma sucursal. El stock de la sucursal sí es compartido y las reservas concurrentes siguen siendo atómicas.

### Verificación y datos de prueba

- Crear al menos un segundo usuario cajero, habilitado para la misma sucursal, con contraseña de prueba generada fuera del repositorio. No registrar contraseñas ni secretos de terminal en documentación, logs o commits.
- Provisionar dos cajas de prueba en esa sucursal y abrir dos instancias Electron con perfiles persistentes separados. Tras reiniciar cada una, debe recuperar su identidad de caja y su propia sesión, sin heredar cookies de la otra.
- Abrir turnos simultáneos con cajeros distintos, vender en ambas cajas y comprobar que los cobros/saldos se separan por caja y turno, mientras el stock se comparte.
- Comprobar que sin turno se rechaza tanto el gesto en pantalla como una solicitud directa de guardado de ticket; que un cajero no abre otra caja simultáneamente; que una caja no admite dos turnos abiertos; y que cerrar turno exige resolver el borrador e identificar al siguiente cajero.
- Conservar los registros existentes durante la migración de desarrollo mediante una asignación explícita a una caja histórica; no atribuir ventas viejas a una caja nueva de prueba de forma silenciosa.

### Comandos y ubicación de las pruebas

- API/dominio y migración: `dotnet test Carnicerias.sln --configuration Release`, con pruebas de integración contra PostgreSQL para exclusividad y autorización concurrentes.
- POS Angular: `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web`, `npm run build --prefix src/Carnicerias.Web`.
- Contenedor Electron: `npm run electron:test --prefix src/Carnicerias.Pos` y `npm run electron:start --prefix src/Carnicerias.Pos` para cada perfil de caja. La verificación visual y funcional se hace en Electron.
- Código esperado: identidad y sesiones en `platform-access`, perfiles de Electron en `devices-printing`, borradores en `pos-sales`, turnos/caja en `payments-cash`. No se cambian credenciales de usuarios existentes ni datos de venta fuera de la migración necesaria.

### Decisión a validar

Se propone que el cierre del turno invalide automáticamente la sesión de esa caja y lleve al acceso, incluso si el usuario también tiene permisos administrativos. Esto garantiza que la siguiente apertura identifique realmente a la persona entrante; la administración podrá usarse tras iniciar su propia sesión.
