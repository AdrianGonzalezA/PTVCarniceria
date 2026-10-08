# Especificación: administrador del POS

## Objetivo

Dar al administrador una interfaz web para mantener los maestros que alimentan al punto de venta, dentro de la misma instalación PostgreSQL. El administrador inicial es `visual-admin`, con el rol `administrator`; los cajeros existentes conservan su acceso al POS, pero no al administrador. La UI administrativa puede abrirse en Angular web y su revisión visual durante el desarrollo se hace en Electron.

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
