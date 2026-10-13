# Cambio propuesto: identidad por empresa y superadministración del fabricante

**Módulo:** `platform-access` (complementa y reemplaza las reglas incompatibles de `SPEC-platform-access.md` y `SPEC-admin.md`).
**Estado:** reglas aprobadas; código y migración preparados, activación pendiente de resolver la asignación multicompañía de `visual-admin` y verificar en la única base.
**Fecha:** 13 de octubre de 2026.

## Objetivo y motivo

Permitir que empresas distintas usen el mismo nombre de usuario, por ejemplo `cajero1`, sin compartir cuenta, sesión, catálogo ni facultades administrativas. La empresa la crea un **superadmin del fabricante del POS**. El administrador de una empresa trabaja solo dentro de esa empresa y nunca crea ni administra otra.

Hoy `platform_access.users` tiene índices únicos globales sobre `UsernameNormalized` y `EmailNormalized`, y `POST /api/sessions` busca la cuenta sin empresa. Además, `POST /api/admin/companies` permite a un administrador común crear empresas y se asigna automáticamente a la nueva. Esos comportamientos contradicen esta propuesta. El mapa modular aprobado ya ubica identidad y empresas en `platform-access`; no se crea otro módulo.

## Reglas propuestas

1. Cada usuario operativo o administrador de cliente pertenece a **una empresa**. `username` y `email` normalizados son únicos **dentro de esa empresa**, no en toda la instalación. Dos `cajero1` de empresas distintas son cuentas diferentes, con contraseñas, sesiones, turnos y permisos independientes. No se fusionan cuentas por coincidencia de nombre o correo.
2. El fabricante tiene cuentas `superadmin` en un ámbito separado, sin asignación a una empresa cliente. El superadmin crea, modifica y desactiva empresas; crea su primera sucursal y el primer administrador de esa empresa. No obtiene automáticamente acceso al POS, caja, ventas ni datos comerciales de cada cliente.
3. El administrador de empresa puede gestionar **todo lo que está debajo de su empresa**: sucursales, cajas, usuarios, catálogo, stock, configuración, ventas y consultas según las funciones disponibles. Puede crear **otros administradores de su misma empresa**, además de cajeros, y administrar sus accesos. No puede crear otra empresa, cambiar el titular de la propia, listar empresas ajenas ni otorgar el rol `superadmin`. Las acciones de alta/estado de empresas quedan en una interfaz y endpoints exclusivos del fabricante, con autorización comprobada en servidor.
4. Cada empresa tiene un **código de acceso único e inmutable** asignado por el fabricante. En el navegador, el ingreso del cliente pide código de empresa, usuario o correo y contraseña. En Electron, la credencial de terminal ya determina la empresa; el servidor busca el usuario solo dentro de ella y rechaza cualquier empresa explícita que no coincida. El superadmin usa un ingreso separado de fabricante, sin fingir ser administrador de una empresa.
5. Una sesión de cliente queda vinculada a su empresa desde el login. Luego se selecciona la sucursal autorizada. Un cliente no puede cambiar a otra empresa mediante el selector de contexto, una URL, un ID enviado en una solicitud o una credencial de terminal de otra empresa. Para trabajar legítimamente en dos empresas se usan cuentas separadas.
6. Un nombre de empresa o sucursal puede cambiar sin cambiar el código de acceso. Los fallos de login no revelan si existen la empresa, el usuario o el correo. Un administrador de cliente que intente consultar o modificar usuarios/recursos ajenos recibe una respuesta no enumerable; los permisos no dependen de ocultar botones.

## Contratos a cambiar

- `POST /api/sessions` (cliente web): recibe `companyCode`, `credential`, `password`. El servidor resuelve empresa activa antes de buscar la cuenta. Con terminal POS válida, `companyCode` puede omitirse y la empresa procede de esa terminal. Los errores de credenciales o ámbito siguen siendo genéricos.
- Ingreso del fabricante: flujo de sesión separado y exclusivo de `superadmin`, sin contexto de sucursal ni permisos operativos. El detalle de ruta y cookie se fijará en el plan técnico para impedir que una sesión de fabricante sea aceptada accidentalmente en endpoints de negocio.
- Alta/edición de usuario cliente: el `CompanyId` sale del contexto autorizado, nunca del formulario. Un duplicado en la **misma** empresa devuelve `409 USER_ALREADY_EXISTS`; el mismo nombre en otra empresa es válido.
- Un administrador de empresa puede crear usuarios con rol `cashier` o `administrator` **dentro de su empresa**. El servidor no acepta `companyId` libre ni un rol fuera de esa lista; la asignación de administrador abarca su empresa y no se extiende a otras. Cambios de rol o inactivación revocan las sesiones afectadas. No se permite dejar la empresa sin al menos un administrador activo y operativo.
- Alta/edición/desactivación de empresa: solo ámbito fabricante. La administración del cliente deja de mostrar la lista global y la acción **Nueva empresa**; muestra únicamente datos de su empresa y las operaciones de sucursal/caja que le correspondan.

## Migración y compatibilidad

- Migrar **en la misma** `carnicerias_test_visual`; no crear, clonar ni reiniciar otra base de Carnicerías. Añadir empresa a las cuentas cliente existentes a partir de sus asignaciones, revisar cualquier usuario que hoy tenga asignaciones a más de una empresa y no dividirlo automáticamente si se detecta ese caso.
- Conservar IDs, hashes, ventas, turnos, auditoría y referencias históricas. `visual-admin` continúa como administrador de Empresa Visual; crear el primer superadmin del fabricante con un procedimiento privilegiado y contraseña interactiva fuera de Git.
- Reemplazar los índices globales por unicidad compuesta por empresa después de validar datos existentes. Las sesiones antiguas se revocan al cambiar el contrato de identidad; no se interpretan silenciosamente con otro alcance.
- El despliegue actual sigue siendo autónomo por sucursal. El superadmin aquí descrito vive en **cada instalación local**; un portal central del fabricante y la administración remota de instalaciones quedan fuera de este cambio.

## Seguridad y casos de abuso

- No basta con filtrar la lista de usuarios: cada endpoint debe comprobar el ámbito del actor y del recurso, incluida creación de otros administradores, edición, cambio de rol, reseteo de contraseña, revocación de sesiones, sucursales y cajas.
- Cambiar `companyCode` en el cuerpo de login de una caja no puede reemplazar la empresa fijada por su credencial de terminal.
- Una cuenta `admin` de Empresa A puede crear otro `admin` de A, pero no puede crear Empresa B, administrar su `cajero1` ni asignar un rol de fabricante, aunque conozca los UUID. Un `superadmin` no puede cobrar una venta solo por administrar el alta de la empresa.
- Auditar creación/desactivación de empresas, alta del administrador inicial y cambios de ámbito sin registrar contraseñas ni credenciales.

## Estructura, estilo y verificación

- Dominio y persistencia: `src/Carnicerias.PlatformAccess` y `src/Carnicerias.Infrastructure`, con migración EF aditiva y controlada.
- API y permisos: `src/Carnicerias.Api/Sessions`, `Users`, `Organization` y `Security`; validación en el límite HTTP y alcance resuelto del servidor.
- UI: ingreso Angular y administración; el POS se revisa visualmente **solo en Electron**.
- Mantener tipos explícitos y errores estructurados. Ejemplo de regla de consulta: `user.CompanyId == authenticatedCompanyId && user.UsernameNormalized == normalizedUsername`; nunca buscar `UsernameNormalized` globalmente para login cliente.
- Pruebas unitarias, de arquitectura y frontend: dos `cajero1` en empresas distintas; rechazo de duplicado dentro de una empresa; login POS con terminal de empresa incorrecta; administrador de A crea otro administrador de A pero no de B ni un superadmin; último administrador activo protegido; superadmin sin permisos de venta; preservación de usuarios existentes en la migración. Ejecutar `dotnet build Carnicerias.sln --configuration Release`, `dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj`, `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web` y `npm run electron:test --prefix src/Carnicerias.Pos`.
- No ejecutar pruebas de integración que creen o reinicien otra base. La verificación de migración e integración en PostgreSQL queda pendiente de un procedimiento controlado sobre la **misma** base autorizada y de resguardar sus datos ficticios.

## Límites de autorización para implementar

- Hacer: preservar el único PostgreSQL de desarrollo, mantener claves fuera de Git, migrar sin perder IDs ni historial y comprobar el aislamiento en servidor.
- El cambio de esquema/autenticación fue aprobado el 13/10/2026. Pedir una regla específica si la auditoría encuentra identidades con asignaciones en varias empresas; no dar acceso operativo implícito al fabricante.
- No hacer: crear otra base, dar permisos globales al administrador de una empresa o usar el nombre visible de empresa como identificador de seguridad.

## Decisiones y supuestos visibles

1. **Confirmado el 13/10/2026:** el navegador pide **código de empresa + usuario/contraseña**; Electron obtiene la empresa de la credencial de la caja.
2. **Supuesto basado en el despliegue autónomo aprobado:** el superadmin del fabricante es local a cada instalación, sin portal central ni acceso a ventas/caja por defecto. Si se requiere gestión remota de varias instalaciones, se diseñará aparte.
3. **Supuesto de identidad:** el correo, igual que el nombre de usuario, puede repetirse entre empresas; dentro de cada empresa ambos son únicos. La futura recuperación por correo también deberá recibir código de empresa para resolver la cuenta sin ambigüedad.
