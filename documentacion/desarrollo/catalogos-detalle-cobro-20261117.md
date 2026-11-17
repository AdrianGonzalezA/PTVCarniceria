# Catálogos de detalle del cobro · 17/11/2026

## Alcance

El cajero conserva los campos de texto de débito, crédito, transferencia y cheque. Cuatro de ellos muestran sugerencias seleccionables: marca de tarjeta, procesador, entidad de transferencia y banco del cheque. Se puede ingresar un valor ausente del catálogo y se muestran sin cambios los nombres ya guardados en cobros anteriores. Los campos propios de cada transacción (terminal, lote, cupón, autorización, referencia, fechas, número de cheque, sucursal y titular) no son catálogos.

La aplicación mantiene cuatro entidades de configuración independientes en PostgreSQL, esquema `payment_config`:

| Tabla | Contenido inicial | Uso en POS |
|---|---:|---|
| `card_brands` | 11 marcas | Filtradas por `SupportsDebit` y `SupportsCredit` |
| `payment_processors` | 6 procesadores | Débito y crédito |
| `financial_institutions` | 60 entidades bancarias | Transferencia y cheque |
| `digital_wallets` | 5 billeteras | Transferencia |

Cada fila tiene código estable, nombre e indicador de actividad. La API de solo lectura `GET /api/catalog/payment-details` requiere contexto operativo y entrega las cuatro colecciones. El POS solicita los catálogos al abrir el cierre de venta. Si la consulta falla, conserva cualquier lista cargada anteriormente y permite escribir el nombre. Se siguen persistiendo los nombres en el contrato `PaymentInstrumentDetails` existente, por lo que no se reescriben pagos históricos. Los mismos datos se ofrecen al reemplazar un pago rechazado.

Todavía no hay ABM. Las tablas y los códigos estables permiten incorporarlo después; habrá que definir si la edición corresponde al SuperAdmin global o admite opciones propias de cada empresa. Las sugerencias iniciales no indican qué redes tiene contratado cada comercio.

## Fuentes de las semillas

- Marcas de débito y crédito: [medios disponibles en Argentina, Mercado Pago Developers](https://www.mercadopago.com.ar/developers/es/docs/automatic-payments-orders/resources/available-payment-methods).
- Códigos y nombres bancarios: [nómina de entidades del BCRA](https://www.bcra.gob.ar/sistema-financiero-nomina-de-entidades/?bco=AAA00&tipo=1) y su [fuente de datos](https://www.bcra.gob.ar/api/endpoints/nomina-entidades.php?action=nomina_AAA00&lang=es), consultadas el 17/11/2026. Se cargaron los 60 bancos y entidades bancarias de esa nómina; se excluyeron las 13 compañías financieras del mismo resultado para no ofrecerlas como bancos de cheque. Se emplean nombres abreviados para el cajero.
- Procesadores y billeteras: opciones iniciales de uso habitual; contrastar con [Payway](https://www.payway.com.ar/terminales-punto-ventas), [Getnet](https://www.getnet.com.ar/) y [Mercado Pago Point](https://www.mercadopago.com.ar/herramientas-para-vender/lectores-point) al ampliar o administrar el catálogo.

## Migración y verificación local

- Migraciones: `20261117123920_AddPaymentDetailCatalogs` crea el esquema, las cuatro tablas, índices únicos por nombre y las primeras filas; `20261117125504_CompleteArgentineBankCatalog2026` completa la nómina bancaria del BCRA. No alteran tablas comerciales.
- Única base utilizada: `carnicerias_test_visual`, contenedor existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`, volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`.
- Respaldo custom anterior: `.local-backups/i-payment-catalogs-before-20261117-0946.dump`, 557302 bytes, SHA-256 `D270868396AB9D1E377601609B606BE7EF39117CDEED33B8A757EF1DC2078D43`. `pg_restore -l` leyó el archivo; no se ejecutó restauración.
- Se verificó que cada migración era la única pendiente antes de aplicarla mediante `tools/Apply-LocalPosMigrations.ps1`. Las consultas de solo lectura devolvieron 11 marcas, 6 procesadores, 60 bancos y 5 billeteras. Los 60 códigos bancarios coinciden exactamente con los 60 códigos de la fuente oficial consultada; no hay faltantes ni extras.
- Verificación de código: 297 pruebas Angular, 108 pruebas de arquitectura, lint, build Web y `dotnet ef migrations has-pending-model-changes` aprobados. La API `5197` reiniciada respondió `healthy`; el endpoint sin sesión respondió `401`.

La observación accesible de Caja A en Electron confirmó que el borrador previo continuó guardado después de recargar. La captura de pantalla de Windows agotó el tiempo de espera y los clics por accesibilidad no tuvieron geometría. El PO revisó el desplegable en Electron y confirmó que está correcto el 17/11/2026.
