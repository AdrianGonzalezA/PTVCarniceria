# 0003 — Identidad por empresa y superadmin del fabricante

**Estado:** aceptada el 13/10/2026; implementación de código en curso, migración de la base pendiente por una asignación multicompañía existente.

## Contexto

La instalación puede alojar varias empresas en su única base local. El índice global de usuarios y el login sin empresa impedían que dos empresas tuvieran cada una un `cajero1`. Además, el administrador de una empresa podía crear otra y asignarse a ella, contrario al límite acordado: el fabricante da de alta empresas y cada cliente gestiona solo la suya.

## Decisión

- Cada cuenta cliente pertenece a una empresa persistida. Usuario y correo son únicos dentro de esa empresa; una cuenta nunca cambia de empresa durante una sesión. El ingreso web pide código de empresa, usuario/correo y contraseña. En el POS Electron, la credencial de terminal fija la empresa y el código no se solicita.
- El fabricante usa una cuenta `superadmin` sin empresa cliente, cookie y rutas `/api/manufacturer/*` separadas. Puede crear una empresa, su primera sucursal y administrador inicial en una transacción; no recibe contexto operativo, caja ni ventas.
- El administrador cliente puede crear otros administradores y cajeros solo en su propia empresa, pero no crear, renombrar ni inactivar empresas. La última cuenta administradora activa de una empresa está protegida.
- Las empresas existentes reciben un código estable `C-` seguido por su UUID sin guiones. Es menos cómodo que un código elegido por el fabricante, pero evita adivinar códigos durante la migración y garantiza unicidad sin cambiar nombres o IDs. Las empresas nuevas reciben un código legible elegido por el superadmin. Los códigos no se modifican al renombrar.
- Las sesiones anteriores se revocan al migrar. Sus registros históricos y los IDs/hashes de usuarios permanecen.

## Migración y límite actual

La migración solo puede aplicarse a `carnicerias_test_visual`, con respaldo previo en archivo fuera de Git. Audita que cada usuario existente tenga exactamente una empresa antes de cambiar índices; una ambigüedad aborta toda la transacción. El preflight detectó que `visual-admin` está asignado a tres empresas. Hace falta una regla explícita del usuario para separar o retirar las asignaciones adicionales sin reasignar referencias históricas; por eso **no se aplicó** la migración ni se arrancó la API modificada sobre la base actual.

El superadmin inicial se crea por consola interactiva después de migrar, sin contraseña en argumentos, logs ni Git. Este superadmin es local a cada instalación autónoma por sucursal; un portal central del fabricante queda fuera del alcance actual.

## Alternativas y consecuencias

Mantener nombres de usuario globales forzaría identificadores artificiales (`empresa-a-cajero1`) y no resolvería el aislamiento de permisos. Permitir que una identidad pertenezca a varias empresas repetiría el problema de atribuir sesiones y acciones a un ámbito mutable. Se eligen cuentas separadas por empresa. El fabricante no usa el rol `administrator` de cliente para administrar otras empresas: su sesión y rutas son distintas, con pruebas negativas pendientes sobre PostgreSQL una vez resuelta la migración.

Véanse [la especificación](../../SPEC-platform-access-tenant-identity.md), [el plan](../../tasks/platform-access-tenancy-plan.md) y [la auditoría de datos](../despliegue/auditoria-identidad-por-empresa-20261013.md).
