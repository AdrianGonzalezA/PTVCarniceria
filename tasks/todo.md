# Tareas de platform access

**Estado:** aprobadas para implementación  
**Fecha de aprobación:** 29 de septiembre de 2026  
**Ejecución:** tareas 1 a 4 completadas; tarea 5 tiene login/logout API, persistencia y formulario Angular implementados con pruebas automatizadas; quedan pendientes E2E visual en navegador/Electron por falta de superficies UI en esta sesión

## Punto de reanudación

- Cambio de prioridad del 6 de octubre de 2026: pausar nuevas tareas de administración de usuarios y centrar el desarrollo en el punto de venta. El estado y los siguientes incrementos de POS están en `tasks/pos-todo.md`.
- Tareas 1 a 4 completadas.
- Tarea 5 implementada; sigue pendiente la revisión manual E2E de login/logout en Electron y completar inspección runtime de tarea 2A.
- Tarea 6 implementada: asignaciones autorizadas, selección explícita sin valor predeterminado, persistencia del contexto en sesión y selector Angular alineado con la paleta y jerarquía visual de `Documentos/maqueta`.
- Tarea 7 en curso: API ya tiene un filtro reutilizable que exige sesión activa, contexto vigente y permiso, además de entregar el `OperationalContext` validado a través de un accessor de request. Falta aplicarlo a los primeros endpoints de negocio y completar pruebas negativas/arquitectura cuando existan esos recursos.
- Tarea 8 en curso: contrato `IOperationalContextChangeBlocker` conectado al cambio de contexto; bloqueos devuelven `409 CONTEXT_CHANGE_BLOCKED`, y fallas del proveedor cierran el cambio con `503`. El selector muestra el motivo funcional. Aún no hay módulos operativos que registren bloqueadores.
- Tarea 9 en curso: `GET /api/users` tiene permiso administrativo, búsqueda segura y paginación; Angular ya incluye guard y pantalla de listado. El alta queda pendiente de definir la invitación/activación sin que el administrador establezca la contraseña, conforme a la especificación y a la tarea 13.
- Tarea 10 implementada: API y pantalla permiten editar usuario/correo, activar o inactivar usuarios y revocar sus sesiones. La inactivación revoca sesiones vigentes dentro de la misma transacción; reactivar no las restaura. API Release compilada con 0 advertencias/errores y Angular compilado sin avisos de presupuesto. Falta verificación automatizada y revisión visual manual.
- El 6 de octubre de 2026 se aplicó la migración `AddOperationalContextToSessions` en la base PostgreSQL temporal del puerto `55432`; API, Angular y Electron fueron reconstruidos. Electron está iniciado para la revisión manual solicitada y `/api/health` responde `200`. La automatización de esta sesión no expone la ventana al control UI, por lo que la inspección visual queda pendiente del usuario.
- Builds de Infrastructure, API, Angular y Electron completados; no se ejecutaron pruebas automatizadas en este paso. Mantener la base temporal mientras el usuario recorre Electron.

## Tarea 1 Crear la base backend y los contratos

**Descripción:** Crear la solución .NET 10 LTS, proyectos mínimos, configuración de análisis y contratos comunes de contexto y errores.

**Criterios de aceptación:**
- [x] La solución compila en Release y fija el SDK soportado.
- [x] `OperationalContext` y el error HTTP uniforme tienen pruebas de contrato.
- [x] No existen dependencias de negocio desde Domain hacia infraestructura.

**Verificación:**
- [x] `dotnet build Carnicerias.sln --configuration Release`
- [x] `dotnet test Carnicerias.sln --configuration Release`
- [x] `dotnet format Carnicerias.sln --verify-no-changes`

**Dependencias:** ninguna  
**Archivos probables:** `global.json`, `Carnicerias.sln`, `src/Carnicerias.Domain/`, `src/Carnicerias.Api/`, `tests/Carnicerias.ArchitectureTests/`  
**Tamaño:** M, principalmente scaffolding generado

## Tarea 2 Crear la base Angular

**Descripción:** Crear la aplicación Angular 22 standalone en modo estricto, con routing, cliente HTTP base y scripts reproducibles.

**Criterios de aceptación:**
- [x] Angular core y CLI quedan alineados y fijados en el lockfile.
- [x] La aplicación muestra una ruta pública inicial y consume un endpoint de salud.
- [x] Compilación, lint y pruebas funcionan sin instalación global del CLI.

**Verificación:**
- [x] `npm ci --prefix src/Carnicerias.Web`
- [x] `npm run lint --prefix src/Carnicerias.Web`
- [x] `npm test --prefix src/Carnicerias.Web -- --watch=false`
- [x] `npm run build --prefix src/Carnicerias.Web`

**Dependencias:** tarea 1 para fijar contrato y puerto de API  
**Archivos probables:** `src/Carnicerias.Web/package.json`, `package-lock.json`, `angular.json`, `src/app/app.config.ts`, `src/app/core/`  
**Tamaño:** M, con scaffolding generado

## Tarea 2A Crear el contenedor Electron del POS

**Descripción:** Empaquetar la aplicación Angular como POS de escritorio y crear un puente nativo mínimo y tipado que permita verificar capacidades sin implementar todavía protocolos de balanza o impresión.

**Criterios de aceptación:**
- [x] Electron carga el build Angular local sin habilitar `nodeIntegration`.
- [x] `contextIsolation` y sandbox permanecen activos y la navegación se restringe a orígenes aprobados.
- [x] El preload expone solo una operación tipada de diagnóstico; no expone `ipcRenderer`, shell, comandos ni sistema de archivos.

**Verificación:**
- [x] `npm run electron:build --prefix src/Carnicerias.Pos`
- [x] `npm run electron:test --prefix src/Carnicerias.Pos` (5 pruebas)
- [ ] Inspección visual/runtime final de aislamiento, CSP y navegación externa; el ejecutable arrancó y la ventana respondió, pero el control UI no está disponible en esta sesión.

**Dependencias:** tarea 2  
**Archivos probables:** `src/Carnicerias.Pos/package.json`, proceso principal, preload, contrato TypeScript, pruebas  
**Tamaño:** M

## Tarea 3 Persistir instalación e identidades

**Descripción:** Incorporar PostgreSQL, migración inicial y entidades mínimas de empresa, sucursal, usuario, rol, permiso y asignación.

**Criterios de aceptación:**
- [x] Una base vacía migra al esquema esperado sin datos manuales.
- [x] Se aplican unicidad normalizada e invariantes empresa-sucursal.
- [x] Las pruebas usan PostgreSQL real de prueba y demuestran rollback limpio.

**Verificación:**
- [x] Ejecutar migración sobre una base vacía y volver a cero sin dejar el esquema.
- [x] Ejecutar build Release, suite completa (9 pruebas), `dotnet format --verify-no-changes` y `dotnet-ef migrations has-pending-model-changes`.

**Nota de integración:** la prueba PostgreSQL requiere `CARNICERIAS_TEST_CONNECTION_STRING` apuntando a una base desechable cuyo nombre comience por `carnicerias_test_`; sin ella, el test queda omitido de forma explícita.

**Dependencias:** tarea 1  
**Archivos probables:** `src/Carnicerias.PlatformAccess/`, `src/Carnicerias.Infrastructure/`, migración inicial, pruebas de integración  
**Tamaño:** M

## Tarea 4 Crear el primer Administrador

**Descripción:** Implementar la herramienta de implantación que crea el primer Administrador sin exponer su contraseña.

**Criterios de aceptación:**
- [x] La contraseña se ingresa sin argumento ni eco y nunca se registra.
- [x] La operación es transaccional y rechaza duplicados claramente.
- [x] El Administrador queda asignado al contexto inicial y su hash se verifica con el mismo hasher.
- [x] Las contraseñas se almacenan con Argon2id (19 MiB, 2 iteraciones, paralelismo 1) y comparación en tiempo constante.

**Verificación:**
- [x] Ejecutar la herramienta contra una base nueva y rechazar una segunda inicialización sin pedir otra contraseña.
- [x] Comprobar en consola que la entrada no se muestra y que la salida no refleja la contraseña.
- [x] Prueba de integración de creación, hash verificable, contexto inicial y duplicado.
- [x] Build Release, suite completa (19 pruebas con PostgreSQL real) y `dotnet format --verify-no-changes`.

**Permisos iniciales:** `platform.users.manage`, `platform.roles.manage` y `platform.assignments.manage`, centralizados en `PlatformPermissionCatalog`. Argon2id reemplaza BCrypt porque OWASP lo recomienda para sistemas nuevos.

**Dependencias:** tarea 3  
**Archivos probables:** `tools/Carnicerias.Bootstrap/`, caso de uso de bootstrap, prueba de integración  
**Tamaño:** M

## Checkpoint 1 Base

- [ ] Backend, Angular y Electron compilan, formatean y prueban correctamente.
- [ ] Una base vacía puede inicializarse de forma reproducible.
- [ ] Revisión humana antes de continuar.

## Tarea 5 Implementar login y logout completos

**Descripción:** Entregar el flujo vertical de formulario Angular reutilizable en web y Electron, API de sesión, cookie segura, persistencia, logout y errores genéricos.

**Criterios de aceptación:**
- [x] Credenciales válidas crean una sesión revocable y una cookie `HttpOnly`, `Secure` y `SameSite=Strict`.
- [x] Usuario inexistente, contraseña incorrecta e inactivo son indistinguibles públicamente.
- [x] Logout invalida la sesión en servidor y elimina la cookie.

**Verificación:**
- [x] Pruebas unitarias e integración de autenticación contra PostgreSQL real.
- [ ] Prueba E2E de login correcto, incorrecto y logout tanto en navegador como en Electron.
- [x] Pruebas de atributos de cookie y ausencia de tokens en `localStorage`.

**Implementación parcial entregada:** `POST /api/sessions`, `GET /api/sessions/current` y `DELETE /api/sessions/current`; token aleatorio con hash SHA-256 persistido, expiración absoluta de 8 horas (parametrizable en la tarea 14), errores de credenciales genéricos y defensa CSRF por validación de `Origin` más `SameSite=Strict`. El proxy `app://bundle/api` del POS conserva el contrato; no se pudo hacer inspección UI real porque esta sesión no expone navegador ni DevTools.

**Dependencias:** tareas 2, 2A, 3 y 4  
**Archivos probables:** caso de uso de sesión, endpoint de sesiones, persistencia de sesión, componente Angular de login, pruebas E2E  
**Tamaño:** M

## Tarea 6 Seleccionar contexto explícitamente

**Descripción:** Implementar listado autorizado y confirmación obligatoria de empresa y sucursal, incluso cuando solo exista una opción.

**Criterios de aceptación:**
- [x] El usuario solo ve combinaciones asignadas.
- [x] Nunca se selecciona el contexto automáticamente.
- [x] La sesión devuelve un `OperationalContext` válido después de confirmar.

**Verificación:**
- [ ] Pruebas de API para cero, una y múltiples opciones.
- [ ] Prueba E2E de confirmación explícita con una sola opción.

**Implementación entregada:** `GET /api/operational-contexts` lista solo sucursales activas incluidas por asignación de empresa o sucursal; `PUT /api/sessions/current/context` vuelve a validar la asignación y persiste empresa/sucursal en la sesión. La sesión actual expone el `OperationalContext` resuelto por servidor. El selector parte vacío incluso con una sola opción. La migración agrega la pareja opcional empresa/sucursal, restricción de integridad y claves foráneas. El build de API, Angular y Electron pasó; pruebas API/E2E siguen pendientes.

**Dependencias:** tarea 5  
**Archivos probables:** caso de uso de contexto, endpoints, selector Angular, guard de rutas, pruebas  
**Tamaño:** M

## Tarea 7 Aplicar permisos y aislamiento

**Descripción:** Incorporar autorización por permisos y el filtro obligatorio de empresa y sucursal para recursos protegidos.

**Criterios de aceptación:**
- [x] `401` y `403` se aplican de manera consistente en el filtro común de autorización operativa.
- [ ] IDs manipulados no permiten leer ni modificar otro contexto.
- [x] El filtro deja disponible al handler únicamente el `OperationalContext` resuelto en servidor mediante `OperationalContextAccessor`.

**Verificación:**
- [ ] Pruebas negativas de acceso horizontal y elevación de privilegios.
- [ ] Pruebas de arquitectura que impiden saltar el contrato de contexto.

**Corte implementado:** `RequireOperationalPermission` se aplica a endpoints o grupos; responde `401 NOT_AUTHENTICATED`, `403 OPERATIONAL_CONTEXT_REQUIRED` o `403 PERMISSION_REQUIRED` con el contrato de error uniforme. Resuelve usuario/sesión/contexto desde la cookie y base de datos; nunca toma IDs de contexto ni permisos del cliente. No hay aún endpoints de negocio que consumirían este filtro, por lo que aislamiento de recursos y pruebas negativas quedan pendientes para el primer módulo protegido.

**Dependencias:** tarea 6  
**Archivos probables:** middleware de contexto, políticas de autorización, filtros de repositorio, pruebas de integración  
**Tamaño:** M

## Tarea 8 Bloquear cambios con operaciones pendientes

**Descripción:** Definir e implementar el contrato interno que impide cambiar de contexto cuando otro módulo informa una operación bloqueante.

**Criterios de aceptación:**
- [ ] Un proveedor de prueba puede declarar ventas, cajas u otros bloqueos.
- [x] La API rechaza el cambio con código estable y detalle funcional seguro.
- [x] Al no existir bloqueos, se conserva el flujo de cambio sin cerrar sesión.

**Verificación:**
- [ ] Pruebas de contrato con cero, uno y varios bloqueos.
- [ ] Prueba E2E del mensaje y navegación de resolución simulada.

**Corte implementado:** los módulos pueden registrar implementaciones de `IOperationalContextChangeBlocker`, que reciben el contexto actual resuelto en servidor. El selector valida primero el contexto destino, consulta bloqueos solo al cambiar de empresa/sucursal y conserva la selección actual ante `409`. Si un proveedor falla o entrega datos inválidos, el servidor responde `503` y no cambia el contexto. El mensaje del proveedor se valida y se renderiza como texto escapado. Pendiente integrar módulos reales y verificar escenarios con proveedores.

**Dependencias:** tareas 6 y 7  
**Archivos probables:** contrato de bloqueo, orquestador de contexto, respuesta API, vista Angular, pruebas  
**Tamaño:** M

## Checkpoint 2 Acceso

- [ ] Login, confirmación de contexto y logout funcionan de extremo a extremo.
- [ ] Las pruebas demuestran aislamiento entre empresas y sucursales.
- [ ] Un bloqueo operativo impide el cambio de contexto.
- [ ] Revisión humana antes de continuar.

## Tarea 9 Consultar y crear usuarios

**Descripción:** Entregar listado paginado y alta de usuarios desde la interfaz administrativa.

**Criterios de aceptación:**
- [ ] Solo un Administrador autorizado accede a la pantalla y endpoints.
- [ ] Búsqueda y paginación conservan aislamiento de la instalación.
- [ ] Alta valida unicidad y no expone secretos.

**Verificación:**
- [ ] Pruebas API de autorización, validación, conflicto y paginación.
- [ ] Prueba E2E de listado y alta.

**Corte implementado:** `GET /api/users` exige `platform.users.manage`, busca por usuario/correo dentro de la base de la instalación, limita páginas a 100 elementos y ordena de forma estable. La pantalla Angular respeta permiso, incluye búsqueda, paginación, estados vacío/error/carga y no muestra datos de credenciales. El alta aún no se expone: se está resolviendo el flujo de invitación compatible con la regla de que el administrador no establece ni conoce la contraseña definitiva.

**Dependencias:** tareas 6 y 7  
**Archivos probables:** caso de uso de usuarios, endpoints, listado Angular, formulario Angular, pruebas  
**Tamaño:** M

## Tarea 10 Mantener estado y sesiones de usuarios

**Descripción:** Implementar edición permitida, activación, inactivación y revocación administrativa de sesiones.

**Criterios de aceptación:**
- [x] Inactivar un usuario revoca sus sesiones de forma transaccional.
- [x] Reactivar no restaura sesiones anteriores.
- [x] La interfaz muestra el resultado sin revelar datos sensibles.

**Verificación:**
- [ ] Pruebas concurrentes de inactivación y uso de sesión.
- [ ] Prueba E2E de edición, inactivación y acceso rechazado.

**Dependencias:** tarea 9  
**Archivos probables:** caso de uso de estado, endpoint PATCH, formulario Angular, persistencia, pruebas  
**Tamaño:** M

**Corte implementado:** `PATCH /api/users/{userId}` exige `platform.users.manage`; valida origen, entrada y duplicados, y no permite auto-inactivación. La transición a inactivo y la revocación de sesiones vigentes se confirman en una transacción. Reactivar solo cambia el estado y no recupera sesiones revocadas. `DELETE /api/users/{userId}/sessions` revoca sesiones administrativamente y rechaza la auto-revocación. La UI permite editar nombre/correo, cambiar estado y revocar sesiones con confirmación, y muestra errores/resultados sin exponer secretos. Builds API y Angular completados; quedan pruebas de concurrencia/E2E y revisión manual visual.

## Tarea 11 Administrar roles y asignaciones

**Descripción:** Entregar CRUD permitido de roles y asignación de permisos por empresa y sucursal.

**Criterios de aceptación:**
- [ ] Un Administrador crea y modifica roles dentro de permisos autorizados.
- [ ] Las asignaciones inválidas entre empresa y sucursal se rechazan.
- [ ] Los cambios efectivos se reflejan sin confiar en claims del cliente.

**Verificación:**
- [ ] Pruebas de API y dominio para asignaciones válidas e inválidas.
- [ ] Prueba E2E de rol, permiso y alcance.

**Dependencias:** tareas 9 y 10  
**Archivos probables:** casos de uso de roles, endpoints, editor Angular, persistencia, pruebas  
**Tamaño:** M

## Tarea 12 Conservar el último Administrador

**Descripción:** Impedir transaccionalmente que la instalación pierda su último acceso administrativo operativo.

**Criterios de aceptación:**
- [ ] Inactivar, degradar o quitar la última asignación administrativa se rechaza.
- [ ] Dos cambios concurrentes no pueden eludir la invariante.
- [ ] El error indica cómo recuperar una configuración válida.

**Verificación:**
- [ ] Pruebas de integración y concurrencia sobre PostgreSQL.
- [ ] Prueba manual desde la interfaz administrativa.

**Dependencias:** tarea 11  
**Archivos probables:** regla de dominio, transacción de asignaciones, endpoint, pruebas concurrentes  
**Tamaño:** S

## Checkpoint 3 Administración

- [ ] El Administrador gestiona usuarios, roles y asignaciones desde Angular.
- [ ] Un usuario común no accede por UI ni API.
- [ ] La instalación conserva siempre un Administrador operativo.
- [ ] Revisión humana antes de continuar.

## Tarea 13 Recuperar contraseña por correo

**Descripción:** Implementar solicitud, correo, token de un solo uso y establecimiento de nueva contraseña sin enumerar cuentas.

**Criterios de aceptación:**
- [ ] La respuesta es idéntica exista o no el correo.
- [ ] El token vence, se almacena de forma no reversible y no puede reutilizarse.
- [ ] El éxito revoca sesiones y tokens pendientes.

**Verificación:**
- [ ] Pruebas de integración con adaptador de correo de prueba.
- [ ] Pruebas de abuso para enumeración, expiración, manipulación y reutilización.
- [ ] Prueba E2E del flujo completo.

**Dependencias:** tareas 5 y 10  
**Archivos probables:** casos de uso de recuperación, endpoints, adaptador de correo, vistas Angular, pruebas  
**Tamaño:** M

## Tarea 14 Administrar políticas de sesión

**Descripción:** Incorporar parámetros tipados de inactividad, duración absoluta, cierre operativo y concurrencia por rol o perfil.

**Criterios de aceptación:**
- [ ] Los valores admiten desactivación explícita y validan combinaciones.
- [ ] Un Administrador ve valor predeterminado y efectivo y puede modificarlos.
- [ ] Todo cambio conserva anterior, nuevo, actor y fecha.

**Verificación:**
- [ ] Pruebas unitarias de precedencia y validación.
- [ ] Pruebas API de autorización y auditoría.
- [ ] Prueba E2E de edición y aplicación del valor.

**Dependencias:** tareas 11 y 12  
**Archivos probables:** modelo de políticas, persistencia, endpoints, editor Angular, pruebas  
**Tamaño:** M

## Tarea 15 Aplicar concurrencia de sesiones

**Descripción:** Aplicar de forma atómica `maxConcurrentSessions` y las acciones `REJECT_NEW` o `REVOKE_OLDEST`.

**Criterios de aceptación:**
- [ ] Accesos concurrentes nunca superan el máximo efectivo.
- [ ] Ambas acciones producen un resultado determinista.
- [ ] La sesión afectada y el motivo quedan trazados sin secretos.

**Verificación:**
- [ ] Pruebas concurrentes reales sobre PostgreSQL.
- [ ] Prueba E2E de cada acción configurada.

**Dependencias:** tarea 14  
**Archivos probables:** coordinador de sesiones, consulta transaccional, evento de seguridad, pruebas  
**Tamaño:** S

## Checkpoint 4 Políticas

- [ ] Recuperación por correo funciona sin enumeración.
- [ ] Las políticas se editan y aplican de forma determinista.
- [ ] La concurrencia resiste accesos simultáneos reales.
- [ ] Revisión humana antes de continuar.

## Tarea 16 Completar hardening y observabilidad

**Descripción:** Cerrar controles de seguridad, cabeceras, CSRF, límite técnico, métricas, eventos y revisión de dependencias.

**Criterios de aceptación:**
- [ ] Las cabeceras, cookies, CORS y CSRF cumplen la especificación.
- [ ] No hay hallazgos críticos o altos alcanzables sin mitigación documentada.
- [ ] Login, recuperación, revocación y cambios administrativos tienen métricas y eventos sin PII sensible.

**Verificación:**
- [ ] Ejecutar suite de abuso y revisión de seguridad.
- [ ] Ejecutar auditorías nativas contra lockfiles.
- [ ] Inspeccionar respuestas y eventos en runtime.

**Dependencias:** tareas 5 a 15  
**Archivos probables:** middleware de seguridad, configuración, instrumentación, pruebas de seguridad, documentación  
**Tamaño:** M

## Tarea 17 Cerrar E2E y documentación operativa

**Descripción:** Verificar todos los criterios del módulo, documentar implantación, configuración, respaldo y camino de migración a cloud.

**Criterios de aceptación:**
- [ ] Los 29 criterios de la especificación tienen evidencia automatizada o manual trazable.
- [ ] La instalación nueva se despliega con configuración externa y sin secretos versionados.
- [ ] OpenAPI y documentación de operación reflejan el comportamiento final.

**Verificación:**
- [ ] Ejecutar todos los comandos del checkpoint global.
- [ ] Ejecutar recorrido E2E completo en una instalación limpia.
- [ ] Revisar la Definition of Done y obtener aprobación humana.

**Dependencias:** tarea 16  
**Archivos probables:** suite E2E, `docs/api/`, guía de implantación, configuración de ejemplo, matriz de aceptación  
**Tamaño:** M

## Checkpoint final

- [ ] Todas las tareas y criterios de aceptación están completos.
- [ ] Compilación, formato, lint, pruebas unitarias, integración, seguridad y E2E pasan.
- [ ] No hay regresiones ni secretos en el repositorio.
- [ ] Contratos y decisiones están documentados.
- [ ] El módulo fue revisado y aprobado antes de integrar o desplegar.
