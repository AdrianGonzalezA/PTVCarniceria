# Estado de la prueba ARCA en homologación

Fecha: 9/10/2026. Se verificó localmente que el PFX entregado coincide con el certificado público, contiene clave privada y está vigente. Los archivos permanecen en `Documentos/Arca/`, excluida de Git.

La primera ejecución obtuvo un ticket real de WSAA para `wsfe`. La consulta posterior de puntos de venta no pudo clasificarse con el diagnóstico disponible en esa ejecución; **no se comprobó aún la respuesta de WSFE**. Los intentos siguientes recibieron el fallo WSAA `ns1:coe.alreadyAuthenticated`, coherente con un ticket todavía vigente. Se detuvieron los reintentos. No se solicitó CAE ni se emitió comprobante, y no se modificó la base de datos.

El cliente ahora identifica de forma segura el código SOAP de WSAA y los códigos numéricos de error de WSFE sin exponer sus mensajes ni credenciales. Próximo paso: repetir una sola ejecución de `Carnicerias.ArcaProbe` después del vencimiento del ticket anterior, registrar solo el estado y los códigos, y confirmar los puntos de venta habilitados. No repetir en bucle; para uso continuo implementar caché segura del ticket.
