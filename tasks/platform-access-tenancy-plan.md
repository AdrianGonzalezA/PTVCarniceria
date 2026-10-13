# Plan: identidad por empresa y superadmin del fabricante

**Estado:** aprobado por el usuario el 13/10/2026; implementación en curso.
**Contrato:** [SPEC-platform-access-tenant-identity.md](../SPEC-platform-access-tenant-identity.md).
**Tareas:** [platform-access-tenancy-todo.md](platform-access-tenancy-todo.md).
**Convivencia:** `tasks/plan.md` y `tasks/todo.md` conservan el plan histórico de `platform-access`; no se sobrescriben ni se dan por terminados. Este plan cubre únicamente el cambio de ámbito aprobado.

## Resultado buscado

En la misma instalación y base, Empresa A y Empresa B pueden tener cada una un `cajero1`. El ingreso web exige código de empresa; Electron identifica la empresa por credencial de caja. Un administrador de A puede crear otro administrador de A y gestionar todo lo perteneciente a A, pero no crear B ni tocarla. El superadmin del fabricante da de alta empresas y su primer administrador, sin permisos operativos implícitos.

## Decisiones técnicas previstas

1. Añadir un código de empresa normalizado, único e inmutable. No usar el nombre editable ni un ID enviado por el cliente como autoridad.
2. Dar a la identidad de cliente un `CompanyId` persistido y reservar un ámbito de cuenta separado para el fabricante. Mantener IDs y hashes existentes. Sustituir índices globales de usuario/correo por índices compuestos con `CompanyId`; impedir credenciales ambiguas dentro de una empresa.
3. Vincular la sesión a la empresa **al autenticar**, antes de elegir sucursal. El login POS usa el `CompanyId` resuelto de la terminal; un `companyCode` enviado con esa terminal no puede cambiarlo.
4. Mantener separados los permisos de fabricante y cliente. Las rutas de alta/estado de empresas requieren sesión de fabricante; las rutas operativas requieren contexto de empresa. Un rol `administrator` de cliente nunca equivale a `superadmin`.
5. Migrar únicamente `carnicerias_test_visual` cuando la implementación esté lista. Antes de migrar, inspeccionar asignaciones que crucen empresas y conflictos de credenciales; detenerse si los datos no admiten conversión inequívoca. Respaldar la única base y preservar historia, stock, ventas, IDs y hashes.

## Orden y puntos de control

1. **Preflight de datos y contrato de migración:** auditoría no destructiva de usuarios, asignaciones y sesiones; definir política de datos ambiguos. No cambiar la base.
2. **Identidad y esquema:** código de empresa, ámbito de cuenta, pertenencia de usuario e índices por empresa; migración con fallo seguro ante conflictos.
3. **Ingreso de cliente:** búsqueda acotada y sesión ligada a empresa; contrato de terminal y errores genéricos; formulario Angular con código de empresa.

**Checkpoint A:** dos usuarios `cajero1` de empresas distintas no se confunden; uno de la empresa equivocada no entra por la otra caja. Build y pruebas unitarias/frontend pasan sin crear otra base.

4. **Fabricante:** alta segura de superadmin existente/nuevo sin secretos en Git; sesión y permisos separados; API y UI para crear empresa con primera sucursal y administrador inicial.
5. **Administrador de empresa:** crear administradores y cajeros de la propia empresa; cambiar accesos con revocación de sesiones y protección del último administrador; impedir rol fabricante.
6. **Organización acotada:** retirar alta/lista global de empresas de la administración cliente, conservar sucursales/cajas propias y exigir alcance en todas las acciones.

**Checkpoint B:** administrador A crea otro administrador A, no puede crear B ni alterar usuarios/cajas de B; superadmin puede crear B pero no realizar ventas. Revisión visual de administración en navegador y POS **en Electron**.

7. **Cierre transversal:** pruebas negativas de autorización y regresión del POS, documentación de ingreso/despliegue/manual, conciliación de los contratos anteriores incompatibles, revisión de seguridad.

**Checkpoint final:** migración aplicada solo a la base autorizada con respaldo, verificación de usuarios históricos, dos empresas de prueba y dos cajas sin mezcla de sesiones ni datos. Las pruebas de integración que crearían/reiniciarían otra base permanecen sin ejecutar y se declaran pendientes.

## Riesgos y mitigación

| Riesgo | Mitigación |
| --- | --- |
| Usuario existente asignado a varias empresas | Auditoría previa; no repartir automáticamente una identidad ni perder referencias. Pedir regla específica solo si aparece el caso. |
| Colisión entre usuario de aspecto correo y email de otro usuario | Resolver el espacio de credenciales antes de quitar índices globales; migración rechaza ambigüedad. |
| Una sesión anterior hereda un nuevo alcance | Revocar sesiones existentes al cambiar el modelo; login nuevo fija empresa. |
| Superadmin entra a endpoints de negocio por permisos heredados | Ámbito de cuenta y middleware independientes, pruebas negativas por ruta. |
| Bloqueo de API en uso o fallo de migración | Preparar build/publicación sin tocar la API abierta; respaldo y ventana controlada antes de aplicar migración. |
| Plan antiguo contradice el nuevo contrato | Conservarlo como histórico y actualizar especificaciones, tareas y manuales afectados al cerrar el cambio. |

## Supuestos a corregir si corresponde

- El superadmin se crea por instalación local; no hay portal central aún, de acuerdo con el despliegue autónomo aprobado.
- El correo puede repetirse entre empresas, pero no dentro de una empresa. La recuperación futura por correo también necesitará el código de empresa.
- El administrador de empresa puede gestionar otros administradores de su empresa, pero no puede retirar el último administrador operativo.

La modificación de autenticación y esquema fue aprobada. La aplicación de la migración a la base queda supeditada a resolver la identidad multicompañía detectada en el preflight y a un respaldo de la misma base.
