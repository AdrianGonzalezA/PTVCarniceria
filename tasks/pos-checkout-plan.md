# Plan de implementación: cierre de venta POS

> El corte original de caja por cajero/turno y su evidencia se conservan abajo como referencia histórica. El siguiente incremento aprobado funcionalmente, pero aún no implementado, es el plan de cajas simultáneas y relevo descrito a continuación.

## Plan para cajas simultáneas y relevo (aprobado para desglose de tareas)

### Resultado esperado

Dos terminales Electron de una misma sucursal operan con caja, sesión, cajero, turno, borrador y saldo independientes, mientras comparten el stock de la sucursal. Ninguna operación del POS que cree o cambie un ticket se admite sin turno abierto. Cerrar el turno exige resolver el borrador y revoca la sesión de esa terminal, por lo que el siguiente responsable se autentica y abre un turno nuevo. Contrato funcional aprobado en `SPEC-pos-checkout.md`.

### Decisiones de arquitectura propuestas

1. Registrar una entidad de caja/terminal asignada a empresa y sucursal. Electron conserva una identidad local estable por perfil; su proceso principal adjunta la credencial de vinculación al tráfico API, sin exponerla al renderer ni almacenarla en el repositorio. La API verifica la vinculación y la correspondencia con la sucursal. Las rutas administrativas existentes en navegador siguen disponibles, pero el POS exige terminal válida.
2. Vincular cada sesión POS a la terminal autenticada y exigir esa misma terminal en las peticiones posteriores. Un usuario puede iniciar sesión en otra terminal, pero no abrir un segundo turno en la misma sucursal. El cierre revoca únicamente la sesión operativa que cerró el turno; no desautentica las demás cajas.
3. Añadir la identidad de caja al turno, borrador, venta y movimientos de caja. El turno determina la pertenencia de los cobros; el servidor comprueba consistencia entre esas referencias en cada transición. Índices únicos parciales impiden dos turnos abiertos en una caja y dos turnos abiertos del mismo cajero en la sucursal, incluso ante aperturas concurrentes.
4. Conservar los registros actuales: crear una caja histórica explícita por sucursal para atribuirles turnos, ventas y movimientos existentes. Los borradores activos se revisan antes de hacer obligatorio el vínculo al turno; si alguno no puede atribuirse sin ambigüedad, la migración se detiene y se informa, sin cancelar ni reasignar reservas silenciosamente.
5. Hacer cumplir el turno y la propiedad de caja/cajero en la API para guardar, recuperar, cancelar y confirmar borradores. El frontend bloquea búsqueda, selección, cambios del ticket y cobro cuando no hay turno, pero ese bloqueo visual no reemplaza la autorización del servidor. Stock disponible y reserva continúan siendo únicos por sucursal.
6. Provisionar dos perfiles persistentes de Electron y dos cajas de prueba, con secretos fuera del repositorio. Crear un segundo cajero de prueba con asignación a la misma sucursal y contraseña entregada fuera de Git. No se introduce todavía una pantalla general de administración de terminales o usuarios.

### Dependencias y cortes verificables

`caja persistida + migración segura` → `vinculación Electron/API + sesión` → `turnos por caja` → `borradores y cobro aislados` → `bloqueo POS + relevo` → `dos cajas reales en Electron`.

1. **Identidad y migración:** tabla de cajas, referencias e índices; backfill histórico y prueba de migración sobre copia de datos existentes. Checkpoint: ventas, turnos, saldos y reservas previos siguen consultables sin alterar importes.
2. **Terminal y sesión:** perfiles Electron separados, credencial de vinculación validada por API y sesión ligada a caja. Checkpoint: dos instancias conservan identidad y cookies propias después de reiniciar; una credencial incorrecta o de otra sucursal no habilita POS.
3. **Turno y borrador:** exclusividad concurrente por caja y cajero, endpoints filtrados por caja/turno, bloqueo de escritura sin turno y cierre con borrador pendiente. Checkpoint: pruebas API de carreras, acceso cruzado, reserva de stock e idempotencia.
4. **Interfaz y relevo:** estado de caja y turno visible, acciones inhabilitadas hasta apertura, mensajes específicos del servidor, cierre que revoca sesión y vuelve al acceso. Checkpoint: pruebas Angular y recorrido del relevo en Electron.
5. **Prueba simultánea:** segundo usuario y dos terminales de prueba; ventas paralelas en la misma sucursal. Checkpoint: saldos separados y stock compartido en PostgreSQL, sin mezcla tras reinicios; revisión visual únicamente en Electron.

### Riesgos y controles

| Riesgo | Control |
| --- | --- |
| Reasignar datos previos a una caja nueva o perder reservas | Backfill a caja histórica y comprobación de conteos/importes/reservas antes y después; detener ante ambigüedad. |
| Suplantar otra caja con un ID del renderer | Credencial de terminal gestionada fuera de Angular, verificada en servidor y ligada a la sesión; no confiar en un ID del body. |
| Dos aperturas o cierre mientras se guarda/confirma un ticket | Restricciones únicas y transacciones; serializar las transiciones de turno/borrador y probar carreras reales en PostgreSQL. |
| Cerrar turno pero dejar sesión habilitada | Revocar sesión y limpiar cookie como parte del cierre; verificar que una nueva llamada POS se rechace. |
| Dos Electron compartiendo cookies o datos locales | Perfil persistente independiente por caja y pruebas de reinicio e intercambio de credenciales. |

### Verificación y límites del incremento

- Backend: `dotnet test Carnicerias.sln --configuration Release` y `dotnet build Carnicerias.sln --configuration Release`; integración PostgreSQL con base desechable `carnicerias_test_...`.
- Angular: `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web`, `npm run build --prefix src/Carnicerias.Web`.
- Electron: `npm run electron:test --prefix src/Carnicerias.Pos`; recorrido funcional y visual con dos instancias/perfiles desde Electron. La administración general de usuarios, arqueo obligatorio y toma forzada de caja permanecen fuera de este corte.
- Desglose de tareas, criterios de aceptación y pruebas en `tasks/pos-todo.md`. El plan técnico fue aprobado por el usuario; el desglose queda pendiente de revisión antes de modificar código.

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
