# Retiro de efectivo con autorización presencial

La caja permite completar el retiro en su propio diálogo cuando un responsable está junto al cajero. El responsable selecciona su usuario e ingresa un PIN personal de seis dígitos. La autorización no requiere entrar a otra pantalla ni deja una solicitud pendiente para los retiros nuevos del POS.

## Contrato y persistencia

- `PUT /api/users/{userId}/withdrawal-pin`: requiere `platform.users.manage`, valida seis dígitos y guarda únicamente un hash Argon2id en `platform_access.users.WithdrawalPinHash`. La respuesta no contiene el PIN ni el hash. La migración aditiva `20261117104853_AddCashWithdrawalPin` deja nulos los PIN de usuarios existentes hasta configurarlos.
- `GET /api/cashier-shifts/current/withdrawals/authorizers`: lista usuarios activos de la empresa, distintos del cajero, con PIN configurado y permiso efectivo `cash.withdrawal.approve` en la sucursal del turno. Respeta las asignaciones de sucursal y los permisos por rol y empresa existentes.
- `POST /api/cashier-shifts/current/withdrawals/instant`: recibe `operationId`, `method: cash`, `amount`, `reason`, `authorizerUserId` y `pin`. Requiere caja y turno abierto. Valida origen, PIN, permiso y saldo. En transacción serializable crea la solicitud ya aprobada y un único movimiento negativo de efectivo; guarda al usuario autorizante. Repetir la misma operación devuelve el mismo retiro, sin duplicar el movimiento. Se limita el intento de PIN a cinco solicitudes por minuto e IP.
- `GET /api/cashier-shifts/current/withdrawals` ahora incluye el nombre del autorizante para los retiros decididos. Los endpoints antiguos de solicitud y decisión administrativa permanecen para resolver solicitudes pendientes anteriores y conservar compatibilidad.

Un PIN erróneo o un autorizante sin permiso devuelven `WITHDRAWAL_AUTHORIZATION_FAILED` sin movimiento. La falta de efectivo devuelve `WITHDRAWAL_INSUFFICIENT_CASH`. El POS borra el PIN tras respuesta, error o cierre del diálogo y mantiene el `operationId` al reintentar tras una falla de red.

## Despliegue y verificación

Aplicar la migración sobre la única base autorizada después de respaldarla, con las APIs detenidas. Configurar un PIN para cada responsable que autorizará retiros. Verificar en Electron el diálogo de Caja A y en el informe de turno el importe descontado. La prueba HTTP `CashWithdrawalVisualEndpointTests` cubre alta de PIN, selección de autorizante, PIN incorrecto, saldo insuficiente, autorización, reintento idempotente y saldo posterior, usando una empresa fixture aislada que se elimina al terminar.

En el laboratorio del 17/11/2026 se verificó el contenedor existente `carnicerias-pos-test-20261007` y su volumen, se detuvieron los escritores y se respaldó `carnicerias_test_visual` en `.local-backups/withdrawal-pin-before-20261117-0757.dump` (556.569 bytes, SHA-256 `c2d34d19cd142e957075d39a9128baee08b9209072bc78083d55f2000c665d37`, 788 entradas de `pg_restore -l`). Se aplicó solo esta migración (77→78). La prueba HTTP pasó y no dejó empresas fixture. Las APIs `5197`, `5249` y `5251` y Caja A Electron fueron reiniciadas. La captura visual automática de la ventana Electron agotó el tiempo de espera; su árbol de accesibilidad confirmó Caja A operativa, y el diálogo quedó cubierto por la prueba Angular. La comprobación manual del aspecto final queda pendiente.
