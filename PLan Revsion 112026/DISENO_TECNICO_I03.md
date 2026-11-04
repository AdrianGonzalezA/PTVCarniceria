# I03 — Diseño propuesto para cobro preparado y venta pendiente

**Fecha de propuesta:** 03/11/2026. **Estado al 04/11/2026:** las cinco decisiones funcionales y la implementación de código de **I03a** fueron autorizadas por el PO. La migración aditiva `20261103193100_AddSaleCheckoutProcess` está preparada y **no aplicada**. No se modificaron datos comerciales. La propuesta original se conserva abajo como antecedente; prevalece la sección de implementación al final. I03b requiere autorización separada.

## Alcance exacto del plan

El [PLAN_EJECUCION_AJUSTADO.md](PLAN_EJECUCION_AJUSTADO.md) separa **I03a / MEJ-03 fase 1** (instrumentos preparados, confirmación final antes del cargo, venta pendiente persistente y reanudable; conservar selección múltiple, cuenta corriente y Mercado Pago) de **I03b / MEJ-03 fase 2** (Mercado Pago aprobado/rechazado/timeout, conciliación y reintento sin duplicado). I03b depende de I03a. Q6, en [DECISIONES_PO_H0.md](DECISIONES_PO_H0.md), fija el resultado funcional: no cobrar al agregar un medio; conservar aprobados ante rechazo parcial; consultar al proveedor cuando el resultado sea incierto. El contrato de entidades, estados y permisos debe volver al PO antes de I03 según el propio plan. Clover como cobro POS y otras pasarelas no están incluidos.

## Evidencia del código y brecha

| Evidencia actual | Consecuencia para I03 |
| --- | --- |
| `src/Carnicerias.Web/src/app/features/pos/pos-page.ts`: `selectedPayments` contiene medio e importe; `paymentBalance` es un cálculo local; `startMercadoPago` llama al proveedor antes de `confirmSale` (:332-365, :1477-1506, :1771-1799). | La selección no es una orden de cobro persistente. Un cierre o error pierde el plan mixto; el orden actual contradice Q6. Reutilizar la UI y el cliente, pero mover la autorización efectiva detrás de una confirmación final duradera. |
| `src/Carnicerias.Infrastructure/SaleDraft.cs`: `Draft`, `Cancelled`, `Confirmed`, líneas y descuento; sin proceso de cobro ni instrumentos preparados. `ConfirmedSale.cs` y `SalePayment` solo registran venta y pagos definitivos. | No hay lugar para una venta pendiente con pagos parcialmente aprobados antes de crear `ConfirmedSale`. No se debe inventar una venta histórica ni marcar como pagada una que sigue pendiente. |
| `src/Carnicerias.Api/Sales/SaleConfirmationEndpoints.cs:145-185`: `PaymentSettlement` exige cobertura total; MP requiere `PointPaymentIntent.Approved`. La venta, movimientos y ledger se confirman juntos después (:200-283). | La API no conserva un saldo pendiente si un medio falla después de otro aprobado. Mantener el cierre atómico final y la foto fiscal, pero separar preparación/ejecución del cierre. |
| `src/Carnicerias.Domain/Sales/PaymentSettlement.cs:61-101`: calcula vuelto, cuenta corriente y crédito mediante su variante; limita a seis medios y rechaza duplicar `PaymentMethod`. | Reutilizar reglas de total y redondeo; revisar si el nuevo requisito de una fila por instrumento admite más de un instrumento del mismo medio. No alterar sin decisión y tests. |
| `src/Carnicerias.Infrastructure/PointPaymentIntent.cs` y `PlatformAccessDbContext.cs:1118-1171`: intento MP durable con idempotencia, estados y referencia externa; índice único de **un** intento MP activo por borrador. `MercadoPagoPaymentEndpoints.cs:163-251,375-410` reutiliza y concilia intentos inciertos. | Reutilizar idempotencia, consulta y estados MP. El intento actual no representa todos los medios ni el proceso mixto, y su índice limita varios instrumentos MP en una venta. |

Una consulta **solo de lectura** a `carnicerias_test_visual:55433` del 03/11 encontró 57 migraciones aplicadas, 26 intentos MP: 19 aprobados ya asociados (18 a ventas y 1 a cobranza), 1 vencido y 6 cancelados. Había 2 borradores activos; no se observó un intento MP preparado/pendiente/por conciliar en ese instante. Es una fotografía de laboratorio, no un backfill ni garantía para el corte futuro. Antes de migrar habrá que repetirla.

**Conclusión de factibilidad:** I03a **sí requiere migración aditiva**. Las tablas actuales carecen del proceso, su revisión inmutable, instrumentos previos, saldos aprobados/pedientes y auditoría genérica. Guardar solo en Angular o reutilizar `PointPaymentIntent` para medios manuales no satisface recuperación, trazabilidad ni convivencia con cuenta corriente. I03b puede requerir otra migración si se admiten varios intentos MP simultáneos o se extiende el vínculo de proveedor. No hay autorización para crear ni aplicar estas migraciones todavía.

## Propuesta de arquitectura para aprobación

### Proceso y estados

1. Un proceso de cobro por borrador activo, ligado a empresa, sucursal, caja, cajero y turno. Mientras se editan medios está **Preparado**: no existe cargo externo. Se almacenan versiones del plan y una clave estable del pedido.
2. La acción final del cajero confirma una versión exacta del plan y la foto del total/documento/cliente. Desde **Confirmado para cobrar** no se cambian importes aprobados ni la identidad de cada instrumento. La API acepta o rechaza cambios de versión de modo explícito; dos cajas no pueden confirmar planes distintos del mismo borrador.
3. Cada instrumento evoluciona de **Preparado → En proceso → Aprobado / Rechazado / Resultado incierto**. Un resultado incierto bloquea otro cargo sobre ese instrumento hasta consultar al proveedor. Cada transición registra actor/hora, clave, referencia externa y respuesta sin datos sensibles de tarjeta.
4. Si un instrumento aprueba y otro falla, el proceso queda **Pendiente de saldo**. El aprobado se conserva, se muestra el remanente y solo el rechazado puede sustituirse. El borrador y las reservas de I02 continúan vinculados hasta resolver el proceso, sujeto a la decisión del PO sobre turnos y cancelación.
5. Cuando el total queda cubierto y todos los resultados externos son ciertos, la confirmación final reutiliza la transacción serializable de `SaleConfirmationEndpoints`: crea `ConfirmedSale`/`SalePayment`, aplica cuenta corriente o saldo a favor una vez, mueve stock/piezas, asienta caja y marca el proceso completo. Reintentar con la misma clave devuelve el mismo resultado; no genera otra venta ni otro cargo.

La distinción entre «confirmar el plan de cobro» y «registrar la venta definitivamente» es una **propuesta**, no una decisión ya aprobada. Permite ordenar el cargo después de la elección final, pero obliga a definir cuándo se emite comprobante y cómo se presenta una operación con dinero acreditado y venta aún pendiente.

### Esquema mínimo propuesto, sin archivo de migración aún

- `payments_cash.sale_payment_processes`: ID, ámbito empresa/sucursal/caja/turno/cajero, `SaleDraftId`, estado, total/foto de cuenta corriente y crédito, revisión, hash del plan, clave de idempotencia, fechas y `ConfirmedSaleId` opcional. Índice único de proceso **activo** por borrador y FK de ámbito. No se rellenan registros históricos por inferencia.
- `payments_cash.sale_payment_instruments`: una fila por instrumento, ID estable, proceso, orden, medio, importe previsto/aprobado, estado, detalle no sensible y referencia al `PointPaymentIntent` cuando es MP; índice único para clave de idempotencia y para vincular una aprobación externa una sola vez. Los datos de cheque/transferencia/tarjeta manual requieren contrato validado en servidor, sin almacenar PAN ni credenciales.
- `payments_cash.sale_payment_events`: transiciones y conciliaciones con estado previo/final, actor, fecha, proveedor y referencia; no sobrescribir evidencia de intentos rechazados o inciertos.
- Vínculo opcional de `SalePayment` al instrumento de origen para que el detalle final conserve una fila por instrumento; valores `NULL` en ventas históricas. Revisar si I03b necesita ajustar el índice MP de un intento activo por borrador **solo después** de decidir multiplicidad del mismo proveedor.

La alternativa de poner una venta `ConfirmedSale` en estado pendiente desde el primer aprobado exige cambiar su semántica actual, el momento de egreso de stock, el ledger y los documentos fiscales. Es una migración y un riesgo mayores; no se elige sin decisión del PO.

### Contratos API propuestos

| Operación nueva | Comportamiento mínimo |
| --- | --- |
| `PUT /api/sales/drafts/{draftId}/checkout` | Guarda o revisa instrumentos **Preparados**, cliente, cuenta corriente, crédito y documento con `revision`/hash; verifica empresa, sucursal, caja, turno, cajero y borrador. No llama a la pasarela. |
| `POST /api/sales/drafts/{draftId}/checkout/{checkoutId}/commit` | Congela el plan con clave idempotente y versión esperada. Respuesta con ID y estado persistido antes de cualquier cargo. Repetición con cuerpo distinto = conflicto. |
| `GET /api/sales/drafts/{draftId}/checkout` | Recupera tras cierre de Electron: instrumentos y estados, aprobado, remanente, resultados inciertos y acciones permitidas. |
| `POST /api/sales/drafts/{draftId}/checkout/{checkoutId}/instruments/{instrumentId}/execute` | Solo después de `commit`; reclama atómicamente el intento, reutiliza su clave estable y llama a MP fuera de la transacción local. Un duplicado en curso responde pendiente/conflicto, nunca crea un cargo nuevo. |
| `POST .../instruments/{instrumentId}/reconcile` | Consulta proveedor antes de reintentar un resultado incierto; guarda respuesta/auditoría. |
| Finalización de venta | Reutiliza `/api/sales/drafts/{draftId}/confirmation` con contrato compatible o una nueva ruta deliberada; debe rechazar una confirmación vieja si existe un checkout comprometido y pendiente. La elección exacta del contrato requiere revisión del PO. |

La API conservaría los contratos actuales de borrador y de cobranza de cuenta corriente. Durante la transición, el cobro viejo de un borrador **sin** proceso nuevo no puede interpretarse como un proceso I03; si existe un proceso comprometido, una llamada vieja no debe crear una venta duplicada. El despliegue debe ser coordinado entre API y Electron.

## Dependencias, riesgos y reversión

| Tema | Condición antes de implementar |
| --- | --- |
| I02 | Reserva de pieza ya existe en PostgreSQL; el checkout pendiente debe conservar su borrador y no liberar ni vender la pieza antes de la confirmación definitiva. Su homologación operativa con dos Electron sigue pendiente. |
| MP / I03b | Reutilizar intentos, idempotencia y consulta por `ExternalReference`. La llamada de cargo actual ocurre antes de `confirmSale`; cambiar el orden sin estado duradero podría perder aprobaciones. Sandbox y webhooks requieren pruebas separadas. |
| Cuenta corriente / saldo a favor | Reutilizar permiso `pos.account.charge`, saldo y aplicación existentes; evitar doble imputación si el proceso se reintenta. Decidir cómo se representa dentro de los instrumentos sin convertir el crédito en ingreso de caja dos veces. |
| Turno y ledger | Un aprobado externo puede preceder a la venta definitiva y al cierre de turno. Debe definirse visibilidad de ese valor, bloqueo/traspaso de turno y regla de cancelación. |
| Reversión | Migración aditiva, sin borrar intentos ni ventas. Si hay procesos nuevos pendientes, volver a la API vieja sin bloquearlos puede duplicar cargos: desactivar inicio, reconciliar/drain o preservar un lector compatible antes de rollback. No hacer `Down` que descarte pagos. |

Antes de una migración futura: inventario de intentos activos y borradores, respaldo verificado de la base única, script EF revisado, prueba de esquema y convivencia, corte de escritores, postcheck y autorización expresa. No crear otra base ni usar las suites que la reinician.

## Pruebas de aceptación propuestas

| Fase | Pruebas sin escribir datos comerciales históricos |
| --- | --- |
| I03a | Máquina de estados y suma exacta de centavos; editar/quitar antes del commit; dos commits concurrentes con una sola revisión ganadora; recuperación del plan tras reinicio; selección múltiple, efectivo/vuelto, cuenta corriente y saldo a favor sin regresión; ningún cargo al preparar. Compilar API/Web y tests puros. La integración con fixture en la base única requerirá autorización específica si escribe. |
| I03b | MP aprobado + rechazado: aprobado intacto, remanente correcto y sustitución sin repetir; timeout/resultado incierto: consulta por referencia antes de intentar de nuevo; replay de webhook y reintento idempotentes; caída tras aprobación antes de confirmación de venta; sandbox y dos Electron cuando estén autorizados. Comparar ledger, intentos y ventas antes/después, con limpieza de fixture. |

## Decisiones que debe revisar el PO

1. **Qué representa «venta pendiente»** tras un medio aprobado: ¿borrador bloqueado con cobro pendiente, sin venta/factura/egreso definitivo hasta completar el total (propuesta), o venta registrada con saldo por cobrar? La elección determina estados, fiscalidad, stock y migración.
2. **Turno y cancelación:** ¿puede cerrarse el turno con un cobro electrónico aprobado y venta pendiente? ¿Quién puede cancelar o devolver ese aprobado y con qué evidencia antes de liberar el borrador/pieza?
3. **Instrumentos repetidos:** ¿se admiten dos tarjetas, cheques o pagos MP del mismo medio en una venta, o se mantiene temporalmente un instrumento por medio? El modelo actual rechaza duplicados y MP permite un solo intento activo por borrador.
4. **Contabilidad de aprobados pendientes:** ¿cómo debe figurar un valor cobrado por MP en la caja/turno antes de la venta definitiva? No puede omitirse ni registrarse dos veces cuando se cierre el ticket.
5. **Contrato de finalización:** ¿el endpoint de confirmación existente acepta el ID del proceso comprometido o se crea una ruta nueva, manteniendo la ruta vieja solo para operaciones anteriores? Es una decisión de arquitectura y compatibilidad.

**Puerta resuelta para I03a:** el PO aprobó las cinco decisiones y autorizó código y archivo de migración. Continúa prohibido aplicar la migración, escribir en PostgreSQL o iniciar I03b sin nueva autorización.

## Implementación I03a preparada — 04/11/2026

Las cinco decisiones vigentes son: venta solo finalizada con pago íntegro y confirmado; turno bloqueado mientras haya ventas pendientes; varias operaciones del mismo medio con ID, importe, referencia y estado propios; aprobados registrados inmediatamente como cobros pendientes e imputados a la venta solo al confirmarla; confirmación final transaccional e idempotente.

### Persistencia y transiciones

- `SaleCheckout` guarda el plan por borrador con empresa, sucursal, caja, cajero y turno; revisión, hash y clave de commit. `Prepared` permite editar instrumentos y no llama a proveedores. `Committed` congela el plan antes de ejecutar un cargo; `PendingBalance` conserva pagos parciales; `Ready` exige cobertura íntegra y resultados ciertos; `Completed` se fija en la misma transacción que la venta, stock, cuenta corriente y ledger. `Canceled` solo se admite antes del commit al cancelar el borrador.
- Cada `SaleCheckoutInstrument` tiene ID propio, medio, importe, referencia y estado `Prepared`, `InProcess`, `Approved`, `Rejected` o `Uncertain`. Un rechazo permite agregar una operación de reemplazo sin borrar el aprobado. Los eventos conservan actor, hora y transición. Los intentos MP reutilizan clave idempotente, consulta de estado, webhook y conciliación existentes.
- La migración añade `payments_cash.sale_payment_processes`, `sale_payment_instruments` y `sale_payment_events`, vínculos opcionales a `point_payment_intents` y `sale_payments`, índices únicos de operación y clave de commit. Las ventas históricas quedan con vínculo nulo. El índice de MP por borrador conserva la regla del flujo viejo y permite instrumentos distintos en el checkout nuevo. `Down` se niega si existen procesos para impedir pérdida accidental de cobros.

### Contratos y permisos implementados

| Ruta | Función |
| --- | --- |
| `GET /api/sales/drafts/{draftId}/checkout` | Recupera plan y estados, o 204 si no existe. |
| `PUT /api/sales/drafts/{draftId}/checkout` | Prepara/revisa términos e instrumentos con `expectedRevision`; valida borrador, turno, cliente, documento y montos. |
| `POST /api/sales/drafts/{draftId}/checkout/{checkoutId}/commit` | Congela la revisión con `key` idempotente; no inicia el proveedor. |
| `POST .../instruments/{instrumentId}/approve` y `/reject` | Registra el resultado de un medio manual con referencia; MP se decide por respuesta del proveedor. |
| `POST .../instruments` | Agrega una operación tras un rechazo sin eliminar aprobados. |
| `POST .../instruments/{instrumentId}/execute` y `GET .../payment` | Inicia o recupera un intento MP vinculado al instrumento. `/check`, `/cancel` y webhooks existentes sincronizan su estado. |
| `POST /api/sales/drafts/{draftId}/checkout/{checkoutId}/confirmation` | Confirma una sola venta si el proceso está `Ready`; conserva una fila y un ID de ledger por operación. |

Todas las rutas de venta exigen contexto operativo de empresa/sucursal/caja/cajero; se verifica turno abierto y propiedad del borrador en servidor. El cargo a cuenta conserva `pos.account.charge` y el descuento `pos.discount.apply`. La ruta histórica `/confirmation` sigue disponible para borradores sin checkout, pero no puede confirmar uno que ya tiene proceso I03a. No se exponen datos de tarjeta ni secretos de proveedor en los nuevos contratos.

### Recuperación, consistencia y despliegue pendiente

Al reabrir el ticket A/B/C/D, el POS consulta el checkout, recupera cada operación y los intentos MP vinculados. Un error de red conserva el plan y los pagos aprobados; no habilita volver a cobrar una operación incierta. El turno no cierra con borradores o procesos pendientes. El saldo de caja incluye efectivo aprobado pendiente en un campo separado y deja la imputación a venta para la confirmación; los importes sin efectivo pendientes también se muestran por separado. La confirmación usa la transacción serializable y bloqueo por borrador existentes, con hash de solicitud estable y vínculos únicos, para evitar duplicar venta, stock o ledger en reintentos.

**Antes de desplegar:** autorización específica, prevuelo de la única base `carnicerias_test_visual:55433`, inventario de intentos/borradores activos, respaldo verificable, revisión SQL y corte coordinado de escritores; aplicar solo la migración I03a, verificar esquema, luego desplegar API/POS compatibles. No volver a una API vieja mientras haya procesos nuevos pendientes. La restauración o conciliación de pagos exige decisión separada; no usar `Down` para borrar procesos.

**Verificaciones sin escribir PostgreSQL:** 27 pruebas .NET focalizadas (modelo de cobro, reparto y metadatos EF), 54 pruebas del POS, 224 del frontend completo, build API/Web y `dotnet ef migrations has-pending-model-changes` correctos. Faltan pruebas HTTP contra esquema migrado, concurrencia con dos cajas, MP sandbox y recuperación real en Electron; dependen de autorización de migración y escrituras controladas. No son evidencia de operación de I03a en PostgreSQL.
