# Plan de implementación: acceso independiente a la central

**Estado:** aprobado por el usuario el 14/10/2026. **Módulo:** `central-access` del [mapa aprobado](../CAPABILITY-MAP-sucursal-central.md). **Especificación aprobada:** [SPEC-central-access.md](../SPEC-central-access.md). **Lista detallada propuesta para revisión:** [central-access-todo.md](central-access-todo.md).

Este archivo no reemplaza `tasks/plan.md` ni `tasks/todo.md`, que aún tienen tareas abiertas de `platform-access`. Sigue la convención de planes específicos por módulo usada en `branch-service-contract`.

## Resultado y alcance

Una API y un sitio Angular centrales, independientes del servidor POS, autenticarán a un administrador propio de una empresa central y mostrarán únicamente sus instalaciones registradas. El equipo de implementación dará de alta empresas, primer administrador y conexiones mediante comandos interactivos protegidos. No habrá aún ABM web central, resúmenes de ventas, proxy de administración local ni copia de datos comerciales. Las sucursales seguirán vendiendo aunque la central esté apagada.

## Decisiones técnicas

1. **Persistencia aislada dentro de la única base visual.** `Carnicerias.Central.Infrastructure` tendrá su propio `DbContext`, entidades y migraciones bajo `central_access`, con una tabla de historial EF propia en ese esquema. La cadena de desarrollo apunta exclusivamente a `carnicerias_test_visual` en `127.0.0.1:55433`. La API central no registra `PlatformAccessDbContext`, no ejecuta migraciones al arrancar y no consulta tablas operativas locales. Antes de aplicar una migración se verifican destino, migraciones pendientes y respaldo de la misma base. Una futura base exclusiva de central requiere aprobación y procedimiento de traslado aparte.
2. **Identidades y sesiones sin herencia local.** Empresa, administrador y sesión centrales tienen IDs propios. Contraseñas con Argon2id y sesiones con token aleatorio cuyo hash se persiste; cookie `carnicerias.central.session` `HttpOnly`, `Secure`, `SameSite=Strict`, máximo ocho horas. Se podrá reutilizar el componente criptográfico existente sin registrar ni consultar el contexto local. Login, sesión actual y logout comprueban empresa/usuario activos y revocación; entrada mal formada, ausencia de usuario y contraseña incorrecta no filtran existencia.
3. **Alta inicial fuera de HTTP.** Un CLI central solicita contraseña de forma interactiva, sin ponerla en argumentos, logs o Git. Crea empresa y primer administrador en una transacción e impide duplicados por código/usuario. Un procedimiento de recuperación para implementación revoca sesiones al cambiar la contraseña; no es el ABM ni el reseteo web diferidos. Las altas y revocaciones generan auditoría persistente sin secretos.
4. **Registro de conexión con verificación remota.** El registro guarda `centralCompanyId` y los tres IDs remotos; no presume que `remoteCompanyId` sea igual al ID central. Destino y referencia a secreto se escriben solo desde el CLI. El token vive en configuración protegida externa y se resuelve por referencia; nunca se devuelve al navegador ni se almacena en la tabla. Antes de activar, el CLI consulta `/api/central/v1/installation` y compara `installationId`, `companyId` y `branchId`. Rechaza redirecciones, TLS inválido, destino fuera de la lista/red VPN aprobada y discrepancias; HTTP loopback requiere una opción explícita de laboratorio. El cliente saliente tiene tiempo máximo y no es un proxy general.
5. **API y UI acotadas.** La API central expone solo `/api/v1/sessions` y `/api/v1/installations` del contrato aprobado. Listado paginado filtra por empresa derivada de la sesión, no por parámetros. Errores estructurados, `no-store`, origen/CSRF en mutaciones y límite estricto de login. El Angular central se sirve desde el mismo origen que su API en despliegue, evitando CORS con credenciales, pero constituye un artefacto diferente del Angular local. La lista muestra *registrada*, nunca *conectada* sin consulta viva posterior.

## Grafo de dependencias

```text
Proyectos centrales y esquema/migraciones aisladas
             │
             ├── Alta protegida de empresa/administrador ──► sesión API ──► login Angular
             │                                                │
             └── Registro de instalación + cliente validado ─┴─► listado API ──► lista Angular
                                                                        │
                                                                        ▼
                                              smoke en la única base, guía y ADR
```

No se habilita una instalación solo porque exista una fila: el contrato remoto debe probar identidad y credencial. El primer smoke real de registro exigirá reiniciar deliberadamente una API de sucursal con `Federation` configurado; la instancia hoy levantada aún ejecuta el binario anterior. Ese reinicio no ocurre durante la planificación. Si no existe un destino VPN/HTTPS de laboratorio, se registrará la verificación física como pendiente, sin fabricar una segunda base.

## Orden de entregas y checkpoints

| Orden | Incremento verificable | Criterio principal y comprobación |
|---|---|---|
| 1 | Proyectos y contexto central aislado | Compilan separados; migración/historial SQL apuntan solo a `central_access`; ninguna migración se ejecuta al iniciar. Revisar SQL y tests de mapeo antes de tocar la base. |
| 2 | Migración controlada de identidades y auditoría | Preflight y respaldo; migración aplicada solo a `carnicerias_test_visual`, sin alterar tablas locales. Consulta de esquema posterior y build. |
| 3 | Alta interactiva de empresa y primer administrador | Contraseña no aparece en proceso/logs/DB en claro; duplicados se rechazan y la operación es atómica. Pruebas de servicio/CLI y smoke acotado. |
| 4 | Login, sesión actual y logout de la API central | Cookie y permisos propios; rechazos cruzados, origen inválido, rate limit y revocación cubiertos por pruebas HTTP no destructivas. |
| 5 | Pantalla Angular de ingreso | Empresa + usuario + contraseña; sesión recuperable y errores genéricos. Tests de componente y recorrido visual del **sitio central**. |
| 6 | Registro protegido de instalaciones | URL/secretos del CLI, validación de red/TLS/identidad, unicidad de `installationId`, auditoría. Tests con cliente HTTP simulado; smoke real solo si se configuró la API de sucursal. |
| 7 | Listado central paginado | Solo instalaciones activas de la empresa de sesión; A no ve B ni recibe destino o secreto. Tests de consultas y HTTP. |
| 8 | Pantalla de sucursales registradas | Consume listado paginado y distingue registro de conectividad; pruebas Angular y revisión visual. |
| 9 | Operación y cierre de calidad | ADR, guía de arranque/alta/rotación/recuperación, smoke central sobre la misma base, revisión de secretos y regresión local acotada. |

**Checkpoint A (1–4):** contexto y migraciones aislados; login real de laboratorio con administrador central; credenciales local/central/servicio no se cruzan. Revisar antes de UI.

**Checkpoint B (5–7):** alta de instalación falla cerrada y el listado prueba alcance por empresa. La UI de login funciona; ningún destino sale del navegador hacia el cliente HTTP remoto.

**Checkpoint final (8–9):** build/test de API y Angular, smoke en la única base, auditoría y documentación; ventas locales sin cambios. El workshop multiservidor por VPN/HTTPS queda identificado como pendiente hasta disponer de esa red.

La lista detallada posterior partirá los incrementos que requieran más de cinco archivos en tareas S/M, cada una con aceptación, verificación y dependencias. No se delegará trabajo en agentes paralelos; las migraciones, la provisión y el smoke comparten la misma base y deben ejecutarse en secuencia.

## Modelo de amenazas y mitigaciones

| Riesgo | Mitigación y evidencia prevista |
|---|---|
| Una cuenta local o un token de sucursal se acepta como sesión central | Almacenes/cookie separados, pruebas negativas en ambos sentidos y ninguna referencia a `PlatformAccessDbContext` desde la API central. |
| Un administrador de A manipula IDs o paginación para ver B | Filtro de empresa obtenido de sesión en cada consulta, límites de paginación, tests con dos empresas homónimas. |
| Registro se convierte en SSRF o suplantación de sucursal | Alta solo CLI, allowlist exacta de esquema/host/puerto y red aprobada, sin redirecciones, TLS validado, identidad remota exacta. Revisar resolución DNS/egreso al implementar. |
| Contraseña, cookie, token remoto o conexión quedan en Git/logs | Entrada interactiva, secretos externos, hashes persistidos, respuestas allowlist y revisión de diff/logs; configuración de ejemplo solo con marcadores. |
| Migración central altera POS o crea otra base | DbContext e historial separados, migración sin `Migrate` en startup, preflight de DB exacta y respaldo, comparación del esquema local antes/después. |
| La sucursal está caída y la central muestra un estado falso | El listado es metadato registrado, no telemetría viva; conectividad y datos incompletos se implementarán en `central-business-view`. |
| El portal central se publica con limitador solo en memoria | Laboratorio de un proceso; antes de exposición pública/multiproceso exigir control compartido o en el borde, HTTPS y revisión de proxy/origen. |

## Verificación permitida

```powershell
dotnet build src/Carnicerias.Central.Api/Carnicerias.Central.Api.csproj --configuration Release --no-restore
dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --no-restore
npm --prefix src/Carnicerias.Central.Web run build
npm --prefix src/Carnicerias.Central.Web test -- --watch=false
git diff --check
```

Las pruebas nuevas no aprovisionarán ni reiniciarán otra base. No se ejecutará la suite de integración local completa: contiene pruebas que crean/reinician bases. La migración y el smoke de login/registro usarán solamente la base visual existente, con verificación previa del destino y sin imprimir credenciales. La revisión visual del POS, si llegara a hacer falta, será en Electron; el nuevo sitio central sí podrá revisarse en navegador.

## Decisiones posteriores, fuera de este plan

- ABM web de administradores y conexiones centrales, reseteo de contraseña para administradores y permisos más finos.
- `central-business-view`: consultar en vivo, agregar totales compatibles y marcar explícitamente sucursales inaccesibles.
- Base exclusiva para central en un despliegue real, más estrategia de migración desde el esquema de laboratorio, sin autorizar otra base durante este desarrollo.
- Red/VPN/HTTPS real, política de egreso y publicación del portal; no se expondrá el laboratorio a Internet por este plan.
