# Balanzas: perfil Kretz Report NX de laboratorio

## Alcance implementado

- Un artículo puede tener un `ScalePlu` numérico único por empresa, conservando ceros iniciales. Se edita en Artículos y se incluye en la plantilla de importación Excel (`PluBalanza`).
- La caja con balanza serial asignada exige una lectura estable: el cajero no puede escribir ni modificar el peso desde el POS. Si no se puede verificar la configuración del dispositivo, el campo queda bloqueado.
- El simulador Electron genera un QR consolidado con varios cortes, cada uno con su EAN13 y peso. También muestra el texto decodificado para copiarlo al lector del POS. Se inicia con `npm run devices:start --prefix src/Carnicerias.Pos`.
- El POS acepta el QR pegado en el campo de búsqueda, un lector que envía el encabezado y cada EAN13 separados por Enter, o una cadena con `;` como separador. También acepta un EAN13 individual. Busca el PLU exacto en la lista de precios vigente, verifica stock y agrega un renglón independiente por corte. El borrador y la venta conservan la referencia de la etiqueta; su peso no se puede editar.

## Perfil de prueba

El perfil EAN13 implementado es **`20` + PLU de 5 dígitos + peso en gramos de 5 dígitos + dígito de control**. Por ejemplo, `2000123007502` identifica PLU `00123` y `0,750 kg`. El QR consolidado usa el encabezado estándar `AAAAAAAAHHHHHTTTTVVGGQQ` y una etiqueta EAN13 por renglón. El simulador fija el vendedor en ` A` y permite editar número de balanza y ticket.

Este perfil requiere configurar la Report NX para codificar **peso**. El ejemplo publicado en el manual Kretz codifica **importe**; no se debe interpretarlo como kilos. La balanza permite configurar el contenido del EAN13. Antes de conectar un equipo real se debe comprobar su patrón de código, separador, PLU y unidades con etiquetas impresas por ese equipo.

Fuentes del fabricante: [Funciones y configuración Report NX/LT, páginas 94–95](https://www.kretz.com.ar/shop/balanza-report-lt-lite-visor-alto-338/document/33) y [producto Report NX](https://www.kretz.com.ar/shop/balanza-report-nx-342).

## Persistencia y despliegue local

Las migraciones aditivas `20261117161904_AddProductScalePlu` y `20261117170357_AddScaleLabelReferences` se aplicaron exclusivamente a `carnicerias_test_visual` en el contenedor existente `carnicerias-pos-test-20261007`, puerto `55433`. Pasó de 80 a 82 migraciones; no se alteraron ventas históricas. El respaldo previo en formato custom está en `.local-backups/scale-before-20261117-1727.dump` (564.088 bytes, SHA-256 `F1EB27049B0707265D9392E0D28F4C7D21B16343D77517511D01572F553FBB30`). `pg_restore -l` verificó 790 objetos, incluidos artículos, borradores y ventas.

Para la prueba de La Rústica, el artículo **TAPA DE ASADO** (`72.000`) de Alta Córdoba quedó con PLU `00123`; el inventario previo mostraba `9.999,000 kg` sin reservas. La etiqueta de ejemplo del simulador `2000123007502` representa `0,750 kg` de ese artículo.

Verificación de código: 97 pruebas Angular focalizadas, 103 pruebas Electron y 118 pruebas de arquitectura aprobadas; plantilla Excel 15 pruebas, lint, compilación API y Web. Se republicó la API de Alta Córdoba en `5249`, se reinició la API local en `5197` y ambas respondieron `healthy`; el laboratorio administrado pasó `Verify`. El árbol de accesibilidad de Caja A Electron mostró el POS operativo y su borrador A intacto. La automatización de ventanas de Windows no permitió clicar ni capturar la imagen del simulador; el aspecto final del QR y el recorrido completo de escaneo en dos cajas quedan pendientes de homologación manual.

Para recuperar ese respaldo en una contingencia se debe detener la aplicación y acordar antes un procedimiento de restauración sobre **la misma base autorizada**. No ejecutar `pg_restore` sobre la base en uso ni crear otra base sin autorización.

## Límites y decisiones pendientes

- La lectura QR por teclado no prueba criptográficamente que el peso provenga de una balanza física. El bloqueo de escritura en el POS y la validación de PLU, kilos y control EAN13 en la API reducen errores de operatoria, pero el control antifraude fuerte requiere un canal confiable del dispositivo o conciliación externa.
- Falta definir si el mismo ticket Kretz debe bloquearse al intentar usarlo en otra caja y cómo actuar si falla la balanza conectada. Hasta resolverlo, la aplicación impide repetir una misma etiqueta dentro de un ticket, pero no garantiza exclusividad entre cajas.
- Falta definir si una caja que opera exclusivamente con QR debe prohibir la carga manual de peso. El bloqueo automático implementado hoy corresponde a la balanza serial asignada.
- La lectura Wi-Fi directa de Report NX, el perfil Systel y los perfiles reales parametrizables requieren muestras del dispositivo y su configuración. La suma de artículos de Systel documentada no permite inferir los pesos individuales de cada renglón sin un protocolo adicional.
- La comparación de kilos vendidos por turno contra la balanza queda para el reporte de conciliación: el sistema ya conserva las cantidades y la referencia de origen en los renglones, pero todavía no dispone del total independiente emitido por la balanza.
