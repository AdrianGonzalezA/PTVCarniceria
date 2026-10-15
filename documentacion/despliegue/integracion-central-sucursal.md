# Activar lectura de una sucursal desde una central

Este procedimiento corresponde solo al contrato `branch-service-contract`. No instala la central, no sincroniza tablas y no cambia las ventas locales. La API de sucursal arranca con la integración apagada; sin configuración completa `/api/central/v1/*` devuelve `404`.

## Requisitos de despliegue

1. Identificar el `companyId` y el `branchId` activos de la instalación y asignar un `installationId` UUID estable y exclusivo. En el laboratorio se usa solo `carnicerias_test_visual` del PostgreSQL Docker local en el puerto 55433; no crear otra base para habilitar este contrato.
2. Asegurar la ruta entre central y sucursal mediante VPN/red privada y HTTPS confiable. Publicar únicamente las rutas necesarias de `/api/central/v1/*` al cliente central autorizado; no exponer toda la API local a Internet.
3. Guardar los valores de `Federation` en configuración protegida del servidor, fuera de Git y de las cajas Electron. La cuenta del servicio API debe poder leerla; otros usuarios del servidor no. Para rotación sin reiniciar, esa fuente debe admitir recarga en ejecución (por ejemplo, un `appsettings.Production.json` protegido en la instalación publicada, no versionado, con el entorno ASP.NET Core configurado como `Production`). Las variables de entorno por sí solas no se recargan en un proceso ya iniciado.

## Configuración

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

`NextCredentialSha256` es opcional y se usa durante la rotación. El token entregado a la central son **32 bytes aleatorios representados por 64 caracteres hexadecimales**; el servidor almacena solo SHA-256 de los bytes, también en hexadecimal. No reutilizar usuarios, contraseñas ni credenciales de terminal. Generar token y verificador con una herramienta criptográfica confiable, transferir el token por un canal de secretos y no pegar ninguno en Git, logs o tickets de soporte.

Al arrancar, la API valida GUIDs y verificadores. Si falta alguno, mantiene la venta local y no registra estas rutas; deja una advertencia sin valores sensibles. Un cambio de identidad (`InstallationId`, `CompanyId` o `BranchId`) requiere reiniciar deliberadamente la API, pero un cambio de verificador en una fuente recargable se toma en las solicitudes siguientes. La cuota actual es de 30 solicitudes por minuto y proceso; el resumen admite ventanas UTC de hasta 31 días y un tiempo máximo de lectura de cinco segundos.

## Comprobación y rotación

1. Con el token en la central, consultar `GET /api/central/v1/installation` y comprobar `installationId`, `companyId`, `branchId` y nombres. Sin token debe dar `401` y con la integración apagada `404`.
2. Consultar `GET /api/central/v1/summary?fromUtc=<ISO-UTC>&toUtc=<ISO-UTC>` y contrastar los agregados con la sucursal. El comienzo es inclusivo y el final exclusivo. No enviar `branchId` en la solicitud: cualquier parámetro adicional devuelve `400`.
3. Para rotar sin detener el POS: generar un token nuevo, colocar su hash en `NextCredentialSha256` de la fuente recargable, comprobar que el token nuevo funciona, cambiar la credencial guardada en la central y luego promover el hash nuevo a `CredentialSha256` y quitar `NextCredentialSha256`. El token anterior deja de funcionar al recargarse el último cambio. Si la fuente no recarga, **no** asumir que se logró una rotación sin interrupción; planificar cómo actualizar la API.

Errores y forma JSON están en [la especificación](../../SPEC-branch-service-contract.md). `BRANCH_BINDING_UNAVAILABLE` indica que la pareja empresa/sucursal fijada no se pudo validar; `SUMMARY_UNAVAILABLE` indica que la lectura del resumen falló o venció el límite. No se debe reintentar en un bucle rápido ni interpretar un resumen ausente como cero ventas. La central deberá marcar el dato como incompleto.

## Verificación de desarrollo permitida

```powershell
dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj
dotnet test tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj --filter FullyQualifiedName~BranchServiceEndpointTests
dotnet test tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj --filter FullyQualifiedName~BranchServiceVisualDatabaseTests
```

La última clase **solo lee** la base existente y exige `CARNICERIAS_VISUAL_CONNECTION_STRING` hacia `carnicerias_test_visual` en `127.0.0.1:55433` o `localhost:55433`. No ejecutar la suite de integración completa: otras clases migran, crean o reinician bases. No guardar la cadena ni su contraseña en el repositorio. El workshop multiservidor, VPN/HTTPS real y una instalación nueva siguen pendientes.
