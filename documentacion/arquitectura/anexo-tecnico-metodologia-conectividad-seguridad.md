# Anexo técnico — metodología, conectividad, tecnologías y seguridad

**Corte:** 19/10/2026. **Complementa:** [arquitectura de la solución](arquitectura-solucion-estado-actual.md). **Destinatarios:** desarrollo, implementación y herramientas que generen documentación o mapas técnicos. **Alcance:** estado comprobable en repositorio y decisiones aceptadas; no es una certificación de seguridad ni una guía para publicar el laboratorio en producción.

Este anexo distingue **implementado**, **decidido pero pendiente de prueba física** y **pendiente de diseño/operación**. Los nombres de puertos locales son ejemplos del laboratorio; no son un contrato de despliegue. No copiar a otra IA contraseñas, tokens, cadenas de conexión completas, PFX ni volcados de base: este documento no contiene esos valores.

## 1. Metodología de trabajo utilizada

El repositorio no registra una ceremonia Scrum formal. El desarrollo documentado es **iterativo e incremental, orientado a flujos de negocio verificables**. Para un cambio relevante se ha seguido este recorrido:

1. **Regla de negocio y alcance.** Se contrasta el pedido con documentos de requerimientos, maqueta, mapa de capacidades y decisiones previas. Las reglas todavía abiertas quedan señaladas como revisables, no se convierten en supuestos definitivos.
2. **Contrato antes del cambio amplio.** Las capacidades que cruzan módulos se describen en `SPEC-*.md` y `CAPABILITY-MAP-*.md`; `tasks/*-plan.md` y `tasks/*-todo.md` ordenan incrementos, dependencias, criterios y pendientes. Los ADR en `documentacion/decisiones/` registran por qué se eligió una topología o frontera. Los planes históricos pueden conservar supuestos ya reemplazados: para el estado actual prevalecen código y decisiones posteriores aceptadas.
3. **Incremento pequeño.** Se modifica una parte funcional o visual por vez, conservando operativo el flujo de venta. En cambios de lógica se han usado pruebas focalizadas rojo/verde; luego se ejecutan suites y compilaciones pertinentes. No se afirma que todos los cambios históricos hayan seguido TDD estricto.
4. **Verificación proporcional.** Se revisan reglas en pruebas .NET, Angular, Electron o PowerShell según el componente, más prueba HTTP y/o revisión visual cuando corresponde. El POS **se revisa visualmente dentro de Electron**; el administrador puede revisarse en navegador. Build verde no se registra como sustituto de prueba de hardware o despliegue físico.
5. **Persistencia y migraciones controladas.** Antes de alterar el esquema se respalda y se revisa el SQL de migración; se aplica sobre la única base de desarrollo autorizada y se comprueba el resultado. No se ejecutan suites de integración que creen, clonen o reinicien otra base.
6. **Registro y aceptación.** Se actualizan documentación operativa/manual y límites conocidos, se conserva evidencia de pruebas, y el usuario valida reglas de negocio o apariencia antes de integrar un cambio de alcance mayor.

El repositorio usa un **Gitflow básico** con `main`, `develop` y ramas `feature/*`. Al corte, la rama de interfaz `feature/ui-v2` sigue separada de `main` hasta aceptación visual; no interpretar una funcionalidad presente en esa rama como ya entregada en `main`. Hay proyectos de pruebas de arquitectura, central e integración .NET, pruebas Angular/Vitest, Electron/Vitest y scripts PowerShell; la suite de integración completa está restringida por la regla de una sola base. No se constató aquí una canalización CI que sustituya estos controles locales.

**Por qué se trabaja así:** la venta, el stock, la caja y el comprobante tienen fallos con consecuencias distintas. Los cortes verificables y la transacción de confirmación reducen el riesgo de dejar una venta parcialmente asentada; la aceptación en Electron evita aprobar un diseño que solo se ve bien en el navegador.

## 2. Tecnologías y función de cada una

| Capa | Tecnología observada | Uso concreto |
| --- | --- | --- |
| API POS y API central | .NET 10 / ASP.NET Core 10, C# | Minimal APIs HTTP JSON, autorización por endpoint, integración con terceros y publicación del admin local en el paquete de sucursal. SDK fijado en `global.json` a `10.0.401` con avance de parche. |
| Dominio e infraestructura | Proyectos .NET separados | Reglas de negocio en `Carnicerias.Domain`; entidades/acceso en `Carnicerias.PlatformAccess`; EF y servicios en `Carnicerias.Infrastructure`. La central tiene infraestructura y contexto propios. |
| Persistencia | PostgreSQL, EF Core 10.0.12, proveedor Npgsql 10.0.3 | Transacciones, migraciones, consultas y esquemas lógicos. La API POS es la autoridad de escritura comercial; la central no mapea sus tablas. |
| Web local y central | Angular 22.2, TypeScript, RxJS | POS y administrador de sucursal comparten `Carnicerias.Web`; el portal central usa `Carnicerias.Central.Web` separado. |
| Puesto de caja | Electron 44.5, Node/TypeScript, `serialport` 13 | Distribuye el Angular de caja, identifica el perfil/terminal y controla COM, impresión y PDF mediante proceso principal/IPC. |
| Seguridad criptográfica | NSec Argon2id, ASP.NET Core Data Protection, SHA-256, AES-GCM, `safeStorage` de Electron | Hash de contraseñas/tokens, protección de credenciales externas y almacenamiento local de la credencial de terminal. Cada mecanismo se aplica a un activo diferente; no son intercambiables. |
| Cargas y comprobantes | ClosedXML, PDF de Electron, clientes HTTP para servicios externos | Importación Excel de catálogos admitidos, PDF local, ARCA homologación, Mercado Pago sandbox y Clover OAuth sandbox. |
| Empaquetado y laboratorio | npm 11.19.0, Electron Forge, `dotnet publish`, PowerShell, Docker | Build/paquete portable y arranques/verificaciones locales. El paquete Electron Windows x64 actual **no es instalador MSI** ni dispone de actualización automática. |

El software se distribuye como **procesos**, no como microservicios por cada módulo de negocio. Los proyectos de dominio e infraestructura son bibliotecas dentro de la API POS. La API central sí es un proceso independiente y una frontera de autenticación separada. Las dependencias npm se fijan por proyecto con sus manifiestos y lockfiles; las versiones anteriores describen el corte, no una política de actualización perpetua.

## 3. Cómo se conectan los componentes

### 3.1 Matriz de transporte y configuración

| Origen → destino | Transporte y contrato | Dónde se configura / identidad | Estado y límite |
| --- | --- | --- | --- |
| Electron caja → API sucursal | HTTP(S) `/api/*` por LAN. El Angular se carga desde `app://bundle`; el proceso principal de Electron reenvía solo las rutas API al `apiOrigin`. | `pos-config.json` del puesto fija la URL. Perfil local cifrado contiene credencial de terminal; Electron agrega `X-Pos-Terminal-Credential` y no acepta el valor de ese encabezado provisto por la página. El cajero obtiene además cookie de sesión. | En laboratorio se admite HTTP local/LAN privada con opt-in; la instalación real apunta a HTTPS. Sin LAN/API no hay venta offline. |
| Navegador administrador → API sucursal | Mismo origen HTTP(S), páginas `/admin` y llamadas `/api/*`. | Publicación Angular en `wwwroot` de la API o proxy de desarrollo; sesión/permiso local. | El navegador nunca conecta a PostgreSQL ni abre COM. |
| API sucursal → PostgreSQL | Protocolo PostgreSQL mediante Npgsql/EF Core. | `ConnectionStrings:PlatformAccess` o `CARNICERIAS_CONNECTION_STRING`, fuera de Git. | La API exige configuración explícita; no debe exponer SQL a las cajas. |
| Portal Angular central → API central | Mismo origen HTTPS previsto, `/api/v1/*` JSON. | Cookie de sesión central diferente de la del POS. En desarrollo el Angular puede usar proxy local. | La API central no autentica con sesión de cajero ni con credencial de servicio de sucursal. |
| API central → sus datos | Npgsql/EF Core con `CentralAccessDbContext`. | `ConnectionStrings:CentralAccess` o `CARNICERIAS_CENTRAL_CONNECTION_STRING`. | Hoy apunta a la **misma base física de laboratorio** pero solo mapea `central_access`. Ubicación física definitiva de central: pendiente. |
| API central → API sucursal | HTTPS privado/VPN previsto; `GET /api/central/v1/installation` y `/summary` con Bearer de servicio. | La sucursal emite paquete de conexión; central exige URL/IP aprobadas por empresa y coteja los tres UUID remotos. | Contrato versionado y de solo lectura; la central no usa conexión SQL remota. HTTP literal `127.0.0.1` es excepción de laboratorio. |
| API sucursal → ARCA | HTTPS, WSAA/WSFE de **homologación**. | Configuración fiscal y PFX protegidos por empresa en la API. | Salida a Internet; la emisión ocurre después de registrar la venta. |
| API sucursal → Mercado Pago | HTTPS a API del proveedor; sondeo de intención/orden. Webhook entrante previsto en `/api/payments/mercado-pago/webhook`. | Configuración por empresa/caja; token protegido. El webhook coteja firma y luego consulta al proveedor. | Sandbox. El webhook público seguro y la operación sin pantalla abierta no se dan por resueltos para producción. |
| API sucursal → Clover | HTTPS OAuth v2 y consulta de comercio/dispositivos en sandbox. | Configuración y tokens protegidos por empresa. Callback público reducido a rutas `/clover/*` en modo túnel separado. | Vinculación realizada; **cobro POS Clover no implementado/verificado**. |
| Electron → periféricos | IPC interno y puertos COM de Windows; lector de códigos como entrada de teclado. | Dispositivos asignados a la caja en el administrador; COM, velocidad y protocolo por puesto. | Sin dispositivo asignado no se hace lectura/impresión. Hasar disponible solo como ensayo con equipo desafectado. |

En el laboratorio actual, `127.0.0.1:55433` identifica PostgreSQL Docker y `localhost:5197` es un ejemplo de API POS compartida. Algunas pruebas inician APIs de sucursal vinculadas en otros puertos y una API/portal centrales en puertos propios; **todos esos procesos siguen usando `carnicerias_test_visual`**. El despliegue objetivo tiene un servidor y PostgreSQL **por sucursal física**, con clientes LAN; la separación de bases aún requiere workshop. No publicar `55433` a las cajas.

### 3.2 Recorrido de una llamada de caja

1. Electron lee el archivo local de configuración; en un paquete instalado no acepta origen API ausente. La URL se restringe a raíz HTTP(S), sin usuario/contraseña, query ni fragmento. HTTP remoto exige opt-in y solo direcciones IPv4 privadas admitidas por el validador.
2. El perfil Electron carga `app://bundle/pos`; `contextIsolation`, `sandbox` y `webSecurity` están habilitados, Node está deshabilitado en la vista. El proceso principal bloquea navegación y ventanas arbitrarias. Solo un preload/IPC acotado expone operaciones nativas.
3. La vista llama a `/api/*`; el proceso principal crea la solicitud hacia el origen configurado y reemplaza el encabezado de terminal por la credencial del perfil. La API comprueba que esa terminal siga activa y que coincida con empresa, sucursal y sesión.
4. El inicio de sesión `POST /api/sessions` establece cookie local. Las rutas protegidas vuelven a validar sesión, contexto operativo y permiso requerido; un turno abierto habilita las operaciones de venta. El estado de catálogo/stock/precios se obtiene de la API, no de archivos locales como fuente de verdad.
5. `PUT /api/sales/drafts/{slot}` guarda cada ticket A/B/C/D. `POST /api/sales/drafts/{draftId}/confirmation` confirma la venta y sus efectos en una transacción serializable. La impresión y emisión fiscal son pasos posteriores; un fallo en ellos no deshace el commit.

El administrador local usa los mismos endpoints de negocio según sus permisos, pero sin la capacidad nativa del proceso Electron. La central usa endpoints y sesión propios; **ningún cliente debe escribir SQL directamente**. Los contratos detallados de ventas y checkout están en las especificaciones enlazadas al final.

### 3.3 Recorrido de una consulta central

1. La sucursal está fijada mediante configuración `Federation` a empresa/sucursal; el administrador local genera un paquete de conexión en modo administrado. El paquete muestra una sola vez URL, tres identificadores y token. La sucursal almacena el verificador del token, no su valor recuperable.
2. El administrador central de esa empresa importa el paquete. La API central comprueba URL raíz exacta, allowlist URL/IP por empresa, DNS, TLS y los tres IDs obtenidos de `/installation`; fija la IP validada y no sigue redirecciones.
3. La central cifra su copia del token con AES-GCM y clave versionada fuera de la base. Ante una consulta, envía solo el Bearer de servicio a las dos rutas versionadas; no envía cookies centrales al POS.
4. `/summary` devuelve agregados de ventas/pagos/turnos, no clientes ni detalle de tickets. Si una sucursal no responde, la central marca el resultado como incompleto y no publica un agregado parcial como total general.

La sucursal continúa vendiendo aunque la central no esté disponible. Esta conexión no es replicación de base ni administración remota de la sucursal.

## 4. Seguridad implementada y fronteras de confianza

### 4.1 Activos, amenazas y controles actuales

| Activo/frontera | Riesgo principal | Control observado | Límite que no debe ocultarse |
| --- | --- | --- | --- |
| Cuenta humana y sesión | Suplantación o uso fuera de empresa/sucursal. | Contraseñas con Argon2id; tokens de sesión aleatorios cuyo hash se guarda en base; cookies `HttpOnly`, `Secure`, `SameSite=Strict`; comprobación de contexto y permisos en rutas operativas. Identidades local/central y SuperAdmin separados. | Las contraseñas simplificadas del laboratorio no son política productiva. No se hizo aquí una auditoría completa de todas las rutas. |
| Caja Electron | Suplantación de terminal o acceso a Node/COM desde contenido web. | Credencial única por terminal, cifrada con `safeStorage`; API valida terminal activa y vínculo con sesión. Vista con sandbox/aislamiento, navegación restringida e IPC específico; proceso principal reemplaza encabezado de terminal. | Copiar el perfil o comprometer la PC sigue siendo un riesgo operativo. Preparar cuentas Windows, bloqueo físico y rotación para instalación real. |
| SQL y datos comerciales | Escritura inconsistente, fuga entre empresas, pérdida de ticket. | EF Core/Npgsql detrás de la API, alcance por contexto; confirmación de venta serializable e idempotencia de reintento; una base por sucursal es objetivo. | El laboratorio comparte una sola base física y no prueba aislamiento de infraestructura entre servidores. Restauración real aún no verificada. |
| Secretos externos | Exposición de PFX, tokens o credenciales. | ARCA, Mercado Pago y Clover usan ASP.NET Core Data Protection; credencial central gestionada usa AES-GCM ligada a empresa/instalación, con clave externa versionada; sucursal conserva hash del token de servicio. No almacenar secretos en Git. | Respaldar las claves de Data Protection y AES-GCM por separado: sin ellas no se descifran filas antiguas. El DPAPI local de laboratorio no migra por sí solo a otro servidor. |
| Administración central → URL de sucursal | SSRF, consulta a otra empresa, fuga de Bearer. | Destino exacto aprobado por empresa, todas las IP DNS autorizadas, IP fijada, TLS, sin redirecciones, comparación de identidad remota; conexiones auditadas sin token. | La allowlist y clave deben aprovisionarse en el servidor central fuera del navegador; la UI no puede ampliarlas. Publicación VPN/HTTPS real pendiente de workshop. |
| Webhook Mercado Pago | Notificación falsa de pago. | Firma HMAC validada y consulta posterior de la orden al proveedor antes de actualizar la intención. | Falta diseñar/operar exposición HTTPS mínima y conciliación robusta fuera del sondeo abierto del POS. |
| Navegador central | CSRF, lectura o incrustación indebida. | Mutaciones exigen origen igual y encabezado CSRF; cookies estrictas; API central agrega CSP, `nosniff`, `DENY` de marco, `no-referrer` y `no-store` en rutas sensibles. Hay limitadores de login/consultas por IP en proceso. | Un proxy, cabeceras TLS/HSTS y límites compartidos entre procesos deben revisarse al publicar. No extrapolar estos encabezados a toda la API POS sin comprobarla. |

**Modelo de confianza:** lector, formulario, archivo Excel, webhook, callback y paquete de conexión son entradas no confiables aunque provengan de una PC local o de un administrador. Las validaciones de UI ayudan a operar, pero las decisiones de permiso, stock, importes, alcance de empresa y pago se toman en la API. `central_access` y los esquemas POS son límites lógicos distintos incluso dentro de la base de laboratorio.

### 4.2 Riesgos abiertos antes de producción

- Definir HTTPS con certificados confiables dentro de cada LAN, VPN/red privada para la central, exposición mínima de callbacks y webhooks, y endurecimiento del proxy/firewall. El HTTP de laboratorio no prueba esa topología.
- Revisar exhaustivamente seguridad de todas las rutas POS (origen/CSRF, límites de login, encabezados, validación de archivos, permisos por recurso), dependencias y manejo de datos personales. Este anexo describe controles observados, **no** declara un pentest ni conformidad normativa.
- En particular, el validador de origen actual de la API POS compara esquema y host, pero no puerto; no tratarlo como una política de origen completo hasta corregirlo y probarlo. El portal central utiliza otra comprobación que sí compara puerto y exige encabezado CSRF en sus mutaciones.
- Formalizar gestor de secretos, respaldo/restauración de claves, cifrado y acceso a backups, rotación y revocación. Confirmar retención, exportación y eliminación de datos personales según las obligaciones aplicables.
- Probar recuperación ante caída de servidor/LAN, restauración de PostgreSQL en entorno autorizado, servicio de arranque, monitoreo y concurrencia multicaja con hardware real. Hoy no hay cola offline de ventas.
- Validar con responsable fiscal y proveedores la facturación productiva, impresora fiscal real, cobros Clover y conciliación completa de pasarelas. Homologación/sandbox y la Hasar desafectada no demuestran aptitud comercial.

## 5. Pautas para implementación y para una IA que genere mapas

- Dibujar por separado **laboratorio actual** (varias APIs posibles, una sola base `carnicerias_test_visual`, central lógica en `central_access`) y **topología objetivo** (servidor/API/PostgreSQL por sucursal). Marcar el workshop físico como pendiente.
- Anotar en cada flecha: iniciador, protocolo, autenticación, configuración de destino, necesidad o no de Internet y efecto de una caída. No trazar flecha SQL desde Electron, navegador o central hacia tablas comerciales de sucursal.
- Para seguridad, marcar cuatro fronteras distintas: navegador/Angular ↔ API, Electron/IPC ↔ hardware, API ↔ PostgreSQL, central ↔ API de sucursal/servicios externos. Un token de terminal no es una sesión humana; un Bearer de central no es una cookie de administrador.
- Para una secuencia de venta, separar el **commit local atómico** de la emisión ARCA y la impresión. Para una secuencia central, separar la verificación inicial de la consulta viva; «registrada» no equivale a «conectada».
- No poner valores de credenciales en diagramas o ejemplos. Las claves de configuración anteriores indican **dónde** va cada secreto, no su contenido.

## 6. Fuentes de verificación

- [Arquitectura de alto nivel](arquitectura-solucion-estado-actual.md), [ADR de instalación por sucursal](../decisiones/0002-instalacion-por-sucursal.md), [ADR de consulta central](../decisiones/0004-consulta-central-de-sucursales.md), [ADR de identidad central](../decisiones/0005-identidad-central-independiente.md), [ADR de conexiones](../decisiones/0007-conexiones-de-sucursal-gestionadas-por-admin-central.md) y [ADR de paquete de sucursal](../decisiones/0008-credencial-de-central-desde-sucursal.md).
- [Guía de workshop](../despliegue/guia-workshop-por-sucursal.md), [integración central–sucursal](../despliegue/integracion-central-sucursal.md), [administración central](../despliegue/administracion-central.md), [plan UI2](../../tasks/ui-v2-plan.md), [ventas](../../SPEC-pos-sales.md) y [checkout](../../SPEC-pos-checkout.md).
- Código: [`Program.cs` POS](../../src/Carnicerias.Api/Program.cs), [`Program.cs` central](../../src/Carnicerias.Central.Api/Program.cs), [filtro operativo](../../src/Carnicerias.Api/Security/OperationalAuthorizationFilter.cs), [sesiones](../../src/Carnicerias.Api/Sessions/SessionEndpoints.cs), [protocolo Electron](../../src/Carnicerias.Pos/src/app-protocol.ts), [política Electron](../../src/Carnicerias.Pos/src/security-policy.ts), [confirmación de venta](../../src/Carnicerias.Api/Sales/SaleConfirmationEndpoints.cs) y [política de destinos centrales](../../src/Carnicerias.Central.Infrastructure/CentralDestinationPolicy.cs).
