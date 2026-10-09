# Plan de implementación: catálogo de impuestos y asignaciones

**Estado (9/10/2026): aprobado; implementación funcional realizada, verificación de integración restringida.** Alcance aprobado en `SPEC-tax-catalog.md`. Lista ejecutable: `tasks/tax-catalog-todo.md`. Este plan no sustituye `tasks/plan.md` ni `tasks/todo.md`, que conservan tareas abiertas de platform access.

## Objetivo

Agregar a Configuración un catálogo reutilizable de impuestos/gravámenes por empresa, con asignación a uno, varios o todos los artículos existentes. Mantener el IVA que alimenta ventas y ARCA en homologación. Los demás gravámenes serán configurables y auditables, pero no calculados en ventas hasta especificar su semántica fiscal.

## Decisiones de arquitectura

- Extender el modelo de IVA existente en vez de reemplazar `product_tax_rules`: sus IDs, vigencias y snapshots de ventas ya son contratos persistidos. Una referencia opcional a la entrada de catálogo vinculará las reglas gravadas; exento/no alcanzado seguirán explícitos.
- Mantener entradas de catálogo por empresa con código, tipo y tasa inmutables. Inactivar no borra referencias. La migración creará entradas IVA por cada tasa gravada ya existente y vinculará versiones históricas sin alterar tasas, fechas ni actores.
- Representar gravámenes `otro` con asignaciones vigentes/históricas separadas de la regla IVA. Así no llegan accidentalmente al calculador actual ni a ARCA.
- Para una asignación masiva, el servidor resuelve todos los artículos de la empresa (también inactivos), toma bloqueo/usa transacción y aplica el mismo estado final a cada uno. La operación repetida no crea nuevas versiones. Un conflicto revierte todo.
- Conservar los endpoints actuales; nuevas respuestas y rutas serán aditivas, con permiso `catalog.manage`, control de origen, validación y errores uniformes. La UI dejará visible la diferencia entre “IVA aplicado” y “gravamen configurado, no calculado”.

## Dependencias y orden

```text
Catálogo persistente → API catálogo → UI catálogo
          ↓
Vínculo de reglas IVA → asignación IVA en API/UI
          ↓
Asignaciones de otros gravámenes → API/lectura UI → asignación masiva
          ↓
Verificación Electron/única base → documentación → revisión Git y publicación
```

La base actual `carnicerias_test_visual` (puerto 55433) es la única base de la aplicación permitida. Antes de aplicar migraciones se revisarán el SQL y el estado del esquema; no se ejecutará la suite que provisiona o reinicia otra base.

## Tareas

1. Persistir catálogo por empresa y migrar las reglas IVA existentes sin reescribir ventas.
2. Exponer ABM seguro del catálogo y cubrir sus invariantes con pruebas permitidas.
3. Mostrar el catálogo en Configuración, con alta, consulta e inactivación.
4. Vincular selección de IVA desde el catálogo a las reglas vigentes e historial por artículo.
5. Persistir asignaciones versionadas de gravámenes no IVA.
6. Exponer consulta y cambio individual de esas asignaciones; presentarlas en la UI como no calculadas.
7. Aplicar y retirar una entrada a selección múltiple o todos los artículos existentes de manera atómica, con confirmación y conteo en la UI.
8. Ejecutar pruebas, revisar administrador y POS dentro de Electron, verificar la base única sin crear otra y actualizar documentación relevante.
9. Auditar el diff previo y nuevo para secretos/regresiones, separar commits lógicos y publicar en `origin` sólo archivos revisados.

Cada tarea tiene criterios, verificación, dependencias y archivos previstos en `tasks/tax-catalog-todo.md`. El punto de control tras las tareas 1–3 verifica catálogo; tras 4–7, asignaciones/compatibilidad fiscal; el último comprueba documentación y publicación.

## Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Alterar IVA de ventas históricas o solicitud ARCA | Migración aditiva; no tocar snapshots; probar equivalencia de reglas actuales y ventas nuevas. |
| Cambio parcial en “todos” o cruce de empresa | Conjunto resuelto en servidor, claves compuestas, transacción y pruebas de fallo/reintento. |
| Cobrar un gravamen sin cálculo definido | Modelo/API separados del calculador y rótulo explícito en UI/documentos. |
| Confundir tasa ficticia 21 % con política fiscal real | Mantener advertencia de homologación y no usarla como predeterminado para nuevos artículos. |
| Trabajo local previo o secretos en Git | Revisar diff y exclusiones, preservar `tasks/nuevasfuncionalidades.md`, stage explícito y commits separados. |
| Pruebas existentes que crean otra base | No ejecutarlas; dejar la cobertura de integración pendiente documentada. |

## Documentación y publicación previstas

Actualizar `SPEC-tax-catalog.md`, `SPEC-fiscal-payments.md`, `SPEC-admin.md`, `SPEC-catalog-pricing.md`, `README.md` y el estado pertinente en `tasks/arca-homologacion.md`; registrar la razón del modelo en una decisión de arquitectura si no existe una convención previa. Corregir textos que aún describen el IVA como sólo editable artículo por artículo. No modificar documentos de capacidades ajenas por mera exhaustividad.

El árbol de trabajo ya contiene cambios de facturación/Electron anteriores a este plan. Se revisarán y probarán antes de decidir su commit; no se incluirán archivos personales o material de ARCA sensible por un `git add .` indiscriminado. El usuario pidió actualizar Git, por lo que el cierre previsto incluye commit y push, sujetos a controles de calidad y a que el repositorio remoto acepte la publicación.

## Criterio de cierre

Todos los criterios de `SPEC-tax-catalog.md` pasan, la UI se recorre en Electron, el IVA actual conserva sus resultados, ningún gravamen adicional altera ventas/ARCA, la documentación coincide con el comportamiento y los commits publicados contienen sólo cambios revisados.

## Conciliación de planes pendiente

El usuario aprobó avanzar y pidió conciliar más adelante este plan con `tasks/plan.md` y `tasks/todo.md`. Esos dos archivos nacieron para *platform access* y contienen estados antiguos de capacidades hoy implementadas, además de tareas realmente abiertas. No se marcan completas ni se eliminan en bloque: la conciliación deberá contrastar cada criterio con código y evidencia, separar pendientes actuales y acordar una fuente única de seguimiento. Esta revisión documental no bloquea el catálogo de impuestos aprobado.
