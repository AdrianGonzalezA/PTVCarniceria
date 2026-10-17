# Tareas de POS UI v2 — Nocturna Touch

**Fuente:** `tasks/ui-v2-plan.md` y `UI2/RI-Nocturna-Touch/INTEGRAR-EN-CODEX.md`.

## 1. Base y referencias

- [x] Verificar en Electron la composición del POS con catálogo a la izquierda, compra a la derecha y controles conservados. Verificar: coordenadas de ambos paneles y pruebas funcionales. Dependencia: ninguna. Evidencia: `tasks/ui-v2-plan.md`.
- [x] Versionar la referencia `UI2` y el plan, sin datos ni JavaScript demo en el bundle de la aplicación. Verificar: `git diff --cached`, sin secretos. Dependencia: ninguna. Archivos: `UI2/RI-Nocturna-Touch/*`, `tasks/ui-v2-*`.

## 2. Shell y catálogo

- [ ] Aplicar encabezado compacto, paleta grafito/cobre y columnas ticket/catálogo manteniendo turno, cuenta, stock y menú. Verificar: pruebas POS, build y Electron; foco visible. Dependencia: 1. Archivos: `pos-page.html`, `pos-page.scss`, `pos-page.spec.ts`.
- [x] Mantener categorías reales en botones verticales desplazables en el extremo izquierdo, conservando búsqueda y lector. Verificar: columna y overflow vertical en Electron, pruebas de búsqueda y código. La indicación posterior del usuario reemplazó la fila horizontal el 16/10/2026. Archivos: `pos-page.html`, `pos-page.scss`, `pos-page.spec.ts`.
- [ ] Compactar tarjetas a 64–70 px, con nombre, precio/unidad y disponibilidad sin datos ficticios nuevos. Verificar: productos por peso/unidad, nombres largos, stock cero, tests y medición en Electron. Dependencia: 2. Archivos: `pos-page.html`, `pos-page.scss`, `pos-page.spec.ts`.

### Checkpoint A

- [ ] El catálogo real puede recorrerse por touch, teclado y lector; nada depende de hover.
- [ ] La aplicación compila, pasan las pruebas POS y la revisión visual se hace en Electron.

## 3. Ticket y editor

- [ ] Mantener tickets A–D en la barra gris inferior del catálogo y lista, comprobante y cliente en el panel derecho; compactar renglones sin perder editar/quitar, pieza, descuentos o alertas. Total y cobro permanecen visibles con scroll independiente. Verificar: tests de tickets, borrador y controles; Electron con ticket lleno. Dependencia: Checkpoint A. Archivos: `pos-page.html`, `pos-page.scss`, `pos-page.spec.ts`.
  - Avance: detalle convertido en tabla semántica de cinco columnas; compra ensanchada a 540 px en Electron maximizado. Pestañas A–D bajo las categorías, con botones de 134 × 60 px y marca «Guardado» legible; la tabla conservó 65 px adicionales de altura al salir las pestañas del panel de compra. Cinco renglones reales de La Rústica ocupan 76 px cada uno, sin desplazar total/cobro. Falta revisar tamaños menores y estados de error visuales.
- [ ] Integrar editor inline en ticket con entrada manual táctil para kg/unidades, sin duplicar estado ni lectura de balanza. Conservar etiqueta trazable y sus kilos. Verificar: tests de cantidad inválida, balanza, lector, cancelar/cambiar ticket y foco; Electron. Dependencia: ticket compacto. Archivos: `pos-page.html`, `pos-page.scss`, `pos-page.ts`, `pos-page.spec.ts`.

### Checkpoint B

- [ ] Un corte se agrega y deja el catálogo listo para el siguiente; ninguna entrada parcial pasa a otro ticket.
- [ ] Borrador, stock y cobro preservan su comportamiento y sus mensajes de error.

## 4. Acabado y aceptación

- [ ] Ajustar 1366 × 768 y 1920 × 1080 CSS px, y DPI 125 %/150 % cuando esté disponible; registrar densidad real (viewport, filas, columnas, altura) sin zoom CSS. Verificar: capturas de Electron, navegación por teclado, contraste y scroll táctil. Dependencia: Checkpoint B. Archivos: `pos-page.scss`, pruebas focalizadas, registro de verificación.
- [ ] Recorrer venta real, tickets A–D, stock, pesaje, factura/ticket, pago e impresión disponible; actualizar el manual solo con lo verificado. Verificar: suite Angular, lint, build y Electron. Dependencia: acabado. Archivos: manual de usuarios y evidencias.

### Checkpoint final

- [ ] Todas las funciones previas siguen accesibles y la venta no utiliza el estado demo de `UI2`.
- [ ] Capturas y medidas de Electron documentadas; límites de hardware y DPI no probado declarados.
- [ ] Revisión de diff antes de integrar `feature/ui-v2` a `main`.

## 5. Temas por perfil local — plan propuesto

**Fuente:** `CAPABILITY-MAP-visual-themes.md` y `SPEC-visual-theme-selection.md`. Esta sección extiende el trabajo UI2 en la **misma rama**; no sustituye las tareas anteriores. No habilitar el selector al público hasta que Original y Nocturno se rendericen correctamente.

- [x] **T1 · Preferencia local y registro:** IDs Original/Nocturno, lectura y escritura por perfil, valor Original ante ausencia, ID inválido o error de almacenamiento. Aplicar el ID antes del primer render sin tocar sesión ni ventas. Dependencia: ninguna. Verificación: 6 pruebas focalizadas, 163 Angular, lint y build correctos el 17/10/2026. Archivos: `core/theme/visual-theme.ts`, `core/theme/visual-theme.spec.ts`, `main.ts` (3). El selector sigue oculto hasta completar T2 y T3.
- [x] **T2 · POS Original/Nocturno:** `SPEC-pos-visual-themes.md` aprobada; Original recupera la distribución previa a UI2 y Nocturno conserva UI2 con un solo estado de venta. Dependencia: T1. Verificación del 17/10/2026: ticket real A de cinco líneas en Electron en ambos temas, cambio A–B–A y recuperación tras recarga; 39 pruebas POS, 166 Angular, 88 Electron, lint y build correctos. La aceptación visual del usuario permanece en el checkpoint final.
  - [x] **T2a · Estilos aislados:** Original usa la base anterior y Nocturno la capa global `html[data-visual-theme="nocturno"]`; ambos medidos en Electron. Dependencia: T1. Archivos: `pos-page.scss`, `pos-page.spec.ts` (2).
  - [x] **T2b · Contenido y detalle:** marca, stock visible y tabla de seis columnas en Original; Nocturno mantiene tabla compacta. Una sola barra A–D cambia de ubicación por CSS. Pruebas POS, lint y build correctos. Dependencia: T2a. Archivos: `pos-page.html`, `pos-page.scss`, `pos-page.spec.ts` (3).
  - [x] **T2c · Electron y regresión:** ticket vacío y cinco líneas revisados a 1366 × 768 y 1920 × 992 CSS px; ambos temas comprobados también a 800 × 768 y 560 × 768 con paneles apilados, A–D y cobro accesibles. Stock cero explícito en Original, scroll interno y menú Nocturno corregidos. La prueba de DPI físico 125 %/150 % queda en la tarea general de acabado UI2. Dependencia: T2b. Archivos: `pos-page.scss`, `pos-page.spec.ts`, `tests/inspect-electron.mjs`, `tasks/ui-v2-plan.md` (4).
- [ ] **T3 · Administración Original/Nocturno:** especificación, plan y lista T3.1–T3.16 aprobados para la aplicación de sucursal/empresa y SuperAdmin local; el portal Central queda fuera. Dependencia: T1 y T2. El selector permanece oculto hasta T4.
- [ ] **T4 · Selector en Configuración:** mostrar opciones y alcance local; cambio inmediato sin recargar ni tocar ticket, persistencia tras reinicio. Dependencias: T1–T3. Verificar: prueba de la pantalla administrativa y prueba en Electron/navegador como perfiles separados; suite Angular, lint y build. Archivos previstos: `admin-page.html`, `admin-page.ts`, `admin-page.scss`, `admin-page.spec.ts` (4).

### T3 · Lista ejecutable aprobada

**Base de archivos:** `src/Carnicerias.Web/src/app/features/`. Las rutas de archivos de cada tarea son relativas a esa carpeta. Se ejecutará `npm test --prefix src/Carnicerias.Web -- --watch=false --include=**/NOMBRE.spec.ts` para la prueba focalizada indicada; en cada checkpoint se ejecutarán además `npm test --prefix src/Carnicerias.Web -- --watch=false`, `npm run lint --prefix src/Carnicerias.Web` y `npm run build --prefix src/Carnicerias.Web`. La inspección del administrador puede hacerse en navegador o Electron; cualquier inspección visual del POS será **solo en Electron**. No se crean ni reinician bases.

- [x] **T3.1 · Shell de Negocio y Configuración.** Tokens Nocturno en el host compartido y alcance del selector compilado comprobados en ambas rutas. El SCSS global Nocturno del POS se acotó a `app-pos-page` tras detectar que ocultaba la marca administrativa.
  - Aceptación: `/admin` y `/admin/configuracion` cambian superficies, texto y cabecera sin recargar; Original, POS y `/` no cambian.
  - Verificar: `admin-page.spec.ts`, build y color calculado/captura de ambas rutas con ambos temas.
  - Dependencias: T1, T2 y plan T3 aprobado. Archivos previstos (2): `admin/admin-page.scss`, `admin/admin-page.spec.ts`.
  - Evidencia 17/10/2026: Original `#111`/cabecera `#3d3d3d`/panel `#ededed`; Nocturno `#181b20`/`#24282e`/`#24282e`, medidos en Chromium aislado a 1366 × 768. Capturas de ambas rutas revisadas. La prueba de colisión de marca falló antes y pasó después; el POS Nocturno se inspeccionó en Electron con cinco renglones y total $47.554,00, luego se restauró Original. Suite Angular 167/167, pruebas POS 39/39, lint y build correctos. Sin cambios de venta o base.
- [x] **T3.2 · Navegación entre áreas.** Pestañas Negocio/Configuración y foco tematizados sin cambiar su navegación.
  - Aceptación: área activa y foco se distinguen en ambos temas; las rutas y `aria-current` se conservan.
  - Verificar: `admin-area-navigation.spec.ts` y navegación por teclado en ambas áreas.
  - Dependencia: T3.1. Archivos previstos (2): `admin/admin-area-tabs.ts`, `admin/admin-area-navigation.spec.ts`.
  - Evidencia 17/10/2026: prueba focalizada rojo/verde (3/3); en `/admin` y `/admin/configuracion` el subrayado y contorno del foco de teclado son turquesa en Original y cobre en Nocturno, con `aria-current` y foco conservados durante el cambio sin recarga. Build y lint correctos.
- [x] **T3.3 · Superficies comunes de ABM.** Paneles, tablas, búsquedas, formularios y mensajes compartidos tematizados.
  - Aceptación: Categorías muestra tabla y formulario legibles, incluidos vacío/error; Original no cambia y otras páginas que importan el SCSS reciben los mismos tokens.
  - Verificar: `categories-page.spec.ts`, captura con tabla poblada y control de desborde a 800/560 px.
  - Dependencia: T3.1. Archivos previstos (2): `admin/categories-page.scss`, `admin/categories-page.spec.ts`.
  - Evidencia 17/10/2026: prueba focalizada rojo/verde; Categorías con 10 registros reales de prueba revisada en Original y Nocturno a 1366 × 768, 800 × 768 y 560 × 768 en Chromium aislado. A 560 px, la tabla desplaza 623 px dentro de 511 px y la última acción es alcanzable sin desbordar el documento. Vacío y error cubiertos con HTTP simulado, sin alterar datos. Los controles de importación quedan para T3.5.

**Checkpoint T3-A (17/10/2026):** shell, pestañas y Categorías comprobados en dos temas; 170/170 pruebas Angular, lint y build correctos. `:host-context()` coincide con el atributo raíz en runtime. Build mantiene advertencias de presupuesto CSS para `admin-page.scss` y `categories-page.scss`, además de la advertencia preexistente de `qrcode` CommonJS; no se relajó el presupuesto.

- [x] **T3.4 · Diálogo compartido.** `AdminDetailDialog` tiene superficie, borde, texto y foco Nocturno.
  - Aceptación: un modal abierto se actualiza al cambiar el tema sin cerrarse ni perder valores; título, cierre, error y scroll interno son legibles.
  - Verificar: `admin-detail-dialog.spec.ts` y prueba visual de alta/edición en un ABM.
  - Dependencias: T3.1 y T3.3. Archivos previstos (2): `admin/admin-detail-dialog.scss`, `admin/admin-detail-dialog.spec.ts`.
  - Evidencia 17/10/2026: prueba focalizada rojo/verde (4/4); el modal de nueva categoría se mantuvo abierto con título y valor escrito al alternar Original/Nocturno, comprobado a 1366 × 768 y 560 × 768 en Chromium aislado. No se guardó la categoría; build correcto.
- [x] **T3.5 · Importación Excel compartida.** Plantilla, selección de archivo, progreso y resultado usan los tokens Original/Nocturno.
  - Aceptación: controles y mensajes de importación se leen en ambos temas sin ejecutar una importación de prueba; el comportamiento existente no cambia.
  - Verificar: `admin-excel-import-actions.spec.ts` y modal de importación abierto en una página de catálogo.
  - Dependencias: T3.3 y T3.4. Archivos previstos (2): `admin/admin-excel-import-actions.scss`, `admin/admin-excel-import-actions.spec.ts`.
  - Evidencia 17/10/2026: prueba focalizada rojo/verde (3/3), sin solicitudes de importación; modal abierto con selector vacío y confirmación deshabilitada en ambos temas a 1366 × 768 y 560 × 768. Texto, botones y foco del selector comprobados en Chromium aislado; build correcto. La previsualización y los resultados conservan sus estados y recibieron tokens, pero no se ejecutó una carga real para probar colores.
- [ ] **T3.6 · Paneles de impuestos reutilizables.** Tematizar catálogo y asignación de gravámenes.
  - Aceptación: filas, selección, validación y estados vacíos/error de ambos paneles se distinguen; Original se conserva.
  - Verificar: `tax-catalog-panel.spec.ts`, `tax-assignment-panel.spec.ts` y modal/tabla de `/admin/taxes`.
  - Dependencias: T3.3 y T3.4. Archivos previstos (4): `admin/tax-catalog-panel.scss`, `admin/tax-catalog-panel.spec.ts`, `admin/tax-assignment-panel.scss`, `admin/tax-assignment-panel.spec.ts`.

**Checkpoint T3-B:** diálogo, Excel e impuestos comprobados con tema cambiado en un modal abierto; suite Angular, lint y build en verde.

- [ ] **T3.7 · Artículos y listas de precios.** Corregir estilos específicos que no hereden de los ABM comunes.
  - Aceptación: tablas, precio/costo, vigencias, botones de fila y modales son legibles en Nocturno; Original no cambia.
  - Verificar: `products-page.spec.ts`, `price-lists-page.spec.ts` y ambas rutas con registros existentes.
  - Dependencias: T3.3–T3.5. Archivos previstos (4): `admin/products-page.scss`, `admin/products-page.spec.ts`, `admin/price-lists-page.scss`, `admin/price-lists-page.spec.ts`.
- [ ] **T3.8 · Clientes.** Cubrir búsqueda, tabla y alta/edición de clientes.
  - Aceptación: `/admin/customers` no deja superficies claras involuntarias y conserva todos los campos/acciones; el modal sigue el tema.
  - Verificar: `customers-page.spec.ts` y tabla/modal en ambos temas. Crear hoja propia solo si los estilos comunes no alcanzan.
  - Dependencias: T3.3 y T3.4. Archivos previstos (hasta 3): `admin/customers-page.ts`, `admin/customers-page.spec.ts`, `admin/customers-page.scss` (nuevo solo si hace falta).
- [ ] **T3.9 · ARCA y pasarelas.** Tematizar certificados, estados de integración y formularios de cobro.
  - Aceptación: datos sensibles ya enmascarados, errores, botones y formularios siguen legibles; no se conecta ni emite nada para verificar colores.
  - Verificar: `arca-settings-page.spec.ts`, `payment-gateways-page.spec.ts` y ambas rutas en dos temas.
  - Dependencia: T3.3. Archivos previstos (4): `admin/arca-settings-page.scss`, `admin/arca-settings-page.spec.ts`, `admin/payment-gateways-page.scss`, `admin/payment-gateways-page.spec.ts`.

**Checkpoint T3-C:** Artículos, Precios, Clientes, ARCA y Pasarelas comprobados en Original/Nocturno; suite Angular, lint y build en verde.

- [ ] **T3.10 · Organización y conexión central de la sucursal.** Tematizar empresas/cajas y paquete de conexión sin cambiar la integración.
  - Aceptación: formularios, estados y credenciales ya ocultas se leen en ambas rutas sin revelar secretos ni cambiar datos.
  - Verificar: `organization-page.spec.ts`, `central-connection-page.spec.ts` y ambas rutas en dos temas.
  - Dependencia: T3.3. Archivos previstos (4): `admin/organization-page.scss`, `admin/organization-page.spec.ts`, `admin/central-connection-page.scss`, `admin/central-connection-page.spec.ts`.
- [ ] **T3.11 · Impuestos por artículo y formatos de códigos.** Cubrir dos pantallas con tablas y editores especializados.
  - Aceptación: asociaciones tributarias, layouts de barra y validaciones son legibles sin alterar las reglas de negocio.
  - Verificar: `product-tax-page.spec.ts`, `barcode-layout-page.spec.ts` y `/admin/taxes`, `/admin/barcode-layouts`.
  - Dependencias: T3.3 y T3.6. Archivos previstos (4): `admin/product-tax-page.scss`, `admin/product-tax-page.spec.ts`, `admin/barcode-layout-page.scss`, `admin/barcode-layout-page.spec.ts`.
- [ ] **T3.12 · Existencias y recepción de piezas.** Cubrir ajustes, importación de stock y trazabilidad.
  - Aceptación: tablas, cantidades y errores se leen en ambos temas; se pueden abrir modales sin guardar movimientos.
  - Verificar: `stock-page.spec.ts`, `piece-receipt-page.spec.ts` y ambas rutas en dos temas.
  - Dependencias: T3.3 y T3.4. Archivos previstos (4): `admin/stock-page.scss`, `admin/stock-page.spec.ts`, `admin/piece-receipt-page.scss`, `admin/piece-receipt-page.spec.ts`.

**Checkpoint T3-D:** Organización, conexión, impuestos, códigos, existencias y piezas comprobados; suite Angular, lint y build en verde.

- [ ] **T3.13 · Historial y cuentas corrientes.** Tematizar resúmenes, importes, filtros y estados.
  - Aceptación: historial, movimientos y saldos conservan jerarquía y signos legibles en ambos temas; no se registra ningún cobro.
  - Verificar: `history-page.spec.ts`, `accounts-page.spec.ts` y ambas rutas con datos existentes.
  - Dependencia: T3.3. Archivos previstos (4): `admin/history-page.scss`, `admin/history-page.spec.ts`, `admin/accounts-page.scss`, `admin/accounts-page.spec.ts`.
- [ ] **T3.14 · Usuarios de empresa.** Tematizar listado, búsqueda, roles y modales de acceso.
  - Aceptación: estados activo/inactivo, errores y foco son legibles; no cambia autorización ni se crea/resetea un usuario para probar colores.
  - Verificar: `users-page.spec.ts` y `/users` con modal abierto en ambos temas.
  - Dependencias: T3.1 y T3.4. Archivos previstos (2): `users/users-page.scss`, `users/users-page.spec.ts`.
- [ ] **T3.15 · SuperAdmin local.** Tematizar tabla de empresas, modales y formulario de acceso propio.
  - Aceptación: Nocturno cubre estados autenticado/no autenticado sin cambiar permisos; Original conserva su diseño.
  - Verificar: `manufacturer-page.spec.ts` y `/superadmin` en ambos estados y temas, sin mutar empresas.
  - Dependencias: T3.1 y T3.4. Archivos previstos (2): `manufacturer/manufacturer-page.scss`, `manufacturer/manufacturer-page.spec.ts`.

**Checkpoint T3-E:** Historial, Cuentas, Usuarios y SuperAdmin comprobados; suite Angular, lint y build en verde.

- [ ] **T3.16 · Auditoría de alcance.** Recorrer las 18 rutas, estados representativos y tamaños de la especificación; corregir hallazgos mediante cortes adicionales de hasta cinco archivos antes de cerrar T3.
  - Aceptación: contraste normal ≥4,5:1, foco visible, acciones accesibles a 560 px, ausencia de desborde global y de cambios accidentales en POS, `/` o Central; selector aún oculto.
  - Verificar: matriz visual Original/Nocturno a 1920 × 992, 1366 × 768, 800 × 768 y 560 px, consola, suite Angular, lint, build y POS solo en Electron.
  - Dependencias: T3.1–T3.15. Archivos previstos (2 de documentación): `tasks/ui-v2-plan.md`, `tasks/ui-v2-todo.md`; cualquier corrección de código será un subcorte separado.

**Checkpoint T3 final:** criterios de `SPEC-admin-visual-themes.md` y Definition of Done satisfechos, registro de tiempo actualizado, diff revisado y rama publicada. T3 no habilita el selector ni fusiona `feature/ui-v2` a `main`; eso corresponde a T4 y a la aceptación visual del usuario.

### Checkpoint de temas

- [ ] Original reproduce la interfaz anterior y Nocturno la actual; ambos preservan la venta y los tickets A–D.
- [ ] Las pantallas administrativas, sus diálogos y estados de error son legibles en ambos temas y funcionan con teclado.
- [ ] Un equipo puede conservar su elección sin cambiar la de otro; una preferencia inválida vuelve a Original.
- [ ] El usuario revisó y aceptó las dos variantes antes de integrar esta rama a `main`.
