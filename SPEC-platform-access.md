# Especificación del módulo platform access

## Estado

**Versión:** 1.1  
**Estado:** aprobado  
**Fecha de aprobación:** 29 de septiembre de 2026  
**Mapa de origen:** `CAPABILITY_MAP.md`  
**Module id:** `platform-access`

## Supuestos del borrador

1. Cada cliente tendrá una instalación aislada y una base PostgreSQL propia.
2. Una instalación podrá contener varias empresas legales y varias sucursales del mismo cliente. **Ratificado.**
3. No se alojarán datos de clientes distintos en una misma instalación.
4. La aplicación se diseñará para poder trasladar posteriormente API, frontend y base de datos a servicios cloud sin modificar las reglas de negocio ni los contratos públicos.
5. La autenticación inicial será local mediante usuario o correo y contraseña. SSO y MFA no forman parte de esta versión. **Ratificado.**
6. El navegador mantendrá la sesión mediante cookie segura administrada por el servidor; no se guardarán tokens de autenticación en `localStorage`. **Ratificado.**

Todos los supuestos de esta versión fueron ratificados.

## Objetivo

Proveer identidad, autenticación, sesión, autorización y contexto operativo para todos los módulos del sistema. Un usuario autenticado deberá poder trabajar únicamente con empresas, sucursales y funciones expresamente autorizadas dentro de la instalación de su cliente.

El módulo debe:

- autenticar usuarios sin revelar información que facilite ataques;
- permitir seleccionar y cambiar el contexto de empresa y sucursal cuando el usuario tenga más de una opción;
- entregar a los módulos consumidores una identidad y un contexto inequívocos;
- aplicar autorización en el servidor para toda operación protegida;
- registrar los eventos de seguridad que luego consumirá `audit-operations`;
- mantener separación lógica estricta entre empresas y sucursales de una misma instalación;
- evitar dependencias con rutas, nombres de host, almacenamiento de sesiones o secretos ligados a una única máquina.

## Usuarios y casos de uso

### Usuario operativo

- Inicia sesión con usuario o correo y contraseña.
- Selecciona y confirma explícitamente una empresa y sucursal entre las asignadas, incluso cuando solo exista una opción.
- Accede únicamente a módulos y acciones permitidas por sus roles en el contexto activo.
- Cambia de contexto sin mezclar datos ni operaciones pendientes.
- Solo puede cambiar de contexto cuando no existen ventas en borrador, cajas abiertas ni otras operaciones pendientes bloqueantes.
- Cierra sesión e invalida su sesión activa.

### Administrador autorizado

- Accede desde la interfaz web al módulo de administración de usuarios, roles y asignaciones.
- Crea, activa, desactiva y consulta usuarios.
- Crea o mantiene roles dentro de las reglas permitidas y asigna roles y alcance por empresa y sucursal.
- Restablece el acceso mediante un procedimiento controlado y auditable.
- No puede recuperar ni visualizar contraseñas.

### Responsable de implantación

- Crea el primer administrador mediante un comando de inicialización ejecutado fuera de la interfaz pública.
- Configura secretos y parámetros mediante variables de entorno o un almacén de secretos.

## Alcance

### Incluido

- inicio y cierre de sesión;
- selección de empresa y sucursal;
- usuarios locales;
- roles, permisos y asignaciones con alcance;
- activación e inactivación de usuarios;
- cambio de contexto operativo;
- invalidación de sesiones;
- recuperación de contraseña por correo electrónico;
- creación segura del administrador inicial;
- interfaz web completa para administrar usuarios, roles y asignaciones, disponible para el rol Administrador;
- eventos de seguridad para auditoría;
- parámetros administrables de política de sesión por rol o perfil operativo;
- controles contra enumeración y tráfico automatizado extremo sin bloqueo de cuentas ni espera progresiva para errores normales.

### Fuera de alcance

- SSO, OAuth social o federación corporativa;
- MFA;
- autoservicio de registro de usuarios;
- portal cloud central para administrar instalaciones;
- autorización propia de ventas, caja, stock o fiscalidad, que será definida por cada módulo consumidor;
- sincronización de identidades entre instalaciones.

## Stack tecnológico

- Backend: .NET 10 LTS, fijado mediante `global.json` y actualizado dentro de la misma línea soportada.
- Persistencia: PostgreSQL en una base independiente por cliente.
- Interfaz: Angular 22 con componentes standalone y modo estricto, fijado mediante `package-lock.json`.
- Runtime de frontend: Node.js 24 dentro del rango oficialmente compatible con Angular 22.
- Contenedor POS: Electron con versión estable fijada en lockfile. Reutilizará la aplicación Angular y expondrá únicamente contratos nativos específicos mediante preload e IPC tipado.
- Transporte: HTTPS en todos los ambientes distintos del desarrollo local.
- Sesión: identificador opaco en cookie `HttpOnly`, `Secure` y `SameSite`, con estado o revocación controlados por servidor.
- Contratos: API HTTP JSON bajo `/api`, documentada mediante OpenAPI.
- Despliegue: configuración externa, procesos sin estado local obligatorio y almacenamiento persistente sustituible para facilitar migración a cloud.
- Periféricos: balanza, lector y comandera serán responsabilidad del contenedor Electron del POS; la aplicación Angular web administrativa no accederá directamente al hardware.

Las versiones exactas de parche y los paquetes auxiliares se fijarán al crear la base del proyecto, después de verificar compatibilidad y soporte oficial, y quedarán inmovilizados mediante `global.json` y lockfiles.

Fuentes de la decisión tecnológica:

- Angular 22 se encuentra en soporte activo: `https://angular.dev/reference/releases`.
- La matriz oficial de Angular 22 admite Node.js 24 y define sus rangos de TypeScript y RxJS: `https://angular.dev/reference/versions`.
- .NET 10 es una versión LTS con soporte hasta noviembre de 2028: `https://learn.microsoft.com/dotnet/core/releases-and-support`.
- Electron documenta acceso a dispositivos seriales, separación de procesos, IPC y aislamiento de contexto: `https://www.electronjs.org/docs/latest/tutorial/devices`, `https://www.electronjs.org/docs/latest/tutorial/process-model` y `https://www.electronjs.org/docs/latest/tutorial/context-isolation`.

## Comandos del proyecto

Estos comandos serán el contrato mínimo una vez creada la solución:

```powershell
dotnet restore Carnicerias.sln
dotnet build Carnicerias.sln --configuration Release --no-restore
dotnet test Carnicerias.sln --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet format Carnicerias.sln --verify-no-changes
dotnet run --project src/Carnicerias.Api/Carnicerias.Api.csproj
npm ci --prefix src/Carnicerias.Web
npm run build --prefix src/Carnicerias.Web
npm test --prefix src/Carnicerias.Web -- --watch=false
npm run lint --prefix src/Carnicerias.Web
npm start --prefix src/Carnicerias.Web
npm run electron:build --prefix src/Carnicerias.Pos
npm run electron:test --prefix src/Carnicerias.Pos
```

El comando de creación del administrador inicial deberá tener una forma equivalente a:

```powershell
dotnet run --project tools/Carnicerias.Bootstrap -- create-admin --username administrador --email admin@cliente.local
```

La contraseña no se aceptará como argumento de línea de comandos; deberá ingresarse de manera interactiva o mediante un secreto de un solo uso.

## Estructura propuesta

```text
src/
  Carnicerias.Api/             API HTTP, middleware y composición
  Carnicerias.Web/             Aplicación Angular 22
  Carnicerias.Pos/             Contenedor Electron, preload y adaptadores de periféricos
  Carnicerias.PlatformAccess/ Casos de uso y reglas del módulo
  Carnicerias.Domain/          Tipos compartidos mínimos y eventos
  Carnicerias.Infrastructure/ Persistencia, sesiones y adaptadores
tests/
  Carnicerias.UnitTests/
  Carnicerias.IntegrationTests/
  Carnicerias.ArchitectureTests/
tools/
  Carnicerias.Bootstrap/       Inicialización segura de instalaciones
docs/
  api/                         Contratos OpenAPI publicados
```

Los demás módulos no podrán consultar directamente las tablas internas de `platform-access`. Consumirán identidad y contexto mediante contratos de aplicación.

La aplicación Angular no importará módulos de Node ni Electron. El contenedor POS ofrecerá capacidades específicas como consultar estado del dispositivo o solicitar una lectura mediante un `contextBridge` tipado. No expondrá `ipcRenderer`, sistema de archivos ni ejecución de comandos de propósito general.

## Modelo funcional

### Entidades principales

- `User`: identidad local, nombre de usuario, correo normalizado, estado y datos de seguridad.
- `Role`: conjunto estable de permisos.
- `Permission`: capacidad atómica identificada por un código estable.
- `UserAssignment`: relación entre usuario, rol, empresa y, opcionalmente, sucursal.
- `Company`: empresa habilitada dentro de la instalación.
- `Branch`: sucursal perteneciente a una empresa.
- `Session`: sesión revocable con creación, expiración, última actividad y contexto activo.
- `SessionPolicy`: parámetros de inactividad, duración absoluta, renovación, cierre por turno y concurrencia aplicables a un rol o perfil operativo.

### Invariantes

1. El nombre de usuario y el correo normalizados serán únicos dentro de la instalación.
2. Toda sucursal pertenecerá exactamente a una empresa.
3. Un contexto activo siempre contendrá una empresa autorizada y, cuando corresponda, una sucursal perteneciente a esa empresa.
4. Ningún contexto se seleccionará automáticamente después del login; el usuario deberá confirmarlo de forma explícita aunque solo tenga una empresa y sucursal habilitadas.
5. Un rol sin asignación al contexto no concede permisos en ese contexto.
6. Un usuario inactivo no podrá iniciar sesión y sus sesiones existentes quedarán invalidadas.
7. El cambio de contexto no reutilizará identificadores de operaciones o borradores pertenecientes al contexto anterior.
8. Ningún cliente podrá enviar un `companyId`, `branchId`, rol o permiso y convertirlo por sí mismo en autoridad; el servidor resolverá y validará todo alcance.
9. El cambio de empresa o sucursal será rechazado mientras cualquier módulo informe una operación pendiente bloqueante para el usuario, terminal o contexto activo.

## Contratos del módulo

### Contexto entregado a consumidores internos

```csharp
public sealed record OperationalContext(
    Guid UserId,
    Guid CompanyId,
    Guid BranchId,
    IReadOnlySet<string> Permissions,
    Guid SessionId);
```

Convenciones:

- identificadores opacos, nunca códigos editables de negocio;
- permisos con nombres estables como `sales.create` o `catalog.view`;
- el contexto se construye en el servidor para cada solicitud;
- los módulos consumidores reciben el contexto ya autenticado, pero deben verificar el permiso específico de su operación.

### API HTTP inicial

| Método y ruta | Propósito | Autenticación |
|---|---|---|
| `POST /api/sessions` | Iniciar sesión | Pública, protegida contra tráfico automatizado extremo |
| `DELETE /api/sessions/current` | Cerrar la sesión actual | Requerida |
| `GET /api/sessions/current` | Obtener identidad, contexto y vencimiento | Requerida |
| `POST /api/password-reset-requests` | Solicitar un enlace de recuperación por correo | Pública, con limitación estricta |
| `POST /api/password-resets` | Establecer una nueva contraseña mediante token de un solo uso | Pública, con token válido |
| `GET /api/operational-contexts` | Listar empresas y sucursales autorizadas | Requerida |
| `PUT /api/sessions/current/context` | Seleccionar o cambiar empresa y sucursal | Requerida |
| `GET /api/users?page=1&pageSize=20` | Consultar usuarios con paginación | Permiso administrativo |
| `POST /api/users` | Crear usuario | Permiso administrativo |
| `PATCH /api/users/{userId}` | Actualizar datos o estado permitido | Permiso administrativo |
| `PUT /api/users/{userId}/assignments` | Reemplazar asignaciones de alcance de forma controlada | Permiso administrativo |
| `DELETE /api/users/{userId}/sessions` | Revocar todas las sesiones del usuario | Permiso administrativo |
| `GET /api/roles?page=1&pageSize=20` | Consultar roles y permisos asignados | Permiso administrativo |
| `POST /api/roles` | Crear un rol dentro de los límites autorizados | Permiso administrativo |
| `PATCH /api/roles/{roleId}` | Modificar nombre y permisos admitidos | Permiso administrativo |

Todas las respuestas de error usarán:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "No se pudo completar la solicitud",
    "details": []
  }
}
```

Los errores de autenticación utilizarán un mensaje genérico idéntico para usuario inexistente, contraseña incorrecta o usuario inactivo. No expondrán hashes, tokens, asignaciones internas, trazas ni causas técnicas.

## Reglas de autenticación y sesión

1. Usuario o correo y contraseña son obligatorios.
2. Las contraseñas se almacenarán únicamente con un algoritmo de hash adaptativo aprobado al momento de implementar y parámetros configurables.
3. La comparación de credenciales no revelará por tiempo o mensaje si el usuario existe.
4. Los intentos fallidos no bloquearán la cuenta ni impondrán una espera progresiva al usuario. El acceso debe permanecer fluido ante errores normales de escritura.
5. La expiración absoluta y por inactividad serán parámetros independientes. Cada uno podrá configurarse por rol o perfil operativo y admitir el valor desactivado cuando la política aprobada lo permita.
6. El cierre de sesión invalidará el identificador en el servidor y eliminará la cookie.
7. Un cambio de contraseña, inactivación o revocación administrativa invalidará las sesiones alcanzadas.
8. Los endpoints mutables protegidos por cookie incorporarán defensa CSRF.
9. La aplicación renovará el identificador de sesión después de autenticar y después de un cambio relevante de privilegios.
10. No se registrarán contraseñas, cookies, tokens ni secretos en logs o auditoría.
11. Para Cajero/Carnicero podrá desactivarse el vencimiento por inactividad para no interrumpir la operación del puesto de venta.
12. Aunque no exista vencimiento por inactividad, la sesión seguirá siendo revocable y podrá cerrarse por fin de turno, cierre de caja, inactivación del usuario, cambio de contraseña o decisión administrativa.
13. La duración absoluta podrá configurarse separadamente. Desactivarla requerirá una decisión explícita del Administrador y quedará auditada.
14. Las operaciones sensibles definidas por otros módulos podrán exigir reautenticación reciente aunque la sesión general continúe activa.
15. La cantidad máxima de sesiones simultáneas será parametrizable por rol o perfil operativo. Un valor sin límite deberá configurarse explícitamente.
16. Al superar el máximo configurado, la política indicará de forma explícita si se rechaza el nuevo acceso o se revoca la sesión más antigua; nunca se elegirá el comportamiento de manera implícita.
17. La creación y revocación provocadas por la política de concurrencia emitirán eventos para `audit-operations`.
18. La infraestructura aplicará un límite técnico alto por origen únicamente para contener tráfico automatizado extremo y denegación de servicio. El umbral será configurable, no alterará el estado de la cuenta y no requerirá desbloqueo administrativo.
19. Los intentos fallidos emitirán métricas y eventos de seguridad sin registrar la contraseña; los patrones anómalos podrán generar alertas sin impedir la operación legítima.

### Parametrización del sistema

- Las políticas de sesión se administrarán desde una interfaz restringida al rol Administrador.
- Los parámetros tendrán tipo, alcance, valor predeterminado, valor efectivo, descripción y validaciones; no se almacenarán como texto libre sin esquema.
- La política de concurrencia incluirá `maxConcurrentSessions` y `concurrencyLimitAction`, cuyos valores válidos serán `REJECT_NEW` y `REVOKE_OLDEST`.
- El alcance podrá ser instalación y, cuando se justifique, rol o perfil operativo. No se duplicarán parámetros por empresa o sucursal sin una necesidad funcional aprobada.
- Todo cambio conservará valor anterior, valor nuevo, actor y fecha, y emitirá un evento para `audit-operations`.
- Cada módulo futuro declarará sus propios parámetros y reglas de precedencia dentro del mecanismo común, evitando constantes operativas dispersas en código.
- Los secretos, certificados y credenciales no son parámetros funcionales y permanecerán en variables de entorno o en un almacén de secretos.

### Recuperación de contraseña

1. El usuario podrá solicitar la recuperación mediante su correo registrado.
2. La respuesta pública será la misma exista o no una cuenta activa asociada al correo.
3. El enlace contendrá un token aleatorio de un solo uso, almacenado únicamente de forma no reversible y con vencimiento configurable.
4. Un token vencido, utilizado, revocado o manipulado será rechazado sin revelar información interna.
5. Al completar el restablecimiento se invalidarán los demás tokens de recuperación y las sesiones activas del usuario.
6. El correo incluirá la identificación de la instalación y una URL base configurada externamente; no se construirá desde encabezados no confiables de la solicitud.
7. La solicitud y el resultado del restablecimiento emitirán eventos para `audit-operations` sin incluir el token ni la contraseña.
8. El Administrador podrá iniciar un restablecimiento o exigir cambio de contraseña, pero nunca establecer o visualizar secretamente la contraseña definitiva del usuario.

## Autorización y aislamiento

- La interfaz podrá ocultar acciones no permitidas, pero la decisión de autorización será siempre del servidor.
- La interfaz de administración será visible para el rol Administrador, pero cada endpoint exigirá el permiso administrativo correspondiente; el nombre del rol por sí solo no sustituirá la autorización del servidor.
- Ningún administrador podrá retirar el último acceso administrativo operativo de la instalación sin un mecanismo explícito de recuperación.
- Cada acceso a datos transaccionales recibirá `CompanyId` y `BranchId` desde `OperationalContext`, nunca directamente como autoridad del cuerpo de la solicitud.
- Las consultas se filtrarán por contexto antes de paginar o calcular totales.
- Los identificadores de otra empresa o sucursal producirán una respuesta que no permita inferir la existencia del recurso.
- La base independiente por cliente será una barrera de aislamiento adicional, no un reemplazo de la autorización entre empresas de la misma instalación.
- Antes de cambiar el contexto, `platform-access` consultará un contrato interno de bloqueos operativos. `pos-sales` informará ventas en borrador, `payments-cash` informará cajas abiertas y otros módulos podrán declarar bloqueos equivalentes sin acoplar sus tablas internas.
- Cuando exista un bloqueo, la API devolverá un error estructurado con un código estable y referencias funcionales suficientes para que la interfaz conduzca al usuario a resolver la operación, sin exponer datos sensibles.

## Modelo de amenazas resumido

| Amenaza | Activo | Control requerido |
|---|---|---|
| Suplantación | Sesión e identidad | Hash robusto, cookies seguras, rotación, expiración y revocación |
| Manipulación | Contexto y asignaciones | Validación en servidor, transacciones y control de concurrencia |
| Repudio | Login, logout y cambios de permisos | Eventos inmutables enviados a `audit-operations` |
| Divulgación | Credenciales y datos entre empresas | Errores genéricos, mínimo privilegio y filtrado por contexto |
| Denegación de servicio | Endpoint de login | Límites de tasa, límites de tamaño y tiempos máximos |
| Elevación de privilegios | Roles y cambio de contexto | Autorización por operación y prohibición de confiar en claims enviados por el cliente |

## Estilo de código

- C# con tipos anulables habilitados.
- Tipos y miembros públicos en `PascalCase`; variables y parámetros en `camelCase`.
- Operaciones asíncronas con sufijo `Async` y `CancellationToken`.
- DTO de entrada, modelo de dominio y respuesta separados.
- Un caso de uso no accede a `HttpContext`; recibe contratos tipados.
- Códigos de error y permisos son constantes estables, no textos visibles reutilizados como lógica.
- Las fechas se almacenan en UTC y se muestran en la zona horaria configurada para la sucursal.

## Estrategia de pruebas

### Unitarias

- normalización de usuario y correo;
- selección válida e inválida de contexto;
- cálculo de permisos por asignación;
- expiración y revocación de sesión;
- resolución de la política de sesión por rol o perfil y aplicación de parámetros desactivados;
- aplicación determinista del máximo de sesiones simultáneas bajo condiciones concurrentes;
- invariantes de empresa y sucursal.

### Integración

- login correcto e incorrecto contra PostgreSQL real de prueba;
- creación, rotación y eliminación de cookies;
- persistencia y revocación de sesiones;
- consultas paginadas y filtradas;
- concurrencia al modificar asignaciones;
- migraciones desde una base vacía.

### Seguridad y abuso

- enumeración de usuarios por cuerpo, estado y tiempos observables;
- fuerza bruta sin bloqueo de cuenta ni espera progresiva, incluyendo el límite técnico ante tráfico automatizado extremo;
- enumeración de cuentas mediante recuperación de contraseña;
- reutilización, vencimiento, manipulación y exposición de tokens de recuperación;
- fijación y robo de sesión;
- CSRF;
- acceso horizontal a otra empresa o sucursal;
- elevación de privilegios mediante manipulación de IDs, roles o permisos;
- inyección y XSS en campos visibles;
- ausencia de secretos y PII sensible en logs.

### End to end

- login, selección de contexto, navegación autorizada y logout;
- confirmación obligatoria del contexto cuando el usuario posee una única empresa y sucursal;
- usuario con varias empresas y sucursales;
- usuario sin contexto habilitado;
- solicitud y finalización de recuperación de contraseña por correo;
- rechazo de token vencido o reutilizado e invalidación de sesiones previas;
- inactivación durante una sesión activa;
- sesión operativa de Cajero/Carnicero sin vencimiento por inactividad, pero revocable por los eventos configurados;
- acceso simultáneo desde varias terminales dentro del máximo configurado y resolución correcta al excederlo;
- modificación auditada de parámetros de sesión por un Administrador;
- administración web de usuarios, roles y asignaciones por un Administrador;
- rechazo de las mismas acciones para un usuario sin permisos administrativos;
- cambio de contexto sin arrastre de datos de la selección anterior.
- rechazo del cambio de contexto ante una venta en borrador, caja abierta u otra operación pendiente, con indicación funcional para resolverla.

La implementación no se considerará terminada con pruebas unitarias solamente. Deberá demostrar aislamiento con pruebas de integración y abuso.

## Límites de trabajo

### Siempre

- validar entradas en el límite HTTP;
- autenticar y autorizar en el servidor;
- usar consultas parametrizadas;
- mantener secretos fuera del código y del repositorio;
- emitir eventos de seguridad sin datos sensibles;
- ejecutar compilación, formato y todas las pruebas antes de integrar cambios;
- documentar contratos y cambios de permisos.

### Consultar antes

- agregar SSO, MFA o proveedores externos de identidad;
- cambiar el modelo de cookie o sesión;
- crear nuevos roles globales o permisos administrativos;
- almacenar nuevas categorías de datos personales;
- cambiar CORS, el límite técnico contra tráfico automatizado extremo o tiempos de sesión;
- crear parámetros sin tipo, alcance, valor predeterminado y reglas de validación documentadas;
- compartir identidades entre instalaciones.

### Nunca

- almacenar contraseñas en texto plano o con cifrado reversible;
- guardar tokens de sesión en `localStorage`;
- aceptar empresa, sucursal, rol o permiso del cliente sin validación del servidor;
- usar comodín de CORS con credenciales;
- registrar contraseñas, hashes, cookies, tokens o secretos;
- permitir que un administrador vea la contraseña de otro usuario;
- revelar si un usuario existe mediante el mensaje de login;
- desactivar controles de seguridad para resolver pruebas fallidas.

## Criterios de aceptación

1. Un usuario válido puede autenticarse y recibe una cookie segura sin exponer credenciales ni tokens al código de la interfaz.
2. Credenciales incorrectas, usuario inexistente e inactivo producen la misma respuesta pública.
3. Los campos vacíos o inválidos se rechazan sin consultar innecesariamente información sensible.
4. Un usuario solo puede listar y seleccionar empresas y sucursales asignadas.
5. El servidor rechaza el acceso a recursos de otro contexto aunque el cliente manipule IDs o solicitudes HTTP.
6. Después de autenticarse, todo usuario debe seleccionar y confirmar explícitamente una combinación válida de empresa y sucursal, incluso cuando solo disponga de una.
7. El contexto activo está disponible de forma tipada para los módulos consumidores e incluye usuario, empresa, sucursal, permisos y sesión.
8. Cambiar de contexto invalida cualquier estado de interfaz que pueda mezclar operaciones entre empresas o sucursales.
9. Cerrar sesión, inactivar un usuario o revocar sus sesiones impide reutilizar la cookie anterior.
10. Los cambios de usuario, estado y asignaciones quedan asociados a actor, fecha UTC y resultado mediante eventos destinados a `audit-operations`.
11. El endpoint de login limita intentos abusivos y se recupera según la política aprobada sin intervención sobre la base.
12. Todas las rutas protegidas devuelven `401` sin sesión y `403` cuando existe sesión pero falta permiso.
13. La solución puede ejecutarse con configuración externa sin depender de una ruta, host o secreto fijo de la instalación local.
14. Las pruebas de integración demuestran que dos empresas de una misma instalación no pueden leer ni modificar datos entre sí.
15. El administrador inicial puede crearse sin colocar su contraseña en argumentos, archivos versionados o logs.
16. Un Administrador puede crear, consultar, modificar, activar o desactivar usuarios y gestionar roles y asignaciones desde la interfaz web.
17. Un usuario sin permiso administrativo no puede acceder ni invocar las operaciones de administración, aunque conozca las rutas o manipule la interfaz.
18. El sistema impide dejar la instalación sin al menos un acceso administrativo operativo o proporciona el procedimiento de recuperación aprobado.
19. Un usuario puede solicitar por correo un enlace de recuperación que vence, se utiliza una sola vez y no permite enumerar cuentas.
20. Una recuperación exitosa invalida tokens pendientes y sesiones anteriores y queda auditada sin exponer secretos.
21. Un Administrador puede parametrizar por rol o perfil operativo el vencimiento por inactividad y la duración absoluta, incluyendo desactivar cada límite cuando esté permitido.
22. Un Cajero/Carnicero puede operar sin vencimiento por inactividad cuando así esté configurado, sin perder la capacidad de revocación por cierre de turno, cierre de caja, inactivación, cambio de contraseña o acción administrativa.
23. Los cambios de parámetros se validan, aplican de forma determinista y conservan trazabilidad del valor anterior y nuevo.
24. Un mismo usuario puede mantener sesiones simultáneas hasta el máximo configurado para su rol o perfil operativo.
25. Al alcanzar el máximo, el sistema aplica atómicamente la acción configurada (`REJECT_NEW` o `REVOKE_OLDEST`) y registra el resultado.
26. Varios errores normales de contraseña no bloquean la cuenta ni agregan espera progresiva; un acceso posterior con credenciales correctas puede completarse inmediatamente.
27. Un volumen automatizado extremo queda contenido por un límite técnico configurable sin modificar ni bloquear la identidad atacada.
28. El sistema impide cambiar de empresa o sucursal mientras exista una venta en borrador, una caja abierta u otra operación pendiente bloqueante.
29. Ante un cambio bloqueado, la interfaz identifica el tipo de pendiente y permite navegar hacia su resolución; una vez resuelto, el cambio puede completarse sin cerrar la sesión.

## Preguntas abiertas para aprobación

No quedan preguntas abiertas para aprobar esta versión.

### Decisiones resueltas

- Una instalación podrá contener varias empresas legales del mismo cliente y múltiples sucursales.
- El MVP incluirá una interfaz completa de administración de usuarios, roles y asignaciones, accesible por el rol Administrador. La herramienta de implantación se reservará para crear el primer administrador.
- El MVP incluirá recuperación de contraseña por correo electrónico mediante token temporal de un solo uso.
- La política de sesión será parametrizable por rol o perfil operativo. Para Cajero/Carnicero podrá desactivarse el vencimiento por inactividad; la revocación por eventos operativos y administrativos permanecerá disponible.
- Las sesiones simultáneas estarán permitidas hasta un máximo parametrizable por rol o perfil operativo. La acción al exceder el máximo también será parametrizable.
- Los intentos fallidos no bloquearán la cuenta ni impondrán espera progresiva. Se conservará únicamente una protección técnica configurable contra tráfico automatizado extremo, sin impacto esperado sobre la operación normal.
- La selección de empresa y sucursal será siempre visible y requerirá confirmación explícita, aunque el usuario solo tenga una combinación disponible.
- No se permitirá cambiar de empresa o sucursal mientras exista una venta en borrador, caja abierta u otra operación pendiente bloqueante.

## Aprobación

La especificación fue aprobada y puede pasar a planificación. La aprobación no autoriza todavía implementación; el plan y la lista de tareas requieren su propia revisión.
