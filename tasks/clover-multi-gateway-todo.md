# Tareas propuestas: Clover y selección de pasarelas

**Estado:** aprobado. Referencia funcional: `SPEC-multiple-payment-gateways.md`. No sustituye `tasks/todo.md` ni `tasks/pos-todo.md`.

**Avance del 13/10/2026:** se implementaron el modelo y migración aditiva de configuración OAuth por empresa, `state` persistido y de un solo uso, intercambio de token con Clover sandbox, validación de comercio/permiso, cifrado y panel administrativo para conectar el comercio. Pasan 31 pruebas .NET de arquitectura y 114 de Angular; build .NET/Angular y lint sin errores. Docker Desktop volvió a responder; se inició el contenedor PostgreSQL existente y se aplicó la migración `20261013124143_AddCloverOAuth` **solo** a `carnicerias_test_visual`, sin crear ni reiniciar otra base. Se verificó en `__EFMigrationsHistory` y las nuevas tablas Clover están vacías. La API actualizada responde en `5197` y el proceso público en `127.0.0.1:5198` responde `/clover/status` (200), rechaza un callback no iniciado (400) y oculta `/api/health` (404); la misma ruta de estado responde por `https://clover-sandbox.nuestromundo.ar/clover/status`. **Aún no se probó un callback OAuth real:** falta configurar App ID/Merchant ID/App Secret en la UI y autorizar el comercio. La asignación de Dev Kit por caja, refresh token y los cobros Clover siguen pendientes; por eso los casilleros 3–5 no se dan aún por terminados. No se ejecutó la suite de integración que crea/reinicia otra base.

## Corte 1 — Selección segura sin alterar Mercado Pago

### 1. Disponibilidad integrada por caja

- [ ] API de solo lectura devuelve modos MP disponibles para empresa/caja/turno de la sesión; caja no configurada no ofrece MP. Contrato tipado sin secretos y prueba de aislamiento.
- **Dependencias:** ninguna. **Archivos probables (3):** `src/Carnicerias.Api/Payments/AvailableGatewayEndpoints.cs`, `src/Carnicerias.Api/Program.cs`, `tests/Carnicerias.ArchitectureTests/GatewayAvailabilityTests.cs`.
- **Verificación:** `dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --configuration Debug --no-restore`; respuesta para caja configurada/no configurada y empresa ajena.

### 2. Selector exclusivo en Cobrar venta

- [ ] La UI presenta ninguna/MP disponible, conserva QR/Point como submodo y permite quitar selección antes de enviar. Orden MP activa o incierta bloquea cambio, aunque el usuario cierre/reabra el diálogo.
- **Dependencias:** 1. **Archivos probables (4):** `src/Carnicerias.Web/src/app/core/sales/payment-gateway-client.ts`, `src/Carnicerias.Web/src/app/features/pos/pos-page.ts`, `pos-page.html`, `pos-page.spec.ts`.
- **Verificación:** `npm test --prefix src/Carnicerias.Web -- --watch=false`; inspección de checkout **dentro de Electron** con MP configurado y sin configurar.

### Control A

- [ ] Compila API y frontend, pruebas no destructivas verdes, flujo MP QR/Point existente intacto y sin dos pasarelas elegibles a la vez.

## Corte 2 — Clover configurado pero sin iniciar pagos

### 3. Persistencia de configuración Clover

- [ ] Modelo por empresa y vínculo caja–dispositivo con restricciones de unicidad; credenciales OAuth cifradas, fecha de vencimiento/estado, migración **aditiva** revisada para la única base de desarrollo.
- **Dependencias:** 1. **Archivos probables (5):** `src/Carnicerias.Infrastructure/CloverGatewaySettings.cs`, `PlatformAccessDbContext.cs`, `Migrations/<fecha>_AddCloverSettings.cs`, su `Designer.cs`/snapshot de EF, `tests/Carnicerias.ArchitectureTests/CloverSettingsTests.cs`.
- **Verificación:** pruebas puras de validación/cifrado y `dotnet build Carnicerias.sln --configuration Debug --no-restore`; revisar SQL antes de aplicar solo en `carnicerias_test_visual`.

### 4. API de administración y autorización OAuth

- [ ] Un administrador conecta/desconecta el comercio sandbox, consulta estado sin ver secretos y asigna dispositivo a caja. El callback OAuth valida `state`, comercio y permiso; solo la API conoce access/refresh token.
- **Dependencias:** 3. **Archivos probables (4):** `src/Carnicerias.Api/Payments/AdminCloverSettingsEndpoints.cs`, `CloverOAuthClient.cs`, `Program.cs`, `tests/Carnicerias.ArchitectureTests/CloverOAuthTests.cs`.
- **Verificación:** pruebas de `state` inválido, token vencido, acceso sin permiso y caja ajena; build de API. Si Clover exige callback HTTPS accesible, consignar ese requisito antes de E2E.

### 5. Configuración Clover en Administración

- [ ] Administrador ve estado OAuth/vencimiento y vincula Dev Kit por caja; no aparecen tokens ni secretos en Angular. Mercado Pago sigue editable en la misma sección.
- **Dependencias:** 4. **Archivos probables (4):** `src/Carnicerias.Web/src/app/core/admin/admin-payment-gateway-client.ts`, `src/Carnicerias.Web/src/app/features/admin/payment-gateways-page.ts`, `payment-gateways-page.html`, `payment-gateways-page.spec.ts`.
- **Verificación:** pruebas frontend de guardar, error, permiso y ausencia de secretos; recorrido de administración y disponibilidad del POS en Electron.

### Control B

- [ ] Clover solo aparece en las cajas habilitadas y con configuración válida; todavía no es seleccionable para solicitar pago si el cliente de cobro no está listo.

## Corte 3 — Cobro Clover recuperable

### 6. Intención Clover durable

- [ ] Entidad/tabla propia con proveedor, borrador/caja/turno/importe ARS, `externalPaymentId` e idempotencia persistidos **antes** de llamar afuera; una intención activa por borrador y transición de estados válida.
- **Dependencias:** 3. **Archivos probables (5):** `src/Carnicerias.Infrastructure/CloverPaymentIntent.cs`, `PlatformAccessDbContext.cs`, `Migrations/<fecha>_AddCloverIntents.cs`, su `Designer.cs`/snapshot, `tests/Carnicerias.ArchitectureTests/CloverPaymentIntentTests.cs`.
- **Verificación:** transiciones, unicidad/reintento y generación de migración revisados; nunca crear otra base para probar.

### 7. Cliente HTTP Clover Cloud aislado

- [ ] Cliente con URL oficial restringida, OAuth API token y cabeceras requeridas; dinero interno decimal convertido a centavos enteros. Parsea/valida respuesta y consulta por ID externo; timeout queda incierto.
- **Dependencias:** 4, 6. **Archivos probables (3):** `src/Carnicerias.Api/Payments/CloverPayDisplayClient.cs`, `CloverContracts.cs`, `tests/Carnicerias.ArchitectureTests/CloverPayDisplayClientTests.cs`.
- **Verificación:** `HttpMessageHandler` simulado para pago/consulta, datos discordantes, errores HTTP, payload malformado, reintento con mismo ID y respuesta tardía.

### 8. Endpoints solicitar y recuperar

- [ ] La API crea la intención antes del POST a Clover, impide iniciar con orden MP activa, recupera por borrador al reiniciar y consulta automáticamente sin convertir timeout en rechazo/aprobación.
- **Dependencias:** 6, 7. **Archivos probables (3):** `src/Carnicerias.Api/Payments/CloverPaymentEndpoints.cs`, `Program.cs`, `tests/Carnicerias.ArchitectureTests/CloverPaymentEndpointTests.cs`.
- **Verificación:** pruebas de autorización, aislamiento, importes, doble clic y recuperación con proveedor simulado; build API.

### 9. Cancelación o conciliación antes del cambio

- [ ] Cancelar cuando Clover lo permita, consultar después y liberar el cambio solo con estado final no aprobado. Si Clover ya aprobó o responde incierto, la intención sigue visible y bloquea otro proveedor.
- **Dependencias:** 8. **Archivos probables (3):** `src/Carnicerias.Api/Payments/CloverPaymentEndpoints.cs`, `CloverPayDisplayClient.cs`, `tests/Carnicerias.ArchitectureTests/CloverPaymentEndpointTests.cs`.
- **Verificación:** pruebas de cancelable, aprobado, incierto y cancelación fallida; nunca emitir segundo ID externo por reintento.

### Control C

- [ ] Solo simulaciones: solicitar/consultar/cancelar/resumir sobreviven a corte de conexión; sin Dev Kit no se declara validación de un pago real.

## Corte 4 — Venta, historial y Electron

### 10. Confirmación transaccional con Clover

- [ ] `PaymentMethod.Clover` y vínculo `CloverIntentId` aditivos; la API exige aprobación del mismo importe/borrador/caja/turno, rechaza pago externo omitido o pendiente y confirma venta/caja/stock una sola vez.
- **Dependencias:** 8, 9. **Archivos probables (4):** `src/Carnicerias.Domain/Sales/PaymentSettlement.cs`, `src/Carnicerias.Api/Sales/SaleConfirmationEndpoints.cs`, `src/Carnicerias.Infrastructure/PlatformAccessDbContext.cs`, `tests/Carnicerias.ArchitectureTests/CloverSaleConfirmationTests.cs`.
- **Verificación:** pruebas de efectivo+Clover, dos proveedores, monto discordante, doble confirmación y aprobación previa a caída; migración aditiva si hace falta persistir referencia.

### 11. POS Clover y recuperación de tickets A–D

- [ ] Cliente tipado y panel Clover con solicitar/consultar/cancelar, estado pendiente/acreditado e indicación de dispositivo. Polling limitado al diálogo; al reabrir ticket o Electron se recupera la intención exacta. Sin confirmación, no se cierra venta.
- **Dependencias:** 2, 8, 9, 10. **Archivos probables (5):** `src/Carnicerias.Web/src/app/core/sales/clover-client.ts`, `src/Carnicerias.Web/src/app/core/sales/sale-draft-client.ts`, `src/Carnicerias.Web/src/app/features/pos/pos-page.ts`, `pos-page.html`, `pos-page.spec.ts`.
- **Verificación:** pruebas frontend de selección/deselección, importe mixto, estados inciertos, reconexión y ticket A–D; recorrido visual exclusivamente en Electron.

### 12. Lecturas y documentación final

- [ ] Historial, resumen de caja y recibo identifican Clover, sin confundirlo con tarjeta manual ni romper Mercado Pago; documentar configuración, operación, incidencias y pruebas pendientes.
- **Dependencias:** 10, 11. **Archivos probables (hasta 5 por subcorte):** `src/Carnicerias.Api/History/AdminHistoryEndpoints.cs`, `src/Carnicerias.Api/Customers/AdminAccountEndpoints.cs`, `src/Carnicerias.Web/src/app/features/pos/pos-page.html`, documentación operativa y pruebas enfocadas.
- **Verificación:** build .NET/Angular/Electron, lint, pruebas no destructivas y venta simulada completa en Electron; revisar diff y no incluir secretos ni archivos de usuario.

### Control final

- [ ] Se cumplen los cinco criterios de `SPEC-multiple-payment-gateways.md`; migraciones y código conservados; solo `carnicerias_test_visual` usada. Documentar explícitamente integración PostgreSQL no ejecutada si la suite necesita otra base.
- [ ] Con cuenta/app/Dev Kit disponibles, ejecutar cobro sandbox aprobado y rechazado; contrastar estado Clover–POS–venta. Sin Dev Kit, dejar esta prueba **pendiente**, nunca reemplazarla por una aprobación simulada presentada como real.
