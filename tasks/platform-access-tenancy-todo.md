# Tareas: identidad por empresa

**Estado:** aprobado; implementación en curso. Seguir [el plan](platform-access-tenancy-plan.md). No ejecutar pruebas que creen otra base.

## T1. Auditar datos y validar conversión

- [x] Relevar usuarios con asignaciones en varias empresas, posibles credenciales ambiguas y sesiones activas, sin modificar datos.
- [x] Dejar documentado el comportamiento de la migración ante cada caso y el respaldo de `carnicerias_test_visual`.
- **Verificar:** consultas de solo lectura y revisión de esquema; ninguna base creada o limpiada.
- **Depende de:** ninguna. **Archivos probables:** especificación, plan de migración, pruebas de modelo.

## T2. Persistir empresa y ámbito de cuenta

- [x] Código de empresa estable y único; usuario cliente con empresa; cuenta de fabricante sin empresa cliente en modelo y migración.
- [x] La migración conserva IDs/hashes y falla si hay una identidad con empresa ambigua; índices por empresa sustituyen los globales solo después de validar. **SQL generado e inspeccionado; no aplicado.**
- **Verificar:** build .NET, pruebas de modelo/migración no destructivas; inspección del SQL generado. Integración PostgreSQL pendiente bajo la regla de base única.
- **Depende de:** T1. **Archivos probables:** entidades Company/UserIdentity, DbContext, migración EF, pruebas.

## T3. Autenticar clientes con ámbito inequívoco

- [x] Login web resuelve `companyCode` y usuario/correo dentro de esa empresa; fallos no enumeran cuentas (código preparado; pendiente prueba en PostgreSQL).
- [x] Login POS usa empresa de la credencial de caja y rechaza una empresa explícita diferente; sesión queda ligada a la empresa (código preparado; pendiente prueba en Electron).
- **Verificar:** pruebas de servicio y endpoint para dos `cajero1`, terminal de otra empresa, sesión antigua y credencial inválida; build API.
- **Depende de:** T2. **Archivos probables:** SessionAuthenticationService, SessionEndpoints, UserSession, pruebas.

## T4. Actualizar ingreso Angular

- [x] El navegador solicita código de empresa, usuario/correo y contraseña; la caja no pide ni permite cambiar empresa.
- [x] El cliente HTTP envía el contrato nuevo y muestra error genérico sin perder las protecciones de sesión.
- **Verificar:** pruebas de componente/cliente, build y lint Angular; revisión POS en Electron.
- **Depende de:** T3. **Archivos probables:** welcome-page, SessionClient, pruebas.

## Checkpoint A

- [ ] Dos `cajero1` de empresas distintas pueden autenticarse solo en su ámbito; Caja A no abre sesión de B.
- [ ] Build y pruebas no destructivas pasan; no se aplicó migración sin respaldo.

## T5. Separar superadmin del fabricante

- [x] Procedimiento privilegiado preparado para crear el superadmin sin contraseña en argumentos, logs o Git; no requiere nueva base. **No ejecutado antes de migrar.**
- [x] La sesión de fabricante no adquiere contexto operativo ni permisos POS, y una sesión cliente no accede a rutas de fabricante por diseño de cookies y filtros. **Pendiente prueba HTTP negativa.**
- **Verificar:** pruebas de permisos y bootstrap, build .NET, revisión de amenazas.
- **Depende de:** T2. **Archivos probables:** Bootstrap, autenticación de fabricante, autorización, pruebas.

## T6. Alta de empresa por fabricante

- [x] Solo superadmin crea empresa con código, primera sucursal y primer administrador; las operaciones son atómicas en el código preparado.
- [x] UI del fabricante permite ver/crear empresas sin exponer contraseñas o datos comerciales.
- **Verificar:** pruebas API/UI de autorización y fallo parcial, build/lint.
- **Depende de:** T5. **Archivos probables:** endpoints Organization, servicio de alta, UI fabricante, pruebas.

## T7. Delegar administradores dentro de empresa

- [x] Admin A crea cajero o admin de A, pero no de B ni un superadmin, según contratos y filtros implementados. **Pendiente prueba HTTP negativa.**
- [x] Cambios de acceso revocan sesiones pertinentes y protegen al último admin operativo de A en código. **Pendiente concurrencia real.**
- **Verificar:** pruebas negativas de usuarios/permisos, frontend, build API/Angular.
- **Depende de:** T3, T6. **Archivos probables:** UserEndpoints, UsersPage, permisos, pruebas.

## T8. Limitar Organización a la empresa propia

- [x] Administración cliente no ofrece alta/listado global de empresas y conserva sucursales/cajas de su empresa.
- [x] Endpoints de organización filtran por empresa y devuelven denegación no enumerable al intentar acceder a otra empresa. **Pendiente prueba HTTP negativa.**
- **Verificar:** pruebas API/UI para admin A/B, recorrido de administración en navegador.
- **Depende de:** T6, T7. **Archivos probables:** AdminOrganizationEndpoints, OrganizationPage, clientes HTTP, pruebas.

## Checkpoint B

- [ ] Superadmin crea Empresa B sin permiso de venta; admin A solo administra A y puede crear otro admin A.
- [ ] POS verificado visualmente en Electron, sin regresión de selección de sucursal/turno.

## T9. Cerrar seguridad, migración y documentación

- [ ] Auditoría horizontal de endpoints con `CompanyId`, sesiones, reseteo de contraseñas y roles; actualizar specs, manual y despliegue.
- [ ] Respaldar y migrar solo `carnicerias_test_visual`; comprobar datos existentes y el flujo de dos empresas sin crear otra base.
- **Verificar:** `dotnet build Carnicerias.sln --configuration Release`; ArchitectureTests; Angular test/build/lint; Electron test y visual; `git diff --check`; registrar integración no ejecutada.
- **Depende de:** T1–T8. **Archivos probables:** pruebas de seguridad, documentación y checklist de migración.

## Checkpoint final

- [ ] Usuarios, ventas, turnos y stock preexistentes preservados; no hay credenciales en Git.
- [ ] Un administrador de empresa no crea ni modifica otra empresa; dos `cajero1` independientes funcionan.
- [ ] Plan histórico `tasks/plan.md` y especificaciones antiguas conciliados sin borrar tareas pendientes ajenas.
