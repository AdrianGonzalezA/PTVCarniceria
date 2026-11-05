# I10 — despliegue de saldo genérico y piezas protegidas

**Fecha:** 05/11/2026. **Única base de aplicación:** `carnicerias_test_visual` en el contenedor existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. Se utilizó el rol PostgreSQL `postgres` de la cadena de conexión de la API. No se creó, reinició ni clonó ninguna base.

## Prevuelo y respaldo

- Contenedor `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`, puerto `55433`, sin conexiones activas a la base. No había API en los puertos locales de laboratorio; Angular en `4200` no escribe directamente en PostgreSQL.
- El prevuelo de solo lectura `tools/sql/20261105_i10_protected_balance_preflight_readonly.sql` confirmó 63 migraciones aplicadas y únicamente `20261105135907_AddProtectedStockI10` pendiente. Había 49 balances; dos combinaciones artículo/sucursal tenían piezas vivas. `ProtectedOnHand` proyectado: 103,952 kg; `ProtectedReserved`: 0; cuarentena: 103,952 kg. Cero conflictos de saldo, reservas, cuarentena y piezas sin balance.
- Respaldo custom `C:\Proyectos\Carnicerias\.local-backups\i10-before-20261105-142243Z.dump`, 454.467 bytes, 618 entradas legibles por `pg_restore -l`. SHA-256 `B39E957B467A6926FF3E14F9FFB1C41442A18D9E669A8D2F984F93188886755F`, idéntico en el archivo local y el del contenedor. No se ejecutó restauración.
- `dotnet ef migrations has-pending-model-changes` indicó que el modelo coincide con la migración. El SQL EF se generó e inspeccionó en `.local-backups/i10-protected-stock-preview.sql`: columnas, predeterminados, backfill determinista desde estados de pieza y CHECK nuevo, todo en una transacción. Las guardas detienen la migración ante pieza viva sin balance, discrepancia de cuarentena o saldo protegido incompatible. No asigna ventas históricas a piezas.

## Aplicación y verificaciones

Se aplicó **solo** `20261105135907_AddProtectedStockI10` con `dotnet ef database update` apuntando a esa migración. EF informó finalización correcta en 3,7 segundos, incluida la adquisición del bloqueo de migración. El primer intento de comando de prevuelo tuvo un error de quoting en la consulta de historial y se detuvo antes de modificar el esquema; se corrigió la invocación y se repitió el control de identidad y conexiones.

El postcheck de lectura `tools/sql/20261105_i10_protected_balance_postcheck_readonly.sql` confirmó 64 migraciones, tres columnas nuevas con sus predeterminados, CHECK protegido nuevo, y ausencia del CHECK global `OnHand >= 0`. Los 49 balances conservaron saldo contable total 384.350,952; saldo genérico total 384.247,000; saldo protegido 103,952. Hubo **cero** diferencias entre piezas y saldos protegidos, cero conflictos de invariantes, y los 53 artículos existentes quedaron con `AllowSaleWithoutStock = true` por defecto.

La prueba HTTP aislada `SaleCheckoutVisualEndpointTests.GenericDebtRemainsSeparateWhenAProtectedPieceArrives` pasó. Creó temporalmente empresa, sucursal, caja, cajero, artículo y lista; confirmó una venta de 2 kg sin stock y reintentó sin duplicar venta ni movimiento. Recibió luego una pieza trazable de 2 kg: saldo contable 0, genérico -2, protegido 2. Rechazó la nueva reserva genérica, reservó/liberó la pieza, rechazó una venta con flag No, aceptó un ajuste positivo idempotente que dejó genérico -1 y rechazó una salida que habría consumido lo protegido. El fixture se eliminó. La verificación final de solo lectura mostró cero empresas/usuarios/piezas/recepciones I10 residuales, 49 balances, 172 movimientos y 80 ventas originales.

Pasaron además 80 pruebas focalizadas Angular de POS, artículos y existencias; 16 pruebas puras .NET de flag, saldos e importación; compilación API/Web y lint. El build Web conserva advertencias previas de presupuesto CSS y dependencia CommonJS `qrcode`.

## Reversión y pendientes

El `Down` de I10 está bloqueado: volver a `OnHand >= 0` podría invalidar ventas nuevas con deuda genérica y perder la separación protegida. Ante fallo posterior, detener escritores, consultar el postcheck y conservar el esquema expandido; restaurar el respaldo únicamente tras una decisión específica sobre los datos generados después de I10. Los binarios anteriores no deben volver a escribir stock bajo este esquema.

Pendientes: recorrido operativo en Electron cuando termine el conjunto de cambios solicitado por el PO, y prueba concurrente específica de dos cajas ante la transición deuda genérica/primera pieza. Las pruebas I02 existentes ya cubren exclusividad de una pieza entre cajas. El permiso configurable de recepción I18a y la rendición I17 son trabajos separados.
