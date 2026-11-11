# Cobro por instrumento y consulta de padrón ARCA · 11/11/2026

## Cambios aplicados en el laboratorio

- El cierre de venta conserva los pasos Cliente → Comprobante → Cobro y permite retroceder antes de registrar pagos. En Cobro, Cuenta corriente aparece como medio combinable y solicita cliente, importe y referencia opcional.
- Cada operación manual de débito, crédito, transferencia y cheque conserva sus datos propios. No se registran números completos de tarjeta ni códigos de seguridad. Efectivo solo solicita importe recibido.
- La consulta de CUIT sin ficha fiscal local completa usa WSAA `ws_sr_constancia_inscripcion` y el padrón de ARCA homologación. Se eliminó la respuesta simulada y la opción de arranque que la activaba. Si ARCA no devuelve datos utilizables, el cajero puede completar la ficha manualmente; no se inventa una condición fiscal.

## Base y respaldo

Se utilizó exclusivamente `carnicerias_test_visual` del contenedor existente `carnicerias-pos-test-20261007` (`127.0.0.1:55433`). Antes de la migración había 75 migraciones aplicadas. El respaldo custom previo quedó en `.local-backups/payment-details-before-20261111-153610Z.dump` (548.768 bytes, 788 entradas en el TOC, SHA-256 `2b1b510540d7eb208dcd9753ea9b363b6ffcc2145a28f12dd6522dc96d43346f`). El manifiesto local tiene el mismo nombre con sufijo `.dump.json`.

Se aplicó únicamente `20261111152357_AddSalePaymentInstrumentDetails`: añade la columna nullable `payments_cash.sale_payment_instruments."DetailsJson"` (`varchar(2000)`). No modifica operaciones históricas. Después quedaron 76 migraciones aplicadas. La reversión de esquema exige que no existan detalles grabados; la migración se detiene antes de eliminarlos. No se ejecutó restauración.

## Verificaciones

- Pruebas Angular: 54 archivos, 286 casos aprobados. Lint y build aprobados; permanecen advertencias previas de presupuesto CSS y módulos CommonJS.
- Pruebas .NET focalizadas: 24 casos aprobados, incluidos contrato de padrón, WSAA, persistencia de cobros y modelo EF.
- Prueba HTTP controlada de checkout: aprobada; la fixture se eliminó. El fingerprint de inventario antes y después fue `49|384350.952|2.000|103.952|0.000|103.952`.
- Consulta autenticada de lectura a la API actual en `5197`: un CUIT de ensayo devolvió `source=arca`, condición `registered` y ficha completa. Otro CUIT válido sin ficha utilizable devolvió `source=manual`; no apareció información simulada.
- Caja A de La Rústica en Electron: se comprobó la consulta real, los tres pasos, Cuenta corriente, campos de transferencia y cheque, desplazamiento del diálogo y retroceso de Cobro a Comprobante. No se confirmó ninguna venta durante esta revisión.
- APIs `5197` y `5249` respondieron `200` en `/api/health`. El binario publicado en `5249` coincide por SHA-256 con la compilación Release local.

La verificación funcional con tarjetas, cheques y cuenta corriente reales queda para la recorrida del PO. La consulta de padrón depende de que el certificado de cada empresa tenga permiso para `ws_sr_constancia_inscripcion` y de la disponibilidad de ARCA homologación.
