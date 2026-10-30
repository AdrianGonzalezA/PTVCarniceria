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

## Corrección de retiro de IVA (30/10/2026)

- `DELETE /api/admin/product-tax-rules/{productId}` cierra la regla fiscal vigente de un artículo de la empresa de la sesión y devuelve `204`. Repetirlo sin regla vigente mantiene ese resultado; un artículo ajeno devuelve `404`. Exige `catalog.manage` y origen permitido. Conserva las versiones anteriores y no modifica snapshots de ventas confirmadas.
- `PUT /api/admin/tax-assignments` admite `isAssigned: false` para una entrada IVA. Cierra solo las reglas vigentes vinculadas a **esa entrada** en el alcance seleccionado o en todos los artículos existentes. Una alícuota distinta permanece vigente. La respuesta mantiene `affectedCount` y `changedCount`; la operación usa la misma transacción y los bloqueos por artículo de la asignación.
- El administrador ofrece **Quitar IVA** por artículo y **Quitar** por selección o por todos. Tras aplicar, recarga ambas grillas de clasificación. El historial distingue reglas terminadas de la única vigente. Quitar deja el artículo **sin regla fiscal configurada**; no lo declara exento ni no alcanzado. Una factura electrónica nueva requiere una regla fiscal completa.
- En la base local de La Rústica, Carbon 5k y Malbec A quedaron con **IVA21 como única regla vigente**; sus reglas de 10,5 % tienen fecha de fin y permanecen solo como historial. No se recalcularon ventas ni se creó otra base.

## Análisis de importes: factura B 00099-00000025 (30/10/2026)

**Estado:** constatación del comportamiento vigente y propuesta de presentación; no cambia el criterio de cálculo ni el comprobante autorizado. Datos ficticios de la única base local, empresa La Rústica.

| Artículo | Cantidad | Precio final/kg | Total con IVA | Neto gravado 10,5 % | IVA incluido |
| --- | ---: | ---: | ---: | ---: | ---: |
| AGUJA PORC. | 1,200 kg | $ 12.602,00 | $ 15.122,40 | $ 13.685,43 | $ 1.436,97 |
| BOLA DE LOMO | 2,200 kg | $ 7.572,00 | $ 16.658,40 | $ 15.075,48 | $ 1.582,92 |
| **Total** | | | **$ 31.780,80** | **$ 28.760,91** | **$ 3.019,89** |

- La venta y el comprobante tienen descuento $ 0,00 y ningún renglón exento o no gravado. Es una factura B autorizada en homologación. El IVA 10,5 % corresponde a la **foto fiscal de cada renglón al confirmar la venta**; modificaciones posteriores del catálogo no reescriben esa foto.
- El precio configurado es final, con IVA incluido. Por renglón se calcula `neto = redondear(total final / 1,105; 2 decimales)` e `IVA = total final − neto`. Se suman los netos y los IVA de los renglones de cada alícuota. Esta es la regla implementada en `SaleAmountCalculator` y `FiscalRequestBuilder`; ARCA recibe `ImpNeto=28.760,91`, `ImpIVA=3.019,89` e `ImpTotal=31.780,80`, con una sola entrada `AlicIva` de 10,5 %.
- Si se divide **el total agrupado** de $ 31.780,80 por 1,105 antes de redondear, resulta neto $ 28.760,90 e IVA $ 3.019,90. La diferencia de $ 0,01 es exclusivamente el orden de redondeo. No mezclar ese resultado con los importes persistidos por artículo ni cambiar la factura 25 ya autorizada.
- Regla común y verificable para facturadores conectados a WSFEv1: **totalizar una sola base e IVA por alícuota**, hacer coincidir `ImpNeto` con las bases, `ImpIVA` con los IVA y `ImpTotal` con neto + IVA + exentos/no gravados + tributos. ARCA permite tolerancia de redondeo en la correspondencia entre base e impuesto. Fuente: [manual oficial WSFEv1](https://arca.gob.ar/ws/WSFEV1/documentos/manual-desarrollador-COMPG-v3-4-2.pdf), validaciones 10022, 10023, 10048, 10051 y 10061. La norma no fija en esas validaciones si el redondeo interno de cada POS se hace por renglón o por grupo.
- La factura B actual muestra el precio final y **IVA contenido** total; el neto no aparece explícitamente en el PDF. Propuesta a revisar: mostrar la alícuota de cada artículo y un cuadro informativo por alícuota con neto, IVA y total, siempre tomado del snapshot autorizado. Para factura A, mantener la exposición de precios netos e IVA discriminado. [ARCA: transparencia fiscal](https://www.afip.gob.ar/comunicacion/transparencia-fiscal/) y [RG 5614/2024](https://biblioteca.arca.gob.ar/search/query/norma.aspx?p=t%3ARAG%7Cn%3A5614%7Co%3A9%7Ca%3A2024%7Cf%3A12%2F12%2F2024).
- Los gravámenes de catálogo tipo `otro` aún no se calculan. La salida actual imprime $ 0,00 de «Otros Impuestos Nacionales Indirectos»; antes de usarla con artículos efectivamente alcanzados debe definirse y calcularse ese concepto, sin derivarlo solo de la asignación de catálogo.
