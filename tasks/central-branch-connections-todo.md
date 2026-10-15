# Tareas: conexiones a sucursales desde la central

**Estado:** aprobado por el usuario y ejecutado en la rama de función el 15/10/2026. Tareas 2–14 implementadas y verificadas en laboratorio sobre la base única; quedan como límites una carrera simultánea real de altas y el aprovisionamiento **persistente** de clave/destinos en el servidor del workshop. Basadas en la [especificación aprobada](../SPEC-central-branch-connections.md) y el [plan aprobado](central-branch-connections-plan.md). No sustituyen las tareas históricas de `platform-access` ni `central-access`.

Cada tarea modifica aproximadamente cinco archivos o menos (sin contar artefactos generados mecánicamente). No crear, clonar ni reiniciar bases: la única base de aplicación de desarrollo es `carnicerias_test_visual` en Docker `127.0.0.1:55433`. Las pruebas de integración que aprovisionan bases están excluidas.

## Tarea 1. Fijar el contrato y los casos de abuso

**Descripción:** definir DTOs, estados y errores de `/api/v1/branch-connections` y pruebas que expresen el aislamiento A/B y la validación de destino antes de desarrollar handlers.

**Aceptación:**
- [ ] Cada ruta tiene entrada/salida tipada; `centralCompanyId` proviene exclusivamente de la sesión.
- [ ] Las pruebas rechazan cookie SuperAdmin/local, UUID de B desde A, origen/CSRF inválidos y URL no permitida sin divulgar secretos.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralBranchConnection --no-restore` (rojo antes del cambio de conducta y verde al implementar los cortes posteriores).

**Dependencias:** ninguna. **Archivos probables:** nuevo contrato en `src/Carnicerias.Central.Api/`, nueva prueba HTTP en `tests/Carnicerias.Central.Tests/`, `tasks/central-branch-connections-ui-contract.md`. **Tamaño:** M.

## Tarea 2. Restringir destinos aprobados por empresa

**Descripción:** admitir entradas de allowlist asociadas al ID de empresa central, conservando la lectura de destinos heredados para registros CLI existentes.

**Aceptación:**
- [ ] A no puede comprobar una URL aprobada solo para B; una entrada heredada no autoriza altas web nuevas.
- [ ] DNS/IP no permitida, redirección, HTTP no-loopback y TLS inválido siguen rechazados antes de enviar un token.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralDestination --no-restore`; build central.

**Dependencias:** 1. **Archivos probables:** `CentralApprovedEndpointConfiguration.cs`, `CentralDestinationPolicy.cs`, `CentralInstallationProbeTests.cs`, nueva prueba de política. **Tamaño:** M.

## Tarea 3. Cifrar credenciales con clave externa versionada

**Descripción:** encapsular cifrado/descifrado autenticado, clave y nonce sin persistencia todavía.

**Aceptación:**
- [ ] Texto, cifrado y datos asociados empresa+instalación funcionan; cambiar cualquier componente o la clave provoca rechazo.
- [ ] Falta de clave, versión desconocida o token malformado fallan cerrados, sin secreto en mensajes o logs.

**Verificación:** `dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --filter FullyQualifiedName~CentralBranchCredentialCrypto --no-restore`.

**Dependencias:** 1. **Archivos probables:** nueva clase de cifrado, lector de clave protegida y prueba pura en `tests/Carnicerias.Central.Tests/`. **Tamaño:** M.

### Checkpoint A — tras tareas 1–3

- [ ] Contrato revisado; pruebas de aislamiento, SSRF y cifrado pasan o fallan únicamente donde todavía falta implementación.
- [ ] La API y la central existente compilan; no se tocó la base.

## Tarea 4. Modelar la credencial cifrada en `central_access`

**Descripción:** agregar entidad y mapeo EF con vínculo inmutable a empresa e instalación.

**Aceptación:**
- [ ] El modelo guarda solo versión, nonce, ciphertext y etiqueta; no hay columna de token en claro.
- [ ] Empresa/instalación y unicidad impiden asociar una credencial a otra conexión.

**Verificación:** prueba de modelo central y `dotnet build src/Carnicerias.Central.Api/Carnicerias.Central.Api.csproj --configuration Release --no-restore`.

**Dependencias:** 3. **Archivos probables:** nueva entidad `CentralBranchCredential.cs`, `CentralAccessDbContext.cs`, nueva prueba de modelo. **Tamaño:** M.

## Tarea 5. Generar y revisar la migración aislada

**Descripción:** generar la migración del contexto central, sin aplicarla todavía.

**Aceptación:**
- [ ] El SQL idempotente crea solo la nueva tabla/índices en `central_access` y no altera esquemas POS.
- [ ] El snapshot coincide con el modelo; no se crea ni reinicia otra base.

**Verificación:** `dotnet ef migrations script --project src/Carnicerias.Central.Infrastructure --startup-project src/Carnicerias.Central.Api --context CentralAccessDbContext --idempotent`; inspección del SQL y build central.

**Dependencias:** 4. **Archivos probables:** migración `.cs`, diseñador y `CentralAccessDbContextModelSnapshot.cs`. **Tamaño:** S (artefactos EF).

## Tarea 6. Resolver credenciales nuevas y heredadas

**Descripción:** escribir/leer tokens cifrados para nuevas conexiones y conservar las referencias de entorno de las conexiones CLI existentes.

**Aceptación:**
- [ ] El dashboard lee ambas fuentes; un fallo al descifrar **no** hace fallback a una variable de entorno.
- [ ] No se devuelven tokens ni referencias; el componente no registra secreto en logs.

**Verificación:** pruebas puras de origen de credencial y regresión `CentralBusinessSummaryServiceTests`; build central.

**Dependencias:** 3–5. **Archivos probables:** nuevo almacén EF, `CentralBusinessSummaryService.cs`, `Program.cs`, nueva prueba de credenciales. **Tamaño:** M.

### Checkpoint B — tras tareas 4–6

- [ ] Build y pruebas .NET centrales verdes; SQL inspeccionado y migración **sin aplicar**.
- [ ] La consulta de las instalaciones CLI continúa operando en pruebas con dobles.

## Tarea 7. Crear/listar conexiones en la API de empresa

**Descripción:** implementar registro atómico tras verificar URL/credencial/tres UUID y listado paginado de activas e inactivas.

**Aceptación:**
- [ ] Alta rechaza duplicados, identidad distinta y destino ajeno a la empresa; guarda conexión+credencial+auditoría o nada.
- [ ] GET filtra por empresa de sesión, devuelve metadatos sin secreto y no expone conexiones de otra empresa.

**Verificación:** pruebas HTTP con dobles para 201/400/401/403/409/422, conflicto concurrente y A/B; build central.

**Dependencias:** 2, 6. **Archivos probables:** nuevo servicio de conexión, endpoint, `Program.cs`, nueva prueba de servicio, nueva prueba HTTP. **Tamaño:** M.

## Tarea 8. Entregar el alta/listado en Angular central

**Descripción:** conectar la sección de gestión al nuevo endpoint con formulario accesible y token de uso único.

**Aceptación:**
- [ ] Lista activa/inactiva con URL y estado correcto; alta desde UI refresca la lista y limpia el token.
- [ ] Errores por campo o por comprobación son claros; el token no aparece en DOM tras envío ni en almacenamiento persistente.

**Verificación:** `npm --prefix src/Carnicerias.Central.Web test -- --watch=false`; `npm --prefix src/Carnicerias.Central.Web run build`; recorrido en navegador real del portal central.

**Dependencias:** 7. **Archivos probables:** `central-installation-client.ts`, nuevo componente de conexiones `.ts/.html/.scss`, especificación de componente. **Tamaño:** M (5).

### Checkpoint C — tras tareas 7–8

- [ ] Flujo API→Angular de alta/listado completo con pruebas de aislamiento y secreto ausente en respuestas.
- [ ] Ante timeout, el cliente relee el listado; no repite ciegamente el POST.

## Tarea 9. Comprobar una conexión existente

**Descripción:** agregar prueba puntual de identidad/conectividad con límite de frecuencia, resultado fechado y auditoría sin modificar configuración.

**Aceptación:**
- [ ] A no comprueba la conexión de B; la prueba no activa una conexión inactiva.
- [ ] Resultado distingue disponible/no disponible y no revela token ni internals.

**Verificación:** pruebas HTTP y de servicio enfocadas en checks, límites y aislamiento; build central.

**Dependencias:** 7. **Archivos probables:** endpoint de conexiones, servicio, `Program.cs`, prueba HTTP, prueba de servicio. **Tamaño:** M.

## Tarea 10. Editar conexión sin perder la configuración válida

**Descripción:** cambiar etiqueta sin red; cambiar URL/token solo tras comprobación y reemplazo transaccional.

**Aceptación:**
- [ ] IDs remotos/empresa son inmutables; un fallo de comprobación conserva URL/token/estado anteriores.
- [ ] Edición ajena responde 404 y cada resultado queda auditado sin secretos.

**Verificación:** pruebas de servicio/HTTP con fallo de red, identidad errónea y empresa ajena; build central.

**Dependencias:** 7, 9. **Archivos probables:** servicio, endpoint, entidad `CentralInstallation.cs`, prueba de servicio, prueba HTTP. **Tamaño:** M.

## Tarea 11. Desactivar y reactivar

**Descripción:** estado administrativo reversible, con nueva verificación obligatoria para reactivar.

**Aceptación:**
- [ ] Desactivada no entra al dashboard, sin borrar credencial ni auditoría; reactivar falla cerrada si la sucursal no verifica.
- [ ] El cambio es idempotente y está aislado por empresa.

**Verificación:** pruebas de estado, endpoint y regresión de `CentralBusinessSummaryServiceTests`; build central.

**Dependencias:** 9, 10. **Archivos probables:** `CentralInstallation.cs`, servicio, endpoint, pruebas HTTP/servicio. **Tamaño:** M.

### Checkpoint D — tras tareas 9–11

- [ ] Editar, comprobar, desactivar y reactivar están cubiertos por pruebas negativas de A/B.
- [ ] Dashboard no suma una instalación desactivada; no se alteran ventas locales.

## Tarea 12. Completar gestión visual y accesibilidad

**Descripción:** añadir comprobar, editar y activar/desactivar a la sección Angular, con confirmación y estados distinguibles.

**Aceptación:**
- [ ] El usuario puede operar sin desplazarse a detalles ocultos; modal/formulario tiene foco, etiquetas y feedback accesible.
- [ ] La UI no confunde última prueba con conectividad permanente ni permite doble envío.

**Verificación:** pruebas de componente y cliente, build Angular y recorrido visual del **portal central** en navegador.

**Dependencias:** 8–11. **Archivos probables:** componente de conexiones `.ts/.html/.scss`, cliente, pruebas de componente. **Tamaño:** M (5).

## Tarea 13. Aplicar migración controlada y smoke en la única base

**Descripción:** preflight de destino, respaldo custom y SHA, revisión final de SQL, migración central y prueba real con una instalación ficticia aprobada.

**Aceptación:**
- [ ] Solo `carnicerias_test_visual` recibe la migración; datos/esquemas POS y conexiones CLI existentes permanecen.
- [ ] Tras reiniciar la central, una conexión nueva funciona con clave protegida y la UI refleja el resultado; no quedan claves de laboratorio en Git.

**Verificación:** procedimiento protegido de `documentacion/despliegue/administracion-central.md`, pruebas .NET/Angular, `git diff --check`, inspección de estado DB sin imprimir credenciales.

**Dependencias:** 2–12. **Archivos probables:** ningún archivo de código adicional salvo ajuste que revelen las pruebas; evidencia en guía de despliegue. **Tamaño:** M (operativo secuencial).

## Tarea 14. Conciliar documentación y decisión

**Descripción:** registrar la nueva autoridad del admin central y la excepción de política de red del fabricante sin borrar la historia CLI-only.

**Aceptación:**
- [ ] Nuevo ADR sustituye la cláusula CLI-only de ADR 0006; `SPEC-central-access.md`, mapa y guías no se contradicen.
- [ ] Manual explica datos necesarios, alta, prueba, desactivación, rotación y qué hacer si falta clave o sucursal.

**Verificación:** revisión cruzada contra código/contrato y comandos reales; `git diff --check`; inspección de diff por secretos antes de commit.

**Dependencias:** 13. **Archivos probables:** nuevo ADR en `documentacion/decisiones/`, `SPEC-central-access.md`, `CAPABILITY-MAP-sucursal-central.md`, `documentacion/despliegue/administracion-central.md`, `documentacion/manual-usuarios/06-administracion-central.md`. **Tamaño:** M (5).

### Checkpoint final

- [ ] Todos los criterios de `SPEC-central-branch-connections.md` verificados o marcados pendientes con evidencia concreta.
- [ ] Build/API y pruebas .NET/Angular verdes; no se ejecutó integración que crease otra base.
- [ ] La única base de aplicación sigue siendo `carnicerias_test_visual`; secretos fuera de Git y revisión POS, si la hubiera, solo en Electron.

## Evidencia de ejecución del 15/10/2026

- La migración `AddCentralBranchCredentials` y la migración `AddCentralConnectionManagement` se aplicaron con el bootstrap protegido **solo** a `carnicerias_test_visual` después de `pg_dump -Fc`, `pg_restore --list` y SHA-256 coincidente. Se conservaron 3 instalaciones centrales, 2 empresas centrales y 56 ventas POS ficticias; no se creó otra base.
- Pasaron 102 pruebas .NET centrales y 25 pruebas Angular. El portal central compiló y se recorrió en Chrome headless autenticado, en pantalla ancha y a 375 px, con listado y modal. No se revisó el POS en navegador.
- El API central nuevo, servido en puerto temporal, devolvió el listado real de 3 sucursales; una edición de etiqueta vía HTTP respondió `200` y la restauración inmediata de la etiqueta original respondió `200`, con revisión incrementada a 2. No se tocó URL, token ni estado de esas conexiones.
- Se completó además un smoke real con `LARUSTICA` y la sucursal local activa **Alta Cordoba**: API de sucursal y central temporales, destino loopback aprobado para esa empresa, clave/token aleatorios solo en memoria. Alta `201`, comprobación **después de reiniciar la API central** `200`, rotación al segundo verificador `200`, desactivación `200` y reactivación `200`. Se validó la identidad del registro temporal y se eliminaron **solo** esa conexión, su credencial cifrada y sus eventos de auditoría de prueba; quedaron las 3 instalaciones originales, cero credenciales nuevas, 2 empresas centrales y 56 ventas POS ficticias. Las conexiones existentes no se modificaron.
- **Pendiente de workshop:** aprovisionar y respaldar una clave maestra y allowlist **persistentes**, luego registrar una conexión definitiva desde la UI. La API temporal inicial sin secretos mostró `hasCredential=false` para registros CLI; la API central preexistente no se sustituyó. También queda la prueba de carrera simultánea real contra la misma base, sin aprovisionar otra.
- La revisión posterior añadió `EnforceUniqueActiveRemoteBranch`: la pareja remota activa ahora es única **entre todas las empresas centrales**, no solo dentro de una. Se comprobó la ausencia de duplicados, se hizo otro respaldo custom verificado y se aplicó la migración protegida en `carnicerias_test_visual`. Una mutación deliberada de la condición de consulta hizo fallar la prueba enfocada y se restauró el código correcto.
