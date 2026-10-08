# Especificación: inventario de piezas trazables

## Estado

**Aprobada para comenzar el desarrollo el 8 de octubre de 2026.** Corresponde al módulo `inventory-traceability` de `CAPABILITY-MAP-pos-peripherals.md`. Los ejemplos reales de ciclo 2 y la regla de relectura manual siguen pendientes.

## Objetivo

Conservar cada pieza recibida del frigorífico/abastecedor como una unidad identificable, con su producto de catálogo, peso real, sucursal, procedencia y ciclo de vida. Permitir recepción por importación/lote o lectura manual; ambos caminos alimentan el mismo inventario. El total disponible por producto y sucursal sigue siendo consultable, pero no sustituye la identidad de cada pieza.

## Reglas confirmadas

- El código de ciclo 2 aporta el identificador de producto y los kilos. Su estructura debe configurarse por implementación; no se fijan posiciones ni prefijos antes de ver etiquetas reales.
- Un perfil de lectura se expresará como una secuencia ordenada de campos con longitud fija, por ejemplo `pro_numero(5) pro_item(3) peso(4)`. El perfil define por separado cuántos decimales tiene `peso`: cuatro caracteres no significan por sí solos una cantidad de kilos. Los nombres y longitudes describen la etiqueta; el vínculo de esos campos con artículo, pieza y origen se configura según el sistema de origen. No se instalará el ejemplo como formato real predeterminado.
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
2. Servicio de recepción compartido para lote y entrada manual, con validación de catálogo, sucursal, kilos y permisos. La importación recibe identificadores de operación y renglón estables para que repetir el mismo lote no duplique piezas. Un identificador de operación reutilizado con otro contenido se rechaza.
3. Consulta de piezas y saldos por producto/sucursal para verificar lo recibido. El POS actual conserva su flujo de venta agregada hasta que `barcode-input` y la reserva/venta de pieza estén completos; no se presentará trazabilidad de venta como terminada antes de esa integración.
4. Implementado: analizador de fórmulas y decodificador puro con pruebas de campos, longitudes, escala decimal y rechazos; perfiles por empresa guardados como revisiones inmutables y probador administrativo en Electron. Guardar un perfil no activa una etiqueta ni modifica inventario.

No incluye todavía asociación del código con artículo/pieza/origen ni recepción o venta trazable, conexión directa con el sistema de frigorífico, emisión fiscal o balanza. La impresión PDF de prueba no fiscal está implementada aparte; no equivale a una impresora física ni a ARCA.

## Estructura y contratos

- Dominio/persistencia: `src/Carnicerias.Infrastructure` y migraciones EF Core existentes.
- API de recepción y consulta: `src/Carnicerias.Api/Inventory`, bajo el contexto operativo y permisos actuales. El servicio normaliza las entradas de importación y lectura manual antes de registrar piezas.
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
- **Confirmar antes de resolver:** estructura de etiquetas reales; existencia o no de correlativo único en ellas; comportamiento ante segunda lectura manual del mismo código; cómo vincular el identificador externo con remito y origen.
- **Nunca:** deduplicar por producto + kilos; asumir que el código contiene el proveedor; abrir un endpoint externo sin autenticación específica; emitir como fiscal un ticket de prueba; crear una segunda base de Carnicerías.
