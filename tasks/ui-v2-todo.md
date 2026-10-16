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

- [ ] Mantener tickets A–D, lista, comprobante y cliente en el panel derecho; compactar renglones sin perder editar/quitar, pieza, descuentos o alertas. Total y cobro permanecen visibles con scroll independiente. Verificar: tests de tickets, borrador y controles; Electron con ticket lleno. Dependencia: Checkpoint A. Archivos: `pos-page.html`, `pos-page.scss`, `pos-page.spec.ts`.
  - Avance: pestañas A–D y renglones compactos implementados; compra ensanchada a 470 px en ventana maximizada y 462 px a 1366 × 768; cuerpo desplazable y total/cobro fijos comprobados con un artículo en Electron. Falta recorrer un ticket lleno y los estados de error visuales.
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
