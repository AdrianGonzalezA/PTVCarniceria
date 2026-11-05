# I18a — permisos de recepción por empresa y rol

**Fecha:** 05/11/2026. **Base única:** `carnicerias_test_visual`, contenedor existente `carnicerias-pos-test-20261007`, `127.0.0.1:55433`. El PO confirmó que cambiar un rol de una empresa no debe cambiar los permisos de otras empresas que usan ese rol.

## Prevuelo y respaldo

- `tools/sql/20261105_company_role_permissions_preflight_readonly.sql`: 66 migraciones aplicadas, última `20261105152647_AddReceiptPieceReadsI18a`; tabla nueva ausente, cuatro empresas, 13 usuarios, un rol `administrator`, un rol `cashier`, permiso de recepción presente y cero escritores activos.
- Se verificaron el contenedor `9a9fb3d3bdbe39c1b0da28f0e76aba8f22bccdb38ca935c734ca3a4f8a091ef0`, puerto `55433` y volumen original `1374f946f94e4918f5ff132b42a3e4f7c33983baf70958def084686c5ae9583d`.
- `tools/Backup-AuthorizedLab.ps1` creó el respaldo custom `.local-backups/i18a-roles-before-20261105-175912Z.dump`: 464.468 bytes, 638 entradas de `pg_restore -l`, SHA-256 local/contenedor `2E6D87770E7EF868E9C8DC18966FE317F0548DE30DC23F51E49BB0E05ED7B861`. El archivo está excluido de Git. No se ejecutó restauración.

## Migración y comportamiento

`tools/Apply-LocalPosMigrations.ps1` aplicó únicamente `20261105175303_AddCompanyRolePermissionOverrides`, una tabla aditiva `platform_access.company_role_permission_overrides` con empresa, rol, permiso, estado y actor/fecha de última modificación. Los grants globales existentes siguen siendo el valor inicial; las excepciones por empresa se aplican por rol antes de unir permisos de un usuario. El `Down` bloquea borrado automático de configuraciones. El postcheck de solo lectura confirmó 67 migraciones, tabla y cinco restricciones presentes, cero excepciones iniciales, cuatro empresas y 13 usuarios intactos.

La API `GET/PUT /api/users/role-permissions` exige `platform.roles.manage` y contexto de empresa; en esta entrega permite configurar únicamente `inventory.receipts.manage` para administrador o cajero. La página Usuarios muestra la función con aviso de alcance empresarial. Los cambios surten efecto en la siguiente solicitud, incluso con una sesión abierta, porque la autorización resuelve los permisos desde la base.

## Pruebas y recuperación

- `tools/Run-I18ARolePermissionsVisualTest.ps1` verificó nuevamente respaldo, identidad, volumen y esquema antes de ejecutar dos pruebas HTTP aisladas. Pasaron: el cajero de la empresa A obtuvo y luego perdió acceso a Recepciones sin cerrar sesión, el de la empresa B no cambió, el cajero no pudo administrar permisos y la regresión de recepción de piezas conservó sus saldos. Limpieza final: cero empresas/recepciones/excepciones de fixture, cuatro empresas, 13 usuarios y cuatro piezas anteriores.
- Pasaron nueve pruebas Angular de Usuarios, 264 Angular en 49 archivos, 106 de arquitectura, compilaciones .NET y Angular y lint Angular. La compilación Angular conserva avisos previos de presupuesto CSS y dependencia CommonJS `qrcode`.
- Ante un problema, detener escrituras de permisos y conservar la tabla y el respaldo; revertir el binario anterior hará que ignore excepciones específicas de empresa, por lo que se debe impedir administrar permisos con esa versión. Restaurar el dump descarta cambios posteriores y requiere decisión separada.

Pendiente: recorrido visual de administración al revisar la interfaz completa con el PO. I18b tendrá su propio permiso de despacho cuando se implemente; esta migración no creó remitos ni alteró movimientos comerciales.
