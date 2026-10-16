# Preflight de identidad por empresa — 13/10/2026

Auditoría **solo lectura** de `carnicerias_test_visual` en el PostgreSQL Docker local (`55433`). No se creó, limpió ni modificó ninguna base.

| Dato | Resultado |
| --- | ---: |
| Empresas | 3 (2 activas, 1 inactiva) |
| Usuarios | 4 |
| Asignaciones | 6 |
| Usuarios sin asignación | 0 |
| Usuarios asignados a más de una empresa | 1 |
| Colisiones entre username normalizado y email normalizado de otra cuenta | 0 |
| Sesiones históricas | 132 |

`visual-admin` conserva asignaciones de administrador en Empresa Visual, Carnicería Rústica SRL y Empresa admin prueba 20261008 (inactiva). Los otros tres usuarios solo están asignados a Empresa Visual. Hay sesiones de `visual-admin` con empresa explícita y sesiones anteriores sin empresa fijada.

La migración debe **fallar antes de cambiar índices o datos** cuando encuentre una identidad con asignaciones de varias empresas. No puede repartir un `UserId` existente entre cuentas nuevas: ventas, auditoría, sesiones y otros registros lo referencian. Se espera la decisión del usuario sobre las dos asignaciones adicionales. También debe rechazar cuentas sin empresa, credenciales ambiguas dentro de una empresa y códigos de empresa duplicados. Los índices globales de usuario/email solo se eliminan después de validar y crear los índices compuestos.

Antes de aplicar migraciones: detener la API para evitar escrituras simultáneas, respaldar **la misma** `carnicerias_test_visual` a un archivo fuera de Git, verificar que el respaldo se pueda listar, aplicar la migración solo a esa base y comparar conteos, IDs y hashes sin imprimir hashes. No restaurar en otra base para probar. Revocar las sesiones antiguas al activar el nuevo contrato de ingreso; conservarlas como historial. Las pruebas de integración que provisionan otra base quedan pendientes y no se ejecutan.

## Activación aprobada y ejecutada

El usuario confirmó que `visual-admin` permanece únicamente en Empresa Visual y que las otras dos empresas reciben administradores independientes; el superadmin del fabricante no puede ser ninguna cuenta anterior. Se respaldó la única base a `.local-backups/carnicerias_test_visual_pre_tenant_20261013.dump` (ignorado por Git). `pg_restore -l` leyó el archivo y los SHA-256 del archivo local y el original del contenedor coincidieron. No se creó ni restauró otra base.

Con la API detenida, el script transaccional [20261013_deduplicate_visual_admin_assignments.sql](../../tools/sql/20261013_deduplicate_visual_admin_assignments.sql) retiró **solo dos asignaciones** de `visual-admin`: Rústica y Empresa admin prueba. Su asignación y `UserId` de Empresa Visual quedaron intactos. Se aplicaron `ScopeUsersByCompany` y `AllowCompanyBoundSessionWithoutBranch` sobre `carnicerias_test_visual`; las sesiones antiguas que aún estaban vigentes se revocaron. La segunda migración corrigió una restricción antigua que rechazaba el login del cliente antes de elegir sucursal; una prueba de modelo y un login HTTP detectaron y verificaron la corrección.

| Ámbito | Código de empresa para ingreso web | Administrador | Estado |
| --- | --- | --- | --- |
| Fabricante | No usa código de empresa; `/fabricante` | `fabricante-admin` | Activo; sin empresa ni asignación de caja |
| Empresa Visual | `C-A07B1E5E9EF34E7C9540177EBED5084C` | `visual-admin` | Activa; identidad original preservada |
| Carniceria Rustica SRL | `C-102EE26253E148ED97567C2C84BCCB44` | `rustica-admin` | Activa; identidad nueva |
| Empresa admin prueba 20261008 | `C-818B47200BF048F6A0A2599995C10FFD` | `prueba-admin` | Inactiva; identidad nueva, login bloqueado hasta activación |

Las contraseñas se ingresaron ocultas en la consola y no se guardaron en Git. Tras las altas había 3 empresas, 7 usuarios y 6 asignaciones. La consulta posterior conservó 38 ventas y 8 turnos; el respaldo permite recuperar el estado previo dentro de la **misma** base si hiciera falta. El login HTTP de `fabricante-admin`, `rustica-admin` y `visual-admin` funcionó. Se rechazaron con 401 `rustica-admin` en Empresa Visual, `visual-admin` en Rústica y `prueba-admin` en la empresa inactiva. El endpoint protegido del fabricante respondió 200 con su cookie; PowerShell no reenvía automáticamente esa cookie `Secure` sobre HTTP, por lo que la comprobación se hizo con encabezado explícito desde `curl.exe` en localhost.

El POS se abrió en Electron con el perfil real de Caja 1. `cajero1` inició sesión, seleccionó Empresa Visual y Sucursal Visual y llegó a la pantalla que bloquea ventas hasta abrir turno; la captura y el estado se inspeccionaron desde el renderer de Electron, no desde una pestaña del navegador. No se abrió un turno ni se registró una venta en esta verificación.

Quedan pendientes el ensayo de dos cajeros homónimos, el flujo completo de venta de varias cajas y las pruebas de integración que provisionan otra base (prohibidas por la regla de base única). No interpretar el respaldo local como respaldo productivo: contiene datos y configuraciones del laboratorio y debe permanecer fuera del repositorio.

## Nombre de acceso actual del SuperAdmin

Por pedido posterior del usuario, el acceso visible pasó a `/superadmin` y la cuenta local inicialmente creada como `fabricante-admin` se renombró a `superadmin` (`superadmin@local.test`). Se conservan el mismo `UserId`, hash de contraseña, rol y sesiones; no se creó otra cuenta ni otra base. Los nombres anteriores en esta auditoría describen el estado verificado antes del cambio. La guía de instalación nueva usa `superadmin` desde el inicio.
