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
