# ADR 0006: SuperAdmin propio del portal central

**Estado:** aceptada, 15/10/2026.

## Contexto

La administración central permite consultar sucursales de una empresa, pero dar de alta cada nueva empresa y sus administradores solo por CLI resulta poco práctico. El POS ya tiene un SuperAdmin local; su identidad y base de autorización pertenecen a una instalación física, no a la central.

## Decisión

La central incorpora un SuperAdmin **independiente del POS y de los administradores de empresa central**. Su primer usuario se crea por un comando interactivo protegido. Usa tablas, sesión y cookie propias dentro de `central_access`. En `/superadmin` puede listar y crear empresas centrales con administrador inicial, y listar/agregar administradores de la empresa elegida. El alta de sucursales, destinos y secretos continúa exclusivamente por procedimiento de implementación, sin campos ni endpoints web para esos valores.

En desarrollo, la migración usa la única base `carnicerias_test_visual`; no se crea una segunda base. Cada empresa central conserva sus propios administradores, que solo ven sus sucursales y negocio. La cuenta SuperAdmin central no abre cajas ni administra ventas del POS.

## Consecuencias

- Hay dos autoridades centrales separadas: SuperAdmin de plataforma y administradores de empresa. El SuperAdmin local del POS no puede entrar en ninguna de ellas por compartir nombre o contraseña.
- Las altas de empresas desde la web quedan auditadas con el identificador del SuperAdmin y ejecutan empresa + primer administrador como una operación atómica.
- La recuperación de la primera cuenta SuperAdmin usa un procedimiento CLI protegido que invalida sus sesiones anteriores. Las contraseñas no se imprimen, registran ni guardan en Git.
- La carga y rotación de credenciales de sucursal no se amplían al navegador; la protección contra destinos arbitrarios se mantiene en el CLI.
