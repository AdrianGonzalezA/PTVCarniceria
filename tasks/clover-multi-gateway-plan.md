# Plan de implementación: Clover y selección de pasarelas

**Estado:** borrador técnico para revisión (13/10/2026). **Contrato aprobado:** `SPEC-multiple-payment-gateways.md`. **Lista de trabajo propuesta:** `tasks/clover-multi-gateway-todo.md`.

## Resultado esperado

Cada caja muestra solo las pasarelas configuradas. En Cobrar venta se puede elegir o quitar Mercado Pago o Clover antes de solicitar pago; en un ticket solo hay una pasarela integrada, combinable con efectivo y otros medios existentes. Clover opera como **postnet presencial**: el cliente paga en el dispositivo, no en una página web del POS. Una solicitud pendiente/incierta no se pierde ni permite cobrar de nuevo por otro proveedor. La aprobación verificada permite confirmar la venta una sola vez, también después de reiniciar Electron.

## Encaje con el producto existente

- `PointPaymentIntent`, `MercadoPagoOrderMode`, los clientes Point/QR y sus rutas actuales son específicos de Mercado Pago. Se conservarán sus IDs, estados y rutas. Clover tendrá una intención persistente propia y un discriminador de proveedor explícito en el contrato de cobro; no se reescribirá el historial de Mercado Pago.
- `SaleConfirmationEndpoints` valida hoy únicamente `PaymentMethod.MercadoPago` y `MercadoPagoIntentId`. Se agregará Clover de manera aditiva, con vinculación de intención aprobada, hash idempotente y bloqueo de cualquier intención externa pendiente del mismo borrador.
- El POS usa hoy casillas de medios de pago, incluida Mercado Pago. Se presentará un selector exclusivo para la porción integrada (ninguna/MP/Clover) y se mantendrán los otros medios. Mercado Pago conserva QR y Point como submodos.
- La administración actual de pasarelas configura solo Mercado Pago. Clover tendrá credenciales OAuth protegidas y asociación de dispositivo por caja. Una lectura de disponibilidad desde la API alimentará el selector sin enviar secretos a Angular/Electron.
- El medio registrado en venta e historial distinguirá Clover de Mercado Pago y de tarjetas declaradas manualmente. ARCA sigue separado del cobro.

## Dependencias y orden de entrega

```text
Configuración por empresa/caja + disponibilidad (MP sin regresión)
    -> selector exclusivo y deselección previa (recorrido Electron)
    -> intención Clover persistida + cliente Clover aislado
    -> solicitar / recuperar / verificar / cancelar Clover
    -> confirmación de venta, caja e historial con Clover
    -> recuperación A–D y pruebas de corte, doble clic y pago incierto
    -> prueba real sandbox con Dev Kit, si está disponible
```

El contrato de errores de API y el aislamiento empresa/sucursal/caja/turno se mantienen. Las comprobaciones de intención activa entre MP y Clover se serializan por borrador en la base para que dos cajas/solicitudes concurrentes no superen la guarda de la UI. `externalPaymentId` e idempotencia se guardan antes del pedido externo. La API consulta a Clover antes de repetir una solicitud incierta. No se marca acreditado por una respuesta incompleta ni por un clic del cajero.

## Cortes verificables

1. **Selector con Mercado Pago existente.** Disponibilidad por caja, selección única, deselección antes de enviar y bloqueo de cambio ante orden MP activa. Sin Clover configurado, el flujo actual sigue operando.
2. **Configuración Clover.** Administración de sandbox/OAuth y vínculo caja–dispositivo, secretos cifrados, estado de configuración visible sin revelar credenciales. La caja sin Clover no lo ofrece.
3. **Cobro Clover aislado.** Intención persistida, cliente Cloud Pay Display, verificación/recuperación/cancelación según estado real. Respuestas simuladas cubren reintentos y cortes; ningún cobro incierto es confirmable.
4. **Venta completa.** Confirmación transaccional, stock/caja/historial/recibo con Clover y mezclas con efectivo. Un reinicio recupera el pago del borrador A–D. Recorrido del POS dentro de Electron.
5. **Sandbox de punta a punta.** Solo cuando haya cuenta, app OAuth y Dev Kit físico de prueba asociados. Sin hardware, el corte 4 puede verificarse por contratos y simulaciones, pero no se declarará pago Clover real probado.

## Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Una orden MP sigue activa al seleccionar Clover | Guardas en UI y API consultan ambos proveedores; el cambio exige resolución/cancelación verificada. |
| Timeout después de enviar pago Clover | ID externo estable; consulta/reconciliación antes de reintentar; estado incierto bloquea otra pasarela. |
| Respuesta externa manipulada o de otra caja/comercio | Validación de comercio, dispositivo, borrador, importe ARS y transacción; proveedor y sesión se resuelven en servidor. |
| Tokens OAuth vencidos o filtrados | Flujo OAuth con `state` y credenciales protegidas; renovación controlada, sin secretos en logs, renderer ni Git. |
| Túnel público a una API local con claves simples de desarrollo | Publicar solamente rutas OAuth necesarias bajo un hostname fijo; rechazar el resto. No usar un Quick Tunnel aleatorio para la Site URL registrada en Clover. |
| Suposiciones incorrectas sobre pantallas argentinas | No inventar número fiscal; probar factura/cuotas con comercio de prueba y Fiserv antes de activarlo. |
| Falta hardware Clover | Simulaciones de cliente/API y recorrido Electron primero; Cloud Pay Display no se probará en Android Emulator. |
| Pruebas que creen otra base | Solo pruebas unitarias, de arquitectura y frontend; migración revisada y aplicada únicamente a `carnicerias_test_visual` (Docker, 55433). |

## Verificación y puntos de control

- Después de los cortes 1–2: pruebas frontend, API no destructiva, compilación y recorrido Electron de selección/deselección con Mercado Pago; verificar que caja sin configuración no ofrece Clover.
- Después del corte 3: pruebas simuladas de aprobación, rechazo, cancelación, timeout, referencia/importe discordantes y dos cajas concurrentes; no ejecutar la suite de integración que crea otra base.
- Después del corte 4: venta, caja, stock e historial coherentes y una sola confirmación aun con reintento; recorrido Electron de ticket A–D y reinicio.
- Después del corte 5: prueba en sandbox real documentada con comercio, terminal y estado observados, sin guardar credenciales ni datos de tarjeta. Si no hay Dev Kit, consignar **pendiente prueba Clover de punta a punta**.

## Preparación del sandbox por el usuario

### Túnel de homologación (13/10/2026)

El túnel remoto `carnicerias-clover-sandbox` ya figura conectado. Para no publicar la API administrativa con las contraseñas de desarrollo, la aplicación incluye un perfil **separado y limitado**:

```powershell
dotnet run --project src/Carnicerias.Api/Carnicerias.Api.csproj --launch-profile clover-tunnel
```

Escucha únicamente en `http://127.0.0.1:5198`. En Cloudflare, agregar una ruta de **aplicación publicada** con subdominio `clover-sandbox`, dominio `nuestromundo.ar` y Service URL `http://127.0.0.1:5198` (sin publicar el puerto 5197). Aunque la ruta alcance todo ese subdominio, el proceso del puerto 5198 devuelve 404 a todo excepto `GET /clover`, `/clover/status`, `/clover/launch` y `/clover/callback`. El estado se comprueba con `https://clover-sandbox.nuestromundo.ar/clover/status`; `/api/health` debe devolver 404 en el dominio público.

En Clover REST Configuration: Site URL `https://clover-sandbox.nuestromundo.ar/clover`, Alternate Launch Path `/clover/launch`, CORS Domain vacío (las llamadas se harán desde la API), Default OAuth Response `Code`. El retorno previsto será `https://clover-sandbox.nuestromundo.ar/clover/callback`, un subpath HTTPS de la Site URL. **Aún no iniciar la autorización:** `/clover/launch` y `/clover/callback` devuelven 503 hasta implementar el flujo OAuth con `state`, intercambio de código y almacenamiento cifrado. La respuesta 200 de `/clover`/`status` solo confirma conectividad, no autorización ni cobro habilitado.

No pegar el token del túnel, el client secret de Clover ni códigos OAuth en chats, capturas o Git. El proceso del túnel se detiene al cerrar su terminal; `cloudflared` conectado no lo reemplaza.

1. Crear o acceder a una cuenta del [Global Developer Dashboard de Clover](https://docs.clover.com/dev/docs/rest-pay-development-basics), en modo **Sandbox**, y crear un comercio de prueba de **Argentina** con moneda **ARS**. No usar una cuenta ni un terminal de producción para probar.
2. Crear una [app semi-integrada](https://docs.clover.com/dev/docs/creating-a-sandbox-app): tipo Payment/Web, país Argentina, permiso Merchant READ y configuración REST/OAuth con respuesta `Code`. Guardar para la configuración de nuestra API los identificadores de app y Remote App ID (RAID); el secreto se cargará solo en administración, nunca en Git ni en un mensaje.
3. [Instalar la app en el comercio de prueba](https://docs.clover.com/dev/docs/gdp-install-your-app-to-a-test-merchant) y autorizarla por [OAuth v2](https://docs.clover.com/dev/docs/obtaining-an-oauth-token). La API necesita el token de **API** (no Merchant token) y gestionará vencimiento/renovación.
4. Para probar el cobro Cloud de punta a punta, conseguir un [Clover Flex o Mini Dev Kit para Argentina](https://docs.clover.com/dev/docs/integration-development), [asociarlo al comercio de prueba](https://docs.clover.com/dev/docs/set-up-and-use-a-dev-kit) e instalar/iniciar [Cloud Pay Display](https://docs.clover.com/dev/docs/installing-a-pay-display-app). [Cloud Pay Display no funciona en el emulador Android](https://docs.clover.com/dev/docs/devices-and-dev-kits-faqs). Clover describe [canales para recibir Dev Kits fuera de EE. UU.](https://docs.clover.com/dev/docs/developer-kit-faqs); confirmar disponibilidad local con Fiserv antes de comprar.
5. Cuando estén listos app, comercio y Dev Kit, cargar los datos **en la futura pantalla de administración** y asociar el dispositivo a una caja. Entonces haremos juntos una venta ARS de prueba, verificaremos aprobación, reinicio y conciliación.

## Pendientes externos, no bloqueantes para desarrollar

- Confirmación Fiserv/Clover de habilitación Cloud Pay Display y condiciones de sandbox para el comercio argentino.
- Definición de las pantallas argentinas de factura/cuotas para el terminal real, sin adelantar numeración ARCA.
- Cuenta/app OAuth y Dev Kit físico para la validación de punta a punta. Hasta disponer de ellos, no habilitar Clover como pago aprobado para operación real.
