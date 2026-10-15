# Configuración de conexiones a sucursales desde la central (`central-access`)

**Estado:** aprobada por el usuario el 15/10/2026. Amplía el módulo `central-access` del [mapa de capacidades](CAPABILITY-MAP-sucursal-central.md). Hasta implementarla, sigue vigente el alta protegida por CLI de [SPEC-central-access.md](SPEC-central-access.md) y [ADR 0006](documentacion/decisiones/0006-superadmin-propio-del-portal-central.md). Tras implementarla, reconciliar esos documentos y registrar una nueva decisión; no borrar el historial de la decisión anterior.

## Objetivo y autoridad

El administrador **central de cada empresa** configura desde su portal las conexiones a las instalaciones de **sus propias sucursales**. Puede verlas, registrar una, corregir etiqueta/destino/credencial, comprobarla, desactivarla y reactivarla. La sesión central fija la empresa: el formulario no acepta un `centralCompanyId` ni permite elegir otra empresa.

El SuperAdmin del portal central **pertenece siempre a Recursos Informáticos, desarrollador del POS**. Sigue creando empresas centrales y sus administradores, pero no se convierte en administrador de una empresa ni es la vía cotidiana para configurar sus sucursales. Los administradores y SuperAdmin locales del POS tampoco adquieren este permiso. Las ventas y cajas locales no dependen de que la central esté disponible.

Esta función registra la conexión **en la central**; no instala la API de la sucursal ni crea su identidad o credencial allí. Esos datos deben existir primero en la instalación local. La fuente de verdad de ventas, stock y caja sigue en la sucursal; no se replica negocio en la central.

## Flujo y criterios funcionales

1. En el portal central autenticado hay una sección **Conexiones a sucursales**, separada del dashboard. Muestra etiqueta, identidad de instalación/sucursal, destino, estado administrativo (activa/inactiva) y resultado/fecha de la última comprobación. *Registrada* no equivale a *conectada*; una sucursal fuera de línea no aparece como venta cero.
2. **Agregar conexión** pide etiqueta, URL raíz, `installationId`, `remoteCompanyId`, `remoteBranchId` y token de servicio de la sucursal (32 bytes hexadecimales). El admin obtiene los IDs y la credencial del procedimiento protegido de esa sucursal. El navegador no almacena ni vuelve a mostrar el token. La central verifica URL permitida para **esa empresa**, token e identidad remota exacta mediante `GET /api/central/v1/installation` antes de activar el registro. Si algo falla, no crea una conexión activa ni persiste la credencial; muestra un error útil sin revelar el secreto.
3. **Editar** puede cambiar etiqueta sin consultar la sucursal; cambiar destino o credencial requiere nueva comprobación. Si esa comprobación falla, se conserva intacta la configuración activa anterior. Se rechazan IDs duplicados globalmente y una segunda conexión activa a la misma pareja de empresa/sucursal remota para evitar doble conteo.
4. **Comprobar** prueba una conexión existente bajo el mismo límite de tiempo y controles de red. Distingue *disponible*, *no disponible* y *no comprobada*; no convierte una comprobación puntual en garantía de disponibilidad continua.
5. **Desactivar** excluye esa conexión del dashboard sin eliminar el registro, auditoría ni configuración. **Reactivar** exige verificar de nuevo destino, credencial e identidad. La rotación del token usa la ventana de dos verificadores de la API de sucursal (`CredentialSha256`/`NextCredentialSha256`): primero se prepara la sucursal, después se cambia el token central, y finalmente se retira el anterior. No se automatiza una escritura de configuración remota.
6. Los registros existentes creados por CLI siguen funcionando. Se podrán administrar desde la nueva pantalla sin exponer su referencia de secreto; para reemplazar su credencial, el admin deberá introducir una nueva. No se pretende recuperar por navegador los secretos ya almacenados en variables del servidor.

## Contrato API propuesto

Se conserva `GET /api/v1/installations` como listado de lectura ya publicado. La administración usa rutas nuevas bajo la **sesión de administrador de empresa central**, no bajo `/api/v1/superadmin`:

| Ruta | Entrada y efecto | Respuesta |
|---|---|---|
| `GET /api/v1/branch-connections?page=1&pageSize=20` | Lista paginada de la empresa de la sesión, incluidas inactivas. | `data` con IDs, etiqueta, URL, `isActive`, `hasCredential`, último resultado/fecha; `pagination`. Nunca token, hash, valor cifrado ni referencia de secreto. |
| `POST /api/v1/branch-connections` | IDs, etiqueta, URL y token; verifica antes de guardar conexión, secreto cifrado y auditoría de forma atómica. | `201` con metadatos sin secreto; `400` forma inválida, `403` origen/CSRF, `409` duplicado, `422` destino/identidad/credencial no verificables. |
| `PATCH /api/v1/branch-connections/{installationId}` | Solo campos permitidos: etiqueta y, opcionalmente, nueva URL/token. Verifica antes de reemplazar datos de conexión. No permite cambiar IDs ni empresa propietaria. | `200` con metadatos; `404` para ID inexistente o de otra empresa; `409` conflicto; `422` comprobación fallida. |
| `POST /api/v1/branch-connections/{installationId}/checks` | Comprobación puntual, acotada y auditada. No cambia URL, token ni estado administrativo. | `200` con resultado y hora, sin secreto; `404` fuera de la empresa. |
| `PATCH /api/v1/branch-connections/{installationId}/state` | `{ "isActive": false }` desactiva; `{ "isActive": true }` verifica antes de reactivar. | `200` con estado; `404` fuera de la empresa; `422` si no se puede reactivar. |

Todas las escrituras exigen cookie de empresa central, origen coincidente y `X-Carnicerias-CSRF`; se limita tamaño de cuerpo y frecuencia de comprobaciones, y se valida cada campo en el borde. Los errores siguen `{ "error": { "code", "message", "details" } }` sin datos internos. Las mutaciones no se reintentan automáticamente tras un timeout: el cliente relee el registro para resolver el resultado desconocido. Se mantiene la restricción única de `installationId` en la base y se prueba la carrera de altas. El contrato de lectura de sucursal no cambia.

## Límites de confianza y protección del secreto

- El destino escrito por un admin **no es autorización para que el servidor lo consulte**. Recursos Informáticos/implementación mantiene fuera de Git una lista de URLs raíz exactas, IP admitidas y **empresa central autorizada** para cada destino. Solo una coincidencia de empresa, URL y todas las respuestas DNS permite la consulta; el cliente HTTP fija la IP validada, no sigue redirecciones, conserva TLS/nombre válido, limita tiempo y tamaño de respuesta. HTTP literal `127.0.0.1` solo se habilita expresamente en laboratorio. Así la pantalla no se convierte en un proxy hacia una intranet arbitraria. Este control se basa en la [guía SSRF de OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html).
- Los nuevos tokens se cifran con autenticación antes de guardarse en una **tabla nueva del mismo esquema `central_access` y la misma base `carnicerias_test_visual`**. Se propone AES-GCM con nonce nuevo por escritura y datos asociados `centralCompanyId` + `installationId`; la clave maestra versionada vive en configuración protegida del servidor, separada de la base, respaldada por el equipo de implementación y nunca en Git, argumentos de comando ni respuestas HTTP. La API falla de modo cerrado si no puede descifrar. El token no aparece en logs, auditoría, errores, HTML ni almacenamiento del navegador. La [API criptográfica de .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.decrypt?view=net-10.0) admite datos asociados y verifica la etiqueta al descifrar.
- Las conexiones existentes con referencia a variable de entorno conservan esa fuente mientras no se roten. La lectura de credenciales distingue explícitamente fuente heredada y fuente cifrada; nunca usa una fuente distinta como fallback silencioso tras fallo de descifrado.
- La central autentica/autoriza **cada solicitud** con la empresa de la cookie. Una conexión de otra empresa responde como inexistente; conocer su UUID o URL no habilita lectura, modificación ni prueba. Los eventos de alta, cambio, comprobación, desactivación y rechazo auditan actor, empresa, instalación, fecha y resultado, jamás el token.
- Se requiere HTTPS para el portal y para conexiones remotas reales. El laboratorio local puede usar HTTP de loopback explícito. La pantalla debe limitar intentos para que un admin comprometido no genere sondeos de red ilimitados.

## Tecnología, estructura y estilo

- `src/Carnicerias.Central.Api/`: endpoints y contratos HTTP de administración, con autorización y CSRF existentes.
- `src/Carnicerias.Central.Infrastructure/`: EF Core/Npgsql, migración **solo** de `central_access`, políticas de destino, comprobación y almacenamiento cifrado; conservar compatibilidad con conexiones CLI existentes.
- `src/Carnicerias.Central.Web/src/app/`: sección Angular accesible, con formulario/modal, confirmación de desactivación, estados claros y errores al lado de la acción; no colocar el secreto en `localStorage` ni mostrarlo después de enviarlo.
- `tests/Carnicerias.Central.Tests/` y especificaciones `*.spec.ts` del sitio: pruebas puras, HTTP con dobles y UI. `documentacion/despliegue/`, `documentacion/manual-usuarios/` y un ADR nuevo explicarán preparación de sucursal, lista permitida, clave de cifrado, respaldo/rotación y operación.

Convenciones: DTO JSON en `camelCase`, nombres C# explícitos, componentes Angular siguiendo el estilo existente y errores estructurados. Ejemplo de alcance: `var companyId = session.Company.Id;` y `query.Where(connection => connection.CentralCompanyId == companyId)`; **nunca** un `companyId` recibido del formulario.

## Comandos y verificación

```powershell
dotnet build src/Carnicerias.Central.Api/Carnicerias.Central.Api.csproj --configuration Release --no-restore
dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --no-restore
npm --prefix src/Carnicerias.Central.Web run build
npm --prefix src/Carnicerias.Central.Web test -- --watch=false
git diff --check
```

Las pruebas cubren autorización cruzada A/B, intento SSRF (URL no aprobada, DNS distinto, redirección), secreto/identidad incorrectos, token ausente, fallo de cifrado, duplicados/carrera, CSRF, desactivar/reactivar, edición fallida que conserva la conexión previa y que ninguna respuesta o log incluye secretos. La UI se recorre en navegador real con estados disponible/no disponible; la regla de revisar **el POS solo dentro de Electron** no cambia. No se ejecutan pruebas que creen/reinicien otra base. Antes de aplicar una migración real a la única base de desarrollo se hará el preflight y respaldo descrito en `documentacion/despliegue/administracion-central.md`.

## Límites de implementación

- **Siempre:** aislar por empresa en servidor, validar entrada, probar abuso, conservar migraciones/scripts, revisar diff por secretos y documentar operación/recuperación.
- **Aprobar con esta especificación:** nueva tabla de credenciales cifradas en la **misma** base, nueva facultad del administrador central de empresa y política de destinos por empresa. La clave maestra se entregará al servidor por configuración protegida; si se pierde, habrá que reprovisionar los tokens afectados.
- **Nunca:** crear otra base de Carnicerías, introducir un endpoint que consulte URLs libres, usar la cookie central como credencial de sucursal, revelar tokens, delegar esta gestión al SuperAdmin de Recursos Informáticos o al SuperAdmin local del POS, ni romper la operación de venta cuando la central falle.

## Aceptación

1. El admin central A registra y prueba su sucursal aprobada desde la web; su dashboard la consulta sin usar el CLI de registro. B no puede verla, modificarla ni probarla por UUID, y el SuperAdmin central no usa la sesión de A.
2. Tras reiniciar la API central, la conexión nueva sigue operativa con la misma base y la clave protegida; no hay token en Git, DB en claro, respuesta, log ni navegador persistente.
3. Editar URL/token con comprobación fallida conserva la conexión anterior. Desactivar la excluye del agregado sin borrar el historial; reactivar exige comprobarla.
4. Una URL no aprobada para A, una IP/DNS fuera de la lista, redirección, TLS inválido, token erróneo o identidad remota distinta no quedan activos ni permiten sondear otro destino.
5. Las instalaciones heredadas continúan funcionando y el POS vende aunque la central esté apagada. La única base de desarrollo sigue siendo `carnicerias_test_visual`.

## Preguntas abiertas

Ninguna regla de negocio pendiente. El usuario aprobó el almacenamiento cifrado de credenciales en la misma base y que Recursos Informáticos conserve la lista de destinos permitidos por empresa.
