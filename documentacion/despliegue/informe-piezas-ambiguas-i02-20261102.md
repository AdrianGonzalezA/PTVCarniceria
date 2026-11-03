# I02 — Investigación de tres piezas ambiguas

**Fecha:** 02/11/2026. **Base consultada:** exclusivamente `carnicerias_test_visual`, en el contenedor existente `carnicerias-pos-test-20261007` publicado en `127.0.0.1:55433`. **Alcance:** consultas de solo lectura. No se hizo respaldo, migración, corrección de datos ni prueba con escritura. Todas las horas de este informe están en UTC.

## Criterio de lectura

La tabla física `inventory.pieces` todavía tiene el esquema previo a I02: **no contiene `Status`, dueño de reserva ni eventos de estado**. Por eso “estado actual” significa estado **demostrable a partir de referencias existentes**, no una etiqueta persistida. Cada pieza tiene una recepción identificada por `InventoryMovementId`; los movimientos posteriores de venta se registran por empresa, sucursal y artículo, sin ID de pieza. Se revisaron también `confirmed_sale_lines`, los borradores de origen y sus líneas. `InventoryMovementKind` vale 1 para ajuste, 2 para venta y 3 para recepción de pieza, según `BranchInventory.cs`.

La guarda `unassigned_ambiguous_stock` de `20261102181956_ReserveTraceablePieces` rechaza una pieza sin venta vinculada ni borrador activo si existe una venta posterior del mismo artículo/sucursal sin `InventoryPieceId`. El prevuelo dio **3** en esa guarda. Las demás cuatro guardas y el control de reservas agregadas dieron **0**. El historial físico tiene 56 migraciones aplicadas; solo I02 está pendiente.

## Pieza `999002` — Asado, Empresa Visual

**Identidad y recepción.** Pieza `8ebaa375-8e1d-46de-b566-63e2c5468eb8`; artículo `1002` **Asado**, unidad kg, sucursal **Sucursal Visual**. Peso recibido **0,750 kg**. Origen declarado `Prueba manual desarrollo`; código leído `2999002007507`; perfil `EAN13 pieza prueba desarrollo` revisión 1. La recibió `visual-admin` el **08/10/2026 17:48:58.384179**. El movimiento de recepción `6ffffe5a-da74-4052-859a-6be050b05f71` registró **+0,750 kg** y está ligado directamente a la pieza. No hay otros movimientos ligados por ID a esta pieza.

**Secuencia registrada para el mismo artículo y sucursal.**

| UTC | Registro | Cantidad | Vínculo con pieza |
| --- | --- | ---: | --- |
| 07/10 23:18:35 | Carga inicial ficticia, movimiento `OpeningBalance` | +80,000 kg | Stock agregado, sin pieza. |
| 07/10 23:27:18 | Venta genérica | −1,000 kg | Antes de `999002`. |
| 08/10 16:01:47 | Recepción de otra pieza, `999001` | +0,500 kg | Identidad distinta. |
| 08/10 17:45:32 | Venta con dos renglones | −1,500 kg | 0,500 kg vinculados expresamente a `999001` y 1,000 kg genéricos; antes de `999002`. |
| 08/10 17:48:58 | Recepción de `999002` | +0,750 kg | Movimiento directo indicado arriba. |
| 08/10 18:18:41 | Venta `61c5cb94-99db-430e-8b8c-ac6d61c845ed` | −5,000 kg | Renglón y borrador sin `InventoryPieceId` ni `PieceIdentifier`. Movimiento de venta `67ff20a6-aa2d-4abb-8d7c-fb4af7bd0516`. |
| 08/10 19:09:00 | Venta `ae889ed8-77af-44b2-9f36-d4a498344783` | −1,750 kg | Renglón y borrador sin identidad de pieza. Movimiento `e0a22553-2f2d-4805-b716-f2444255c44d`. |
| 09/10 15:27:05 | Venta `e0a6b4c9-a6d3-4e8f-90a8-dffecaa6eb07` | −10,000 kg | Renglón y borrador sin identidad de pieza. Movimiento `e2955aac-1218-453a-8de7-d9b76614dd8d`. |

Las tres ventas posteriores suman **16,750 kg**. El balance agregado actual del artículo es **62,000 kg disponibles y 0,000 kg reservados**, coherente con todos los movimientos (`80 − 1 + 0,5 − 1,5 + 0,75 − 5 − 1,75 − 10 = 62`). Ninguna venta posterior coincide en cantidad con los **0,750 kg** de la pieza. No se encontró ninguna referencia a `999002` en renglones de borrador o venta, ni por ID ni por identificador.

**Estado demostrable y clasificación:** fue recibida; no consta venta identificada ni reserva activa. Su ubicación y disponibilidad física actuales **no se pueden establecer** con estos datos. **Ambiguo sin evidencia suficiente.** La existencia de stock genérico previo permite explicar las ventas sin consumir esta pieza, pero no demuestra que eso haya ocurrido físicamente.

## Pieza `250661` — AGUJA, Carniceria Rustica SRL

**Identidad y recepción.** Pieza `5c326cdf-ef92-41d5-9922-91b20dea2eb3`; artículo `71.000` **AGUJA**, unidad kg, sucursal **Alta Cordoba**. Peso recibido **51,601 kg**. Origen declarado `Frigorífico ciclo 2`; código leído `925066151601`; perfil `Ciclo 2 - EAN-13 de prueba` revisión 1. La recibió `admin` el **30/10/2026 11:31:35.570844**. El movimiento `6fd68d9b-f6e2-4281-90cf-03dec1ff285b` registró **+51,601 kg** y está ligado directamente a la pieza. No hay otros movimientos ligados por ID.

**Secuencia registrada para el mismo artículo y sucursal.** El 14/10 a las 18:18:52 hubo un **ajuste** de +10.000,000 kg (motivo `.`). Hubo cuatro ventas genéricas de −1,000 kg cada una el 16/10 a las 13:08:39 y 14:56:53, y el 27/10 a las 19:11:40 y 21:53:30; todas son anteriores a esta recepción. El 30/10 a las **11:31:35** ingresó `250661` (+51,601 kg); a las **11:32:31** ingresó otra pieza, `071000` (+51,601 kg). El 30/10 a las **16:17:01** se confirmó la venta `f0bcb3b0-632e-4526-813f-bcb8e3739271`, renglón de AGUJA **1,350 kg** sin `InventoryPieceId` ni `PieceIdentifier`; su borrador de origen tampoco identifica pieza. El movimiento `b64c1e0d-cf23-4f02-b8cb-ab129831c7d3` registra **−1,350 kg** para esa venta, solo a nivel de artículo.

El balance agregado actual de AGUJA es **10.097,852 kg disponibles y 0,000 kg reservados**: `10.000 − 4 + 51,601 + 51,601 − 1,350 = 10.097,852`. La venta posterior no equivale al peso entero de `250661`; podría corresponder a stock genérico, a un fraccionamiento de una de las piezas o a otra operación física no individualizada. No hay referencia a `250661` en renglones de borrador o venta.

**Estado demostrable y clasificación:** recibida, sin venta identificada ni reserva activa; destino físico y disponibilidad **indeterminados**. **Ambiguo sin evidencia suficiente.** El alto saldo agregado no identifica la procedencia de los 1,350 kg vendidos.

## Pieza `071000` — AGUJA, Carniceria Rustica SRL

**Identidad y recepción.** Pieza `08d95c2f-7d23-4422-a4f3-b8b2b02dc15c`; mismo artículo `71.000` **AGUJA** y sucursal **Alta Cordoba**. Peso recibido **51,601 kg**. Origen declarado `Frigorífico ciclo 2`; código leído `907100051601`; mismo perfil `Ciclo 2 - EAN-13 de prueba` revisión 1. La recibió `admin` el **30/10/2026 11:32:31.655550**. El movimiento `a15ea95c-3d3c-4dd9-aceb-e3152a76c287` registró **+51,601 kg** y está ligado directamente a esta pieza. No hay otros movimientos ligados por ID.

**Secuencia registrada.** Comparte el historial agregado anterior de AGUJA descrito para `250661`. Ingresó **56 segundos después** de aquella pieza, con otro identificador y otro código de barras. A las **16:17:01** se registró la misma venta genérica de **1,350 kg** indicada arriba; es **un solo renglón y un solo egreso**, no una venta distinta por cada pieza. Ni el renglón confirmado ni el borrador de origen identifican `071000` o `250661`. El saldo agregado de ambas es el mismo balance del artículo, no dos saldos individuales.

**Estado demostrable y clasificación:** recibida, sin venta identificada ni reserva activa; destino físico y disponibilidad **indeterminados**. **Ambiguo sin evidencia suficiente.** Igual artículo y peso no demuestran duplicación de recepción: hay dos identificadores, códigos, operaciones y movimientos de entrada distintos. Tampoco permiten asignar los 1,350 kg a una de las dos piezas.

## Alternativas de conciliación y efecto sobre I02

| Alternativa | Qué preserva y qué evidencia exige | Efecto con las guardas actuales de I02 |
| --- | --- | --- |
| **A. Mantener los registros y detener I02** (recomendada ahora) | Conserva recepciones, movimientos, ventas y borradores tal como ocurrieron en el POS. Se registra el estado como indeterminado en este informe, sin equipararlo a Disponible. | La guarda `unassigned_ambiguous_stock` sigue dando 3; **I02 no puede aplicarse**. No requiere escritura. |
| **B. Verificación física y documental por identificador** | Un inventario con lectura de `999002`, `250661` y `071000`, más remitos/registro de fraccionamiento si existen, puede aportar evidencia nueva. El resultado se conservaría como constancia de conciliación auditada, sin alterar las ventas históricas por inferencia. | Aun si se confirma físicamente la existencia o ausencia de las piezas, la guarda actual seguirá dando 3 porque las ventas genéricas permanecen; **no habilita por sí sola I02**. Requiere una decisión posterior sobre cómo representar la conciliación en el modelo, sin debilitar la seguridad. |
| **C. Atribución documental de una venta a una pieza** | Solo sería admisible con prueba externa inequívoca y una operación de corrección auditada. Los renglones actuales son de 5,000/1,750/10,000 kg frente a `999002` de 0,750 kg, o 1,350 kg frente a las piezas de 51,601 kg; ninguno coincide con la pieza completa. No se deben inventar enlaces ni modificar importes o cantidades para forzar coincidencias. | **No hay corrección válida demostrada con los datos actuales.** El guard de I02 y su chequeo de igualdad de peso impedirían vincular directamente estos renglones. |

**Recomendación al PO:** aprobar la clasificación de las tres piezas como **ambiguas sin evidencia suficiente** y mantener I02 bloqueado en esta base. Solicitar una comprobación física/documental independiente si se quiere determinar su destino real. Con las ventas existentes, no se halló una regularización exclusivamente de datos que preserve la historia y satisfaga las guardas **sin crear una asociación ficticia o una operación de negocio artificial**. Cualquier diseño posterior de conciliación, escritura, respaldo o migración requiere autorización separada; esta investigación no cambia las guardas ni los datos.

## Evidencia reproducible

- `inventory.pieces` unido por `InventoryMovementId` a `inventory.inventory_movements`, y por empresa/sucursal/artículo a `catalog_pricing.products`, `platform_access.companies` y `platform_access.branches`.
- `pos_sales.confirmed_sale_lines` y `pos_sales.sale_draft_lines` revisados por `InventoryPieceId`, `PieceIdentifier`, artículo, sucursal y fecha; se verificaron también `SourceDraftId` y `Status=2` de los borradores de origen.
- `inventory.inventory_movements` consultado en orden cronológico para cada combinación empresa/sucursal/artículo. La razón `Egreso por venta <SaleId sin guiones>` permite unir los egresos genéricos a las ventas confirmadas, pero **no** a una pieza.
- Todas las consultas se ejecutaron con `psql -X -v ON_ERROR_STOP=1` contra la única base autorizada, dentro de `BEGIN TRANSACTION READ ONLY; ... COMMIT;`. El contenedor y su montaje se verificaron antes de consultar.
