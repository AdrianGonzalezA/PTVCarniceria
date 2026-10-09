# Plan: importación Excel de maestros

**Estado:** implementado y verificado el 9/10/2026; integración con base adicional pendiente por la regla de base única. Contrato: `SPEC-admin-excel-import.md`. Este plan no modifica los planes históricos de `tasks/`.

## Orden de entrega

1. Separar costo estadístico del precio y conservar vigencias de costo en la única base existente.
2. Incorporar lectura/creación segura de `.xlsx`, plantillas con ejemplos aislados y validación de estructura.
3. Entregar previsualización y aplicación transaccional de categorías y clientes.
4. Extender a artículos/códigos alternativos, con artículos existentes marcados como no importados.
5. Extender a listas/precios/sucursales, conservando vigencias de precio.
6. Integrar los cuatro ABM con el modal de importación, probar y recorrer en Electron.

## Límites y verificación

- Nunca crear otra base ni ejecutar pruebas que la creen o reinicien. La única base es `carnicerias_test_visual` en puerto 55433.
- El archivo es entrada no confiable: límite de tamaño/filas, `.xlsx` real, sin fórmulas, macros ni vínculos externos; autorización y empresa derivadas de la sesión.
- Vista previa sin escritura. Aplicación atómica, con clave idempotente y revalidación para concurrencia.
- Pruebas puras y de frontend, compilaciones, SQL de migración revisado y prueba manual acotada en la base existente.
- Revisar visualmente POS sólo en Electron. No incluir secretos ni `tasks/nuevasfuncionalidades.md` en commits.
