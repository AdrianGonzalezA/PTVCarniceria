# Activar lectura de una sucursal desde una central

Este procedimiento corresponde al contrato `branch-service-contract` en modo **administrado** o **heredado**. No instala la central, no sincroniza tablas y no cambia las ventas locales. La API de sucursal arranca con la integración apagada; sin configuración completa `/api/central/v1/*` devuelve `404`.

## Modo administrado: recomendado para instalaciones nuevas

Aplicar las migraciones locales `AddManagedBranchCentralConnections` y `AddBranchCentralConnectionAudit` a la base existente de la sucursal. En este desarrollo la única base de aplicación sigue siendo `carnicerias_test_visual` en `127.0.0.1:55433`; revisar el SQL y respaldarla antes de migrar. No ejecutar pruebas que creen o reinicien otra base.

El proceso de API debe quedar fijado a **una** empresa y **una** sucursal activas mediante configuración protegida del servidor, fuera de Git y de Electron:

```json
{
  "Federation": {
    "Enabled": true,
    "Mode": "managed",
    "CompanyId": "<uuid-empresa-local>",
    "BranchId": "<uuid-sucursal-local>"
  }
}
```

Esta vinculación no se toma de una selección del navegador. Con `Mode=managed` no configurar `InstallationId`, `CredentialSha256` ni `NextCredentialSha256`: el ID estable y los verificadores se administran en la misma base desde **Administración de sucursal → Configuración → Conexión con central**. Un usuario con `organization.manage` y contexto de esa sucursal guarda la URL raíz alcanzable desde la central. Se exige HTTPS; únicamente en laboratorio puede habilitarse `Federation:AllowLabLoopback=true` para `http://127.0.0.1:<puerto>/`. Una API ligada a otra sucursal muestra conflicto y no emite credenciales engañosas.

**Generar datos de conexión** entrega una sola vez un paquete JSON versión 1 con etiqueta, URL, `installationId`, `remoteCompanyId`, `remoteBranchId` y token. Todos esos campos salen de esa pantalla, no de SQL ni de un script. La base guarda únicamente el hash del token. El administrador central de la **misma empresa** lo pega en **Conexiones a sucursales**, revisa el formulario y verifica antes de agregar. La central debe tener previamente aprobados la URL/IP exactos para esa empresa y su propia clave de cifrado protegida, según [administracion-central.md](administracion-central.md). Que la URL esté guardada localmente no prueba conectividad.

Para rotar: emitir paquete nuevo en sucursal, abrir **Editar** de esa misma conexión en central, pegarlo y comprobarlo. Después finalizar la rotación local para revocar el token anterior; si no se finaliza, vence como máximo una hora después. Si se pierde el paquete antes de pegarlo, finalizar la transición pendiente y emitir otro. **Desactivar conexión** revoca el acceso remoto sin impedir venta local. El token no se recupera de la base ni del navegador.

No cambiar una instalación de `Mode=managed` a legado como reacción automática a un error: cada modo tiene un origen distinto de credenciales y uno inválido falla cerrado. El cambio de modo requiere un plan de aprovisionamiento y verificación deliberado.

## Requisitos de despliegue

1. Identificar el `companyId` y el `branchId` activos de la instalación. En modo **heredado** también se asigna un `installationId` UUID estable y exclusivo. En el laboratorio se usa solo `carnicerias_test_visual` del PostgreSQL Docker local en el puerto 55433; no crear otra base para habilitar este contrato.
2. Asegurar la ruta entre central y sucursal mediante VPN/red privada y HTTPS confiable. Publicar únicamente las rutas necesarias de `/api/central/v1/*` al cliente central autorizado; no exponer toda la API local a Internet.
3. Guardar los valores de `Federation` en configuración protegida del servidor, fuera de Git y de las cajas Electron. La cuenta del servicio API debe poder leerla; otros usuarios del servidor no. Para rotación sin reiniciar, esa fuente debe admitir recarga en ejecución (por ejemplo, un `appsettings.Production.json` protegido en la instalación publicada, no versionado, con el entorno ASP.NET Core configurado como `Production`). Las variables de entorno por sí solas no se recargan en un proceso ya iniciado.

## Configuración heredada

Nombres de claves del servidor (los valores entre `<...>` son marcadores, no credenciales):

```json
{
  "Federation": {
    "Enabled": true,
    "InstallationId": "<uuid-estable-de-instalacion>",
    "CompanyId": "<uuid-empresa-local>",
    "BranchId": "<uuid-sucursal-local>",
    "CredentialSha256": "<64-caracteres-hex-del-verificador>"
  }
}
```

La omisión de `Mode` mantiene la compatibilidad heredada; también se admite `"Mode": "legacy"`. `NextCredentialSha256` es opcional y se usa durante la rotación heredada. El token entregado a la central son **32 bytes aleatorios representados por 64 caracteres hexadecimales**; el servidor almacena solo SHA-256 de los bytes, también en hexadecimal. No reutilizar usuarios, contraseñas ni credenciales de terminal. Generar token y verificador con una herramienta criptográfica confiable, transferir el token por un canal de secretos y no pegar ninguno en Git, logs o tickets de soporte.

Al arrancar, la API valida GUIDs y verificadores. Si falta alguno, mantiene la venta local y no registra estas rutas; deja una advertencia sin valores sensibles. Un cambio de identidad (`InstallationId`, `CompanyId` o `BranchId`) requiere reiniciar deliberadamente la API, pero un cambio de verificador en una fuente recargable se toma en las solicitudes siguientes. La cuota actual es de 30 solicitudes por minuto y proceso; el resumen admite ventanas UTC de hasta 31 días y un tiempo máximo de lectura de cinco segundos.

## Comprobación y rotación heredada

1. Con el token en la central, consultar `GET /api/central/v1/installation` y comprobar `installationId`, `companyId`, `branchId` y nombres. Sin token debe dar `401` y con la integración apagada `404`.
2. Consultar `GET /api/central/v1/summary?fromUtc=<ISO-UTC>&toUtc=<ISO-UTC>` y contrastar los agregados con la sucursal. El comienzo es inclusivo y el final exclusivo. No enviar `branchId` en la solicitud: cualquier parámetro adicional devuelve `400`.
3. Para rotar sin detener el POS: generar un token nuevo, colocar su hash en `NextCredentialSha256` de la fuente recargable, comprobar que el token nuevo funciona, cambiar la credencial guardada en la central y luego promover el hash nuevo a `CredentialSha256` y quitar `NextCredentialSha256`. El token anterior deja de funcionar al recargarse el último cambio. Si la fuente no recarga, **no** asumir que se logró una rotación sin interrupción; planificar cómo actualizar la API.

Errores y forma JSON están en [la especificación](../../SPEC-branch-service-contract.md). `BRANCH_BINDING_UNAVAILABLE` indica que la pareja empresa/sucursal fijada no se pudo validar; `SUMMARY_UNAVAILABLE` indica que la lectura del resumen falló o venció el límite. No se debe reintentar en un bucle rápido ni interpretar un resumen ausente como cero ventas. La central deberá marcar el dato como incompleto.

## Verificación de desarrollo permitida

```powershell
dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj
```

No ejecutar la suite de integración completa: varias clases migran, crean o reinician bases. Las verificaciones HTTP manuales de esta fase usan **la base existente** y no crean otra. No guardar la cadena ni su contraseña en el repositorio. El workshop multiservidor, VPN/HTTPS real y una instalación nueva siguen pendientes.
