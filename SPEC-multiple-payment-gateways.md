# Especificación propuesta: selección de pasarela y Clover en el POS

**Estado:** regla de negocio aprobada el 13/10/2026: una pasarela integrada por ticket, combinable con efectivo, y Cloud Pay Display como primera modalidad Clover. La vinculación OAuth sandbox se implementó y se autorizó un comercio de prueba en `carnicerias_test_visual`; la asignación de dispositivo, la selección y el cobro Clover siguen pendientes. Complementa `SPEC-mercado-pago.md`, `SPEC-pos-checkout.md` y `SPEC-fiscal-payments.md`.

**Corte verificable sin Dev Kit (14/10/2026):** Clover OAuth se renovó y el comercio sandbox respondió `verified` mediante una consulta real, sin pago. `GET /api/pos/payment-gateways` usa empresa, sucursal, caja, cajero y turno de la sesión; responde `{ gateways: [{ provider: "mercadoPago", modes: ["qr", "qrStatic", "point"] }] }` con solo los modos configurados, o `gateways: []`. No entrega tokens ni identificadores de dispositivos. El POS oculta Mercado Pago para nuevas órdenes si no está disponible, pero conserva y bloquea una orden previa mientras se consulta/cancela; Clover no se ofrece aún como cobro. Las pruebas de Cloud Pay Display siguen requiriendo Dev Kit físico.

`POST /api/admin/payment-gateways/clover/devices` es una acción administrativa con validación de origen (puede renovar OAuth) que consulta la lista de dispositivos provisionados del comercio en Clover sandbox. Devuelve únicamente ID, nombre y modelo, no serial, token ni datos de tarjeta. La consulta real del 13/10/2026 respondió `devices: []`. No se equipara “dispositivo provisionado” con “Cloud Pay Display instalado y activo”, ni se habilita Clover en el POS por esta consulta.

## Objetivo

Permitir que un comercio configure más de una pasarela por empresa y caja, y que el cajero seleccione o deseleccione en **Cobrar venta** cuál usar. Incorporar Clover como segunda pasarela presencial integrada, manteniendo Mercado Pago QR/Point. **Clover será el postnet físico:** el POS envía importe y referencia al dispositivo; el cliente presenta la tarjeta en el postnet. Ni Angular ni Electron capturan datos de tarjeta ni muestran un checkout web de Clover. Ningún proveedor debe figurar como pago recibido por el solo hecho de estar seleccionado o de haber enviado una solicitud.

## Relevamiento de otros POS y Clover

- [Odoo POS](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/payment_methods.html) configura métodos por punto de venta y vincula cada método terminal a una integración. El cajero elige el método en la pantalla de pago; el método configurado no se cobra automáticamente. [Odoo, terminales](https://www.odoo.com/documentation/18.0/applications/sales/point_of_sale/payment_methods/terminals.html).
- [Shopify POS](https://help.shopify.com/en/manual/sell-in-person/getting-started/setup-payment-method/enable-payments) permite habilitar/deshabilitar opciones por dispositivo y [combinar medios en un mismo pedido](https://help.shopify.com/en/manual/sell-in-person/shopify-pos/payment-management/multiple-partial-payments). Diferencia terminales integradas de [medios externos manuales](https://help.shopify.com/en/manual/sell-in-person/shopify-pos/payment-management/card-payments), cuyo resultado el operador debe declarar. No tomamos esa declaración manual como aprobación de Clover.
- [Clover Argentina](https://docs.clover.com/dev/docs/argentine-merchants) incluye Cloud Pay Display para Flex/Mini. Su [guía LATAM](https://docs.clover.com/dev/docs/quick-reference-guides-latam-developers) distingue conexión por nube de REST Pay Display en red local. Para una API central y cajas Electron se **propone Cloud Pay Display**, sujeto a comprobar habilitación y dispositivo del comercio. La documentación genérica de [configuración cloud](https://docs.clover.com/dev/docs/configuring-a-cloud-connection) no etiqueta Argentina aunque la página regional sí afirma soporte: validar ese punto con Clover/Fiserv antes de una prueba de punta a punta.
- La API de [solicitud de pago Clover](https://docs.clover.com/dev/reference/pay) usa importe en centavos, `externalPaymentId` e `Idempotency-Key`; [consulta por externalPaymentId](https://docs.clover.com/dev/reference/get_payment_by_external_payment_id) y la [guía de recuperación](https://docs.clover.com/dev/docs/payment-reconciliation-and-recovery) exigen conservar la operación pendiente, consultar antes de reintentar y no interpretar un timeout como rechazo. La [autenticación REST Pay Display](https://docs.clover.com/dev/docs/obtaining-an-oauth-token) requiere token API OAuth de la app/comercio, no token de comerciante.
- Clover contempla [número de factura y cuotas en la pantalla argentina](https://docs.clover.com/dev/docs/regional-extras). Esos datos no sustituyen la emisión ARCA del POS. La página de [comercios argentinos](https://docs.clover.com/dev/docs/argentine-merchants) dice ARS, mientras otra sección de parámetros regionales menciona flujos bimonetarios: este primer corte se limita **a ARS**, que es la moneda actual del POS; validar otra moneda por separado. Para probar Cloud Pay Display de punta a punta, Clover indica que [no sirve el emulador Android](https://docs.clover.com/dev/docs/devices-and-dev-kits-faqs): se necesita un Dev Kit físico en sandbox; los contratos/API sí se pueden probar sin él.

## Regla propuesta para seleccionar y deseleccionar

1. Administración habilita cada pasarela para la empresa y vincula las cajas autorizadas; el POS solo ofrece las que estén operativas en la caja actual. No se deshabilita Mercado Pago al agregar Clover.
2. En **Cobrar venta** se elige **ninguna, Mercado Pago o Clover** para la porción integrada. Antes de solicitar el cobro, el cajero puede cambiar o deseleccionar libremente la pasarela y ajustar el importe. Siguen permitidos los medios manuales y mixtos ya existentes (por ejemplo, efectivo + una pasarela), además de cuenta corriente.
3. En este primer corte hay **como máximo una pasarela integrada activa por ticket**. No se parte el mismo ticket entre Mercado Pago y Clover; esa ampliación requeriría dos intenciones verificadas y una regla explícita de reversos parciales. La pantalla no debe presentarlas como casillas independientes que permitan activarlas simultáneamente.
4. Una vez solicitada una orden, deseleccionar o cambiar de proveedor **no borra la orden**. Primero se consulta su estado y, si todavía es cancelable, se pide la cancelación al proveedor y se verifica el resultado. Mientras esté pendiente o incierta, se bloquea el cambio y la confirmación por otro medio. Si ya fue aprobada, se debe completar la venta con ese pago o iniciar un flujo formal de devolución separado; nunca se pasa silenciosamente a efectivo/u otra pasarela.
5. El pago externo se verifica del lado del servidor por empresa, sucursal, caja, cajero, turno, borrador, importe, referencia e identidad del proveedor. El cajero confirma la venta después de ver **Acreditado**. Confirmar registra venta, pago, caja y stock una sola vez. Reiniciar Electron recupera la intención pendiente para el ticket A–D correspondiente.
6. Si el proveedor responde con estado desconocido, conexión interrumpida u operación offline, no se marca como pagado hasta conciliar. La consulta automática y un botón manual de verificación deben mostrar el estado; no se genera una segunda solicitud con un identificador nuevo por un reintento.

## Contrato y estructura propuestos

- Conservar los IDs y comprobantes históricos de Mercado Pago. Añadir una identidad de proveedor explícita (`mercadoPago` o `clover`) a la intención de cobro y al pago confirmado, con una migración aditiva en la única base de desarrollo. El método contable, el proveedor y el modo/dispositivo son dimensiones distintas; los reportes deberán poder separar montos por pasarela sin confundir Clover con un pago de tarjeta declarado manualmente.
- Exponer al POS las **pasarelas disponibles para su caja** con estado de configuración y modos habilitados, sin enviar secretos. Mantener las rutas existentes de Mercado Pago; las rutas nuevas de Clover deben usar el mismo sobre de error `{ error: { code, message, details } }` y contratos tipados para crear/recuperar/consultar/cancelar una intención. La mutación usa la sesión, caja y turno del servidor, no IDs confiados desde Angular.
- Administración → Pasarelas de cobro añade Clover con comercio/app de prueba, OAuth/token de sandbox y vínculo de dispositivo por caja. Guardar credenciales cifradas y mostrar solo presencia/vencimiento; nunca persistir PAN/CVV ni exponer tokens al renderizador Electron. La URL regional y permisos deben estar restringidos a destinos Clover oficiales, no ser texto libre que la API solicite (SSRF).
- Persistir la intención y su `externalPaymentId`/clave idempotente **antes** del pedido a Clover. La misma intención reusa la misma clave tras timeout. Una consulta recupera el pago por identificador externo y valida monto, moneda ARS, resultado, transacción cerrada, comercio/dispositivo y ausencia de reverso antes de acreditarlo. Una respuesta HTTP de pago, un evento o una confirmación del cajero sin esa verificación no bastan.
- La configuración fiscal ARCA sigue independiente. No enviar a Clover un número fiscal inventado o todavía no autorizado; definir con el comercio/Fiserv cómo se usan o se omiten las pantallas de factura y cuotas antes de habilitar el terminal real.

## Tecnología, ubicación y estilo

- API .NET 10 y PostgreSQL: contratos/endpoints en `src/Carnicerias.Api/Payments`, intención y configuración en `src/Carnicerias.Infrastructure`, migración aditiva. Mantener `decimal` para dinero interno; convertir a centavos enteros solo en el borde Clover. Respuestas externas son datos no confiables y se validan antes de cualquier transición financiera.
- Angular 22.2: cliente tipado en `src/Carnicerias.Web/src/app/core/sales`; selector y estados en `src/Carnicerias.Web/src/app/features/pos`, configuración en `features/admin`. Electron conserva la revisión visual obligatoria del POS.
- Ejemplo de contrato, **ilustrativo y sujeto a aprobación**: `PaymentIntent(provider, draftId, amount, status, externalId)`; `provider` es un discriminador estable, no un texto enviado por el cajero para autorizar un cobro. El servidor resuelve configuración y caja desde la sesión.

```csharp
// Valor interno estable; nunca se acepta como autorización de pago del navegador.
public enum IntegratedPaymentProvider { MercadoPago, Clover }
```

## Verificación y comandos permitidos

- Pruebas de dominio/transporte con respuesta Clover simulada: mismo ID en reintentos; pendiente, aprobado, rechazado, offline, timeout, cancelación, importes/referencias discordantes, reinicio y carrera entre dos cajas. Sin un dispositivo/cuenta de Clover no se afirmará una prueba de cobro real.
- Angular: selección/deselección antes del pedido, bloqueo después, recuperación A–D, combinación con efectivo, mensajes y ausencia de doble cobro. Revisar el POS **dentro de Electron**, nunca sustituirlo por navegador.
- Comandos: `dotnet build Carnicerias.sln --configuration Debug --no-restore`; `dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --configuration Debug --no-restore`; `npm test --prefix src/Carnicerias.Web -- --watch=false`; `npm run lint --prefix src/Carnicerias.Web`; `npm run build:web --prefix src/Carnicerias.Pos`.
- No ejecutar la suite de integración que crea/reinicia otra base. Conservar y migrar solo `carnicerias_test_visual`; documentar la integración PostgreSQL pendiente. Probar el circuito Clover en sandbox cuando exista cuenta/app/dispositivo de prueba autorizado.

## Límites de implementación

- **Siempre:** validar estados e importes en la API, conservar claves de reintento, ejecutar las pruebas no destructivas y revisar visualmente el POS en Electron.
- **Consultar antes:** cambiar la regla de un proveedor integrado por ticket, decidir conexión Clover local en vez de nube, o habilitar cobros reales con credenciales/dispositivo del comercio.
- **Nunca:** crear otra base de Carnicerías para pruebas, confirmar una venta por una respuesta incierta, guardar datos de tarjeta o credenciales en Git, ni borrar una intención pendiente para permitir otro cobro.

## Criterios de aceptación

1. Con ambas pasarelas configuradas para Caja 1, el cajero puede elegir Mercado Pago o Clover, y quitar la selección **antes** de enviar una solicitud. Caja 2 solo ve las pasarelas habilitadas para ella.
2. Una solicitud activa o incierta no puede ocultarse, reemplazarse ni convertirse en un cobro manual por destildar. Una aprobación no se pierde al cambiar de pestaña o reiniciar Electron.
3. Clover no permite confirmar venta sin verificación del pago correcto; un reintento no duplica cargos. Una confirmación aprobada guarda una sola venta y un solo egreso de stock; Mercado Pago existente sigue funcionando.
4. La venta/historial/caja distinguen proveedor. Los importes mixtos suman exactamente el total y solo efectivo genera vuelto.
5. UI, API, documentación y tests explican qué es cobro integrado y qué es un medio declarado manualmente.

## Decisiones necesarias antes de implementar

1. **Alcance de pagos mixtos:** aprobado un solo proveedor integrado por ticket, combinable con efectivo/cuenta corriente; Mercado Pago + Clover en la **misma** venta queda fuera de este corte.
2. **Modalidad Clover:** aprobada Cloud Pay Display como primera opción. Falta confirmar con Fiserv/Clover si el comercio/terminal argentinos tienen esa app y acceso API; conexión local queda como alternativa posterior.
3. **Pantallas argentinas:** definir si el terminal pedirá cuotas y número de factura, o si se usarán opciones regionales para omitir/precargar campos sin adelantar un número ARCA inexistente.
4. **Prueba de punta a punta:** harán falta cuenta Clover de desarrollador, app semi-integrada, comercio de prueba, credenciales OAuth de prueba y Dev Kit físico Clover Flex/Mini asociado al comercio de prueba. Cloud Pay Display no funciona en el emulador Android. Hasta entonces solo se podrán verificar contrato, UI y respuestas simuladas.
