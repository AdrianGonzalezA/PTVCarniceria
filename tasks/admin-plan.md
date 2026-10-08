# Plan de implementación: administrador

La prioridad pasa al sitio administrativo sin modificar la base única `carnicerias_test_visual`. Este plan convive con `tasks/plan.md` y `tasks/pos-todo.md`; no sustituye sus pendientes. El usuario autorizó avanzar sin preguntas intermedias salvo una regla de negocio indispensable.

## Decisiones

- Reutilizar sesión, contexto y rol `administrator`. Separar permiso `catalog.manage` para escrituras de catálogo y otorgarlo al rol existente con una migración de datos aditiva en la misma base.
- Mantener las transacciones confirmadas inmutables; “baja” de maestros referenciados significa inactivación.
- Construir cortes verticales API + Angular + pruebas, revisando UI exclusivamente en Electron. Conservar todos los scripts y migraciones en Git.
- No ejecutar la suite de integración actual porque exige una segunda base; reemplazarla gradualmente por pruebas compatibles con una sola base o solicitar autorización expresa para otra estrategia.

## Tareas

### A1. Acceso y estructura administrativa

- [x] Ruta `/admin`, guard y navegación para `visual-admin`, sin acceso para cajeros.
- [x] Pantalla base alineada con la maqueta, secciones distinguibles y enlaces al POS/contexto.
- [ ] Verificar rutas y estados de permiso con Angular; compilar y recorrer en Electron.

### A2. Categorías completas

- [x] Permiso `catalog.manage` persistido y otorgado al administrador existente sin otra base.
- [x] API de listado, alta y edición/estado con aislamiento por empresa, validación, duplicados y protección de origen.
- [x] Listado y formulario Angular con carga, vacío, error, confirmación de inactivación y prueba de regresión.
- [ ] Verificar por API y Electron con datos ficticios de la base existente; no tocar ventas.

### A3. Productos y códigos

- [x] Listado de todos los productos, incluidos inactivos, con filtros y paginación.
- [x] Alta/edición de categoría, denominación, unidad, modalidad, costo y estado; código principal inmutable.
- [x] ABM de códigos alternativos sin colisiones y con historial de uso preservado.
- [ ] Formulario alineado a `Articulo agregar- desktop.png`; pruebas de validación y reflejo en POS.

### A4. Listas y precios

- [x] ABM de listas de precios y habilitación por sucursal.
- [x] Cambio de precio con vigencia/historial y regla precio ≥ costo, sin alterar tickets ya guardados.
- [ ] Pruebas de visibilidad por sucursal y selección en POS.

### A5. Organización y stock

- [ ] ABM seguro de empresas y sucursales con inactivación restringida por operaciones pendientes.
- [ ] Gestión de terminales y credenciales de un solo uso.
- [ ] Pantalla de existencias/ajustes que reutilice el endpoint de movimientos existente.

### A6. Usuarios y consulta histórica

- [ ] Completar alta y asignación de usuarios; conservar un único rol administrador en este corte.
- [ ] Consultar ventas, pagos, turnos y movimientos por sucursal/caja/fecha, sin mutaciones transaccionales.
- [ ] Revisión integral de autorización, accesibilidad y recorrido funcional con dos Electron.

## Riesgos y controles

- Cambio de catálogo durante tickets abiertos: precio/detalle congelado en borrador y prohibición de borrado físico.
- Permisos client-side: guard solo mejora UX; cada endpoint exige autorización servidor.
- Pruebas sin segunda base: unitarias/arquitectura/Angular más verificación controlada sobre la base única; cobertura de integración pendiente declarada.
- Código y datos de ventas existentes: migraciones aditivas, no truncar ni recrear la base.

## Punto de control

Tras A2: compilar backend/frontend, pruebas disponibles verdes, catálogo del POS intacto, UI administrativa revisada en Electron, commits en Git y sin bases nuevas.
