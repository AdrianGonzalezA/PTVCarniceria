# ADR 0007: conexiones de sucursal gestionadas por el administrador central

**Estado:** aceptada el 15/10/2026. Amplía y sustituye únicamente la cláusula de alta de sucursales «solo por CLI» de [ADR 0006](0006-superadmin-propio-del-portal-central.md); no modifica la titularidad del SuperAdmin central.

## Contexto

El registro por CLI permitió probar las primeras sucursales, pero cada empresa necesita mantener sus propias conexiones desde el portal central. La URL y el token de una sucursal no son datos confiables: autorizar una consulta HTTP únicamente porque un administrador escribió su destino convertiría la central en un proxy hacia la red interna.

## Decisión

- El **administrador central de la empresa** crea, lista, edita, comprueba, desactiva y reactiva sus conexiones en **Conexiones a sucursales**. La empresa propietaria se toma exclusivamente de su sesión central. No se delega esta tarea al SuperAdmin local del POS.
- El **SuperAdmin del portal central pertenece siempre a Recursos Informáticos**, desarrollador del POS. Crea empresas centrales y sus administradores, pero no usa la sesión de una empresa ni configura ordinariamente sus sucursales.
- Recursos Informáticos/implementación mantiene fuera de Git una lista de destinos exactos e IP autorizadas **por empresa central**. La API valida la URL e IP antes de enviar el token; exige TLS para red real, no sigue redirecciones, fija la IP validada y comprueba `installationId`, `remoteCompanyId` y `remoteBranchId` exactos. HTTP literal de loopback requiere habilitación explícita solo en laboratorio. La pantalla no puede ampliar la lista aprobada.
- Los tokens nuevos se cifran con AES-GCM y clave maestra versionada fuera de la base. El registro `central_access.branch_credentials` contiene solo versión, nonce, texto cifrado y etiqueta autenticada vinculados a empresa e instalación. No se devuelve un token ni su referencia al navegador. Si falta la clave o falla el descifrado, no se usa silenciosamente otro origen de credenciales.
- Los registros existentes del CLI siguen leyendo su referencia de variable de entorno. Para cambiarles el token desde la web hay que introducir uno nuevo: se verifica antes de sustituir la configuración y pasa al almacenamiento cifrado. Una edición de etiqueta no consulta la sucursal. Reactivar exige verificación; desactivar excluye del dashboard sin borrar la conexión. La última comprobación es puntual, no un indicador continuo.
- Las mutaciones usan control de revisión y auditoría sin secreto. Una comprobación o edición desde la empresa A no ve ni cambia una conexión de B; el UUID conocido de B responde como inexistente. Las ventas, cajas y stock locales continúan aunque la central falle.

## Consecuencias

La central requiere una clave protegida y respaldada, una lista de destinos administrada por implementación y una credencial de servicio preparada previamente en cada sucursal. La pérdida de la clave obliga a reprovisionar tokens cifrados. La rotación del token se prepara primero en el verificador `NextCredentialSha256` de la sucursal; después se actualiza en la central y por último se retira el anterior. La base de desarrollo sigue siendo únicamente `carnicerias_test_visual`; no se copiaron datos comerciales a `central_access`.

La ruta CLI queda disponible para instalaciones heredadas y recuperación, no como única vía de operación. Los contratos, controles y límites están en [SPEC-central-branch-connections.md](../../SPEC-central-branch-connections.md) y el procedimiento operativo en [administracion-central.md](../despliegue/administracion-central.md).
