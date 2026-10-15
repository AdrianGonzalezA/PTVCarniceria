# Especificación: importación de existencias por artículo

## Alcance aprobado

El 14/10/2026 se definió que cada cantidad importada **suma o resta al stock actual** y que esta primera versión trabaja **sólo por artículo**. Se ejecuta en la empresa y sucursal de la sesión administrativa; el archivo no puede elegir otra sucursal. No recibe piezas trazables ni reemplaza la recepción de piezas del frigorífico.

## Plantilla y reglas

En **Negocio → Existencias**, `Descargar plantilla` entrega un `.xlsx` con una hoja de carga vacía `Existencias`, una hoja `Ejemplos` no importable y una marca de versión. Las columnas son `CodigoArticulo` (código principal del catálogo, texto), `Variacion` (número Excel positivo o negativo) y `Descripcion` (texto opcional). Una variación positiva agrega stock; una negativa lo descuenta. El código conserva ceros iniciales. No se aceptan fórmulas, macros ni vínculos externos; límite de 5 MiB y 2.000 filas.

- El artículo debe existir y estar activo en la empresa. Un código principal repetido en el archivo bloquea la carga completa. Los códigos alternativos no identifican filas en esta versión.
- `Variacion` debe ser distinta de cero, entre -100.000 y 100.000 y tener como máximo tres decimales. Los artículos vendidos por unidad requieren cantidad entera.
- La existencia resultante no puede ser negativa ni quedar por debajo de lo reservado por tickets abiertos. Tampoco puede superar la capacidad numérica del saldo.
- La descripción puede omitirse. El motivo auditable queda como `Importación Excel` o `Importación Excel: <descripción>`; el motivo completo no puede superar 240 caracteres.
- Cada fila válida crea un movimiento de ajuste. No crea ni modifica piezas trazables, precios, ventas ni datos históricos.

## Confirmación

`Previsualizar` consulta saldos actuales sin modificar datos y muestra el saldo anterior y el resultante de cada fila. `Confirmar importación` vuelve a validar dentro de una transacción serializable: si cualquier fila falla, no se aplica ninguna. Un conflicto concurrente pide volver a previsualizar. Una clave `Idempotency-Key` estable por archivo evita duplicaciones en reintentos; reutilizarla con otro archivo o desde otra sucursal devuelve conflicto. Al confirmar, la grilla de Existencias se actualiza.

`GET /api/admin/imports/stock/template`, `POST /api/admin/imports/stock/preview` y `POST /api/admin/imports/stock/apply` reutilizan el contrato de importación Excel existente, pero requieren `inventory.stock.manage`. El servidor toma empresa, sucursal y usuario de la sesión, valida el origen de los `POST` y nunca confía en identificadores enviados dentro del Excel.
