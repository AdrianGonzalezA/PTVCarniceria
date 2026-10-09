# ARCA: homologación WSFEv1

Estado al 9/10/2026. Esta integración solo apunta a homologación y usa el PFX local de `Documentos/Arca/`, excluido de Git. El certificado tiene clave privada y está vigente. La CUIT representada es 30710106513. El usuario indicó el punto de venta **99**, compartido con otro equipo.

## Numeración y resultados observados

Una consulta real a `FECompUltimoAutorizado` devolvió A: 12 y B: 2 antes de estas pruebas. No se guardan esos valores como secuencia local: el API vuelve a consultar el último autorizado antes de **cada** solicitud de CAE y usa `último + 1`. Un rechazo explícito por numeración (10016) provoca una nueva consulta y un único reintento. Cada intento se registra antes de llamar a `FECAESolicitar`, con unicidad local por CUIT, puesto, tipo y número, en la única base `carnicerias_test_visual`.

Se probaron dos ventas ficticias de Gaseosa cola 1,5 L. Para ellas se cargó explícitamente una regla tributaria de prueba del 21 % en el catálogo; el sistema no asigna IVA por defecto a productos sin regla. ARCA homologación autorizó:

| Venta | Comprobante | CAE | Vencimiento |
|---|---|---|---|
| `70b23df6-5a72-4a16-a88a-b22ce45a849d` | Factura B 00099-00000003 | 86410975393203 | 19/10/2026 |
| `074a24ea-deeb-4a41-9d58-fcf3fdf84e0b` | Ticket fiscal de prueba B 00099-00000004 | 86410975401751 | 19/10/2026 |

Los intentos, números y CAE persisten en `pos_sales.fiscal_documents`. La segunda venta se envió a COM1/9600; el controlador virtual informó escritura, pero no confirmó vaciado, de modo que la recepción debe observarse en PuTTY. El PDF de la primera venta se regeneró después de reiniciar Electron y se inspeccionó visualmente: muestra CUIT, numeración, CAE y vencimiento. La primera generación, hecha por la instancia antigua de Electron, fue **no fiscal** y no debe utilizarse.

## Seguridad y límites de la prueba

La venta, pagos y stock se confirman independientemente de ARCA. Si la respuesta de `FECAESolicitar` es incierta, el intento queda en `NeedsReconciliation`; nunca se reenvía a ciegas. `FECompConsultar` permite comprobar el número, pero en un puesto compartido aun un comprobante con datos aparentemente iguales podría pertenecer al otro equipo. Por eso el sistema **no adopta automáticamente** un CAE obtenido solo por esa consulta. El caso requiere conciliación manual. Si la consulta demuestra que el número corresponde a otros datos, se registra la colisión y se puede volver a consultar el último autorizado para un nuevo intento.

El nombre y domicilio del emisor se configuraron como datos ficticios y deben reemplazarse con datos fiscales verificados antes de producción. PDF y COM1 están rotulados «HOMOLOGACIÓN / SIN VALIDEZ FISCAL». El QR sigue pendiente por decisión expresa para esta prueba controlada; será obligatorio antes de representar una factura válida. El «ticket fiscal de prueba» utiliza CAE de WSFE, **no** equivale a la salida de un controlador fiscal homologado. La tributación de prueba tampoco define la alícuota real de la gaseosa ni de otros artículos.

## Configuración local

Variables de entorno del API, nunca en Git: `ARCA_HOMO_PFX_PATH`, `ARCA_HOMO_CUIT`, `ARCA_HOMO_COMPANY_ID`, `ARCA_HOMO_POINT_OF_SALE=99`, `ARCA_HOMO_ISSUER_NAME`, `ARCA_HOMO_ISSUER_ADDRESS` y, solo si el PFX la requiere, `ARCA_HOMO_PFX_PASSWORD`. El ticket WSAA se conserva en memoria hasta cerca de su vencimiento. Reiniciar el API durante su vigencia puede causar `coe.alreadyAuthenticated`; no reintentar WSAA en bucle. El API tiene endpoints de homologación fijos, sin ruta de producción.

Fuentes: [manual WSFEv1](https://www.arca.gob.ar/fe/ayuda/documentos/wsfev1-RG-4291.pdf), [tipos de comprobante](https://www.arca.gob.ar/facturacion/regimen-general/comprobantes.asp), [RG 4291, art. 15](https://biblioteca.arca.gob.ar/search/query/norma.aspx?p=t%3ARAG%7Cn%3A4291%7Co%3A3%7Ca%3A2018%7Cf%3A02%2F08%2F2018), [QR de factura electrónica](https://www.arca.gob.ar/fe/qr/conceptos-generales.asp).
