# Desarrollo del punto de venta

## Piezas trazables en el ticket (8 de octubre de 2026)

- [x] Cada pieza leída conserva un renglón propio, su identificador y sus kilos en el borrador, la venta y el PDF de prueba, aunque comparta artículo con otra pieza.
- [x] La reserva y el egreso del stock agregado suman todos los renglones del artículo. Se impide repetir la misma pieza en el ticket o confirmarla en una segunda venta.
- [ ] Modelar el estado individual de la pieza (`disponible`, `reservada`, `vendida`) y el movimiento de egreso por pieza; el saldo físico continúa agregado por artículo.

**Prioridad:** acordada el 6 de octubre de 2026. El módulo de administración de usuarios queda en pausa para centrar el desarrollo en el POS y permitir probar el producto desde Electron.

**Regla vigente de datos de desarrollo:** usar únicamente `carnicerias_test_visual` como base de la aplicación. No crear bases adicionales para pruebas o reinicios sin autorización explícita; limpiar y recargar los datos en esa misma base. El set actual tiene dos cajas, nueve productos con stock y cero ventas iniciales. Las pruebas de integración que exigen una base desechable separada quedan pendientes bajo esta regla.

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
- El peso se puede ingresar manualmente o leer de un emulador externo de balanza; los controladores USB/serial reales siguen pendientes.
- Una confirmación protege contra salir de la pantalla con productos en la venta de demostración.

## Pendientes del POS

**Lectura trazable (8 de octubre de 2026):** el analizador, probador y perfiles versionados siguen disponibles. La plantilla EAN-13 editable `prefijo(1) pro_identif(6) peso(5) control_ean13(1)` interpreta `2250661516008` como pieza `250661`, 51,600 kg. La recepción manual en `/admin/pieces` exige perfil y artículo por peso; guarda pieza, movimiento y stock agregado en la única base. Una segunda recepción del mismo origen/identificador se rechaza provisionalmente; reintentar la misma operación no duplica stock. El POS captura códigos numéricos desde lector/teclado con turno abierto. Primero resuelve códigos exactos de catálogo; si no encuentra uno, busca una pieza recibida en esa sucursal y prellena artículo y kilos de su etiqueta. Una lectura desconocida o ambigua no agrega nada. **Pendiente:** importación por lote, asignar la pieza específica a reserva/venta y definir excepciones de relectura. El ticket sigue siendo agregado y el listado de piezas muestra recepciones históricas, no disponibilidad por pieza tras vender. Nunca deduplicar por producto y kilos.

Ejemplo provisional para validar: `25066151600` = `PRO_IDENTIF 250661` + `peso 51600` con 3 decimales = `51,600 kg`. Configuración de prueba: `pro_identif(6) peso(5)`. El usuario confirmó que `PRO_IDENTIF` identifica de forma única la pieza en su ERP; cinco piezas distintas del artículo `2546` (`CORTITO C/FALDA EXP`) suman `250,000 kg` en `SPEC-inventory-traceability.md`. **No** activar aún el formato como definitivo. Definir el alcance entre emisores y la respuesta a una segunda lectura manual; confirmar si el artículo, `PRO_NUMERO` y `PRO_ITEM` viajan en la etiqueta o llegan por importación. El valor `250.63` se conserva sin normalizar hasta confirmar su formato original.

**Ticket virtual (8 de octubre de 2026):** desde una venta confirmada se puede guardar un PDF de prueba dentro de `userData/tickets` del perfil Electron. El archivo lleva la leyenda **NO FISCAL**; no invoca una impresora física ni ARCA. La ruta no es elegida por el renderer y un error de archivo no revierte la venta. Pendiente: verificar visualmente un PDF generado con una venta real y definir formato físico de impresora para despliegue.

**Emulación de dispositivos (8 de octubre de 2026):** un segundo proceso Electron escucha en un *named pipe* local. El POS lee un peso estable y reciente para artículos por kg sin sobrescribir piezas trazables; tras confirmar una venta puede enviar su ticket validado a una impresora virtual que lo muestra y guarda como HTML **NO FISCAL**. El PDF se corrigió para que su puente funcione dentro del preload sandboxed. Pruebas de IPC reales pasaron para peso, impresión y PDF; queda recorrer visualmente el flujo completo con una venta real. `SPEC-device-emulation.md` documenta contrato y límites.

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
- Clientes registrados, descuentos, impuestos, correlativos trazables, controladores reales de balanza/impresora, lector y ARCA aún no están integrados.
- La administración de usuarios permanece disponible como módulo previo, pero su desarrollo queda pausado por prioridad del POS.

## Cierre de venta y caja por cajero/turno

Alcance aprobado por el usuario el 6 de octubre de 2026; contrato en `SPEC-pos-checkout.md`, secuencia técnica en `tasks/pos-checkout-plan.md`.

- [x] Persistir apertura/consulta/cierre del turno propio y bloquear cambio de contexto mientras siga abierto.
- [x] Implementar y probar las reglas de pagos combinados y vuelto solo efectivo.
- [x] Confirmar borrador real en una transacción: venta, pagos, caja y egreso de stock con idempotencia.
- [x] Conectar apertura, pagos y resumen al POS sin habilitar ventas demo reales.
- [ ] Completar el recorrido integrado desde Electron: observar directamente la confirmación y el resumen final del cobro. Apertura, ticket recuperado y cierre de turno ya fueron comprobados; evidencia y límites en `tasks/pos-checkout-plan.md`.

## Cajas simultáneas y relevo (desglose aprobado; implementación en curso)

Contrato aprobado en `SPEC-pos-checkout.md` y plan técnico aprobado en `tasks/pos-checkout-plan.md`. Prioridad POS; no reactivar por este corte la administración general de usuarios. Cada tarea se cierra con pruebas y evidencia; los checkpoints no requieren detener el trabajo si las reglas aprobadas se mantienen.

### Tarea C1: Registrar cajas de una sucursal

**Descripción:** crear una identidad persistente de caja vinculada a empresa y sucursal, distinguible de la identidad del cajero.

**Aceptación:**
- [x] La caja tiene ID estable, nombre, sucursal y estado; no puede vincularse a otra empresa/sucursal por un ID del cliente.
- [x] El modelo rechaza referencias inválidas y permite más de una caja activa en la misma sucursal.

**Verificación:** pruebas de modelo y `dotnet build Carnicerias.sln --configuration Release`.
**Dependencias:** ninguna. **Archivos probables:** nueva entidad de caja, `PlatformAccessDbContext.cs`, pruebas de dominio/integración. **Tamaño:** mediano.

### Tarea C2: Extender las referencias de caja y turno

**Descripción:** preparar turno, borrador, venta, sesión y libro de caja para conservar explícitamente su caja; el borrador nuevo queda ligado además al turno que lo creó.

**Aceptación:**
- [x] El modelo expresa pertenencia a caja sin cambiar importes, renglones ni reglas de stock.
- [ ] Una venta o movimiento no puede atribuirse a una caja distinta de su turno; las transiciones de borrador preservan turno/cajero.

**Verificación:** pruebas de modelo y compilación .NET. **Dependencias:** C1. **Archivos probables:** `CashierShift.cs`, `SaleDraft.cs`, `ConfirmedSale.cs`, `UserSession.cs`, `PlatformAccessDbContext.cs` (el libro de caja se ajusta con la persistencia). **Tamaño:** mediano.

### Tarea C3: Migrar sin perder datos ni reservas

**Descripción:** crear las tablas, claves e índices de caja; atribuir datos anteriores a cajas históricas por sucursal y detectar borradores activos ambiguos antes de aplicar restricciones obligatorias.

**Aceptación:**
- [x] Conteos, importes de ventas/caja y reservas anteriores coinciden antes y después de migrar; ninguna venta antigua se atribuye a Caja 1 o Caja 2 nuevas.
- [x] Un borrador activo sin turno atribuible detiene la migración con diagnóstico; no se cancela ni libera stock silenciosamente.
- [x] Restricciones únicas parciales impiden turnos abiertos duplicados por caja y por cajero/sucursal.

**Verificación:** prueba de migración sobre copia desechable de PostgreSQL y `dotnet test Carnicerias.sln --configuration Release`. **Dependencias:** C1, C2. **Archivos probables:** migración EF y archivos generados, prueba de migración. **Tamaño:** mediano.

### Checkpoint A: Persistencia

- [x] Migración y rollback ensayados en base de prueba, sin pérdida ni cambio de saldos/reservas.
- [x] Solución .NET compila y sus pruebas pasan (45 pruebas, PostgreSQL incluido; 7 de arquitectura y 38 de integración).

### Tarea C4: Validar la credencial de terminal en la API

**Descripción:** provisionar una credencial por caja fuera del repositorio y resolver la caja desde ella, sin aceptar un ID del body como prueba de identidad.

**Aceptación:**
- [ ] Credencial inválida, revocada o de otra sucursal no habilita operaciones POS; la comparación no expone secretos en respuestas/logs.
- [ ] La API entrega al cliente solo identidad/nombre/estado de caja, nunca la credencial.

**Verificación:** pruebas negativas de API con PostgreSQL. **Dependencias:** C3. **Archivos probables:** servicio/endpoint de caja, registro de servicios, pruebas de integración. **Tamaño:** mediano.

### Tarea C5: Aislar perfiles Electron

**Descripción:** iniciar cada caja con perfil local persistente propio y adjuntar su credencial desde el proceso principal al proxy API, fuera del renderer.

**Aceptación:**
- [ ] Dos Electron no comparten cookies ni almacenamiento y recuperan la misma caja al reiniciar por separado.
- [ ] Angular no recibe la credencial; el protocolo sigue bloqueando navegación y peticiones no permitidas.

**Verificación:** `npm run electron:test --prefix src/Carnicerias.Pos` y prueba con dos procesos Electron. **Dependencias:** C4. **Archivos probables:** `main.ts`, `app-protocol.ts`, pruebas Electron, documentación de arranque. **Tamaño:** mediano.

### Tarea C6: Ligar la sesión a su caja

**Descripción:** autenticar el usuario dentro de la caja validada y exigir esa misma caja durante toda la sesión POS; conservar el acceso administrativo web fuera del POS.

**Aceptación:**
- [ ] Una cookie obtenida en Caja 1 no autoriza una llamada POS desde Caja 2, aun con empresa/sucursal iguales.
- [ ] Selección de contexto comprueba que la sucursal corresponde a la caja; sesiones de otras cajas no se revocan por error.

**Verificación:** pruebas de `SessionEndpointTests`/`UserSessionTests` y Electron. **Dependencias:** C4, C5. **Archivos probables:** `SessionAuthenticationService.cs`, `SessionEndpoints.cs`, autorización operativa, pruebas de sesión. **Tamaño:** mediano.

### Checkpoint B: Identidad

- [ ] Dos perfiles y dos sesiones independientes sobreviven a reinicios.
- [ ] Pruebas negativas de suplantación de caja y sucursal pasan; Electron y .NET compilan.

### Tarea C7: Abrir y consultar turno por caja

**Descripción:** aplicar exclusividad concurrente por caja y por cajero/sucursal; mostrar solo el turno y último cierre que corresponden a la caja autenticada.

**Aceptación:**
- [ ] Dos cajeros abren turnos simultáneos en cajas distintas de la misma sucursal.
- [ ] Segundo turno en una misma caja o mismo cajero en otra caja recibe conflicto, incluso con solicitudes concurrentes.

**Verificación:** `CashierShiftEndpointTests` sobre PostgreSQL. **Dependencias:** C3, C6. **Archivos probables:** `CashierShiftEndpoints.cs`, `CashierShift.cs`, pruebas de turno. **Tamaño:** mediano.

### Tarea C8: Aislar y bloquear los borradores

**Descripción:** guardar, recuperar y cancelar el ticket solo dentro de caja/cajero/turno abiertos; mantener la reserva de stock agregada por sucursal.

**Aceptación:**
- [ ] Sin turno, guardado/cancelación y cualquier modificación de ticket devuelven código específico; otra caja no lee ni cambia el borrador.
- [ ] El borrador creado en un turno no reaparece para otro cajero/turno y sus reservas se contabilizan correctamente.

**Verificación:** `SaleDraftPersistenceTests` y pruebas de endpoint sobre PostgreSQL. **Dependencias:** C7. **Archivos probables:** `SaleDraftEndpoints.cs`, `SaleDraft.cs`, pruebas de borrador. **Tamaño:** mediano.

### Tarea C9: Confirmar la venta en la caja correcta

**Descripción:** reforzar la transacción de confirmación para que borrador, turno, venta y movimientos coincidan en caja/cajero/sucursal sin romper idempotencia.

**Aceptación:**
- [ ] Reintento idéntico devuelve la misma venta; intento cruzado o con turno ajeno falla sin alterar pagos, caja ni stock.
- [ ] Dos cajas descuentan el stock compartido exactamente una vez por venta y sus saldos permanecen separados.

**Verificación:** pruebas de integración de confirmación, concurrencia e idempotencia en PostgreSQL. **Dependencias:** C7, C8. **Archivos probables:** `SaleConfirmationEndpoints.cs`, modelos de venta/libro de caja, pruebas de confirmación. **Tamaño:** mediano.

### Checkpoint C: Operación del servidor

- [ ] Guardar, cobrar y cancelar sin turno o desde otra caja está rechazado por API.
- [ ] Ventas, pagos, saldos y reservas se mantienen consistentes con dos cajas concurrentes.
- [ ] Suite y compilación .NET pasan.

### Tarea C10: Cerrar turno y revocar la sesión

**Descripción:** impedir el cierre con borrador pendiente; al cerrar, persistir el resumen e invalidar solo la sesión de esa caja.

**Aceptación:**
- [ ] Cierre con borrador activo exige confirmarlo o cancelarlo; no cambia turno ni reservas.
- [ ] Cierre exitoso conserva el resumen, borra la cookie y rechaza nuevas acciones POS de esa sesión; otra caja sigue operativa.

**Verificación:** pruebas API de cierre, revocación y carreras con guardado/confirmación. **Dependencias:** C8, C9. **Archivos probables:** `CashierShiftEndpoints.cs`, servicio de sesiones, pruebas de turno/sesión. **Tamaño:** mediano.

### Tarea C11: Bloquear acciones POS hasta abrir turno

**Descripción:** mostrar caja y estado de turno; impedir desde Angular búsqueda, agregado, edición, guardado y cobro antes de abrir, y dirigir a login después del cierre.

**Aceptación:**
- [ ] Sin turno no se puede operar el catálogo/ticket ni por controles ni por atajos; se explica que hay que abrir turno.
- [ ] Tras cerrar se muestra el acceso, no un botón de reapertura con la sesión anterior; con turno abierto continúa el flujo actual.

**Verificación:** `npm test --prefix src/Carnicerias.Web -- --watch=false`, lint/build y revisión visual en Electron. **Dependencias:** C7, C8, C10. **Archivos probables:** `pos-page.ts`, `pos-page.html`, `pos-page.spec.ts`, estilos POS si hacen falta. **Tamaño:** mediano.

### Tarea C12: Probar dos cajas y el relevo completo

**Descripción:** crear segundo cajero y dos cajas de prueba con credenciales fuera de Git; iniciar dos Electron separados y recorrer ventas y relevo con datos persistidos.

**Aceptación:**
- [ ] Dos cajeros venden simultáneamente en la misma sucursal; PostgreSQL evidencia saldos/turnos separados y stock compartido.
- [ ] Cerrar Caja 1 no interrumpe el turno de Caja 2. Para probar el relevo con solo dos cajeros, primero se cierra también Caja 2; entonces su cajero inicia sesión en Caja 1 y abre allí un turno nuevo.
- [ ] Reiniciar cada Electron conserva su caja pero no restaura una sesión revocada; se observa confirmación y resumen final del cobro en Electron.

**Verificación:** suites .NET/Angular/Electron, recorrido visual y funcional en dos Electron, comprobación read-only de ventas/caja/stock en PostgreSQL. **Dependencias:** C5–C11. **Archivos probables:** script/documentación local de provisionamiento, pruebas de integración/Electron, `tasks/pos-checkout-plan.md`. **Tamaño:** mediano.

### Checkpoint final: Dos cajas operativas

- [ ] Todos los criterios C1–C12 y el E2E histórico pendiente están verificados.
- [ ] Ningún secreto de cajero o terminal aparece en archivos versionados, respuestas o logs.
- [ ] Compilaciones, lint y pruebas .NET/Angular/Electron pasan; documentación refleja el comportamiento efectivamente implementado.
