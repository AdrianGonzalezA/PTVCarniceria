# Especificación propuesta: catálogo y asignación de impuestos por artículo

**Estado (9/10/2026): alcance aprobado e implementado en la base local de desarrollo.** Esta capacidad amplía Configuración → Impuestos por artículo. No modifica por sí sola las reglas tributarias reales de la empresa ni autoriza emisión productiva. Complementa `SPEC-fiscal-payments.md` y conserva el contrato de IVA ya usado por ventas y homologación ARCA.

## Objetivo

El administrador de la empresa podrá mantener un catálogo reutilizable de impuestos o gravámenes y elegir a qué artículos se aplica cada entrada: uno, varios o todos. Podrá consultar las asignaciones vigentes y su historial. La pantalla dejará de exigir que el mismo porcentaje se escriba artículo por artículo.

## Reglas de alcance aprobadas

1. “Todos” significa **todos los artículos existentes de la empresa**, activos e inactivos, al confirmar la operación; no crea una regla automática para artículos futuros. La pantalla mostrará el número exacto de afectados y pedirá confirmación antes de reemplazar asignaciones.
2. El primer impuesto operativo será IVA. Las reglas actuales de tratamiento (`taxed`, `exempt`, `notTaxed`) y alícuota seguirán determinando el snapshot de cada nueva venta y su preparación para ARCA. Cada artículo tendrá como máximo una regla vigente de IVA.
3. Se podrán catalogar y asignar otros gravámenes a un artículo, incluso varios simultáneos, pero quedarán identificados como **configurados, no calculados**. No modificarán el precio, el total, el ticket ni el pedido ARCA hasta especificar base, inclusión en precio, orden de aplicación, redondeo y representación fiscal. No se los presentará como cobrados.
4. El 21 % cargado actualmente a los artículos de Empresa Visual es dato ficticio de homologación. Se vinculará al catálogo sin cambiar su vigencia, las ventas anteriores ni su porcentaje. No se propone como tasa general del negocio.

## Contrato funcional

- El catálogo pertenece a la empresa de la sesión, nunca a una caja o sucursal. Cada entrada tendrá código único e inmutable por empresa, nombre, tipo (`iva` u `otro`), porcentaje con hasta dos decimales, estado activo/inactivo y auditoría de alta/inactivación. Código y porcentaje no se editan en una entrada usada: un cambio fiscal exige crear otra entrada y reasignar, conservando la anterior para el historial.
- Un artículo gravado selecciona una entrada activa de IVA; el tratamiento exento o no alcanzado se registra explícitamente sin asignar una alícuota gravada. Elegir una nueva entrada de IVA cierra la versión vigente de la regla por artículo y abre otra. Repetir la misma elección no crea una versión duplicada.
- Los gravámenes de tipo `otro` admiten varias asignaciones vigentes por artículo, pero no dos veces la misma entrada. Asignar o quitar conserva fecha y actor; inactivar una entrada impide asignaciones nuevas y no borra las históricas. Una entrada con asignaciones vigentes requiere retirarlas o sustituirlas antes de inactivarse.
- La asignación puede apuntar a un artículo, a una selección explícita de artículos o a todos los artículos de la empresa. El servidor determina y confirma el conjunto completo dentro de una transacción; no depende de la página de resultados visible en Angular. Si alguno no pertenece a la empresa o la operación falla, no se aplica parcialmente. La repetición del mismo estado final no genera historial nuevo.
- La UI tendrá un sector “Catálogo de impuestos y gravámenes” con listado, alta e inactivación, y un sector “Asignaciones por artículo” con búsqueda, selección múltiple, acción “Aplicar a todos”, vigencia e historial. Mostrará claramente qué artículos no tienen clasificación IVA y qué gravámenes aún no afectan ventas.
- Las ventas confirmadas y sus snapshots fiscales son inmutables. La emisión ARCA ya existente mantiene el mismo criterio de IVA; ningún gravamen `otro` aparece en el comprobante ni en la impresión fiscal en esta etapa.

## Interfaz propuesta

- Mantener `GET /api/admin/product-tax-rules`, `GET /api/admin/product-tax-rules/{productId}` y `PUT /api/admin/product-tax-rules/{productId}` para no romper clientes actuales; extender respuestas de forma aditiva con el identificador opcional de la entrada de IVA.
- Añadir `GET /api/admin/taxes?page&search`, `POST /api/admin/taxes` y `PATCH /api/admin/taxes/{id}` para consultar, crear e inactivar entradas; las listas son paginadas y `PATCH` no altera código, tipo ni porcentaje.
- Añadir `GET /api/admin/tax-assignments/{productId}` para vigentes e historial y `PUT /api/admin/tax-assignments` para fijar el estado deseado de una entrada en `{ scope: "selected", productIds: [...] }` o `{ scope: "all" }`. La respuesta informa artículos alcanzados y cambios efectivos. Los errores usan el sobre actual: 400 validación, 403 permiso/origen, 404 recurso ajeno y 409 conflicto de estado.
- Toda lectura/mutación exige contexto de empresa y permiso `catalog.manage`; las mutaciones validan origen. Los identificadores de otra empresa nunca se aceptan. El contrato de la asignación masiva se concretará en tipos y tests antes de codificar el endpoint; no se aceptará un `Idempotency-Key` sin garantizar su efecto.

## Tecnología, estructura y comandos

- .NET 10 Minimal API y EF Core/PostgreSQL: entidades, índices y migración aditiva en `src/Carnicerias.Infrastructure`; endpoints y contratos en `src/Carnicerias.Api`; pruebas unitarias/arquitectura en `tests`.
- Angular 22: cliente tipado en `src/Carnicerias.Web/src/app/core/admin`; componentes, estilos y pruebas en `src/Carnicerias.Web/src/app/features/admin`. La referencia visual es la maqueta del administrador. La revisión visual se hará dentro de Electron.
- Compilar: `dotnet build Carnicerias.sln --configuration Debug --no-restore` y `npm run build --prefix src/Carnicerias.Web`.
- Probar sin otra base: `dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --configuration Debug --no-restore`, `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web`.
- Abrir interfaz administrativa para revisión visual: `npm run electron:start:admin --prefix src/Carnicerias.Pos` (con API local en funcionamiento).

## Estilo de código

Mantener nombres de contratos explícitos y respuestas tipadas, como en los endpoints actuales:

```csharp
private sealed record TaxCatalogRow(Guid Id, string Code, string Name,
    string Kind, decimal RatePercent, bool IsActive);
```

Validar la entrada en el borde de la API y sostener índices de unicidad/versión en PostgreSQL; Angular muestra errores específicos y estados de carga sin inventar datos.

## Estrategia de pruebas

- Pruebas de dominio/API sin crear bases: código duplicado, tasa inválida, cruce entre empresas, conflicto IVA, repetición idempotente, inactivación referenciada y asignación a varios/todos sin parcialidad.
- Pruebas Angular: alta, selección paginada, confirmación de “todos”, errores, historial, etiquetas de “no calculado” y conservación del contrato anterior.
- Verificación no destructiva de migración y datos sobre la única base autorizada. No ejecutar la suite de integración que crea o reinicia otra base; esa verificación queda pendiente mientras rija esta restricción.
- Recorrido visual del administrador y del POS en Electron para verificar que las ventas con IVA siguen igual y que los gravámenes adicionales no aparecen como cobrados.

## Límites

- **Siempre:** una sola base `carnicerias_test_visual` en PostgreSQL local `55433`; migración aditiva; empresa desde sesión; historial/auditoría; snapshots confirmados intactos; pruebas antes de integrar.
- **Consultar antes:** cambiar semántica financiera de gravámenes adicionales, hacer que “todos” alcance automáticamente artículos futuros, o alterar la clasificación real requerida para facturar.
- **Nunca:** crear otra base para probar, recalcular ventas anteriores, aplicar silenciosamente un gravamen al total o enviarlo a ARCA, guardar credenciales en Git, confiar sólo en controles de Angular.

## Criterios de éxito

1. `visual-admin` crea un gravamen reutilizable y lo asigna a un artículo, a varios y a todos; la UI y la base muestran exactamente el conjunto afectado, sin duplicados al repetir.
2. Un cajero no puede llamar las APIs; una empresa no ve ni modifica entradas o artículos de otra.
3. El IVA 21 % ficticio ya configurado continúa produciendo los mismos snapshots/solicitudes de homologación para ventas nuevas; las ventas anteriores no cambian.
4. Un gravamen adicional asignado se ve en Configuración como “no calculado”, sin alterar precio, cobro, ticket ni ARCA.
5. Historial, inactivación y errores parciales se comportan como se describe; compilaciones y pruebas permitidas pasan, y se verifica visualmente en Electron.

## Decisiones pendientes para una etapa posterior

- Definir base, inclusión en precio, orden, redondeo y representación fiscal de cada gravamen distinto de IVA antes de aplicarlo a ventas.
- Si se desea una política automática para artículos futuros, especificarla como otra capacidad; la operación “todos” aprobada aquí sólo alcanza los existentes al confirmarla.

## Corte implementado y verificación (9/10/2026)

- El catálogo y las asignaciones versionadas se guardan en `catalog_pricing.tax_catalog_entries` y `catalog_pricing.other_tax_assignments`. Las reglas IVA existentes conservan sus versiones y ahora pueden referenciar la entrada de catálogo; las tres migraciones aditivas se aplicaron sólo a `carnicerias_test_visual`.
- La API implementa `GET/POST/PATCH /api/admin/taxes`, `GET /api/admin/taxes/options`, `GET /api/admin/taxes/active`, `GET /api/admin/tax-assignments/count`, `GET /api/admin/tax-assignments/{productId}` y `PUT /api/admin/tax-assignments`. Este último recibe además `isAssigned` y, para detectar cambios del conjunto durante la confirmación de “todos”, `expectedProductCount`. La ruta IVA anterior sigue disponible y acepta `taxCatalogEntryId` opcional.
- La pantalla de Configuración permite alta e inactivación, selección de IVA por catálogo, asignación/retiro a artículos seleccionados o todos los existentes, conteo previo e historial de gravámenes adicionales. El tipo `otro` está rotulado «no calculado» y no modifica venta, caja, ticket ni ARCA.
- Se verificó dentro de Electron el alta de un gravamen ficticio del 3 %, su aplicación a dos artículos y después a los diez existentes (incluido uno inactivo); repetir la asignación informó cero cambios. La base mostró diez asignaciones vigentes. No se ejecutó la suite de integración PostgreSQL que crea/reinicia otra base: queda pendiente mientras rija la regla de base única. Esta prueba manual no sustituye la revisión fiscal de alícuotas reales.
