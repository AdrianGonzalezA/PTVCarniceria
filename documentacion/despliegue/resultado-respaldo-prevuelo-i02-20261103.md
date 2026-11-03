# I02 — Respaldo y último prevuelo antes de la migración

**Actualización posterior:** I02 se aplicó después de este prevuelo. Ver [resultado-migracion-i02-20261103.md](resultado-migracion-i02-20261103.md). Los datos siguientes son la referencia anterior a la migración.

**Ejecución:** 03/11/2026, hora de Argentina. **Alcance autorizado:** respaldo custom y consultas de solo lectura sobre `carnicerias_test_visual`. **Estado:** respaldo íntegro y prevuelo favorable; I02 continúa sin aplicar.

## Identidad y aislamiento

- Contenedor existente `carnicerias-pos-test-20261007`, ID `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, creado el 07/10/2026 y en ejecución. Antes y después del respaldo se constató el mismo volumen Docker `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d` en `/var/lib/postgresql/data` y la publicación `127.0.0.1:55433 → 5432`.
- `psql` confirmó `current_database() = carnicerias_test_visual`, usuario `postgres` y PostgreSQL 16.10. No se utilizó el servidor de `55432`.
- Antes del respaldo no había API Carnicerías escuchando en los puertos locales conocidos ni sesiones de aplicación conectadas a esta base. No hubo un escritor que detener. Se conservaron los otros procesos y servicios; no se ejecutó `pg_terminate_backend`. Al terminar el prevuelo, `pg_stat_activity` volvió a mostrar **cero conexiones ajenas** a la consulta de control. Si se inicia una API o caja antes de la migración, será necesario detener sus escritores y repetir la comprobación.

## Respaldo ejecutado

| Dato | Resultado |
| --- | --- |
| Comando | `pg_dump -U postgres -d carnicerias_test_visual -Fc` en el contenedor autorizado |
| Archivo local | `C:\Proyectos\Carnicerias\.local-backups\i02-before-20261103-140552-7d9413d0.dump` |
| Archivo original en contenedor | `/tmp/i02-before-20261103-140552-7d9413d0.dump` |
| Fecha de creación local | 03/11/2026 14:05:52, UTC−03:00 |
| Tamaño local | 394.910 bytes |
| SHA-256 | `1223B32EFAEFD467FAC356C65447892B3EAC3B56781BF87CD01FFDD4FA156C30` |

`pg_restore --list` identificó un archivo **CUSTOM** de `carnicerias_test_visual`, PostgreSQL 16.10, con 501 entradas de catálogo. Se comprobaron, entre otros, los datos de `inventory.pieces`, `inventory.branch_inventory`, `inventory.inventory_movements`, `pos_sales.confirmed_sales`, `pos_sales.confirmed_sale_lines`, `payments_cash.point_payment_intents` y las tablas de historial EF. `pg_restore --file=/dev/null` leyó el archivo completo con código de salida cero, sin conectarse a la base de destino. El SHA-256 de la copia local coincide con el del archivo original del contenedor. `.local-backups/` está excluida de Git. **No se hizo una restauración de ensayo**, ya que requiere escritura y una autorización aparte.

## Prevuelo de solo lectura posterior al respaldo

Se ejecutó completo [20261102_i02_preflight_readonly.sql](../../tools/sql/20261102_i02_preflight_readonly.sql) mediante `psql -X -v ON_ERROR_STOP=1 -f -`, dentro de `BEGIN TRANSACTION READ ONLY`; terminó en `COMMIT` sin errores. Se contrastó además el historial real contra los archivos locales de migración: **56 aplicadas de 57 locales**, todas las 56 anteriores coinciden; la única diferencia es `20261102181956_ReserveTraceablePieces`, todavía pendiente. El último `MigrationId` aplicado es `20261030151155_AddSalePieceCounts`.

Persisten las siete tablas previas necesarias y sus índices, claves y restricciones comprobados en el [prevuelo anterior](preflight-i02-20261102.md). La tabla `inventory.piece_state_events` y las columnas nuevas de I02 todavía no existen, como corresponde. `gen_random_uuid()` está disponible. Se contaron cuatro piezas, dos borradores activos, una línea trazable en borrador y una en venta confirmada.

| Control de datos | Resultado |
| --- | ---: |
| Reservas simultáneas de una pieza en borradores activos | 0 |
| Pieza vendida presente en borrador activo | 0 |
| Pieza de borrador con empresa, sucursal, artículo o peso divergente | 0 |
| Pieza de venta confirmada con empresa, sucursal, artículo o peso divergente | 0 |
| Reserva agregada distinta de los borradores activos | 0 |
| Balances previos negativos o con reserva superior a existencia | 0 |
| Piezas históricas sin destino individual verificable | **3** |

La última cifra es una **clasificación conservadora**, no una asignación de ventas. La migración corregida las clasifica como `HistoricalUnverified`; no elimina la protección contra doble venta. Las cuatro guardas fatales previas y la comprobación de reserva agregada dan cero. El chequeo de capacidad de cuarentena devuelve `READY` para ambos artículos. Esto demuestra compatibilidad de las guardas con los datos leídos; la migración no se ejecutó ni se ensayó escribiendo en PostgreSQL.

## Piezas ambiguas y saldo proyectado

| Pieza | Empresa / sucursal | Artículo | Peso recibido | Venta vinculada | Reserva activa | Renglones de venta genéricos posteriores |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| `999002` | Empresa Visual / Sucursal Visual | `1002` Asado | 0,750 kg | 0 | 0 | 3 |
| `250661` | Carniceria Rustica SRL / Alta Cordoba | `71.000` AGUJA | 51,601 kg | 0 | 0 | 1 |
| `071000` | Carniceria Rustica SRL / Alta Cordoba | `71.000` AGUJA | 51,601 kg | 0 | 0 | 1 |

El renglón genérico posterior de AGUJA es **el mismo** para `250661` y `071000`: no se cuenta como dos ventas ni se atribuye a ninguna. La identidad, origen y cronología se documentaron individualmente en el [informe de piezas ambiguas](informe-piezas-ambiguas-i02-20261102.md) y se volvieron a verificar por ID, artículo, sucursal, peso y ausencia de enlaces.

| Empresa / sucursal | Artículo | Saldo contable `OnHand` | Reserva actual | Kilos a inmovilizar | Disponible proyectado tras I02 |
| --- | --- | ---: | ---: | ---: | ---: |
| Empresa Visual / Sucursal Visual | `1002` Asado | 62,000 kg | 0,000 kg | 0,750 kg | **61,250 kg** |
| Carniceria Rustica SRL / Alta Cordoba | `71.000` AGUJA | 10.097,852 kg | 0,000 kg | 103,202 kg | **9.994,650 kg** |

La regla proyectada es `disponible = OnHand − Reserved − Quarantined`. `OnHand` ya incluye recepciones y ventas anteriores y **no cambia** al inmovilizar. `Quarantined` es una restricción sobre ese mismo saldo, no un segundo movimiento negativo; por eso no hay doble descuento. La suma de 103,202 kg de AGUJA corresponde a dos piezas diferentes, cada una de 51,601 kg.

## Recuperación y siguiente puerta

El procedimiento exacto de aplicación, [postcheck](../../tools/sql/20261102_i02_postcheck_readonly.sql) y recuperación en **la misma base** está en [preflight-i02-20261102.md](preflight-i02-20261102.md#aplicación-y-comprobación-posterior--no-autorizadas-todavía). El `Down` de I02 no es una reversión segura; el runbook exige mantener escritores detenidos, verificar hash y requerir autorización específica antes de `pg_restore`. Una restauración posterior a ventas nuevas podría perder esas operaciones. No se ejecutó restauración, migración, conciliación ni prueba con escritura.

**Conclusión:** el respaldo está verificado y los datos actuales satisfacen el prevuelo de I02. Para un corte futuro se debe conservar este archivo, volver a confirmar identidad y ausencia de escritores, y repetir el prevuelo inmediatamente antes de aplicar. **Se detiene aquí, a la espera de autorización expresa para la migración I02.**
