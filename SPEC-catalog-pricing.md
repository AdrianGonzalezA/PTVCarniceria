# Especificación del módulo catalog pricing

## Estado

**Estado:** decisión de listas ratificada; implementación parcial  
**Fecha:** 6 de octubre de 2026  
**Module id:** `catalog-pricing`  
**Mapa de origen:** `CAPABILITY_MAP.md`

## Objetivo

Reemplazar el catálogo de demostración del punto de venta por productos activos y precios vigentes del servidor, aislados por empresa y sucursal. El cajero debe poder navegar categorías, buscar desde tres caracteres e ingresar un código exacto sin ver productos de otro contexto.

## Fuentes y decisiones ya establecidas

- `REQUERIMIENTOS_MVP.md`, RF-03, RF-04 y RF-07: producto, categoría, unidad, modalidad de venta, códigos, precios vigentes y búsqueda.
- `Documentos/Puesto de venta_ selección de productos - Especificación de Requerimientos de Software.docx`: selección por categoría, nombre y código, más carga de peso o unidad.
- `Documentos/Administrador_ ABM productos - Especificación de Requerimientos de Software.docx`: alta, modificación, validación de costo/precio e historial.
- La eliminación física que aparece en la ERS de administración contradice RF-03 del MVP para productos con movimientos. La implementación conservará productos históricos mediante inactivación.
- La maqueta `Documentos/maqueta/Landing Puesto de venta 1 - Desktop-1.png` guía la disposición visual del POS; sus valores de precio son ilustrativos.

## Alcance de la primera entrega

1. Categorías y productos pertenecen a una empresa. El código principal de producto es único dentro de esa empresa y permanece inmutable después del alta.
2. Cada producto tiene denominación, categoría, unidad, modalidad de venta (`weight` o `unit`), costo positivo, estado activo y códigos alternativos cuando correspondan.
3. Una sucursal puede tener varias listas de precios habilitadas. El cajero elige explícitamente la lista correspondiente antes de agregar productos; la elección pertenece a la venta en curso y no se deduce solo de la sucursal.
4. Cada cambio de precio conserva valor anterior, nuevo, vigencia, fecha de cambio y usuario. El POS recibe solo productos activos con precio vigente en la lista elegida para su contexto. Los importes se expresan en ARS con dos decimales. Los productos por peso muestran precio por kilogramo.
5. La búsqueda por texto se inicia con tres caracteres y examina nombre y código. La búsqueda exacta por código puede ejecutarse con menos caracteres y devuelve un producto inequívoco o «Producto no encontrado».
6. Las consultas del POS usan el contexto de empresa y sucursal resuelto desde la sesión del servidor. No aceptan esos IDs como parámetros del cliente.
7. La lista elegida se valida de nuevo en el servidor contra la sucursal de la sesión. Si deja de estar habilitada, el cajero debe elegir otra antes de continuar; no se aplican precios de otra lista por omisión.
8. Una venta con productos conserva el precio de cada línea. Cambiar de lista exige cerrar o cancelar ese borrador; no se recalcula silenciosamente el detalle.
9. El catálogo del POS deja de utilizar datos de demostración cuando la sucursal tiene productos configurados. Si está vacío, muestra un estado vacío explícito; nunca presenta precios de prueba como comerciales.

## Contrato propuesto de lectura

- `GET /api/catalog/price-lists`: devuelve `{ id, name }[]` con las listas activas habilitadas para la sucursal de la sesión.
- `GET /api/catalog/categories?priceListId=`: devuelve `{ id, name, productCount }[]` para el contexto y lista elegidos. El conteo considera solo productos activos con precio vigente.
- `GET /api/catalog/products?priceListId=&categoryId=&q=&code=&page=1&pageSize=50`: devuelve `{ items, page, pageSize, totalItems }`. Cada item incluye `id`, `code`, `name`, `categoryId`, `unit`, `saleMode`, `price`, `currency` y `priceEffectiveFromUtc`.
- `priceListId` es obligatorio para categorías y productos. La API rechaza una lista ajena o no habilitada en la sucursal activa.
- `categoryId`, `q` y `code` son filtros opcionales; `code` es una coincidencia exacta. `q` requiere al menos tres caracteres. `pageSize` tiene límite de 100.
- Errores con el contrato existente `{ error: { code, message, details } }`: `401 NOT_AUTHENTICATED`, `403 OPERATIONAL_CONTEXT_REQUIRED`, `403 PRICE_LIST_NOT_AVAILABLE`, `400 VALIDATION_ERROR`.

## Gestión y límites

- Un usuario con contexto operativo vigente puede consultar las listas habilitadas para su sucursal. La lectura de productos y precios tendrá permiso de lectura del catálogo; crear o editar productos y precios requiere un permiso de gestión distinto y un evento de auditoría.
- Los productos inactivos no entran en nuevas ventas. Los precios ya utilizados se conservarán como instantánea en el futuro borrador de venta.
- Esta entrega no calcula impuestos, stock, descuentos, trazabilidad, pagos ni comprobantes. Tampoco conecta aún balanza o lector físico.
- La carga rápida de un producto inexistente desde el POS queda pendiente de definir: la ERS la muestra, pero puede eludir catálogo, costo, IVA y permisos.

## Verificación

- Una sucursal puede ofrecer varias listas y el cajero puede elegir una; otra sucursal de la misma empresa puede ofrecer un conjunto distinto.
- El mismo producto puede tener precios distintos entre listas sin cruzar resultados.
- Un usuario no obtiene categorías, productos ni precios de otra empresa o sucursal al manipular filtros.
- Producto inactivo o sin precio vigente no aparece en categoría, búsqueda ni código exacto.
- El POS muestra nombre, precio y modalidad recibidos del servidor y conserva búsqueda, selección y total.
- Build y pruebas enfocadas de API/Angular pasan; la vista se revisa dentro de Electron a 1920 × 1080 y en una ventana más pequeña.

## Estructura y comandos de trabajo

- API: `src/Carnicerias.Api`; entidades y migraciones: `src/Carnicerias.Infrastructure`; POS Angular: `src/Carnicerias.Web/src/app/features/pos`.
- Build backend: `dotnet build Carnicerias.sln --configuration Release`.
- Verificación Angular: `npm run lint --prefix src/Carnicerias.Web`, `npm test --prefix src/Carnicerias.Web -- --watch=false` y `npm run build --prefix src/Carnicerias.Web`.
- Revisión en escritorio: `npm run electron:start --prefix src/Carnicerias.Pos`.
- Seguir los contratos C#/TypeScript existentes, validar en el borde HTTP y mantener migraciones explícitas. No introducir precios ni catálogos comerciales de ejemplo en producción.

## Decisión ratificada

El 6 de octubre de 2026 el usuario confirmó que la sucursal puede tener listas de precios y que el cajero elige la correspondiente. Esta especificación modela varias listas habilitadas por sucursal y selección explícita para cada venta.

## Corte implementado

- Modelo y migración aditiva para listas de precios y su habilitación por sucursal, con claves compuestas que impiden asociaciones entre empresas distintas.
- `GET /api/catalog/price-lists` exige sesión y contexto operativo vigentes; devuelve solo listas activas habilitadas para la sucursal actual.
- El POS consulta las listas habilitadas para el contexto operativo de la sesión y las distingue de dos listas de prueba. Las listas reales consultan categorías, productos y precios vigentes desde la API; si todavía no hay datos, se muestra un estado vacío sin inventar precios.
- Las listas de prueba permiten recorrer la venta sin datos comerciales y bloquean el cambio mientras haya productos en el ticket. Sus precios siguen en memoria y no representan listas comerciales.
- Pendiente: operaciones y pantallas de gestión para cargar categorías, productos, códigos y precios; el modelo y las lecturas del POS ya están disponibles.
