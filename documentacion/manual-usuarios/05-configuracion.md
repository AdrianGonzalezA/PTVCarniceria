# 05 · Administración: Configuración

Ingresá en **Administración → Configuración**. Los maestros se editan en formularios o ventanas de detalle, con búsqueda y paginación según la pantalla. Las altas y cambios se aplican a la empresa del contexto; verificá la sucursal antes de configurar cajas o habilitar listas.

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

Un artículo cuyo código principal ya existe se marca **No importado** y no se sobrescribe, aunque sí pueden agregarse códigos alternativos nuevos. En listas existentes, nuevos precios abren una vigencia sin borrar el historial y se pueden habilitar sucursales. No se importan por este mecanismo ventas, stock, usuarios, credenciales, impuestos ni piezas. Ver [contrato de plantillas](../../SPEC-admin-excel-import.md) para columnas y límites.

## Organización y usuarios

**Organización** mantiene empresas, sucursales y cajas. Una sucursal puede tener varias cajas; cada una puede activarse/inactivarse y tener una credencial propia. Al crear o rotar una credencial de terminal, guardala por un canal seguro y vinculala al perfil de Electron correspondiente. La credencial no debe copiarse a documentación ni a Git.

**Usuarios** permite dar de alta cajeros, editar sus datos, asignar sucursales, activar/inactivar cuentas, cerrar sesiones y **restablecer la contraseña** de cualquier usuario de la empresa. Restablecer una contraseña revoca las sesiones activas de esa persona. La interfaz actual no crea otros roles ni administradores nuevos.

## Etiquetas e integraciones

- **Lectura de etiquetas:** definir una fórmula de campos y longitudes, probar un código escrito y guardar una nueva revisión del perfil. El ejemplo EAN-13 es solo una plantilla de prueba; guardar el perfil no recibe stock por sí mismo.
- **ARCA:** mantener datos del emisor, punto de venta de homologación y certificado PFX; se informa el estado/vencimiento del certificado. Esta configuración no convierte el entorno en producción.
- **Pasarelas de cobro:** configurar la cuenta vendedora de prueba de Mercado Pago y vincular a cada caja el identificador QR y/o terminal Point. Las credenciales no se muestran de vuelta como texto. La integración actual requiere configuración correcta por empresa y caja.

La configuración de puertos de balanza e impresora **todavía no se administra desde esta sección**; está pendiente su parametrización. No documentes COM1/COM6 como garantía para una instalación distinta de la actual.

Base técnica: [administración](../../SPEC-admin.md), [impuestos](../../SPEC-tax-catalog.md), [ARCA](../../SPEC-admin-arca-settings.md) y [Mercado Pago](../../SPEC-mercado-pago.md).
