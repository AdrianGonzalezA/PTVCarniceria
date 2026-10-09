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

- [x] ABM seguro de empresas y sucursales con inactivación restringida por operaciones pendientes.
- [x] Gestión de terminales y credenciales de un solo uso.
- [x] Pantalla de existencias/ajustes que reutilice el endpoint de movimientos existente.

### A6. Usuarios y consulta histórica

- [x] Completar alta y asignación de cajeros; conservar un único rol y usuario administrador en este corte.
- [x] Consultar ventas, pagos, turnos y movimientos por sucursal/caja/fecha, sin mutaciones transaccionales.
- [ ] Revisión integral de autorización, accesibilidad y recorrido funcional con dos Electron.

### A7. Configuración de periféricos por caja

- [ ] ABM administrativo de balanzas e impresoras, con asignación a sucursal/caja y parámetros de puerto, velocidad, bits, paridad y protocolo.
- [ ] Prueba de conexión/lectura/impresión desde la caja, estado visible y fallback de peso manual sin afectar ventas confirmadas.
- [ ] Retirar COM1/COM6 fijos del producto; el controlador virtual de desarrollo tiene licencia temporal de 14 días y no es requisito del despliegue.

### A8. Configuración ARCA por empresa (9 de octubre de 2026)

- [x] Sección **Configuración → ARCA** para `organization.manage`: CUIT, punto de venta, emisor, domicilio, IIBB e inicio de actividades de homologación.
- [x] Carga/renovación de PFX con validación de formato, clave privada y vigencia; muestra sujeto, huella y fecha de vencimiento sin devolver clave ni contraseña.
- [x] PFX y contraseña protegidos con Data Protection en la única base `carnicerias_test_visual`; la API usa esta configuración sin variables `ARCA_HOMO_*` tras reiniciar.
- [x] Nuevos intentos fiscales guardan snapshot del emisor para reimpresión; las autorizaciones existentes se consultan aunque falte el certificado actual.
- [x] API, Angular y Electron compilados y probados; carga real y recarga de ruta verificadas en Electron. Ver `SPEC-admin-arca-settings.md` y `tasks/arca-homologacion.md`.
- [ ] Verificar un **nuevo** CAE de homologación usando exclusivamente el PFX guardado, sin duplicar venta ni descontar stock al reintentar.
- [ ] Antes del despliegue: almacén de claves Data Protection persistente, protegido y respaldado; validación de datos fiscales reales y política de renovación del certificado.

## Riesgos y controles

- Cambio de catálogo durante tickets abiertos: precio/detalle congelado en borrador y prohibición de borrado físico.
- Permisos client-side: guard solo mejora UX; cada endpoint exige autorización servidor.
- Pruebas sin segunda base: unitarias/arquitectura/Angular más verificación controlada sobre la base única; cobertura de integración pendiente declarada.
- Código y datos de ventas existentes: migraciones aditivas, no truncar ni recrear la base.

## Punto de control

Tras A2: compilar backend/frontend, pruebas disponibles verdes, catálogo del POS intacto, UI administrativa revisada en Electron, commits en Git y sin bases nuevas.
