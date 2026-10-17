# 05 · Administración: Configuración

Ingresá en **Administración → Configuración**. Los maestros se editan en formularios o ventanas de detalle, con búsqueda y paginación según la pantalla. Las altas y cambios se aplican a la empresa del contexto; verificá la sucursal antes de configurar cajas o habilitar listas.

## Tema visual

En **Administración → Configuración → Tema visual**, elegí **Original** o **Nocturno**. El cambio se ve al instante y queda guardado en el perfil local de ese Electron o navegador, incluso al recargar. No modifica ventas, borradores ni datos de la empresa. La caja usa el tema elegido desde **ese mismo perfil de Electron**; cambiarlo desde Chrome o Edge no cambia la caja Electron ni otras computadoras. Si todavía no hay una elección, se usa **Original**.

## Catálogo y precios

- **Categorías:** crear, editar, activar o inactivar familias para ordenar el catálogo del POS.
- **Artículos:** definir código principal, nombre, categoría, **modalidad de venta por peso o por unidad**, unidad de medida y costo estadístico. También se administran códigos alternativos. Los cambios de costo conservan vigencias y **no** cambian automáticamente el precio.
- **Listas de precios:** crear listas, asignarlas a sucursales y fijar el precio vigente por artículo. Los cambios de precio conservan historial; solo una lista habilitada en la sucursal y con precio vigente sirve al cajero.
- **Impuestos por artículo:** crear entradas de IVA u otros gravámenes y asignarlas a uno, varios o todos los artículos existentes. El IVA operativo clasifica artículos gravados/exentos/no alcanzados; los **otros gravámenes están configurados, pero todavía no se calculan ni se cobran**. Aplicar a «todos» no configura automáticamente artículos que se creen después.

El costo es orientativo; la venta toma el precio de la lista. El IVA del 21 % cargado en la empresa visual es un dato ficticio de homologación, no una regla fiscal general.

## Clientes

En **Clientes** se crean/editan cuentas por código y nombre, se activan/inactivan y se habilita o quita expresamente **cuenta corriente**. Tener un cliente registrado no implica autorizar venta a cuenta. Los saldos y cobranzas se consultan en **Negocio → Estados de cuenta**.

## Importar desde Excel

**Categorías, Artículos y códigos alternativos, Listas de precios y Clientes** ofrecen **Descargar plantilla** e **Importar Excel**. Cada plantilla `.xlsx` trae hojas de carga vacías y una hoja **Ejemplos** que no se importa. Completá las hojas de carga, elegí el archivo, pulsá **Previsualizar** y revisá altas, cambios, filas no importadas y errores por hoja/fila antes de **Confirmar importación**.

Un artículo cuyo código principal ya existe se marca **No importado** y no se sobrescribe, aunque sí pueden agregarse códigos alternativos nuevos. En listas existentes, nuevos precios abren una vigencia sin borrar el historial y se pueden habilitar sucursales. Estos cuatro maestros no importan ventas, stock, usuarios, credenciales, impuestos ni piezas. Las variaciones de stock se cargan por separado desde **Negocio → Existencias**. Ver [contrato de plantillas](../../SPEC-admin-excel-import.md) y [contrato de existencias](../../SPEC-stock-import.md).

## Organización y usuarios

**Organización** mantiene empresas, sucursales y cajas. Una sucursal puede tener varias cajas; cada una puede activarse/inactivarse y tener una credencial propia. Al crear o rotar una credencial de terminal, guardala por un canal seguro y vinculala al perfil de Electron correspondiente. La credencial no debe copiarse a documentación ni a Git.

**Usuarios** permite dar de alta cajeros, editar sus datos, asignar sucursales, activar/inactivar cuentas, cerrar sesiones y **restablecer la contraseña** de cualquier usuario de la empresa. Restablecer una contraseña revoca las sesiones activas de esa persona. La interfaz actual no crea otros roles ni administradores nuevos.

## Etiquetas e integraciones

- **Lectura de etiquetas:** definir una fórmula de campos y longitudes, probar un código escrito y guardar una nueva revisión del perfil. El ejemplo EAN-13 es solo una plantilla de prueba; guardar el perfil no recibe stock por sí mismo.
- **ARCA:** mantener datos del emisor, punto de venta de homologación y certificado PFX; se informa el estado/vencimiento del certificado. Esta configuración no convierte el entorno en producción.
- **Pasarelas de cobro:** configurar la cuenta vendedora de prueba de Mercado Pago y vincular a cada caja el `external_id` alfanumérico de un puesto QR activo/integrado y/o terminal Point. El mismo puesto permite QR dinámico o fijo; el fijo conserva el mismo código visual pero cada cobro tiene orden e importe propios. No usar el ID numérico del puesto como `external_id`. Para Clover sandbox, cargar App ID, Merchant ID y App Secret, conectar el comercio mediante OAuth y usar **Probar conexión Clover** para consultar su identidad. **Consultar dispositivos Clover** muestra los equipos provisionados que informa el comercio; una lista vacía no verifica la instalación de Cloud Pay Display. El sistema renueva un token de acceso vencido; si la renovación queda incierta, indica **Reconexión necesaria** y exige repetir OAuth. Estas pruebas no cobran ni habilitan todavía Clover en el POS. Las credenciales y números de serie no se muestran de vuelta como texto.

En **Organización → Cajas → Dispositivos**, asigná por caja una balanza serial y/o una impresora serial de texto o Hasar SMH/P-441F, indicando puerto COM y velocidad. Cada dispositivo puede quedar en **Sin balanza** o **Sin impresora**. Sin asignación, Electron no abre la balanza ni intenta imprimir al cerrar la venta; el peso manual, la venta y el PDF siguen disponibles. La Hasar usa 9600 baudios y no puede compartir puerto con la balanza. Recargá la caja Electron después de cambiar la asignación para actualizar los controles visibles. El archivo local `pos-config.json` conserva únicamente el destino de la API: los campos de puertos de versiones anteriores se ignoran. Ver la [guía de despliegue](../despliegue/guia-workshop-por-sucursal.md).

## Conexión con central

En una instalación de sucursal preparada en **modo administrado**, abrí **Configuración → Conexión con central** con un usuario que tenga el permiso `organization.manage`. La pantalla muestra la empresa, sucursal y sus identificadores desde la API local: no hay que buscarlos en la base. Si indica que la API no está vinculada a esa sucursal, pedí a implementación que revise la instalación; no generes un paquete para otra sucursal.

1. Ingresá y guardá la **URL raíz de esta sucursal** por la que el servidor central podrá alcanzarla. Guardar una URL no prueba la conexión. En instalaciones reales se usa HTTPS; HTTP `127.0.0.1` solo está habilitado en el laboratorio.
2. Pulsá **Generar datos de conexión** y copiá el paquete completo. Incluye nombre, URL, tres IDs y una credencial de lectura para identidad y resumen de ventas/cajas. **Se muestra una sola vez**; tratala como secreta y no la incluyas en capturas, chat ni tickets.
3. Entregá el paquete al administrador central de **la misma empresa** para pegarlo en **Conexiones a sucursales**. Allí deberá verificarse antes de quedar activo. Si el servidor central no permite aún esa URL/IP, implementación debe aprobarla en la configuración protegida del servidor central.
4. Si se pierde la credencial, pulsá **Rotar credencial**, actualizá la conexión existente en la central con el paquete nuevo y, tras comprobarla, pulsá **Finalizar rotación**. La anterior funciona durante una hora como máximo o hasta finalizar. **Desactivar conexión** revoca la lectura remota, pero no detiene el POS ni borra ventas.

El token no reaparece al recargar la página. La pantalla local muestra *configurada* o *credencial activa*; para saber si la central llega a la sucursal usá **Comprobar** en el portal central. Ver [guía técnica](../despliegue/integracion-central-sucursal.md).

Base técnica: [administración](../../SPEC-admin.md), [impuestos](../../SPEC-tax-catalog.md), [ARCA](../../SPEC-admin-arca-settings.md) y [Mercado Pago](../../SPEC-mercado-pago.md).
