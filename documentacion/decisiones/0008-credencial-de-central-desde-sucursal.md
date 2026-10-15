# ADR 0008: datos de conexión a central emitidos desde la sucursal

**Estado:** aceptada e implementada el 15/10/2026. Complementa [ADR 0007](0007-conexiones-de-sucursal-gestionadas-por-admin-central.md). Reemplaza el aprovisionamiento por CLI de identificadores y token **para instalaciones en modo administrado**; no elimina el modo heredado.

## Contexto

El administrador central puede dar de alta conexiones, pero hasta ahora Recursos Informáticos debía preparar en la API de cada sucursal el ID de instalación y el hash del token. Para conectar La Rústica / Alta Córdoba sin acudir a consola o base, la sucursal debe entregar desde su propio administrador todos los campos que se pegan en la central. La URL de red no se puede inferir del navegador ni debe autorizar por sí sola el acceso de la central.

## Decisión

- El proceso API de sucursal se vincula por configuración protegida a **una empresa y una sucursal** (`Federation:CompanyId`, `Federation:BranchId`, `Federation:Mode=managed`). Solo un usuario con `organization.manage` en ese contexto puede abrir **Configuración → Conexión con central**. La pantalla no acepta IDs de empresa/sucursal para cambiar el alcance. Un administrador de otra sucursal o empresa recibe un conflicto.
- El administrador local guarda la URL raíz por la que la central alcanzará **esa API**. Se exige HTTPS, salvo HTTP literal de loopback expresamente habilitado para laboratorio. Al guardar por primera vez se genera un `installationId` estable; no cambia al rotar token o URL.
- Al solicitarlo, el servidor emite 32 bytes aleatorios como token hexadecimal y entrega **una vez** un paquete versión 1: etiqueta, URL, `installationId`, `remoteCompanyId`, `remoteBranchId` y token. El navegador no lo guarda entre cargas; la respuesta es `no-store`. La base conserva solo SHA-256 de los bytes del token, con auditoría sin secretos. La credencial permite **leer identidad y resumen de negocio**, no escribir en la sucursal.
- El administrador de la empresa central pega el paquete en **Conexiones a sucursales**. Para una conexión existente, el paquete debe corresponder a los mismos tres IDs; una URL o credencial nueva se comprueba antes de guardar. La central cifra su copia del token con la clave de servidor protegida y mantiene su política de URL/IP aprobadas **por empresa central**. El administrador local no puede ampliar esa política.
- Rotar conserva el ID y admite el token anterior durante una hora como máximo. Cuando la central ya utiliza el nuevo, el administrador de sucursal finaliza la rotación para revocar antes el anterior. Desactivar revoca ambos verificadores inmediatamente sin borrar la configuración ni afectar POS, caja o ventas locales.
- `Federation:Mode=managed` no reutiliza `CredentialSha256` ni `NextCredentialSha256` de la configuración heredada. Un modo inválido falla cerrado. Las instalaciones heredadas sin `Mode` conservan su comportamiento anterior; la migración a administrado es deliberada, con nueva conexión verificada antes de retirar secretos heredados.

## Consecuencias

La única base de desarrollo sigue siendo `carnicerias_test_visual`; las migraciones `AddManagedBranchCentralConnections` y `AddBranchCentralConnectionAudit` agregan tablas a `platform_access`, sin copiar ventas a la central. La configuración del despliegue sigue siendo responsable de la vinculación fija, la red/TLS y la allowlist; **ninguno de los seis campos del paquete se obtiene de SQL o consola**. La central no deduce que una URL guardada esté conectada: consulta y compara los tres IDs con un token válido.

La clave de cifrado central del laboratorio se conserva mediante DPAPI de la cuenta Windows actual y no sirve como copia recuperable en otro servidor. El despliegue real requiere gestor de secretos y respaldo independiente. Ver [procedimiento de sucursal](../despliegue/integracion-central-sucursal.md), [central](../despliegue/administracion-central.md) y [especificación](../../SPEC-branch-central-self-service.md).
