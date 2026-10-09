# Base del manual de usuarios de Carnicerías

Esta carpeta describe **las funciones disponibles en el corte del 9 de octubre de 2026**. Es una base de trabajo para redactar el manual definitivo, no una certificación de aptitud productiva. Los ejemplos de la instalación actual usan datos ficticios; ARCA está en homologación y Mercado Pago usa credenciales de prueba.

## Recorrido de lectura

| Necesidad | Documento |
| --- | --- |
| Ingresar, elegir sucursal y operar una caja/turno | [01 · Acceso y turnos](01-acceso-y-turnos.md) |
| Armar tickets, usar lector/balanza y controlar stock | [02 · Venta en el POS](02-venta-en-el-pos.md) |
| Cobrar, emitir comprobantes de prueba y cobrar cuentas | [03 · Cobros y comprobantes](03-cobros-y-comprobantes.md) |
| Consultar ventas, cuentas e inventario desde administración | [04 · Negocio](04-negocio.md) |
| Mantener maestros, usuarios e integraciones | [05 · Configuración](05-configuracion.md) |
| Interpretar mensajes y conocer límites del corte | [06 · Límites y problemas frecuentes](06-limites-y-problemas-frecuentes.md) |

## Perfiles y entornos

- **Cajero:** vende desde una caja identificada en Electron y abre su propio turno. No debe compartir su usuario con el siguiente responsable.
- **Administrador:** trabaja en **Negocio** y **Configuración** con los permisos asignados. La administración puede abrirse en navegador cuando la interfaz web esté servida; el POS se revisa y opera en Electron.
- Las opciones visibles dependen de los permisos de la sesión. Una sucursal puede tener varias cajas, y cada caja conserva sus propios turnos y tickets A–D. El stock pertenece a la sucursal y es compartido.

## Criterio editorial para el manual definitivo

Se documenta la operación observable en pantalla. Los contratos técnicos y las decisiones futuras siguen en los archivos `SPEC-*` del repositorio; no se mezclan con instrucciones para cajeros. Antes de publicar un manual de producción faltará sustituir los ejemplos ficticios, validar reglas fiscales con el responsable contable, incorporar capturas hechas en Electron para el POS y revisar cada procedimiento con usuarios de caja y administración. No agregar usuarios, contraseñas, tokens, certificados ni credenciales de terminal a esta carpeta.
