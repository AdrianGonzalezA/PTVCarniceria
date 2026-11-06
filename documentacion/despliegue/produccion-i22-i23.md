# Despliegue de I22/I23 en una sucursal

Este procedimiento acompaña la [guía de workshop](guia-workshop-por-sucursal.md). En el laboratorio actual solo se usa `carnicerias_test_visual` del contenedor existente `carnicerias-pos-test-20261007` en `127.0.0.1:55433`. No crear ni clonar otra base. En una instalación futura, sustituir los identificadores y rutas por los de esa sucursal y obtener las credenciales por el canal seguro del equipo de implementación.

## Orden de actualización

1. Cerrar turnos y operaciones pendientes; detener API y cualquier escritor. Confirmar identidad de instancia, base, volumen, conexión y último ID de `__EFMigrationsHistory`. Registrar conteos de artículos, piezas, movimientos, órdenes y sumas de `OnHand`, `Reserved`, `ProtectedOnHand`, `ProtectedReserved` y `Quarantined`.
2. Ejecutar `pg_dump -Fc` de la base existente. Verificar con `pg_restore -l`, tamaño y SHA-256. Guardar respaldo y hash fuera del servidor. No usar una prueba de restauración en otra base de Carnicerías sin autorización.
3. Aplicar, en orden, `20261106151328_AddProductionOrdersI23`, `20261106160757_AddAnatomicalGroupI22` y `20261106165321_AddAnatomicalGroupCountSnapshotI22` junto con las migraciones anteriores de la versión. En el laboratorio se usaron `tools/Deploy-I23.ps1`, `tools/Deploy-I22.ps1` y `tools/Deploy-I22CountSnapshot.ps1`, cada uno con `Backup`, `Apply` y `Verify`; sus IDs fijos impiden ejecutarlos accidentalmente contra otra base.
4. Confirmar 75 migraciones en la base de desarrollo actual; en otra instalación, comprobar que el historial termina en el último ID indicado. Comparar inventario y conteos comerciales antes y después. Las columnas de agrupador histórico deben conservar `NULL`.
5. Publicar API y Angular del mismo commit. Revisar permisos por empresa, acceso del rol Producción, ABM de clasificadores, orden abierta y consulta de piezas. La etiqueta depende de la impresión configurada en el equipo desde el cual se abre el módulo web; probar lector e impresora reales antes de operar.
6. Ejecutar aceptación controlada con una orden de prueba aislada: reserva exclusiva, cancelación, cierre, kardex y balance. Registrar evidencia, retirar solo el fixture creado y contrastar saldos. No modificar ventas ni movimientos históricos.

## Reversión

Si falla una guarda o la aplicación de esquema, detener el proceso y preservar logs y respaldo. No corregir movimientos históricos a mano. Una versión anterior de API no entiende órdenes de producción ni los nuevos vínculos de pieza; detener nuevas operaciones antes de cambiar binarios. Las migraciones descendentes impiden perder órdenes o agrupadores con datos. La restauración de respaldo requiere procedimiento y autorización específicos, nunca se ejecuta automáticamente desde los scripts.
