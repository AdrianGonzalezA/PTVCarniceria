# Plan de implementación: cierre de venta POS

## Alcance aprobado

Caja por cajero y turno dentro de la sucursal activa; pagos manuales combinables; vuelto solo efectivo; confirmación atómica de venta, pagos, caja y egreso de stock. Se excluyen fiscalidad, cuenta corriente y arqueo obligatorio según `SPEC-pos-checkout.md`.

## Cortes

1. **Turno propio:** persistir apertura, fondo inicial, consulta y cierre; impedir segundo turno abierto y cambio de contexto mientras el turno siga abierto.
2. **Reglas de cobro:** validar pagos mixtos, saldo y vuelto como lógica pura con pruebas de dominio.
3. **Confirmación transaccional:** crear venta, detalle y movimientos de pago/caja; consumir reserva de stock en una transacción; asegurar repetición idempotente y rollback.
4. **POS:** mostrar estado de turno, abrirlo, presentar captura de pagos y resumen final; deshabilitar cierre si falta turno o el borrador es de demostración.
5. **Verificación Electron:** ejecutar pruebas/build y recorrer el flujo dentro de Electron.

## Evidencia de integración (7 de octubre de 2026)

- En Electron se abrió el turno `a1f8e87a` con fondo inicial $0, se guardó un borrador real de Bondiola por 0,1 kg y se recuperó después de reiniciar y recargar `/pos`.
- Esa recarga descubrió que el protocolo `app://bundle/pos` buscaba un archivo `pos`. Se corrigió para servir `index.html` en las rutas Angular conocidas; la prueba de regresión falló antes del cambio y pasó después. Las 6 pruebas de Electron y la compilación TypeScript pasaron.
- En la misma sesión compartida se confirmó una venta de $12.390 en efectivo con Bondiola (0,1 kg) y Asado (1 kg). PostgreSQL conserva venta, dos renglones, pago y movimiento de caja; Bondiola quedó con 17,9 kg físicos y reserva 0. La acción de confirmar la venta no fue observada directamente por esta verificación, por lo que tampoco se capturó el resumen inmediato posterior al cobro.
- Desde Electron se cerró el turno. La pantalla de último turno cerrado mostró fondo $0, ventas $12.390, efectivo $12.390 y otros medios $0; PostgreSQL confirmó el estado cerrado. El saldo mostrado es contable, no arqueo físico.
- Pendiente para dar por completo el recorrido E2E: observar directamente la confirmación y el resumen final de una venta desde Electron, sin interacción concurrente sobre el mismo cajero.

## Riesgos y controles

- Carrera/doble clic: restricción única de turno abierto, venta única por borrador y clave única para movimientos de caja.
- Reintento con datos distintos: hash del contenido de pagos vinculado a la venta; devolver conflicto.
- Sobrepago: servidor limita el exceso al pago en efectivo y calcula vuelto.
- Parcialidad ante error: venta, pagos, caja, estado de borrador y stock quedan en la misma transacción.
- Contexto cruzado: empresa/sucursal/cajero salen de la sesión y toda consulta incluye ese alcance.

## Verificación

- Pruebas de dominio para turnos y cálculo de cobros.
- Pruebas API con PostgreSQL para autorización, restricciones, idempotencia y rollback (requieren `CARNICERIAS_TEST_CONNECTION_STRING` apuntando a una base desechable cuyo nombre empiece con `carnicerias_test_`).
- `dotnet build Carnicerias.sln --configuration Release`.
- `dotnet test Carnicerias.sln --configuration Release`.
- `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web`, `npm run build --prefix src/Carnicerias.Web`.
- Recorrido manual dentro de Electron; mantener las ventas demo sin persistencia ni efectos.
