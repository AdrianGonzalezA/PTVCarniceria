# Especificación: administrador del POS

## Objetivo

Dar al administrador una interfaz web para mantener los maestros que alimentan al punto de venta, dentro de la misma instalación PostgreSQL. El administrador inicial es `visual-admin`, con el rol `administrator`; los cajeros existentes conservan su acceso al POS, pero no al administrador. La UI administrativa puede abrirse en Angular web y su revisión visual durante el desarrollo se hace en Electron.

**Separación aprobada:** el administrador tendrá pestañas principales **Negocio** y **Configuración**, sin otro login. Este documento conserva el contrato de los ABM y permisos actuales; `SPEC-reporting.md` define el dashboard, ventas y estados de cuenta. Existencias y Recepción de piezas pertenecen a Negocio; Lectura de etiquetas queda en Configuración. Los enlaces y APIs existentes permanecen operativos durante el cambio de navegación.

## Alcance y orden

El mapa aprobado en `CAPABILITY_MAP.md` sigue siendo el índice modular. La administración se entrega por capacidades verificables:

1. **Acceso y navegación:** entrada `/admin` desde la sesión existente, contexto de empresa/sucursal explícito, menú de secciones y autorización en servidor. No se crea otro flujo de autenticación ni otro rol administrativo.
2. **Catálogo:** ABM de categorías, productos y códigos alternativos; listas de precios, habilitación por sucursal, precio vigente e historial. El código principal de producto es inmutable. Peso y unidad de venta se definen en producto. La baja de maestros con referencias es lógica (inactivación), no borrado físico.
3. **Organización y operación:** empresas, sucursales, terminales y stock por sucursal. Las credenciales de terminal solo se revelan al crearlas o rotarlas; nunca se listan en claro. Un ajuste de stock registra movimiento y motivo.
4. **Accesos:** mantener usuarios y sus asignaciones dentro de la instalación; en esta etapa solo existe un rol administrador. La administración de roles/permisos y una política de altas de más administradores quedan fuera del primer corte.
5. **Consulta transaccional:** ventas, pagos, turnos y movimientos son históricos de solo lectura en el administrador. No hay ABM destructivo de ventas confirmadas ni de asientos de caja.

## Primera entrega vertical

El primer incremento publicable comprende navegación administrativa y ABM de categorías de la empresa activa:

- `GET /api/admin/categories?page=1&pageSize=20&search=` lista categorías activas e inactivas con conteo de productos, ordenadas por nombre y paginadas (máximo 100 por página).
- `POST /api/admin/categories` crea una categoría activa. El nombre se recorta, debe medir 1–120 caracteres y ser único por empresa.
- `PATCH /api/admin/categories/{id}` cambia nombre y/o estado. Un ID de otra empresa responde 404. Inactivar una categoría no borra productos ni ventas; sus productos dejan de aparecer en el POS por el filtro de categoría activa.
- Todos los endpoints exigen sesión, contexto operativo y permiso `catalog.manage`; las mutaciones validan origen. Errores usan el sobre `ErrorResponse` actual, con 400 por datos inválidos, 401/403 por acceso, 404 por recurso fuera de alcance y 409 por duplicado.
- La UI ofrece listado, búsqueda, alta, edición e inactivación/reactivación, con estados de carga, vacío, error y éxito. No usa datos simulados.

## Segundo incremento: artículos

- `GET /api/admin/products` lista artículos activos e inactivos de la empresa, con búsqueda, categoría y paginación. No depende de que exista precio vigente.
- `POST /api/admin/products` crea un artículo activo con código principal único, categoría activa, nombre, unidad de venta, modalidad (`weight` o `unit`) y costo positivo. Nace sin precio ni stock: no aparece en el POS hasta completar esos datos.
- `PATCH /api/admin/products/{id}` permite cambiar categoría, nombre, unidad, modalidad, costo y estado, pero nunca el código principal. Una baja es inactivación lógica.
- La unidad o modalidad no se modifica si el artículo ya tiene movimientos/existencias o renglones de ticket, para no reinterpretar cantidades históricas. La edición de nombre, categoría, costo y estado conserva los snapshots de tickets existentes.
- Los códigos alternativos se gestionan aparte y no pueden coincidir con códigos principales ni con otros alternativos de la empresa.
- Los códigos alternativos tienen estado activo/inactivo; inactivarlos no borra la fila ni libera el código para otro artículo. El POS ignora los inactivos al buscar. Las altas de artículos y códigos alternativos serializan la reserva por empresa para impedir colisiones entre ambas tablas.

## Tercer incremento: listas y precios

- Cada lista pertenece a una empresa y puede habilitarse en varias sucursales; una sucursal puede ofrecer varias listas. El cajero elige entre las listas activas asignadas a su sucursal.
- La baja de lista o asignación es lógica. Se rechaza mientras existan borradores de venta abiertos que dependan de ella, para no interrumpir una caja en uso.
- El precio de un artículo se define por lista, debe ser positivo y no inferior al costo vigente. Un cambio cierra el precio anterior y abre otro con fecha y usuario responsables en una transacción. Los tickets ya guardados mantienen sus importes snapshot.
- Si el costo de un artículo sube por encima de alguno de sus precios vigentes, la edición se rechaza hasta ajustar primero esos precios.

## Impuestos por artículo

- Configuración → Impuestos por artículo mantiene un catálogo por empresa de entradas `iva` u `otro`, con código/tasa inmutables e inactivación lógica. Sólo un administrador con `catalog.manage` puede crearlas o asignarlas; la API valida sesión, empresa y origen.
- El IVA de un artículo se elige entre entradas activas y conserva versiones de su regla gravada, exenta o no alcanzada. Los otros gravámenes pueden asignarse a uno, varios o todos los artículos existentes, activos e inactivos, con historial y conteo de cambios. Repetir el estado deseado no duplica versiones.
- Los gravámenes `otro` se muestran como «no calculados» y no afectan ventas ni ARCA. El 21 % de Empresa Visual es ficticio de homologación, no configuración fiscal general. Contrato, evidencia y pendientes: `SPEC-tax-catalog.md`.

## Organización y existencias

- Una empresa nueva nace con su primera sucursal, indicada en el formulario, y el administrador que la crea recibe allí la asignación de su rol existente. No se crea otro usuario ni otro rol.
- Sucursales y empresas se renombran o inactivan sin borrado físico. Se rechaza la inactivación de la sucursal del contexto actual y de cualquier sucursal con turno o borrador abierto. No se inactiva una empresa desde su propio contexto ni si alguna sucursal tiene operaciones abiertas.
- El stock se consulta por sucursal y los ajustes reutilizan el ledger de movimientos, con motivo y clave de idempotencia; nunca se edita un saldo directamente.
- La pantalla de existencias opera exclusivamente sobre la sucursal del contexto activo. Para otra sucursal, el administrador cambia el contexto de su sesión; la API no acepta un identificador de sucursal arbitrario en el ajuste. Se muestran existencia, reserva y disponible. El ajuste de productos por unidad exige enteros y el de peso admite hasta tres decimales. El motivo es obligatorio y una salida que deje menos que lo reservado se rechaza. Ante una respuesta fallida, el mismo envío puede reintentarse con la misma clave de operación.
- Las cajas se administran dentro de su sucursal, con nombre y estado. Crear, reactivar o rotar emite una credencial nueva que se muestra una sola vez; el listado no incluye secretos. Inactivar o rotar una caja con turno o borrador abierto se rechaza y el cambio revoca sus sesiones.

## Usuarios y asignaciones

- El administrador existente es el único usuario con rol `administrator` en este corte. El alta desde el sitio crea únicamente cuentas con el rol `cashier` y asignación a una o más sucursales activas de la empresa del contexto actual. La contraseña inicial requiere al menos 12 caracteres, salvo en la base visual local de desarrollo, donde se admiten 6. No se crea otro rol.
- El listado, la edición de datos y las asignaciones sólo muestran usuarios vinculados a la empresa del contexto. Las asignaciones del administrador no se pueden reemplazar desde el sitio. Al cambiar las sucursales del cajero, se cierran sus sesiones anteriores; no se puede quitar una sucursal donde tenga turno o ticket abierto ni dejarlo sin sucursales.
- La inactivación de una cuenta se rechaza si el usuario mantiene turnos o tickets abiertos, incluso en otra empresa. El administrador puede restablecer la contraseña de cualquier usuario vinculado a la empresa del contexto, incluida su propia cuenta y otra cuenta administrativa si existiera. El restablecimiento revoca todas las sesiones de ese usuario; si es la propia cuenta, la interfaz vuelve al ingreso. Nunca se devuelve el hash ni la contraseña anterior. La longitud mínima es 12, salvo en la base visual local de desarrollo (`127.0.0.1:55433/carnicerias_test_visual`), donde es 6 y la interfaz consulta la política al API.
- `GET /api/users/password-policy` devuelve `{ "minimumLength": número }` y `PUT /api/users/{userId}/password` recibe `{ "password": texto }`, devuelve `204` y no es idempotente respecto de sesiones activas. Ambos requieren `platform.users.manage`; el restablecimiento devuelve `404` si el usuario no pertenece a la empresa del contexto y `400` para una clave inválida.

## Consulta histórica

- `/api/admin/history/sales`, `/shifts`, `/cash-movements` y `/stock-movements` son listados paginados (máximo 100 por página) de solo lectura de la empresa del contexto actual. Permiten filtrar sucursal y fechas; ventas, turnos y caja también permiten caja/terminal. La fecha se envía en UTC y el límite superior es exclusivo, de modo que el formulario incluye el día final completo en hora local.
- `/api/admin/history/sales/{id}` muestra renglones y pagos del ticket confirmado, sin exponer el hash de solicitud ni permitir modificaciones. Los importes y nombres de artículos provienen del snapshot confirmado; los nombres de sucursal, caja y cajero se muestran con su valor actual.
- La consulta exige permiso `organization.manage` en servidor; el cajero no puede acceder aunque conozca la ruta. Los índices de empresa, sucursal/caja y fecha facilitan el recorrido cuando crezca el volumen de tickets. No se crean bases nuevas ni se alteran ventas previas.

## Tecnología y comandos

- Angular 22, formularios reactivos, rutas protegidas y `HttpClient` en `src/Carnicerias.Web`.
- .NET 10 Minimal API + EF Core/PostgreSQL en `src/Carnicerias.Api` e `src/Carnicerias.Infrastructure`.
- Compilar: `dotnet build Carnicerias.sln --configuration Debug --no-restore`; `npm run build --prefix src/Carnicerias.Web`.
- Probar sin crear otra base: `dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --configuration Debug --no-restore`; `npm test --prefix src/Carnicerias.Web -- --watch=false`; `npm run lint --prefix src/Carnicerias.Web`. Las pruebas de integración PostgreSQL existentes permanecen pendientes bajo la regla de base única.

## Estructura y estilo

- Endpoints administrativos por módulo en `src/Carnicerias.Api`; entidades y mapeos en `src/Carnicerias.Infrastructure`; contratos Angular y páginas en `src/Carnicerias.Web/src/app`.
- Seguir el estilo de endpoints tipados, señales Angular, `@if`/`@for`, SCSS y tokens visuales del proyecto. La maqueta `Documentos/maqueta/Articulo agregar- desktop.png` guía los formularios de artículo; `Configuración - Desktop.png` guía el encabezado y la jerarquía visual, no el alcance funcional de balanzas.

## Límites de seguridad y datos

- Siempre: resolver empresa desde sesión, validar entrada en API, verificar permiso en servidor, preservar historial, no exponer secretos y mantener el POS operativo.
- Antes de decisiones nuevas: documentar reglas de negocio no especificadas (por ejemplo, desactivar una sucursal con turno abierto) y adoptar el comportamiento conservador que no pierda datos.
- Nunca: crear otra base de desarrollo sin autorización, permitir editar ventas confirmadas, confiar solo en el guard de Angular o guardar credenciales en Git.

## Criterios de éxito del administrador completo

- `visual-admin` puede entrar a `/admin`; `visual-cashier` recibe 403 en APIs administrativas aunque conozca la URL.
- El administrador mantiene categorías, productos, precios/listas y sus asignaciones; los cambios se reflejan en el POS respetando stock, modalidad de venta y sucursal.
- Puede mantener organización, terminales, usuarios y stock con auditoría/restricciones de seguridad; los datos transaccionales se consultan sin alterarlos.
- El recorrido funcional se verifica en Electron y con consultas de solo lectura a la única base; no se crean bases adicionales.

## Preguntas diferidas

Solo se pedirán reglas al usuario cuando una decisión cambie materialmente el negocio y no exista una opción conservadora documentada. La primera entrega no depende de ninguna de ellas.
