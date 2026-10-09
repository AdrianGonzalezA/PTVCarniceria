# Estado de la prueba ARCA en homologación

Fecha: 9/10/2026. Se verificó localmente que el PFX entregado coincide con el certificado público, contiene clave privada y está vigente. Los archivos permanecen en `Documentos/Arca/`, excluida de Git.

La primera ejecución obtuvo un ticket real de WSAA para `wsfe`. La consulta posterior de puntos de venta no pudo clasificarse con el diagnóstico disponible en esa ejecución; **no se comprobó aún la respuesta de WSFE**. Los intentos siguientes recibieron el fallo WSAA `ns1:coe.alreadyAuthenticated`, coherente con un ticket todavía vigente. Se detuvieron los reintentos. No se solicitó CAE ni se emitió comprobante, y no se modificó la base de datos.

El cliente ahora identifica de forma segura el código SOAP de WSAA y los códigos numéricos de error de WSFE sin exponer sus mensajes ni credenciales. Próximo paso: repetir una sola ejecución de `Carnicerias.ArcaProbe` después del vencimiento del ticket anterior, registrar solo el estado y los códigos, y confirmar los puntos de venta habilitados. No repetir en bucle; para uso continuo implementar caché segura del ticket.

## Verificación posterior (9/10/2026)

Se ejecutó una sola consulta de diagnóstico con el PFX local y la CUIT representada, sin registrar secretos. WSAA autenticó correctamente y `FEParamGetPtosVenta` de WSFEv1 respondió con el **código 602**. El manual oficial lo define como «No existen datos en nuestros registros»; por sí solo no prueba cuál dato falta. Es necesario verificar en la administración de ARCA/WSASS que la CUIT tenga un punto de venta de Web Services habilitado para CAE en homologación y confirmar su número. No se llamó a `FECAESolicitar`, no hay CAE y no se imprimió ni generó una factura fiscal. [Manual WSFEv1, errores y puntos de venta](https://www.arca.gob.ar/fe/ayuda/documentos/wsfev1-RG-4291.pdf).

La única venta local que solicita factura electrónica tiene un renglón sin regla tributaria congelada; por tanto **no es apta para emisión**. Antes de solicitar CAE también faltan la condición IVA, razón social y domicilio fiscal validados del emisor, además del punto de venta. No inferirlos del nombre del PFX ni inventar una alícuota. El ticket enviado a COM1 solo podrá rotularse fiscal cuando represente un comprobante electrónico autorizado; su representación gráfica debe incluir CAE y QR. [RG 4291, art. 15](https://biblioteca.arca.gob.ar/search/query/norma.aspx?p=t%3ARAG%7Cn%3A4291%7Co%3A3%7Ca%3A2018%7Cf%3A02%2F08%2F2018), [QR de factura electrónica](https://www.arca.gob.ar/fe/qr/conceptos-generales.asp).
