# I10 — propuesta de separación del saldo genérico y las piezas protegidas

**Estado:** propuesta técnica para revisión del PO; sin cambios de código, esquema ni datos I10. Continúa la alternativa A ya elegida: un artículo/sucursal con piezas protegidas bloquea nuevas ventas genéricas sin saldo, mientras una pieza individual nunca admite saldo negativo. El PO permitió recibir la primera pieza aunque exista déficit genérico previo.

## Problema comprobado en el código

`inventory.branch_inventory` conserva `OnHand`, `Reserved` y `Quarantined` para el artículo entero. Sus CHECK actuales exigen `OnHand >= 0` y `Reserved + Quarantined <= OnHand`. El borrador aumenta `Reserved` si hay disponible, la confirmación resta `OnHand` y `Reserved`, la recepción de pieza suma `OnHand` y la conciliación histórica puede corregir `OnHand` y `Quarantined`. Así, si se permitiera `OnHand < 0` y luego entrara una pieza, el balance total podría ocultar si los kilos de esa pieza compensaron una deuda genérica. Quitar el CHECK por sí solo rompería la garantía I02.

## Modelo propuesto

Agregar al mismo balance dos columnas en kg/unidades, con cero predeterminado: `ProtectedOnHand` y `ProtectedReserved`. Comprenden **solo** piezas trazables en estado `Available`, `Reserved` o `HistoricalUnverified` de esa combinación artículo/sucursal. `ProtectedReserved` comprende las reservadas. `Quarantined` sigue siendo la porción histórica inmovilizada y no se vuelve a sumar a `ProtectedOnHand`.

- Saldo contable total: `OnHand`, sin cambio de significado; puede ser negativo cuando hay deuda genérica.
- Saldo genérico: `OnHand - ProtectedOnHand`. Si es negativo, esa diferencia es el déficit genérico. No se descuenta otra vez de la pieza.
- Reservas genéricas: `Reserved - ProtectedReserved`; las reservas anteriores a recibir la primera pieza siguen perteneciendo al borrador original.
- Disponible genérico: `(OnHand - ProtectedOnHand) - (Reserved - ProtectedReserved)`.
- Disponible trazable: `ProtectedOnHand - ProtectedReserved - Quarantined`, sujeto además al estado individual de cada pieza.

CHECK propuestos: ambas columnas protegidas no negativas; `Reserved >= ProtectedReserved`; `ProtectedOnHand >= ProtectedReserved + Quarantined`; límites `numeric(12,3)` y bloqueos de conciliación existentes. El CHECK `OnHand >= 0` y el límite global `Reserved + Quarantined <= OnHand` se sustituyen **solo al activar todos los escritores actualizados**. La regla «no aumentar déficit genérico cuando hay pieza protegida» se valida en la misma transacción serializable que reserva/ajusta, porque el CHECK de un balance no puede consultar el flag del artículo ni el estado de piezas.

## Transiciones

| Operación | Cambio contable | Cambio protegido |
| --- | --- | --- |
| Venta genérica sin pieza | Reservar: `Reserved += q`; confirmar: `OnHand -= q`, `Reserved -= q` | Ninguno. Con pieza protegida, la **nueva** reserva exige disponible genérico `>= q`; sin pieza, el flag del artículo decide si admite déficit. |
| Pieza recibida | `OnHand += q` | `ProtectedOnHand += q`. El déficit genérico anterior se conserva exactamente. |
| Pieza reservada/liberada | `Reserved += q` / `-= q` | `ProtectedReserved += q` / `-= q`, con transición individual I02. |
| Pieza vendida | `OnHand -= q`, `Reserved -= q` | `ProtectedOnHand -= q`, `ProtectedReserved -= q`; el saldo genérico permanece igual. |
| Entrada o ajuste genérico | `OnHand += delta` | Ninguno; una entrada positiva cubre primero el déficit genérico por simple suma, sin tocar la pieza. Una salida con piezas protegidas no puede empeorar el déficit genérico por debajo de las reservas genéricas. |
| Conciliación histórica | `OnHand` toma el conteo documentado; `Quarantined -= q` | Si la pieza queda `Available`, `ProtectedOnHand` se mantiene. Si queda `ReconciledUnavailable`, `ProtectedOnHand -= q`. El ajuste contable se registra una sola vez con el movimiento existente. |

Una reserva genérica creada **antes** de recibir la primera pieza se mantiene y puede finalizarse; la nueva pieza nunca se usa para cubrirla. Recepciones y ventas de piezas deben actualizar ambas columnas y el estado de pieza en una sola transacción. Transferencia/consumo futuros deben reducir `ProtectedOnHand` y `OnHand` de forma pareja, cuando esos incrementos se implementen.

## Migración, compatibilidad y reversión

1. Antes de escribir, consultar `OnHand`, `Reserved`, `Quarantined` y piezas vivas por artículo/sucursal en la base única. Precalcular `ProtectedOnHand` como suma de pesos de piezas vivas y `ProtectedReserved` como suma de las `Reserved`. Verificar `OnHand >= ProtectedOnHand`, `Reserved >= ProtectedReserved` y `ProtectedOnHand >= ProtectedReserved + Quarantined` en los datos actuales; cualquier discrepancia detiene la migración y exige análisis con evidencia, sin asignar ventas históricas a piezas.
2. Preparar migración aditiva de columnas y backfill **determinista desde estados I02 actuales**, sin alterar movimientos ni ventas. Respaldo custom verificado y escritores detenidos antes de aplicar.
3. Desplegar API/administración/POS compatibles en conjunto antes de permitir ventas negativas. Los binarios viejos no entienden saldos negativos ni mantienen las columnas nuevas; no deben volver a escribir después de activar I10. El cambio de CHECK se realiza al final del mantenimiento controlado.
4. Rollback operativo: desactivar nuevas ventas negativas y conservar saldos/documentos. Volver al CHECK anterior solo si todos los balances cumplen otra vez sus condiciones; no corregir negativos borrando movimientos. Si no, mantener esquema expandido y volver a un binario lector compatible.

**Prevuelo de lectura 05/11/2026:** `tools/sql/20261105_i10_protected_balance_preflight_readonly.sql` se ejecutó en transacción `READ ONLY` contra `carnicerias_test_visual` del contenedor autorizado. Hay 49 balances y dos combinaciones con piezas vivas; `ProtectedOnHand` proyectado suma 103,952 kg, todo en `HistoricalUnverified`, y `ProtectedReserved` es cero. Las tres guardas propuestas dieron **cero conflictos**. No se alteraron registros ni esquema.

## Pruebas requeridas

- Saldo genérico `-5 kg`, primera pieza `+2 kg`: contable `-3`, protegido `2`, genérico `-5`, pieza disponible `2`, venta genérica nueva bloqueada y pieza vendible solo por su ID.
- Reserva genérica anterior `3 kg` y primera pieza `2 kg`: la reserva conserva dueño y cantidad; confirmar el borrador no afecta `ProtectedOnHand`.
- Dos cajas con piezas y ventas genéricas simultáneas: exclusividad I02, déficit no ampliado y un único movimiento por confirmación.
- Alta manual/XLSX y stock negativo: producto con flag Sí/No; entrada posterior regulariza genérico sin doble descuento; cancelación/reintento conservan cantidades.
- Conciliación `HistoricalUnverified` hacia disponible o no disponible con conteo: comparar saldo contable, protegido, genérico y ledger antes/después.
- Reportes POS/administración muestran por separado disponibilidad genérica y trazable cuando el total contable es negativo.

## Revisión solicitada al PO

Confirmar esta separación de dos columnas protegidas y el orden de despliegue antes de modificar el modelo o preparar la migración I10. La decisión funcional ya está cerrada; esta revisión es únicamente de arquitectura e invariantes.
