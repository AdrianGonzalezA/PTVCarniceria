# Tareas: catálogo de impuestos y gravámenes

**Estado (9/10/2026): flujo funcional de tareas 1–7 implementado; criterios de integración PostgreSQL pendientes por la regla de base única.** Fuente: `SPEC-tax-catalog.md`; orden y riesgos: `tasks/tax-catalog-plan.md`. Las casillas sin marcar requieren evidencia adicional antes de darse por cerradas. No altera la lista abierta `tasks/todo.md`.

## 1. Catálogo persistente y compatibilidad IVA

**Aceptación**
- [ ] Entradas de catálogo pertenecen a una empresa, con código único, tipo, tasa y estado; tasa/código usados no se reescriben.
- [ ] La migración vincula cada versión de IVA gravado existente a la tasa de catálogo correcta, conservando IDs, fechas, actor y snapshots.
- [ ] La única migración aplicada para pruebas es sobre `carnicerias_test_visual`; ninguna otra base es creada.

**Verificar:** tests puros del modelo, `dotnet build Carnicerias.sln --configuration Debug --no-restore`, revisión del SQL generado y consultas de sólo lectura antes/después en la base autorizada. **Depende de:** ninguna. **Archivos:** entidad nueva, `ProductTaxRule.cs`, `PlatformAccessDbContext.cs` y migración EF generada. **Tamaño:** medio; archivos EF generados son mecánicos.

## 2. API de catálogo

**Aceptación**
- [ ] Listar/buscar paginado, crear e inactivar entrada; una entrada referenciada vigente no se inactiva.
- [ ] Permiso, origen, validación, unicidad y aislamiento por empresa se verifican en servidor; error uniforme.
- [ ] Código/tipo/tasa no cambian al editar y una solicitud repetida no duplica registros.

**Verificar:** pruebas unitarias de validación/dominio y pruebas de contrato que no creen bases, build .NET. **Depende de:** 1. **Archivos:** endpoint nuevo, registro en `Program.cs`, pruebas focalizadas. **Tamaño:** medio.

## 3. UI del catálogo

**Aceptación**
- [ ] El administrador consulta, crea e inactiva entradas, con estados de carga, vacío, error y éxito.
- [ ] La pantalla distingue IVA operativo de gravámenes “no calculados” y muestra el contexto de empresa.
- [ ] El cajero no recibe controles administrativos y una ruta directa no sustituye la autorización de API.

**Verificar:** tests Angular focalizados, `npm run lint --prefix src/Carnicerias.Web`, `npm run build --prefix src/Carnicerias.Web`. **Depende de:** 2. **Archivos:** cliente admin, `product-tax-page.ts`, `.html`, `.scss`, `.spec.ts`. **Tamaño:** medio.

### Control A: catálogo

- [ ] Compilaciones y pruebas permitidas pasan.
- [ ] Migración no cambió ventas, saldos ni reglas de IVA efectivas.
- [ ] El catálogo se puede crear y consultar desde Electron.

## 4. Selección de IVA del catálogo

**Aceptación**
- [ ] El administrador asigna una entrada IVA activa a un artículo; como máximo una queda vigente.
- [ ] Exento/no alcanzado sigue siendo explícito, y el endpoint anterior mantiene su contrato.
- [ ] Cambio crea versión; repetición no la crea; snapshots pasados no se recalculan.

**Verificar:** tests de dominio/API permitidos, tests Angular y comparación de cálculo IVA antes/después. **Depende de:** 1–3. **Archivos:** `AdminProductTaxEndpoints.cs`, cliente admin, página TS/HTML y pruebas focalizadas. **Tamaño:** medio.

## 5. Persistencia de gravámenes adicionales

**Aceptación**
- [ ] Varias entradas `otro` pueden estar vigentes en un artículo, pero no dos veces la misma.
- [ ] Alta/retiro conservan vigencia y actor; vínculos entre empresas distintas son imposibles.
- [ ] El calculador de ventas y ARCA siguen leyendo sólo la regla IVA actual.

**Verificar:** tests puros, build .NET, revisión de constraints/SQL y consultas no destructivas en la base autorizada. **Depende de:** 1. **Archivos:** entidad nueva, `PlatformAccessDbContext.cs`, migración EF generada y pruebas. **Tamaño:** medio.

## 6. Consulta y cambio individual de asignaciones adicionales

**Aceptación**
- [ ] API devuelve vigentes/historial y permite fijar o retirar el estado de una entrada sin duplicar versiones.
- [ ] UI muestra las asignaciones del artículo y su historial, con aviso “no calculado”.
- [ ] Rechaza empresa ajena y entrada inactiva; para IVA sólo permite asignar una entrada activa, no quitarla sin elegir tratamiento.

**Verificar:** tests API/dominio permitidos, tests Angular y build. **Depende de:** 5. **Archivos:** endpoint de asignaciones, cliente admin, página TS/HTML y pruebas focalizadas. **Tamaño:** medio.

## 7. Asignación masiva a varios o todos

**Aceptación**
- [ ] Selección explícita admite varios IDs; “todos” incluye activos e inactivos existentes y no futuros.
- [ ] El servidor confirma cantidad, realiza todos los cambios o ninguno y repetir la operación no crea historial.
- [ ] La UI solicita confirmación con conteo real y no confunde una página de búsqueda con “todos”.

**Verificar:** tests de conjuntos, duplicados, conflicto y rollback sin crear otra base; tests Angular; recorrido en Electron. **Depende de:** 4, 6. **Archivos:** endpoint, cliente admin, página TS/HTML y pruebas focalizadas. **Tamaño:** medio.

### Control B: asignaciones y compatibilidad

- [ ] IVA nuevo conserva total, snapshot y preparación ARCA de homologación.
- [ ] Gravámenes adicionales son visibles pero no alteran cobro, ticket ni pedido fiscal.
- [ ] Operaciones masivas respetan aislamiento y atomicidad; cobertura de integración prohibida queda documentada.

## 8. Verificación y documentación

**Aceptación**
- [ ] Pruebas permitidas y compilaciones pasan; administrador y POS se revisan sólo en Electron.
- [ ] `SPEC-tax-catalog.md`, `SPEC-fiscal-payments.md`, `SPEC-admin.md`, `SPEC-catalog-pricing.md`, `README.md` y `tasks/arca-homologacion.md` reflejan el estado real, no el plan deseado.
- [ ] La decisión de mantener IVA separado de gravámenes adicionales y sus riesgos queda documentada; la prueba de integración no ejecutada se identifica con motivo.

**Verificar:** `git diff --check`, búsquedas de textos obsoletos, revisión documental y consultas de sólo lectura a la base única. **Depende de:** 1–7. **Archivos:** documentos citados, decisión de arquitectura y este listado. **Tamaño:** dividir en commits documentales si supera ~5 archivos.

## 9. Revisión Git y publicación

**Aceptación**
- [ ] Se revisan los cambios preexistentes de facturación/Electron y los nuevos por separado, sin perder trabajo del usuario.
- [ ] Los commits son lógicos, no contienen PFX, contraseñas, tokens ni archivos personales; sólo se agregan rutas explícitas.
- [ ] `origin` contiene los commits verificados o se informa con precisión por qué falló el push.

**Verificar:** `git status --short`, `git diff --check`, revisión del diff staged y búsqueda de secretos antes de `git commit`/`git push`. **Depende de:** 8. **Archivos:** sólo los modificados y verificados. **Tamaño:** pequeño.

### Control final

- [ ] Criterios de `SPEC-tax-catalog.md` completos.
- [ ] Compilación, lint y pruebas permitidas verdes; integración que crea otra base expresamente pendiente.
- [ ] Estado de Git y documentación coherentes; revisión humana del comportamiento en Electron.

## Evidencia y límites de este corte

- Migraciones aplicadas sólo a `carnicerias_test_visual`; las 12 versiones tributarias existentes se vincularon sin alterar su vigencia ni las ventas confirmadas.
- En Electron se creó un gravamen ficticio, se aplicó a dos artículos y a los diez existentes, incluido uno inactivo. Un reintento notificó cero cambios; la base mostró diez asignaciones vigentes.
- Pasaron build .NET Release, pruebas puras focalizadas, arquitectura, Angular, lint y pruebas Electron. La prueba de integración que crea o reinicia otra base no se ejecuta por `AGENTS.md`; falta cobertura automatizada de transacción/aislamiento en PostgreSQL.
- La conciliación con `tasks/plan.md` y `tasks/todo.md` es una tarea documental futura solicitada por el usuario; no se cierran sus criterios históricos sin revisión.
