# Tareas de `central-access`

**Estado:** aprobado por el usuario, en ejecución desde el 14/10/2026. [Especificación aprobada](../SPEC-central-access.md) y [plan aprobado](central-access-plan.md). Este archivo no reemplaza `tasks/todo.md`, que tiene pendientes de otro módulo.

Cada tarea deja el nuevo proyecto compilable, sin tocar la API POS salvo verificaciones negativas. Los archivos son una estimación (máximo aproximado de cinco por tarea); los archivos generados por Angular/EF pueden sumar artefactos mecánicos. No se creará ni reiniciará otra base: solo `carnicerias_test_visual` en PostgreSQL Docker, puerto local 55433.

## Tarea 1. Crear el contenedor backend central

**Descripción:** agregar los proyectos de API, infraestructura y pruebas centrales a la solución, con un `health` mínimo y sin registrar rutas POS ni migraciones automáticas.

**Aceptación:**
- [x] Los proyectos centrales compilan independientemente; la API arranca sin ejecutar `Migrate` ni necesitar datos locales para `health`.
- [x] No hay referencia de ejecución a `PlatformAccessDbContext` ni se reutiliza `Program.cs` del POS.

**Verificación:** `dotnet build src/Carnicerias.Central.Api/Carnicerias.Central.Api.csproj --configuration Release --no-restore`; inspección de dependencias y `Program.cs`.

**Dependencias:** ninguna. **Archivos previstos:** `Carnicerias.sln`, proyectos `.csproj` de API/infraestructura/pruebas, `src/Carnicerias.Central.Api/Program.cs`. **Tamaño:** M (5).

## Tarea 2. Modelar empresas, administradores y sesiones centrales

**Descripción:** crear entidades propias y `CentralAccessDbContext` con límites por empresa, unicidad y hashes, sin mapear tablas locales.

**Aceptación:**
- [x] Empresa tiene código único e inmutable; usuario normalizado es único dentro de su empresa; la sesión queda ligada a ambas identidades.
- [x] El contexto solo mapea `central_access` y configura un historial EF separado; los campos de secreto no se exponen por entidades de respuesta.

**Verificación:** prueba de modelo `CentralIdentityModelTests` y build central; inspección de mapeo EF.

**Dependencias:** 1. **Archivos previstos:** `CentralCompany.cs`, `CentralAdministrator.cs`, `CentralSession.cs`, `CentralAccessDbContext.cs`, `CentralIdentityModelTests.cs`. **Tamaño:** M (5).

## Tarea 3. Agregar auditoría y primera migración central

**Descripción:** persistir eventos de alta/login/revocación sin secretos y generar la migración inicial del esquema central.

**Aceptación:**
- [x] El SQL generado crea solo `central_access` y su historial, sin modificar `platform_access`, `pos_sales` ni otros esquemas locales.
- [x] Auditoría conserva actor/acción/resultado/fecha y no acepta contraseña, cookie, token o cadena de conexión.

**Verificación:** `dotnet ef migrations script --project src/Carnicerias.Central.Infrastructure --startup-project src/Carnicerias.Central.Api --context CentralAccessDbContext --idempotent`; revisar SQL y build, **sin ejecutar** migración aún.

**Dependencias:** 2. **Archivos previstos:** `CentralAuditEvent.cs`, `CentralAccessDbContext.cs`, primera migración (clase y diseñador), snapshot EF. **Tamaño:** M (5).

### Checkpoint A — después de 1–3

- [x] Proyectos compilan; pruebas de modelo pasan; script EF no toca esquemas locales.
- [x] Ninguna base fue creada, reiniciada o migrada durante este checkpoint.

## Tarea 4. Preparar migración controlada de la misma base

**Descripción:** agregar un comando de implementación `migrate` que valide destino exacto, esquema y respaldo antes de aplicar migraciones centrales; no se ejecuta en el arranque HTTP.

**Aceptación:**
- [x] En laboratorio, un destino distinto de `carnicerias_test_visual` en `127.0.0.1:55433` falla cerrado antes de migrar.
- [x] El procedimiento documenta y verifica respaldo/preflight, migra solo el contexto central y conserva el historial local.

**Verificación:** pruebas puras de guardia de conexión; inspección de migraciones pendientes; ejecución controlada **únicamente** sobre la base visual existente, sin imprimir contraseña.

**Dependencias:** 3. **Archivos previstos:** proyecto CLI `.csproj`, `Program.cs`, `CentralMigrationCommand.cs`, `CentralConnectionGuard.cs`, `CentralConnectionGuardTests.cs`. **Tamaño:** M (5).

## Tarea 5. Dar de alta empresa y primer administrador de modo interactivo

**Descripción:** incorporar al CLI el alta transaccional y la recuperación operativa de contraseña con revocación de sesiones, sin ABM HTTP.

**Aceptación:**
- [x] Contraseña se lee interactivamente, nunca desde argumentos/logs/Git; su hash Argon2id queda en la tabla central.
- [x] Alta de empresa+primer administrador es atómica y rechaza duplicados; un reseteo operativo revoca sesiones previas y queda auditado.

**Smoke realizado:** alta interactiva de `VISUAL-CENTRAL` / `admin-central` en la única base visual, con contraseña aleatoria no versionada. Login y logout reales comprobados.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralBootstrapTests --no-restore`; smoke acotado de alta en la misma base visual.

**Dependencias:** 4. **Archivos previstos:** `Program.cs` del CLI, `CentralBootstrapCommand.cs`, `CentralBootstrapService.cs`, `CentralBootstrapTests.cs`. **Tamaño:** M (4).

## Tarea 6. Emitir y comprobar sesiones centrales

**Descripción:** construir el servicio de credenciales central con verificación de contraseña, token aleatorio de 256 bits, hash persistido, expiración y revocación.

**Aceptación:**
- [x] Usuario inexistente y contraseña incorrecta hacen verificación de costo comparable y devuelven el mismo fallo.
- [x] Empresa o administrador inactivo, sesión vencida o revocada no autoriza; ninguna sesión local es reconocida.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralSessionServiceTests --no-restore`.

**Dependencias:** 2 y 5. **Archivos previstos:** `CentralSessionService.cs`, `CentralSessionServiceTests.cs`, ajustes acotados a `CentralSession.cs`. **Tamaño:** M (3).

### Checkpoint B — después de 4–6

- [x] Preflight, respaldo y migración afectaron solo la única base visual; esquema local intacto.
- [x] Existe una cuenta central ficticia independiente y las pruebas de sesión cubren revocación y empresa inactiva.

## Tarea 7. Exponer login, sesión actual y logout centrales

**Descripción:** implementar `/api/v1/sessions` y la cookie exclusiva de la central, con validación de origen, CSRF, cuota de login y errores no enumerables.

**Aceptación:**
- [x] Login crea cookie `HttpOnly`, `Secure`, `SameSite=Strict`; logout revoca servidor; `current` exige sesión vigente.
- [x] Origen inválido, exceso de intentos, cookie local y token de sucursal no autorizan; errores usan formato y `no-store` pactados.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralSessionEndpointTests --no-restore`; build API central.

**Dependencias:** 6. **Archivos previstos:** `CentralSessionEndpoints.cs`, `Program.cs` central, `CentralSessionEndpointTests.cs`, contratos de sesión. **Tamaño:** M (4).

## Tarea 8. Preparar el sitio Angular central y su cliente HTTP

**Descripción:** crear el artefacto web independiente y un cliente tipado para sesión, sin compartir almacenamiento de credenciales con POS.

**Aceptación:**
- [x] Compila y prueba el shell Angular; usa el mismo origen central para `/api/v1` y no guarda tokens en `localStorage`.
- [x] Dependencias y lockfile se revisan; no se abre CORS con credenciales.

**Verificación:** `npm --prefix src/Carnicerias.Central.Web run build`; test de cliente/shell y revisión de dependencias.

**Dependencias:** 7. **Archivos previstos:** configuración Angular y npm, `src/main.ts`, shell de app, cliente HTTP central; lockfile generado adicional. **Tamaño:** M (~5).

## Tarea 9. Construir ingreso de administrador central

**Descripción:** formulario de código de empresa, usuario y contraseña con estados accesibles de carga/error y recuperación de sesión actual.

**Aceptación:**
- [x] Login válido lleva al área central; fallo no revela si existe empresa/usuario y no conserva contraseña en estado persistente.
- [ ] El estilo se alinea con la administración aprobada y funciona con teclado; no simula pertenecer a una caja Electron. Falta el recorrido visual por ausencia de navegador controlable en esta sesión.

**Verificación:** `npm --prefix src/Carnicerias.Central.Web test -- --watch=false`, build y recorrido visual del **sitio central** en navegador.

**Dependencias:** 8. **Archivos previstos:** componente TS, plantilla, estilos y prueba de login. **Tamaño:** M (4).

### Checkpoint C — después de 7–9

- [ ] Login/logout/current funcionan desde el sitio central y no desde cookies locales.
- [ ] API y Angular compilan; se revisó visualmente el portal. No se revisó el POS fuera de Electron.

## Tarea 10. Modelar instalaciones registradas

**Descripción:** agregar registro central con empresa propietaria, tres IDs remotos, etiqueta, destino y referencia de secreto, pero nunca el token.

**Aceptación:**
- [x] `installationId` es único globalmente y la relación `centralCompanyId` no depende del UUID remoto de empresa.
- [x] El modelo distingue instalación registrada/activa de conectividad viva y no contiene datos comerciales.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralInstallationModelTests --no-restore`.

**Dependencias:** 2. **Archivos previstos:** `CentralInstallation.cs`, `CentralAccessDbContext.cs`, `CentralInstallationModelTests.cs`. **Tamaño:** M (3).

## Tarea 11. Migrar registro sin tocar esquemas locales

**Descripción:** generar y aplicar la migración aditiva de instalaciones mediante el comando protegido.

**Aceptación:**
- [x] El SQL altera solo `central_access`, incluye unicidad e índices de empresa; historial central propio.
- [x] El preflight confirma la misma base y respaldo antes de aplicar; ninguna tabla/sesión POS se modifica.

**Verificación:** script EF revisado y comprobación SQL de solo lectura posterior en `carnicerias_test_visual`; build central.

**Dependencias:** 4 y 10. **Archivos previstos:** migración (clase/diseñador), snapshot EF y prueba de migración/modelo. **Tamaño:** M (4).

## Tarea 12. Validar destino e identidad remota antes del alta

**Descripción:** cliente saliente y política de destinos que leen el secreto protegido, limitan tiempo/red y comparan la respuesta de `/installation`.

**Aceptación:**
- [x] Rechaza URL no aprobada, HTTP no loopback no autorizado, redirección, TLS inválido, secreto ausente o respuesta con IDs distintos.
- [x] No sigue redirecciones ni transmite token a otro origen; no registra ni devuelve secretos.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralInstallationProbeTests --no-restore` con HTTP simulado; revisión de DNS/egreso.

**Dependencias:** 10. **Archivos previstos:** `CentralDestinationPolicy.cs`, `CentralInstallationProbe.cs`, pruebas de política y cliente. **Tamaño:** M (4).

### Checkpoint D — después de 10–12

- [x] Registro/migración central no afectan tablas locales; validador remoto falla cerrado.
- [x] Todavía no se habilita una sucursal si su API no ofrece el contrato con identidad coincidente.

## Tarea 13. Registrar instalaciones mediante CLI protegido

**Descripción:** conectar validación remota y persistencia transaccional/auditoría al comando de implementación, sin endpoint web de alta.

**Aceptación:**
- [x] Solo inserta/activa después de verificar identidad exacta y secreto externo; duplicado de `installationId` no crea otro registro.
- [x] Falla sin crear instalación ante sucursal caída, configuración incompleta o discrepancia; registra rechazo sin token.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralRegistrationTests --no-restore`; smoke local real si la API de sucursal se reinició con `Federation` válido.

**Dependencias:** 11 y 12. **Archivos previstos:** `CentralRegistrationCommand.cs`, `CentralRegistrationService.cs`, `Program.cs` del CLI, `CentralRegistrationTests.cs`. **Tamaño:** M (4).

## Tarea 14. Listar instalaciones solo de la empresa de sesión

**Descripción:** consulta paginada y endpoint `GET /api/v1/installations` con DTO de campos permitidos.

**Aceptación:**
- [x] A no ve B con UUIDs conocidos; `page` y `pageSize` se validan y no hay filtro de empresa recibido por HTTP.
- [x] Respuesta no incluye URL, referencia, token ni estado ficticio de conexión.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralInstallationEndpointTests --no-restore`.

**Dependencias:** 7 y 11. **Archivos previstos:** `CentralInstallationQuery.cs`, `CentralInstallationEndpoints.cs`, contratos de respuesta y `CentralInstallationEndpointTests.cs`. **Tamaño:** M (4).

## Tarea 15. Mostrar sucursales registradas en Angular

**Descripción:** listar instalaciones de la empresa con paginación, estados vacíos y error, sin declarar *conectada* una sucursal que no fue consultada.

**Aceptación:**
- [x] La pantalla consume solo el endpoint central con cookie y muestra etiqueta/IDs permitidos.
- [x] Sesión vencida vuelve al ingreso; sucursal no registrada o central sin datos no aparece como venta cero. Una respuesta atrasada de otra sesión tampoco se muestra.

**Verificación:** `npm --prefix src/Carnicerias.Central.Web test -- --watch=false`, build y recorrido visual del portal central.

**Dependencias:** 9 y 14. **Archivos previstos:** cliente de instalaciones, componente TS, plantilla, estilos y prueba. **Tamaño:** M (5).

### Checkpoint E — después de 13–15

- [x] Alta protegida y listado cubren dos empresas distintas sin fugas entre ellas (aislamiento A/B probado por HTTP; alta real de una sucursal visual).
- [ ] UI central funciona y distingue registro de estado vivo; API POS sigue sin cambios.

## Tarea 16. Documentar, auditar y verificar en laboratorio

**Descripción:** registrar decisión técnica, guía de despliegue/rotación/recuperación y evidencia del recorrido no destructivo en la única base.

**Aceptación:**
- [x] ADR y guía explican cuentas independientes, dos identidades de empresa, secretos fuera de Git, arranque separado y que una central caída no detiene ventas.
- [x] Tests/builds pasan, la revisión de diff y búsqueda acotada no detectó secretos, y el smoke documenta lo probado y lo pendiente de VPN/HTTPS multiservidor.

**Verificación:** todos los comandos de `SPEC-central-access.md`, `git diff --check`, diff acotado y lectura del esquema de `carnicerias_test_visual`; no ejecutar suite que aprovisione otra base.

**Dependencias:** 1–15. **Archivos previstos:** `documentacion/decisiones/0005-identidad-central-independiente.md`, `documentacion/despliegue/administracion-central.md`, esta lista y el plan. **Tamaño:** M (4).

### Checkpoint final

- [ ] Se cumplen los cinco criterios de `SPEC-central-access.md` y la Definition of Done, con pruebas runtime además de compilación.
- [x] No se creó ni reinició otra base; se preservaron código, migraciones, datos y permisos locales.
- [x] La prueba física de VPN/HTTPS, multiproceso y sucursales en servidores separados queda explícitamente pendiente del workshop.
- [x] El humano revisó y aprobó la lista antes de iniciar tareas.
