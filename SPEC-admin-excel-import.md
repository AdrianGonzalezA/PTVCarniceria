# Especificación: importación Excel de maestros administrativos

## Estado y alcance aprobado

**Estado:** aprobado para desarrollo el 9/10/2026. El usuario seleccionó categorías, artículos junto con códigos alternativos, listas de precios y clientes. Confirmó que las listas incluyen precios por artículo y habilitación por sucursal; el costo del artículo es estadístico y tanto sus cambios como los cambios de precio conservan vigencias. Este alcance complementa `SPEC-admin.md`, `SPEC-catalog-pricing.md` y `SPEC-customers-credit.md`.

Quedan fuera empresas, sucursales, cajas, usuarios, impuestos, perfiles de etiquetas, existencias, recepción de piezas y movimientos transaccionales. La importación no sustituye los ABM manuales ni modifica tickets históricos.

## Objetivo

Un administrador puede descargar desde cada ABM una plantilla `.xlsx`, completarla y previsualizar una carga masiva antes de confirmarla. Cada plantilla contiene una hoja de carga vacía y otra de ejemplos, para que descargarla y subirla sin editar no genere datos ficticios. Una carga válida se aplica completamente o no se aplica; los errores identifican hoja, fila y columna. El contexto de empresa y los permisos se resuelven siempre en el servidor.

## Contrato de las cuatro plantillas

| ABM | Hojas de carga | Identificación y columnas de carga |
|---|---|---|
| Categorías | `Categorias` | `Nombre` (obligatorio, único por empresa). |
| Artículos y códigos | `Articulos`, `CodigosAlternativos` | Artículos: `Codigo`, `Nombre`, `Categoria`, `ModalidadVenta` (`weight`/`unit`), `Unidad`, `CostoARS`. Códigos: `CodigoArticulo`, `CodigoAlternativo`; refieren a artículos nuevos del archivo o ya existentes en la empresa. |
| Listas de precios | `Listas`, `Precios`, `Sucursales` | Listas: `Nombre`. Precios: `Lista`, `CodigoArticulo`, `PrecioARS`. Sucursales: `Lista`, `Sucursal`. Las referencias pueden señalar una lista nueva del archivo o una ya existente de la empresa; producto y sucursal deben existir en esa empresa. |
| Clientes | `Clientes` | `Codigo`, `Nombre`, `CuentaCorriente` (`SI`/`NO`, opcional; por defecto `NO`). La habilitación explícita de cuenta corriente se destaca en la vista previa. |

Cada archivo incorpora `Ejemplos` con al menos una fila coherente por hoja y una instrucción breve para copiarla a la hoja de carga y reemplazar sus valores. La hoja `Ejemplos` jamás se procesa. Los identificadores y códigos se leen como texto, aun si contienen ceros iniciales. Los importes se aceptan como celdas numéricas de Excel, positivos y con hasta dos decimales. Se rechazan fórmulas, macros, vínculos externos y tipos de archivo distintos de `.xlsx`; no se evalúa contenido activo.

## Reglas de aplicación implementadas

- Las filas de categorías y clientes **crean registros nuevos**. Si su nombre o código ya existe en la empresa, la vista previa señala un conflicto y no se aplica el archivo. No se renombran ni inactivan por importación.
- El código principal identifica al artículo. Una fila con código nuevo lo crea. Si el código ya existe, se marca **«No importado: el artículo ya existe»** y no se cambia ninguno de sus datos, incluido el costo. El resto de las filas válidas puede importarse. La nueva vigencia de costo se abre cuando éste cambia mediante la edición del ABM, no por reimportar un artículo existente. El costo no modifica precios ni impone un mínimo a la lista.
- Una lista existente identificada por su nombre se reutiliza para cargar precios y habilitaciones, sin crear otra lista. Una fila de `Listas` con nombre ya existente no cambia su nombre ni su estado.
- Los códigos alternativos nuevos se agregan tanto a artículos creados por el archivo como a artículos ya existentes, aun cuando la fila principal de éstos figure «No importado». Si un código ya pertenece al mismo artículo, se informa «Sin cambios» y conserva su estado actual; no se duplica ni se reactiva automáticamente. Si está reservado como principal o alternativo por otro artículo de la empresa, es conflicto. Repetir el mismo código dentro del archivo también es error.
- Las filas de precios sobrescriben el **valor vigente** para un par lista/artículo. Si el importe cambió, se cierra la vigencia anterior y se crea otra con fecha y usuario responsables; si es igual, no se crea una versión nueva. No se borra el historial ni se compara el precio con el costo estadístico.
- Las filas de sucursales habilitan la lista para esa sucursal. Una habilitación ya vigente no se duplica. No se deshabilitan asignaciones por importación.
- `CuentaCorriente=SI` crea al cliente con cuenta habilitada sólo cuando se marcó explícitamente. No se importan saldos, deudas, anticipos ni datos fiscales aún no definidos en el ABM.
- Las referencias a categorías, artículos, listas o sucursales se resuelven exclusivamente dentro de la empresa de la sesión. Una referencia inexistente o ambigua impide aplicar el archivo.
- La plantilla y la vista previa son versionadas. Un encabezado ausente, duplicado o desconocido se informa como error; no se interpreta por posición. Las filas completamente vacías se ignoran. Límite inicial: 5 MiB por archivo y 2.000 filas de carga por hoja.

## Interfaz y contrato de API implementado

En las páginas de los cuatro ABM aparecen `Descargar plantilla` e `Importar Excel`. La importación abre un modal con archivo elegido, conteos de altas/cambios/no importados/sin cambios, errores por hoja y fila, y confirmación explícita. Mientras se aplica, no se permite cerrar accidentalmente. Al terminar se actualiza la grilla sin perder los filtros de búsqueda.

- `GET /api/admin/imports/{kind}/template` descarga el `.xlsx` versionado. `kind` admite `categories`, `products`, `price-lists` o `customers`.
- `POST /api/admin/imports/{kind}/preview` recibe un `.xlsx` y devuelve un resumen tipado, filas no importadas con motivo y `issues: [{ sheet, row, column, code, message }]` para errores bloqueantes; no escribe datos.
- `POST /api/admin/imports/{kind}/apply` recibe el mismo archivo y una clave de operación estable para reintentos. Vuelve a validar contra el estado actual y aplica en una transacción; un cambio concurrente produce conflicto, no una carga parcial. Un reintento de una operación confirmada devuelve el resultado anterior sin duplicar registros ni vigencias.
- Los dos `POST` reciben el contenido binario del `.xlsx` en el cuerpo con `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`. `apply` exige además `Idempotency-Key` con un UUID; la misma clave con otro contenido o tipo de maestro devuelve 409. No se envía empresa, sucursal ni usuario en el archivo ni en el cuerpo.
- Los errores generales conservan el sobre `{ error: { code, message, details } }`; los errores por fila/columna devuelven `ImportPreviewResult` con `issues`. 400: archivo/estructura inválidos (incluidas demasiadas filas); 401/403: sesión o permiso; 409: conflicto con datos actuales o reutilización de clave; 413: límite de tamaño del archivo.
- `catalog.manage` autoriza categorías, artículos y listas; `organization.manage` autoriza clientes. La API aplica validación de origen a `preview` y `apply`, usa la empresa de la sesión y no acepta un `companyId` del archivo ni del cliente.

El siguiente tipo ilustra el estilo de contrato; la forma definitiva queda en los contratos C#/TypeScript y sus pruebas:

```csharp
public sealed record ImportIssue(string Sheet, int Row, string Column, string Code, string Message);
```

## Tecnología, estructura y comandos

- Angular 22: páginas y clientes HTTP en `src/Carnicerias.Web/src/app/features/admin` y `src/Carnicerias.Web/src/app/core/admin`.
- .NET 10 Minimal API, EF Core y PostgreSQL: endpoints y validaciones en `src/Carnicerias.Api`; persistencia y cualquier migración aditiva en `src/Carnicerias.Infrastructure`.
- Compilar: `dotnet build Carnicerias.sln --configuration Debug --no-restore`; `npm run build --prefix src/Carnicerias.Web`.
- Probar sin bases adicionales: `dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --configuration Debug --no-restore`; `npm test --prefix src/Carnicerias.Web -- --watch=false`; `npm run lint --prefix src/Carnicerias.Web`.
- Revisión visual del administrador en Electron: `npm run electron:start:admin --prefix src/Carnicerias.Pos`.

## Estrategia de prueba

Pruebas unitarias de lectura `.xlsx`, encabezados, textos con ceros iniciales, decimales, relaciones entre hojas, duplicados, permisos y límites. Pruebas de API sin crear ni reiniciar otra base para previsualización y aplicación cuando sea posible; la integración PostgreSQL que requiera una segunda base queda pendiente por `AGENTS.md`. En la única base local se prueba manualmente una carga pequeña y un reintento, previa revisión del destino. Pruebas Angular cubren selección, errores y confirmación; la apariencia y descarga se revisan en Electron.

Las migraciones aditivas `AddProductCostVersions` y `AddAdminImportOperations` se aplicaron a `carnicerias_test_visual`. Los costos preexistentes recibieron una vigencia basal cuyo inicio y usuario originales son desconocidos: se consigna la fecha de migración y usuario nulo, sin inventar histórico. En la misma base se verificó la carga real de los cuatro maestros, la reimportación de un artículo que no altera sus propiedades pero añade códigos, el precio inferior al costo, las vigencias de precio y costo, la cuenta corriente de un cliente y el reintento idempotente. En Electron se comprobó la descarga de una plantilla y la vista previa y confirmación de una categoría. Los datos cargados son ficticios de desarrollo.

## Límites

- Siempre: vista previa sin escrituras, confirmación antes de aplicar, validación en servidor, transacción por archivo, autorización por empresa, historial de costos y precios, y resultado seguro ante reintento.
- Consultar antes: cambiar la regla de creación sin sobrescritura, importar datos fiscales o modificar saldos de cuenta, ampliar entidades, o crear otra base.
- Nunca: aceptar macros o fórmulas como instrucciones, cargar contraseñas/credenciales desde Excel, importar movimientos de stock o ventas por estos endpoints, ni guardar secretos o archivos subidos en Git.

## Criterios de éxito

1. Cada uno de los cuatro ABM descarga una plantilla Excel válida y legible, con ejemplo separado de los datos que se importan.
2. Una carga correcta de categorías, artículos con códigos, listas con precios/sucursales o clientes se refleja en su grilla y, cuando corresponde, en el catálogo del POS.
3. Un artículo cuyo código ya existe se informa como «No importado» y conserva sus datos; sus códigos alternativos nuevos sí pueden agregarse. Los demás artículos válidos del archivo se crean. Archivo incorrecto, referencia ajena, duplicado de un código alternativo de otro artículo o falta de permiso no produce escrituras parciales y muestra errores concretos. Un precio positivo inferior al costo estadístico es válido.
4. Repetir la misma operación de aplicación no duplica maestros, habilitaciones ni vigencias de costo o precio.
5. La base `carnicerias_test_visual` sigue siendo la única base de la aplicación durante el desarrollo. El POS se revisa en Electron.

## Decisión de duplicados

El usuario confirmó que un artículo existente no se sobrescribe y se marca «No importado», pero sus códigos alternativos nuevos sí se actualizan. Categorías y clientes existentes permanecen sin sobrescritura según la propuesta conservadora; se informan como conflicto hasta que se defina otra regla.
