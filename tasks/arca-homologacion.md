# ARCA: homologación WSFEv1

Estado al 9/10/2026. Esta integración solo apunta a homologación y usa el PFX local de `Documentos/Arca/`, excluido de Git. El certificado tiene clave privada y está vigente. La CUIT representada es 30710106513. El usuario indicó el punto de venta **99**, compartido con otro equipo.

## Numeración y resultados observados

Una consulta real a `FECompUltimoAutorizado` devolvió A: 12 y B: 2 antes de estas pruebas. No se guardan esos valores como secuencia local: el API vuelve a consultar el último autorizado antes de **cada** solicitud de CAE y usa `último + 1`. Un rechazo explícito por numeración (10016) provoca una nueva consulta y un único reintento. Cada intento se registra antes de llamar a `FECAESolicitar`, con unicidad local por CUIT, puesto, tipo y número, en la única base `carnicerias_test_visual`.

Se probaron dos ventas ficticias de Gaseosa cola 1,5 L. Para ellas se cargó explícitamente una regla tributaria de prueba del 21 % en el catálogo; el sistema no asigna IVA por defecto a productos sin regla. ARCA homologación autorizó:

| Venta | Comprobante | CAE | Vencimiento |
|---|---|---|---|
| `70b23df6-5a72-4a16-a88a-b22ce45a849d` | Factura B 00099-00000003 | 86410975393203 | 19/10/2026 |
| `074a24ea-deeb-4a41-9d58-fcf3fdf84e0b` | Ticket fiscal de prueba B 00099-00000004 | 86410975401751 | 19/10/2026 |
| `6830dbd0-0f07-45a2-b098-2c22659af4a0` | Factura B 00099-00000005 | 86410975719376 | 19/10/2026 |
| `73c61b32-2fb8-468e-b5cb-548c5ec9c6b7` | Factura B 00099-00000006 | 86410977273862 | 19/10/2026 |

Los intentos, números y CAE persisten en `pos_sales.fiscal_documents`. La segunda venta se envió a COM1/9600; el controlador virtual informó escritura, pero no confirmó vaciado, de modo que la recepción debe observarse en PuTTY. El PDF de la primera venta se regeneró después de reiniciar Electron y se inspeccionó visualmente: muestra CUIT, numeración, CAE y vencimiento. La primera generación, hecha por la instancia antigua de Electron, fue **no fiscal** y no debe utilizarse.

En la tercera prueba, el cajero confirmó en Electron una venta ficticia de $2.500 en efectivo. Se verificó en la única base de desarrollo la venta, el comprobante autorizado B 99-5 y el egreso de una unidad de stock. Se inspeccionó el PDF A4 de una página con QR, CAE y total; Adrián confirmó que PuTTY recibió por COM1 el comprobante con CAE. Esta emisión de homologación tampoco tiene validez fiscal comercial.

Una venta posterior de 10 kg de Asado por $115.000 quedó confirmada sin CAE: el artículo no tenía regla de IVA cuando se registró el renglón, así que el snapshot tributario quedó incompleto y el sistema no invocó ARCA. A pedido del usuario se aplicó una regla **ficticia de prueba** «gravado 21 %» a los 10 artículos de Empresa Visual con `tools/SetVisualProductVat21.sql`, sin crear otra base y conservando el historial. Repetir el script no agregó versiones. La venta anterior no se recalcula ni se factura al reintentar; la regla se utilizará en ventas nuevas. La clasificación tributaria real de cada artículo sigue pendiente de validación antes del despliegue productivo.

El catálogo tributario nuevo vinculó las reglas de IVA existentes a una entrada de la misma tasa sin modificar sus vigencias ni los snapshots de esas ventas. En Configuración se pueden asignar otros gravámenes, pero éstos son **no calculados**: no forman parte del pedido a WSFE, del CAE ni del PDF/COM1 fiscal de prueba. Su cálculo y representación fiscal siguen pendientes de reglas aprobadas; ver `SPEC-tax-catalog.md`.

## Seguridad y límites de la prueba

La venta, pagos y stock se confirman independientemente de ARCA. Si la respuesta de `FECAESolicitar` es incierta, el intento queda en `NeedsReconciliation`; nunca se reenvía a ciegas. `FECompConsultar` permite comprobar el número, pero en un puesto compartido aun un comprobante con datos aparentemente iguales podría pertenecer al otro equipo. Por eso el sistema **no adopta automáticamente** un CAE obtenido solo por esa consulta. El caso requiere conciliación manual. Si la consulta demuestra que el número corresponde a otros datos, se registra la colisión y se puede volver a consultar el último autorizado para un nuevo intento.

El nombre y domicilio del emisor se configuraron como datos ficticios y deben reemplazarse con datos fiscales verificados antes de producción. PDF y COM1 están rotulados «HOMOLOGACIÓN / SIN VALIDEZ FISCAL». El «ticket fiscal de prueba» utiliza CAE de WSFE, **no** equivale a la salida de un controlador fiscal homologado. La tributación de prueba tampoco define la alícuota real de la gaseosa ni de otros artículos.

## Factura PDF A4 y QR

La factura electrónica autorizada se guarda como A4 desde Electron, con la composición visual de las facturas A/B del modelo local `Documentos/Arca/Modelos_Comprobantes_PDV_Carniceria_ARCA.pdf`. El ticket no fiscal y el «ticket fiscal de prueba» conservan la salida térmica; este último **no** recibe QR de factura electrónica. A corresponde a receptor responsable inscripto o monotributista y B a consumidor final o exento, según la selección confirmada antes del cobro. No se habilitan C, notas ni otros tipos solo por aparecer en el PDF de referencia.

El QR de la factura A/B contiene la URL y el JSON Base64 de la [especificación oficial ARCA](https://www.arca.gob.ar/fe/qr/documentos/QRespecificaciones.pdf): fecha, CUIT, punto de venta, tipo y número, total, PES/1, documento del receptor cuando corresponde y CAE con código de autorización `E`. Se genera **solo** a partir de la autorización persistida; no se inventa CAE ni se imprime QR para ventas pendientes o rechazadas. Es QR de **homologación**: su estructura es reglamentaria, pero no convierte el comprobante en factura válida de producción. La verificación con cámara y el flujo completo de emisión/descarga se harán con Adrián.

La factura A muestra importes netos e IVA separado; B muestra importes finales y el IVA contenido sin sumarlo otra vez. A monotributista incorpora la leyenda de la [guía de ARCA para RI](https://www.arca.gob.ar/facturacion/regimen-general/comprobantes.asp) y del modelo aportado. Para la A, el PDF exige los importes tributarios persistidos de cada línea, además del desglose global; si faltan o no reconcilian, no genera un PDF potencialmente erróneo. La respuesta de venta agrega `netAfterDiscount`, `taxableBase` y `taxAmount` por renglón sin cambiar los campos existentes. El emisor puede informar opcionalmente IIBB e inicio de actividades; si faltan, el PDF muestra `NR`, nunca los valores ficticios del modelo. También queda pendiente validar razón social, domicilio, IIBB, inicio de actividades y alícuotas reales antes de producción.

En el POS, «Tipo de comprobante» y «Tipo de cliente» se eligen en la venta, antes de cobrar. La elección se conserva por caja, turno, cajero y ticket A–D en el almacenamiento local de Electron; al cambiar de ticket o reabrir el POS se recupera. Al cancelar o confirmar esa venta vuelve al valor predeterminado. La ventana de cobro presenta la elección sin duplicar los selectores y solicita nombre, documento y domicilio solo para comprobantes fiscales, con obligatoriedad según tipo de cliente e importe. Los datos personales no se guardan en ese almacenamiento local: se completan al cierre. La elección sigue siendo un dato local de interfaz hasta confirmar; no se agregó al borrador PostgreSQL.

## Configuración local

Desde el 9/10/2026, **Configuración → ARCA** permite al administrador de la empresa editar CUIT, punto de venta, razón social, domicilio, IIBB e inicio de actividades, cargar/renovar el PFX y ver sujeto, huella y vencimiento. La carga validada quedó en la única base `carnicerias_test_visual`; en Electron se comprobó que el PFX de homologación vence el **29/09/2028**. El archivo y la contraseña se protegen antes de persistirse; la API no los devuelve. La clave de Data Protection del perfil Windows debe conservarse para poder descifrarlos. Al desplegar en otro servidor hay que configurar y respaldar un almacén persistente y protegido de claves; no basta con restaurar PostgreSQL. Ver `SPEC-admin-arca-settings.md`.

Los nuevos intentos fiscales guardan copia de los datos del emisor para que una reimpresión posterior no use valores modificados. Los comprobantes anteriores a esta migración no tenían ese snapshot y pueden mostrar los datos del emisor actualmente configurados; CUIT, punto de venta, número, CAE y vencimiento autorizados sí permanecen en su registro original. No se recalculan ni se reemiten automáticamente. La prueba completa de un CAE nuevo usando el PFX almacenado queda pendiente de una próxima venta de homologación; la carga, persistencia, lectura de vencimiento y pruebas unitarias ya se verificaron.

Variables de entorno del API, nunca en Git: `ARCA_HOMO_PFX_PATH`, `ARCA_HOMO_CUIT`, `ARCA_HOMO_COMPANY_ID`, `ARCA_HOMO_POINT_OF_SALE=99`, `ARCA_HOMO_ISSUER_NAME`, `ARCA_HOMO_ISSUER_ADDRESS` y, solo si el PFX la requiere, `ARCA_HOMO_PFX_PASSWORD`. Para completar el encabezado se admiten `ARCA_HOMO_ISSUER_IIBB` y `ARCA_HOMO_ISSUER_ACTIVITY_START_DATE` (ISO `AAAA-MM-DD`). El ticket WSAA se conserva en memoria hasta cerca de su vencimiento. Reiniciar el API durante su vigencia puede causar `coe.alreadyAuthenticated`; no reintentar WSAA en bucle. El API tiene endpoints de homologación fijos, sin ruta de producción.

Fuentes: [manual WSFEv1](https://www.arca.gob.ar/fe/ayuda/documentos/wsfev1-RG-4291.pdf), [tipos de comprobante](https://www.arca.gob.ar/facturacion/regimen-general/comprobantes.asp), [RG 4291, art. 15](https://biblioteca.arca.gob.ar/search/query/norma.aspx?p=t%3ARAG%7Cn%3A4291%7Co%3A3%7Ca%3A2018%7Cf%3A02%2F08%2F2018), [QR de factura electrónica](https://www.arca.gob.ar/fe/qr/conceptos-generales.asp).
