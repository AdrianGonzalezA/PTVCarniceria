# Plan de implementación: clientes y cuenta corriente

**Estado:** aprobado provisionalmente el 8 de octubre de 2026; tareas y reglas a revisar durante pruebas. Complementa `SPEC-customers-credit.md`. No modifica las tareas abiertas de `tasks/plan.md` ni `tasks/todo.md`.

## Dependencias y decisiones

El POS y la caja ya guardan ventas, pagos, stock y turnos. Se agregará primero identidad/habilitación del cliente, después deuda como movimiento de cuenta atómico con la venta, luego cobranzas/imputaciones, anticipos y correcciones. El saldo surge del libro, no de un campo editable. Las operaciones con dinero real producen movimientos de caja; pasar a cuenta y consumir anticipo no. No hay integración fiscal en esta fase.

## Tareas verificables

### 1. Cliente administrable

- [x] Modelo/migración aditiva de cliente registrado, unicidad dentro de empresa y habilitación explícita de cuenta; historial conservado al desactivar.
- [x] API autenticada para listar/crear/editar con aislamiento de empresa, validación y errores uniformes.
- [x] Administración Angular para gestionar clientes; prueba del cliente HTTP y revisión visual en Electron. No se cambió el aspecto del POS.
- [ ] Pruebas negativas automatizadas de autorización/aislamiento en API. Pendientes porque el arnés actual de integración crea o reinicia otra base, prohibido por la regla de este proyecto.
- **Verificación:** pruebas unitarias/API que no provisionen base; builds .NET/Angular; revisar migración antes de aplicarla a `carnicerias_test_visual`.

**Verificación del primer corte (8/10/2026):** migración `AddCustomerAccounts` aplicada solo en `carnicerias_test_visual` (55433); se verificó la tabla nueva y que no hay otra base de aplicación. API Release compila sin advertencias; `/api/health` responde 200 y `/api/admin/customers` sin sesión responde 401. En Electron con perfil administrativo se creó `CLI-CC-PRUEBA`, se habilitó su cuenta y se revisó visualmente la pantalla; la Caja 1 y sus borradores no se modificaron. Suite .NET Debug: 120 aprobadas y 12 de integración omitidas por la regla de base única; Angular: 78 aprobadas; Electron: 42 aprobadas; lint y build Angular correctos. `dotnet format --verify-no-changes` señala dos errores previos en `AdminProductCodeEndpoints.cs` y `AdminProductPriceEndpoints.cs`, ajenos a este corte.

### 2. Cliente y autorización de crédito en venta

- [ ] Selección de cliente en POS; ninguna venta a cuenta para eventual, cliente inactivo o no habilitado.
- [ ] Permiso específico en rol, confirmación adicional, actor e importe auditados.
- [ ] Venta mixta idempotente: pago inmediato + crédito aplicado + deuda nueva = total; stock y caja atómicos.
- **Verificación:** tests de importes, permiso, reintentos/rollback y manual en Electron; no ejecutar integración que cree otra base.
- **Depende de:** tarea 1.

### Checkpoint de ventas

- [ ] Confirmar una venta con pago parcial y deuda, reabrir Electron y ver persistencia; impedir la misma operación con cajero/cliente no habilitado.

### 3. Cobranzas e imputación

- [ ] Consulta de deuda por cliente/venta y propuesta de las más antiguas, con selección y parcialidad editables.
- [ ] Recibo interno idempotente, imputaciones auditables y entrada en caja/turno para cobros reales.
- **Verificación:** pruebas de distribución, concurrencia y aislamiento; flujo manual en Electron.
- **Depende de:** tarea 2.

### 4. Saldo a favor

- [ ] Excedente de cobranza se conserva como anticipo.
- [ ] Aplicación explícita de cero, parte o todo en venta posterior, sin nuevo ingreso de caja.
- **Verificación:** invariantes de saldo y concurrencia; recibo, venta y resumen de caja en Electron.
- **Depende de:** tarea 3.

### Checkpoint de cuenta

- [ ] Vender a cuenta, cobrar en parte, dejar excedente a favor y aplicarlo en otra venta; reiniciar Electron entre pasos y conservar saldos.

### 5. Corrección de cobranza errónea

- [ ] Constancia interna numerada y vinculada al recibo original, motivo/actor y estado anulado sin borrar registros.
- [ ] Distinguir corrección de imputación sin salida de caja de devolución/no ingreso con reversión de caja; controles de permiso e idempotencia.
- **Verificación:** pruebas de ambos casos y manual en Electron; revisión contable antes de uso productivo.
- **Depende de:** tareas 3 y 4.

### Checkpoint final

- [ ] Conciliación de venta, deuda, anticipo y caja; errores previsibles; documentación de reglas revisadas.
- [ ] Integración fiscal sigue fuera de alcance hasta aprobación específica.

## Riesgos y controles

| Riesgo | Control |
|---|---|
| Cobro/deuda duplicado en reintentos | Clave idempotente, transacción y restricciones únicas. |
| Doble consumo del saldo | Serialización/bloqueo de cuenta y pruebas concurrentes cuando se autorice integración. |
| Mezclar deuda con efectivo de caja | Tipos distintos de movimiento y conciliación por turno. |
| Presentar recibo interno como fiscal | Rotulado explícito y exclusión de ARCA hasta revisión contable. |
| Pruebas alteran otra base | Solo pruebas no destructivas y única base autorizada. |

## Preguntas abiertas

Datos fiscales obligatorios, alcance entre sucursales, numeración de recibos, aprobación de anulaciones y futura política de crédito figuran en la especificación. No bloquean la primera tarea; sí pueden cambiar el contrato antes de avanzar en las tareas posteriores.
