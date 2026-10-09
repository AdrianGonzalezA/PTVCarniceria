# Configuración de ARCA por empresa (homologación)

## Alcance

La pestaña **Configuración → ARCA** permite al administrador de la empresa cargar y revisar los datos fiscales usados por WSFEv1 de homologación: CUIT representada, punto de venta, razón social, domicilio, IIBB e inicio de actividades. También permite subir un PFX, con contraseña temporal si corresponde, y ver sujeto, huella, inicio y vencimiento del certificado. No se muestra ni se descarga su clave privada.

La configuración es de la empresa de la sesión; requiere `organization.manage`. El cajero no puede leerla ni cambiarla. La pantalla no ofrece producción ni cambia el servicio de homologación a producción. La clasificación tributaria de artículos, habilitación de un puesto ante ARCA y datos fiscales reales siguen siendo responsabilidades separadas.

## Persistencia y seguridad

- Los datos públicos y metadatos del certificado viven en `carnicerias_test_visual` mediante migración aditiva. El PFX y su contraseña se protegen con ASP.NET Core Data Protection antes de persistirlos en PostgreSQL. La contraseña no vuelve al navegador y no se registra en logs.
- La clave de Data Protection debe conservarse y respaldarse fuera de PostgreSQL. En esta instalación Windows se usa el almacén persistente del perfil del proceso; al desplegar en un servidor se configurará un repositorio de claves compartido y protegido antes de habilitar la carga de certificados. Perder esa clave vuelve ilegible el PFX guardado.
- Máximo 1 MiB para el PFX, sin aceptar archivos arbitrarios ni confiar en la extensión. El servidor valida PKCS#12, clave privada y vigencia. Una carga inválida no reemplaza el certificado anterior.
- Mientras esta instalación se migra, la configuración de entorno existente puede usarse como lectura de respaldo; una vez cargados datos y PFX desde administración, éstos prevalecen. No se guardan rutas locales ni contraseñas en Git.

## Contrato

- `GET /api/admin/arca-settings` devuelve sólo los datos públicos, estado de configuración y metadatos del certificado; nunca bytes PFX ni contraseña.
- `PUT /api/admin/arca-settings` guarda los campos fiscales validados para la empresa de la sesión, sin cambiar el certificado.
- `POST /api/admin/arca-settings/certificate` recibe JSON `{ contentBase64, password }` limitado en tamaño; valida y guarda el PFX protegido, reemplazando la referencia vigente. La contraseña puede omitirse. No acepta `companyId` ni rutas de archivo del cliente.
- La emisión usa la configuración vigente de la empresa; un cambio de certificado invalida el ticket WSAA en memoria. Los nuevos intentos fiscales conservan un snapshot de los datos del emisor. Los comprobantes previos a esta migración carecen de ese snapshot y pueden mostrar datos actuales del encabezado al reimprimir; su CUIT, número, CAE y vencimiento autorizados no se alteran. Un error de configuración no vuelve a cobrar ni descontar stock.

## Verificación

Probar validación de campos y PFX sin base, migración sobre la única base local, permiso de administrador, no exposición de secretos, carga real desde Electron y lectura de fecha de vencimiento. No ejecutar pruebas que creen otra base. Una factura ya confirmada se reintenta por su `saleId`, nunca como otra venta.
