# Acceso independiente a la administración central (`central-access`)

**Estado:** aprobada por el usuario el 14/10/2026 y ampliada el 15/10/2026. El SuperAdmin **propio del portal central** administra empresas y cuentas; los administradores centrales de cada empresa gestionan sus conexiones según [SPEC-central-branch-connections.md](SPEC-central-branch-connections.md) y [ADR 0007](documentacion/decisiones/0007-conexiones-de-sucursal-gestionadas-por-admin-central.md). El CLI protegido se conserva para registros heredados y recuperación. **Módulo:** `central-access` del [mapa aprobado](CAPABILITY-MAP-sucursal-central.md). Depende del [contrato de lectura de sucursal](SPEC-branch-service-contract.md). El dashboard se describe por separado en `central-business-view`.

## Objetivo

Permitir que el administrador de una empresa entre al portal central con una cuenta **propia de la central**, vea únicamente las sucursales registradas de su empresa y quede autenticado para las futuras consultas de negocio. La central tendrá API y sitio web separados del POS y de la administración local. Ninguna operación de venta, caja, stock o facturación dependerá de este login.

La cuenta central no comparte identidad, hash ni sesiones con la cuenta local: un `admin` local, un `superadmin` de una instalación, un cajero, una caja Electron y una credencial de servicio de sucursal no adquieren acceso central por existir. En esta base ficticia, el usuario eligió posteriormente la misma clave textual corta para facilitar las pruebas; se restableció **solo** la cuenta central y no hay sincronización de claves entre sistemas. Esa excepción de longitud no aplica a instalaciones futuras.

## Alcance de datos y despliegue

- En desarrollo se usa **solo** `carnicerias_test_visual` de PostgreSQL Docker (`127.0.0.1:55433`). Las identidades, sesiones y el registro central se guardarán en tablas de un esquema `central_access` y un contexto de persistencia propios, sin crear ni clonar otra base. Las migraciones de este esquema se conservarán en Git y se aplicarán únicamente a esa misma base con preflight y respaldo. Una base exclusiva para la central en un despliegue futuro requiere una decisión y autorización separadas.
- `centralCompanyId` identifica a la empresa en la central. Cada registro de instalación relaciona ese ID con `installationId`, `companyId` y `branchId` declarados por la sucursal. **No se supone que los UUID locales de empresa coincidan** entre sucursales físicas. El administrador no envía IDs remotos para elegir el alcance de una consulta.
- La central conserva identidad de administradores, sesiones y metadatos mínimos de conexión. No copia usuarios locales, clientes, tickets, stock, pagos ni agregados comerciales. La sucursal sigue siendo la fuente de verdad.
- El CLI interactivo crea **solo la primera identidad SuperAdmin central** y conserva la recuperación operativa y el registro heredado de instalaciones. Ese SuperAdmin pertenece a Recursos Informáticos: crea empresas con su primer administrador y agrega administradores adicionales desde `/superadmin`. El administrador de cada empresa gestiona **solo sus propias** conexiones en la sección web separada; la lista de destinos aprobados permanece fuera del navegador y a cargo de implementación.
- El nombre de sucursal guardado en el registro es una etiqueta configurada; **no prueba** que la sucursal esté en línea. El estado vivo y los resúmenes pertenecen a `central-business-view`.

## Identidad y modelo propuesto

| Entidad propia de la central | Campos y restricciones principales |
|---|---|
| Empresa central | `id`, código único normalizado e inmutable, nombre, estado activo. |
| Administrador central | `id`, `centralCompanyId` inmutable, usuario normalizado único dentro de la empresa, hash Argon2id, estado activo. Sin contraseña en texto ni reutilización de `platform_access.users`. |
| Sesión central | `id`, administrador, empresa fijada al login, hash de token aleatorio de 256 bits, creación, expiración absoluta y revocación. Nunca reutiliza `platform_access.sessions`. |
| SuperAdmin central | Identidad y sesión propias de `central_access`, sin empresa local ni rol POS. Una cookie y endpoints distintos de los de administradores de empresa central. |
| Instalación registrada | `installationId` globalmente único, `centralCompanyId`, `remoteCompanyId`, `remoteBranchId`, etiqueta, destino HTTPS previamente aprobado, referencia a secreto y estado activo. No contiene el token en claro. |

El procedimiento de alta de **cuentas** recibe contraseña de manera interactiva, sin argumentos de comando ni archivos del repositorio. Las conexiones CLI heredadas siguen usando una referencia a variable de entorno; las nuevas conexiones web guardan el token **cifrado** en `central_access.branch_credentials`, con clave maestra versionada y protegida fuera de la base y de Git. Antes de activar una instalación, la central consulta `GET /api/central/v1/installation` con su credencial por VPN/HTTPS y exige coincidencia exacta de los tres IDs remotos. Si falta el secreto, falla TLS, hay redirección o la identidad difiere, no la registra como activa. HTTP literal de `127.0.0.1` se habilita explícitamente solo en laboratorio.

## API central v1

La API central vive en **otro proceso y origen** que la API de sucursal. Sus respuestas de sesión llevan `Cache-Control: no-store`; errores siguen `{ "error": { "code", "message", "details" } }` sin distinguir si existe una empresa o un usuario. La cookie `carnicerias.central.session` será `HttpOnly`, `Secure`, `SameSite=Strict`, con expiración absoluta máxima de ocho horas. Toda solicitud mutadora desde el sitio web exige origen coincidente y protección CSRF; los endpoints comprueban el usuario y su empresa en el servidor en cada acceso.

| Método y ruta central | Entrada | Respuesta y autorización |
|---|---|---|
| `POST /api/v1/sessions` | `{ "companyCode", "credential", "password" }`, con longitud y formato limitados. | `200` con `{ userId, centralCompanyId, username, expiresAtUtc }` y cookie propia; `400` por forma inválida, `401` genérico por credenciales/empresa inactiva, `429` por exceso. |
| `GET /api/v1/sessions/current` | Cookie central. | `200` con identidad y empresa fijada; `401` si no es válida. |
| `DELETE /api/v1/sessions/current` | Cookie central y origen válido. | `204`, revoca servidor y borra cookie; `401` sin sesión, `403` por origen rechazado. |
| `GET /api/v1/installations?page=1&pageSize=20` | Cookie central; `page >= 1`, `1 <= pageSize <= 100`. | `200` con `data` y `pagination`, filtrados por `centralCompanyId` de la sesión. Devuelve IDs y etiqueta, no destino, referencia ni token. `400` por parámetros inválidos, `401` sin sesión. |

La ampliación agrega, en el **mismo origen central**, rutas `/api/v1/superadmin/*` autenticadas exclusivamente por `carnicerias.central.superadmin` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/v1/superadmin`). `POST /sessions`, `GET/DELETE /sessions/current` inician, consultan y revocan esa sesión; `GET/POST /companies` listan o crean empresa con su primer administrador en una sola operación; `GET/POST /companies/{companyId}/administrators` listan o agregan administradores de la empresa seleccionada. Los listados son paginados. Los cambios requieren origen coincidente y encabezado CSRF. Un administrador de empresa central no puede usar esas rutas con su cookie habitual; un SuperAdmin central no se convierte en usuario de una empresa ni del POS. Las rutas `/api/v1/branch-connections` usan exclusivamente la **cookie del administrador de empresa**, nunca la del SuperAdmin.

No se expone `companyId`, `branchId`, URL o token como selector de alcance en esos endpoints. Un administrador central de A no puede listar instalaciones de B aunque conozca sus UUID. Las respuestas no contienen hashes, secretos ni datos personales de sucursales. Las rutas de sucursal `/api/central/v1/*` continúan autorizadas **solo** por su credencial de servicio, no por esta cookie.

## Límites de confianza y abuso

- El navegador puede proponer una URL para registrar o editar **su propia** conexión, pero la central no la consulta hasta encontrar URL exacta, empresa e IP/DNS en la lista protegida de implementación. Esquema y certificado se validan, no se siguen redirecciones y hay plazo máximo. La lista aprobada no se modifica desde el portal; así se evita un proxy SSRF hacia equipos internos.
- Los tokens de sucursal son independientes por instalación y rotables mediante su fuente protegida. Para nuevas conexiones se envían una vez y se cifran; no se devuelven al navegador, registran en logs ni incluyen en errores. La central no comparte su cookie con la API local.
- Login con verificación Argon2id también para usuario inexistente, respuesta genérica y limitación de intentos por origen y cuenta. Para publicar el portal fuera del laboratorio se exige HTTPS y un limitador compartido o en el borde si hay más de un proceso. No se adoptan contraseñas de prueba comunes como credenciales permanentes.
- Al desactivar una empresa o un administrador central, todas sus sesiones dejan de autorizar solicitudes. Los eventos de alta, rechazo de registro, login y revocación se auditan con IDs y resultado, nunca con contraseña, token o cadena de conexión.
- Un registro cuya sucursal deja de responder puede seguir apareciendo como *registrado*; no se informa *conectado* ni un total comercial de cero. La degradación y los totales parciales se resolverán en `central-business-view`.

## Tecnología, estructura y estilo

- API y persistencia: .NET 10, EF Core/Npgsql y PostgreSQL ya usados por el proyecto, en proyectos separados `src/Carnicerias.Central.Api/` y `src/Carnicerias.Central.Infrastructure/`. El contexto central migra solo `central_access`; no registra endpoints POS ni accede a las tablas operativas locales. El procedimiento protegido reside en `tools/Carnicerias.Central.Bootstrap/`.
- Sitio: Angular, en `src/Carnicerias.Central.Web/`, con ingreso y listado inicial. Mantendrá las convenciones visuales y accesibles de la administración existente, pero será un artefacto desplegable independiente. La regla de revisar el **POS** visualmente solo en Electron sigue vigente.
- Pruebas puras y HTTP sin aprovisionamiento de base en `tests/Carnicerias.Central.Tests/`. Tipos explícitos y filtros de empresa en la consulta, por ejemplo `registry.Where(item => item.CentralCompanyId == session.CentralCompanyId)`; nunca filtrar con un ID de empresa recibido del navegador. Los contratos JSON usarán `camelCase` y errores estructurados.

## Comandos previstos de verificación

Después de crear los proyectos, los comandos serán:

```powershell
dotnet build src/Carnicerias.Central.Api/Carnicerias.Central.Api.csproj --configuration Release --no-restore
dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --no-restore
npm --prefix src/Carnicerias.Central.Web run build
npm --prefix src/Carnicerias.Central.Web test -- --watch=false
git diff --check
```

Las pruebas de contrato, autenticación, aislamiento y registro usarán dobles de persistencia/HTTP sin crear otra base. El smoke real de login y lectura se hará sobre la **misma** base visual, con datos ficticios identificados y sin reiniciarla. No se ejecutará la suite de integración existente que pueda crear o reiniciar bases. El acceso multiservidor real por VPN/HTTPS quedará pendiente del workshop.

## Criterios de aceptación

1. Una cuenta central independiente de Empresa A inicia y cierra sesión sin poder usar su cookie en la administración local; una cuenta local o un token de sucursal no entra a la central.
2. La central lista únicamente instalaciones registradas y activas de la empresa fijada en la sesión, con paginación; UUIDs o parámetros manipulados no revelan B.
3. El alta protegida rechaza destino no aprobado, redirección, TLS inválido, secreto ausente o identidad remota discrepante, sin guardar token **en claro** en DB ni Git.
4. La central no guarda datos comerciales y una sucursal desconectada no bloquea login ni listado; el POS sigue confirmando ventas con la central apagada.
5. Migración, configuración y pruebas conservan la única base de desarrollo y no alteran cuentas, sesiones ni permisos locales existentes.

## Límites para la implementación

- **Siempre:** validar entrada en el borde, autorizar por empresa en servidor, revisar el diff por secretos, probar rechazo cruzado y registrar migraciones/código en Git.
- **Requiere aprobación del contrato:** añadir el esquema central, nuevos proyectos, login y el procedimiento de alta. Un almacén central independiente, publicación pública o integración de SSO son decisiones posteriores.
- **Nunca:** crear otra base de Carnicerías en desarrollo, copiar hashes/contraseñas locales, consultar destinos **no aprobados por empresa** aunque vengan del navegador, usar la credencial Electron como token central o comprometer ventas locales por fallos centrales.

## Decisiones confirmadas para el plan

1. Decisión original (14/10): el equipo de implementación hacía altas iniciales por CLI; **sustituida el 15/10 para empresas y administradores**. El primer SuperAdmin sigue naciendo por procedimiento interactivo, sin contraseña predeterminada.
2. El SuperAdmin del **portal central**, de Recursos Informáticos y no de una instalación POS, da de alta empresas y administradores centrales. El registro de sucursales pasó al administrador **de cada empresa central** en la ampliación del 15/10; el CLI sigue para legado/recuperación. El reseteo de contraseñas de administradores de empresa sigue siendo una operación de implementación por CLI. No se comparte identidad ni sesión con `platform_access`.
