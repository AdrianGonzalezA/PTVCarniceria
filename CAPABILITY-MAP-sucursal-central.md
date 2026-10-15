# Mapa de capacidades: sucursales autónomas y administración central

**Estado:** aprobado por el usuario (14/10/2026), con las respuestas al final de este documento. No reemplaza el despliegue autónomo aprobado en `SPEC-despliegue-sucursal.md` ni autoriza crear otra base de desarrollo.

## Objetivo

Conservar la venta y la administración local de cada sucursal cuando la central o la conexión externa fallen, y permitir que un portal de la empresa consulte varias sucursales. La central tendrá su propia API y sitio web. Los datos comerciales operativos permanecen en la sucursal; no se replican tablas ni se mantiene una copia central de ventas, stock, pagos o clientes en esta primera etapa.

| Módulo | Responsabilidad verificable | Depende de |
|---|---|---|
| `branch-service-contract` | Identidad estable de la instalación y su única sucursal operativa; API de lectura acotada para salud, capacidades y resúmenes, con autenticación de servicio, alcance de empresa/sucursal y versión de contrato. | `platform-access`, `reporting`, despliegue por sucursal |
| `central-access` | Identidad y permisos de administradores de la empresa en la central; registro de sucursales, destinos y credenciales de conexión fuera de Git, sin reutilizar cookies ni credenciales de caja. | `branch-service-contract` |
| `central-business-view` | Consultar en vivo las sucursales autorizadas, agregar métricas compatibles y mostrar explícitamente sucursal inaccesible, dato incompleto y hora de consulta. No presentar un total parcial como total general. | `central-access`, `branch-service-contract` |
| `central-remote-admin` | Abrir operaciones administrativas de una sucursal desde la central, con autorización por acción, alcance fijo, auditoría y confirmación de cambios. No es un proxy libre hacia la API POS. | `central-access`, `branch-service-contract` |
| `branch-change-signals` | Avisos salientes de cambio/estado desde la sucursal para acelerar la actualización central. Son idempotentes y no transportan una copia de las entidades comerciales; la central vuelve a consultar el contrato de lectura. Si falla el aviso, la venta local no falla y la consulta periódica sigue disponible. | `central-business-view`, `branch-service-contract` |

## Orden propuesto

1. `branch-service-contract`: fijar identidad, autenticación y un primer resumen de solo lectura en la API local.
2. `central-access` y `central-business-view`: central por empresa, listado de sucursales, consulta en vivo y degradación visible si alguna no responde.
3. `central-remote-admin`: habilitar operaciones concretas una por una, empezando por consultas; ningún permiso global implícito.
4. `branch-change-signals`: agregar avisos salientes y conciliación por consulta, manteniendo el pull como respaldo.

Cada módulo tendrá su propia especificación y pruebas antes de implementar sus endpoints. La central no forma parte del camino crítico de confirmar una venta.

## Límites de la primera entrega

- El laboratorio sigue usando únicamente `carnicerias_test_visual` en el PostgreSQL local del puerto 55433. No se crea otra base, aunque el despliegue futuro de una central requiera decidir su propio almacén para identidades y registro de conexiones.
- La red entre central y sucursales se considera privada/VPN con HTTPS y credenciales de servicio por instalación. No se expone la API local completa a Internet ni se usa la credencial de terminal Electron para esta integración.
- Las consultas no alteran stock, caja, ventas ni facturación. Los avisos salientes no son una cola de replicación de datos de negocio.
- La restricción técnica de una sucursal operativa por instalación sigue pendiente en el código actual; el contrato deberá impedir seleccionar otra por un parámetro enviado desde la central.
- Se conservan `SPEC-despliegue-sucursal.md` y la decisión `documentacion/decisiones/0002-instalacion-por-sucursal.md` como descripción del workshop sin central. Al aprobar este mapa, una especificación y una nueva decisión documentarán la ampliación, sin reescribir el histórico.

## Respuestas del usuario

1. Acceso a la central: «usuario admin empresa». El 14/10/2026 el usuario confirmó que la cuenta y contraseña de la central serán **independientes** de las cuentas locales. No se copian usuarios, hashes, contraseñas ni sesiones entre instalaciones; el alcance empresarial y el alta inicial se detallan en [la especificación de `central-access`](SPEC-central-access.md).
   El 15/10/2026 aprobó además un **SuperAdmin propio del portal central**, perteneciente a Recursos Informáticos, para crear empresas centrales y sus administradores; no es el SuperAdmin de una instalación POS ([ADR 0006](documentacion/decisiones/0006-superadmin-propio-del-portal-central.md)). Después aprobó que el **admin central de cada empresa** gestione las conexiones de sus sucursales desde su portal, con destinos autorizados por implementación y credenciales cifradas ([ADR 0007](documentacion/decisiones/0007-conexiones-de-sucursal-gestionadas-por-admin-central.md)).
2. Primer corte de administración remota: «solo lectura». Las ediciones quedan fuera de esa entrega y requieren contratos y permisos posteriores.
3. Conectividad privada/VPN y HTTPS: «Sí». El laboratorio puede simularla localmente sin abrir la API de sucursal a Internet.
