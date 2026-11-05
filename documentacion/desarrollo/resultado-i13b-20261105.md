# I13b — Libro IVA Ventas y exportación Excel

**Estado:** implementado en código el 05/11/2026. I13b no tiene migración ni altera ventas, comprobantes o snapshots. Reutiliza los intentos ARCA y snapshots de I13a; la vista nueva vive en **Administración → Negocio**, independiente del historial de tickets.

## Contrato de lectura

- `GET /api/admin/fiscal-sales-book?from=YYYY-MM-DD&to=YYYY-MM-DD&branchId=&page=1&pageSize=30` devuelve comprobantes **Authorized** con CAE de la empresa en sesión. `branchId` es opcional y debe pertenecer a esa empresa. Fechas inclusivas, período máximo de 366 días, página entre 1 y el límite entero y tamaño entre 1 y 100.
- `GET /api/admin/fiscal-sales-book/export` usa los mismos filtros de fecha y sucursal y devuelve un `.xlsx` de hasta 50 000 comprobantes. El archivo tiene hojas **Completos** y **Pendientes**; no pagina la exportación.
- Ambos endpoints conservan el middleware `organization.manage` y exigen además `platform.users.manage` en el servidor. El guard y el enlace del administrador reflejan esos permisos. Ninguno modifica el contrato del historial ni el endpoint de emisión.
- Un comprobante completo tiene snapshot fiscal y total idéntico al intento autorizado. Sus bases, IVA por alícuota, exento y no gravado salen del snapshot. Un autorizado anterior sin snapshot es `legacyIncomplete`; una discrepancia es `inconsistent`. Sus importes fiscales no se infieren del catálogo, venta ni reglas vigentes. El total conocido de pendientes se informa **separado** de los totales completos.
- El campo `otherTaxesStatus: notModeled` y la columna Excel «Otros tributos: No modelado» evitan presentar cero como si fuera un importe calculado. Los tributos adicionales siguen diferidos por decisión del PO.

## Verificaciones

| Prueba | Resultado |
| --- | --- |
| Libro Excel con IVA 10,5 % y 21 %, CAE, factura B, total y hoja histórica separada | 2/2 .NET aprobadas |
| Angular: desglose, estado incompleto, filtros sucursal/página, exportación y recuperación de error | 3/3 focalizadas y 251/251 suite completa aprobadas |
| Regresión fiscal .NET (snapshot, solicitud, documento, Excel) | 17/17 aprobadas |
| Lectura EF en `carnicerias_test_visual`, contenedor `carnicerias-pos-test-20261007`, `127.0.0.1:55433` | 1/1: listado, agregado, filtro de sucursal, rechazo de sucursal ajena y usuario sin permiso, fecha inválida y construcción del Excel. Cero escrituras. |
| Compilación API/Web y lint Angular | Correctos; build Web conserva advertencias preexistentes de presupuesto CSS y `qrcode` CommonJS. |

La prueba de lectura detectó inicialmente que EF Core no traducía el constructor del registro de IVA dentro del agregado SQL. Se corrigió proyectando primero los valores escalares y construyendo el registro después de materializar la consulta; la misma prueba volvió a pasar.

## Límite de homologación

Al verificar, la base tenía 24 intentos fiscales, 23 autorizados y **cero snapshots I13a**. Por eso los comprobantes existentes se clasifican como históricos incompletos. Las pruebas con objetos construidos cubren IVA mixto y totales; todavía falta emitir un comprobante nuevo con la API actual y contrastar su fila del Libro IVA con ARCA, PDF y Excel. El nombre de sucursal se lee del registro vigente de sucursales porque I13a no guardó un snapshot textual de ese nombre; los importes fiscales y el receptor sí provienen de datos persistidos de la emisión/venta. No se pidió revisión visual del POS antes de completar los cambios autorizados.
