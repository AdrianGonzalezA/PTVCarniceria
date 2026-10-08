# Especificación: inventario de piezas trazables

## Estado

**Aprobada para comenzar el desarrollo el 8 de octubre de 2026.** Corresponde al módulo `inventory-traceability` de `CAPABILITY-MAP-pos-peripherals.md`. Ya se recibió un ejemplo de registros del ERP cárnico; siguen pendientes la estructura final de la etiqueta y la regla de relectura manual.

## Objetivo

Conservar cada pieza recibida del frigorífico/abastecedor como una unidad identificable, con su producto de catálogo, peso real, sucursal, procedencia y ciclo de vida. Permitir recepción por importación/lote o lectura manual; ambos caminos alimentan el mismo inventario. El total disponible por producto y sucursal sigue siendo consultable, pero no sustituye la identidad de cada pieza.

## Reglas confirmadas

- En el ejemplo de ciclo 2, el código aporta `PRO_IDENTIF` (identificador externo único de pieza en el ERP emisor) y los kilos. Su estructura debe configurarse por implementación; no se fijan posiciones ni prefijos definitivos antes de ver etiquetas reales. La correspondencia con el producto de catálogo se resolverá con los datos del ERP.
- Un perfil de lectura se expresará como una secuencia ordenada de campos con longitud fija, por ejemplo `pro_numero(5) pro_item(3) peso(4)`. El perfil define por separado cuántos decimales tiene `peso`: cuatro caracteres no significan por sí solos una cantidad de kilos. Los nombres y longitudes describen la etiqueta; el vínculo de esos campos con artículo, pieza y origen se configura según el sistema de origen. No se instalará el ejemplo como formato real predeterminado.
- **Ejemplo provisional de etiqueta (8 de octubre de 2026):** `25066151600` puede interpretarse con `pro_identif(6) peso(5)` y 3 decimales para el peso: `PRO_IDENTIF = 250661` y `51600` equivale a **51,600 kg** (51 kg y 600 g). Coincide con la primera fila del ERP que sigue. Es un caso de prueba, no un perfil definitivo ni activado.
- **Perfil EAN-13 inicial para pruebas:** en Administración → Perfiles de lectura se puede cargar y editar `prefijo(1) pro_identif(6) peso(5) control_ean13(1)`, con 3 decimales de peso. El código de muestra `2250661516008` separa prefijo `2`, pieza `250661`, peso `51600` = 51,600 kg y control `8`. `control_ean13(1)` es un campo reservado: debe ocupar el último carácter de una fórmula de 13 dígitos y valida el dígito verificador según [GS1](https://www.gs1.org/services/how-calculate-check-digit-manually). El prefijo `2` es sólo un dato de prueba; no se afirma que esta distribución interna esté asignada o aprobada para uso comercial. Se podrá guardar una nueva revisión del perfil al cambiar la etiqueta real, sin fijar las posiciones en código. El ejemplo de 11 caracteres permanece como evidencia de la información recibida, no como EAN-13.
- El usuario confirmó que `PRO_IDENTIF` es único en su ERP de frigorífico/abastecedor. En estos registros distingue cada pieza: no es una clave de catálogo compartida por todas las piezas del mismo producto. Conservarlo como identificador externo de pieza junto con la identidad del sistema emisor; la clave interna del POS sigue siendo independiente. No se ha confirmado que la unicidad abarque otros sistemas emisores.

  | Producto / PRO_NUMERO | Ítem / PRO_ITEM | Identificador / PRO_IDENTIF | Kilos / PRO_KILOS | Artículo | Denominación |
  | ---: | ---: | ---: | ---: | ---: | --- |
  | 7.791.407 | 0 | 250.661 | 51,600 | 2546 | CORTITO C/FALDA EXP |
  | 7.791.406 | 0 | 250.656 | 50,400 | 2546 | CORTITO C/FALDA EXP |
  | 7.791.405 | 0 | 250.655 | 50,600 | 2546 | CORTITO C/FALDA EXP |
  | 7.791.404 | 0 | 250.654 | 49,000 | 2546 | CORTITO C/FALDA EXP |
  | 7.791.403 | 0 | 250.63 | 48,400 | 2546 | CORTITO C/FALDA EXP |

- Las cinco filas son **cinco piezas distintas del mismo artículo 2546**, con un peso conjunto de **250,000 kg**. El POS deberá asociar cada `PRO_IDENTIF` a ese artículo sin fusionar las identidades de pieza; el artículo es la clave de catálogo, no `PRO_IDENTIF`. El último identificador se recibió escrito `250.63`: conservarlo tal cual hasta confirmar su representación sin separadores, sin rellenarlo automáticamente con un cero.
- El ejemplo de código leído sólo evidencia `PRO_IDENTIF` y kilos. Falta confirmar si artículo `2546`, `PRO_NUMERO` y `PRO_ITEM` vienen también en alguna etiqueta o únicamente en el registro/importación del ERP. En la recepción manual inicial se selecciona explícitamente el artículo. Como protección provisional, una segunda recepción del mismo origen e identificador se rechaza sin sumar stock; siguen pendientes las excepciones operativas de relectura.
- La fórmula debe poder validarse y probarse con un código escrito manualmente antes de activarse. Una lectura con longitud o contenido incompatible se rechaza sin modificar stock ni ticket. Las versiones anteriores de perfiles deben poder explicar lecturas históricas cuando cambie un formato.
- Dos piezas del mismo artículo pueden pesar exactamente lo mismo. Producto y peso **nunca** son una clave de unicidad ni motivo para omitir una entrada.
- El origen puede venir de los datos de importación/remito o del código, si los ejemplos confirman que está codificado. No se presume que el código incluya frigorífico o abastecedor.
- La importación debe admitir recepción de muchas piezas y reintento seguro de la misma operación. La lectura manual debe admitir una pieza por vez. Una venta posterior descontará la pieza específica y no sólo kilos del saldo agregado.
- La lectura de una pieza para vender no debe crear existencia. El ingreso de stock requiere una acción de recepción autorizada.
- Todo permanece en la única base de desarrollo `carnicerias_test_visual`; no se crearán bases de aplicación adicionales.

## Contrato conceptual de la pieza

Cada pieza tendrá un ID interno estable, empresa, sucursal, producto, cantidad en kilos con hasta tres decimales, estado (`disponible`, `reservada`, `vendida` u otra transición expresamente definida), fecha y fuente de recepción, y referencia de origen cuando exista. Se conservarán el código leído y el identificador externo sólo cuando se reciban; ninguno sustituye automáticamente al ID interno. Los movimientos enlazarán la pieza con la recepción y, cuando se venda, con la venta correspondiente. El stock agregado deberá ser coherente con las piezas y el stock a granel existente, sin convertir retrospectivamente ajustes históricos en piezas ficticias.

## Alcance del primer corte

1. Modelo y migración aditiva para piezas y recepción; ninguna venta ni saldo existente se borra o reinicia.
2. Recepción manual implementada con validación de perfil guardado, artículo por peso, sucursal, kilos y permisos. El ingreso suma stock agregado y guarda pieza y movimiento en una transacción. Reintentar el mismo identificador de operación con idéntico contenido recupera la misma pieza; reutilizarlo con otro contenido se rechaza. Queda pendiente un servicio compartido con la importación por lote, cuyos renglones tendrán claves estables.
3. Consulta de piezas y saldos por producto/sucursal para verificar lo recibido. El POS lee el código de una pieza ya recibida en la sucursal, resuelve su artículo en la lista de precios y prellena los kilos de la etiqueta sin permitir editarlos en ese diálogo. Para vender otro peso se selecciona el artículo y se ingresa el peso manualmente o desde una futura balanza. El cajero confirma el renglón con la validación de stock habitual. **La venta sigue siendo agregada:** todavía no reserva ni marca esa pieza como vendida, por lo que la lectura no constituye trazabilidad de venta.
4. Implementado: analizador de fórmulas y decodificador puro con pruebas de campos, longitudes, escala decimal, control EAN-13 optativo y rechazos; perfiles por empresa guardados como revisiones inmutables y probador administrativo en Electron. La plantilla EAN-13 se carga en el formulario para editarla y probarla; guardar un perfil no activa una etiqueta ni modifica inventario.
5. Implementado: pantalla administrativa de recepción manual en `/admin/pieces`, listado de las últimas entradas y tabla `inventory.pieces` con identidad externa única por empresa/origen, código y perfil usado, peso recibido, operación, actor, sucursal, artículo y movimiento enlazado. **La tabla registra recepciones, no disponibilidad actual de cada pieza**: el POS aún vende contra el saldo agregado sin asignar una pieza concreta.

No incluye todavía importación por lote, reserva/venta de la pieza específica, conexión directa con el sistema de frigorífico, emisión fiscal o balanza. La impresión PDF de prueba no fiscal está implementada aparte; no equivale a una impresora física ni a ARCA.

## Estructura y contratos

- Dominio/persistencia: `src/Carnicerias.Infrastructure` y migraciones EF Core existentes.
- API de recepción y consulta: `src/Carnicerias.Api/Inventory`, bajo el contexto operativo y permisos actuales. La recepción manual valida y normaliza cada lectura antes de registrar la pieza; la importación todavía no está implementada.
- Contrato manual actual: `POST /api/inventory/pieces` recibe `operationId`, `productId`, `barcodeProfileId`, `sourceSystem`, `identifierField` y `code`; `GET /api/inventory/pieces?page=1&pageSize=20` lista por sucursal y `GET /api/inventory/pieces/{id}` consulta una pieza del mismo contexto. Requieren `inventory.stock.manage`. El cliente conserva `operationId` al reintentar un resultado incierto. Códigos de conflicto: `PIECE_ALREADY_RECEIVED` y `OPERATION_ID_REUSED`.
- Lectura en POS: `GET /api/pos/pieces/lookup?code=...` requiere contexto operativo y sólo busca recepciones de la sucursal activa; devuelve artículo, identificador externo y kilos registrados. Responde 404 si no existe y 409 si el mismo código es ambiguo. Primero se busca un código exacto de catálogo: un EAN estático de artículo no inventa peso. Si no existe como artículo, se consulta la pieza recibida y se prellena el peso; no se modifica stock hasta guardar/confirmar el flujo normal de venta.
- Pruebas: convenciones existentes en `tests/Carnicerias.IntegrationTests` y pruebas de reglas sin tocar una segunda base.
- El endpoint de integración máquina a máquina requerirá autenticación propia antes de exponerse al frigorífico; no se reutilizará una credencial de cajero ni se aceptarán empresa/sucursal arbitrarias como identidad confiable.

## Comandos y estilo

```powershell
dotnet build Carnicerias.sln --configuration Release
dotnet test Carnicerias.sln --configuration Release
dotnet format Carnicerias.sln --verify-no-changes
```

Antes de ejecutar pruebas se verificará que no creen, reinicien ni limpien otra base: esa restricción de `AGENTS.md` prevalece sobre el comando general de prueba. Se usarán nombres y errores HTTP uniformes de los endpoints existentes, validación en el borde y cantidades `decimal` de tres posiciones, por ejemplo:

```csharp
if (weightKg <= 0 || decimal.Round(weightKg, 3) != weightKg)
    return Error(StatusCodes.Status400BadRequest, "INVALID_PIECE_WEIGHT");
```

## Criterios de aceptación

- Dos piezas de un mismo producto y peso se registran como dos piezas y suman ambas al stock.
- La fórmula de ejemplo separa `pro_numero`, `pro_item` y `peso` en 5, 3 y 4 caracteres; otra secuencia/longitud válida se interpreta según su propio perfil y la escala decimal configurada.
- Reintentar el mismo lote con las mismas claves no aumenta stock; reutilizar la clave con otro contenido produce conflicto.
- Una sucursal o usuario sin permiso no puede consultar ni registrar piezas ajenas.
- La recepción fallida no deja piezas, movimientos ni saldo parcialmente aplicados.
- Las ventas y saldos actuales permanecen intactos tras la migración; las nuevas piezas pueden consultarse por producto y sucursal.
- Las pruebas de reglas y el build pasan sin crear otra base de aplicación.

## Límites y decisiones pendientes

- **Siempre:** transacción para pieza, movimiento y saldo; auditoría de fuente, actor y operación; conservar datos anteriores; distinguir entrada de stock de lectura para venta.
- **Confirmar antes de resolver:** estructura completa de etiquetas reales; alcance de unicidad entre distintos emisores; si artículo, `PRO_NUMERO` y `PRO_ITEM` viajan en el código o sólo en la importación; representación original del identificador mostrado como `250.63`; comportamiento ante segunda lectura manual del mismo `PRO_IDENTIF`; cómo vincular los campos externos con catálogo, remito y origen.
- **Nunca:** deduplicar por producto + kilos; asumir que el código contiene el proveedor; abrir un endpoint externo sin autenticación específica; emitir como fiscal un ticket de prueba; crear una segunda base de Carnicerías.
