# Plan de implementación: POS UI v2 — Nocturna Touch

**Rama:** `feature/ui-v2`, creada desde `main` después de integrar `feature/federacion-sucursales`.
**Referencia aprobada:** `UI2/RI-Nocturna-Touch/INTEGRAR-EN-CODEX.md`, `nocturna-touch.html`, `nocturna-touch.scss` y `tokens.json`.
**Lista ejecutable:** `tasks/ui-v2-todo.md`. Los planes históricos de `tasks/plan.md` y `tasks/todo.md` no se reemplazan.

## Objetivo y contrato visual

Rediseñar solamente la pantalla real del POS Angular/Electron para operar rápido en pantalla táctil: identidad grafito/cobre, encabezado compacto, catálogo denso a la izquierda y compra/ticket a la derecha (270–310 CSS px). Los botones de producto apuntan a 64–70 px de alto, con nombre, precio y unidad de venta cuando corresponda. El objetivo de densidad es mostrar los 35 cortes de referencia en 1366 × 768 CSS px al 100 %, con editor cerrado; es una meta que se debe medir en Electron, no una garantía tomada de la maqueta.

**Decisión de distribución (16/10/2026):** la referencia UI2 propone el ticket a la izquierda, pero el usuario prefirió expresamente conservar la distribución anterior del POS: catálogo a la izquierda y compra a la derecha. Se mantiene la identidad, densidad y funcionalidad de UI2; sus archivos de referencia no se modifican.

## Alcance y límites

- Conservar servicios, señales, permisos, persistencia de borradores, tickets A–D, lista de precios, stock, lector, balanza, descuentos, cobro, ARCA e impresión existentes. La maqueta usa datos ficticios y su JavaScript no se incorpora a producción.
- Reorganizar el template y el SCSS del POS; extraer componentes visuales solo si reducen complejidad sin crear un segundo estado de venta.
- Mantener todos los selectores reales de cliente/comprobante y las acciones menos frecuentes en un lugar accesible, sin pasos adicionales en la venta habitual.
- No modificar API, base de datos, contratos, pasarelas, fiscalidad ni protocolos de dispositivos. No agregar dependencias, CDN ni imágenes remotas.
- Una caja sin turno abierto sigue bloqueada. Errores de guardado, stock desactualizado y pagos inciertos continúan visibles y no se ocultan por el rediseño.
- No inventar favoritos: si no existe una selección real persistida, el diseño no mostrará los favoritos ficticios de la maqueta.

## Orden de implementación

1. Establecer pruebas de estructura/función existentes y un contrato visual automatizado para el nuevo layout.
2. Adaptar encabezado, paleta y columnas; mantener los controles operativos.
3. Compactar búsqueda y tarjetas con productos reales, unidades y estado de disponibilidad; mantener las categorías como botones en una fila horizontal desplazable.
4. Compactar ticket, controles fiscales, tickets A–D y scroll independiente; mantener total y cobro visibles.
5. Llevar el editor de cantidad/peso al ticket sin alterar la lectura de balanza o etiqueta y con teclado manual táctil solo cuando corresponde.
6. Ajustar responsive, foco, estados de error y contraste; medir el resultado real en Electron y corregir la densidad sin usar zoom CSS.
7. Recorrer el flujo de venta y cobro existente, actualizar manual de usuarios y presentar capturas/medidas y límites pendientes.

Cada etapa se entrega en un incremento compilable, con prueba focalizada antes de pasar a la siguiente. Los criterios detallados y checkpoints están en `tasks/ui-v2-todo.md`.

## Estado y medición en Electron (16/10/2026)

Se aplicaron la identidad Nocturna, las tarjetas compactas, las categorías horizontales, las pestañas A–D dentro de la compra, el detalle en renglones y el cuerpo desplazable con total/cobro fijos. El editor de peso/cantidad sigue en el diálogo existente. La fila horizontal de categorías es una preferencia explícita del usuario y reemplaza la propuesta inicial de convertirlas en un combo.

- Ventana maximizada, viewport 1920 × 992 CSS px: catálogo a `x=14`, ancho 1568 px; ticket a `x=1596`, ancho 310 px. Tarjeta de producto medida: 150,5 × 70 px. Sin desborde horizontal de página ni del pie del ticket.
- La fila de categorías en Electron tiene `flex-direction: row` y `overflow-x: auto`; muestra las cinco categorías reales de la lista activa. Con más categorías se desplazará lateralmente, sin ocultar opciones en un menú.
- Ventana exterior 1366 × 768, viewport efectivo 1358 × 712 CSS px: catálogo a `x=14`, ancho 1017 px; ticket a `x=1045`, ancho 299 px. El primer renglón de 157 px queda completo en el cuerpo visible; los controles inferiores se recorren con scroll propio y total/cobro permanecen visibles.
- Ventana exterior 800 × 768, viewport efectivo 792 × 712 CSS px: catálogo de 517 px y ticket de 245 px, sin desborde horizontal. El ticket necesita desplazamiento vertical para leer el renglón completo.
- Suite Angular: 157/157; lint y build: correctos. La captura se revisó dentro de Electron. No se crearon bases ni ventas para esta verificación. El catálogo activo tenía solo dos artículos: la meta de 35 cortes y DPI 125 %/150 % siguen pendientes de medición.

## Validación

- `npm test --prefix src/Carnicerias.Web -- --watch=false`, secuencial para evitar presión de memoria.
- `npm run lint --prefix src/Carnicerias.Web` y `npm run build --prefix src/Carnicerias.Web`.
- `npm run electron:test --prefix src/Carnicerias.Pos` si se toca el empaquetado o el puente nativo.
- Revisión visual y funcional del POS **en Electron**, en 1366 × 768 y 1920 × 1080 CSS px; registrar `window.innerWidth/innerHeight`, filas, columnas y altura de tarjeta. Comprobar Windows al 125 %/150 % si ese escalado está disponible. No usar una pestaña del navegador para validar el POS.
- Probar tickets A–D, borrador recuperado, producto por kg y por unidad, código común y trazable, stock insuficiente, balanza, descuentos, tipo de cliente/comprobante y cobro sin alterar la base por pruebas de integración destructivas.

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| El ticket derecho de 270 px no admite la tabla actual de seis columnas. | Convertir renglones a filas compactas sin quitar edición, eliminación ni información de pieza. |
| El editor inline compite con renglones y controles fiscales. | Scroll independiente del cuerpo del ticket; total/cobro fijos; controles contextuales colapsables sin ocultar validaciones. |
| Menos texto en tarjetas puede confundir productos semejantes o por unidad. | Mantener nombre distintivo y unidad visible, con código/stock en editor y estado explícito de no disponibilidad. |
| Densidad objetivo imposible con DPI, nombres reales o múltiples controles. | Medir viewport efectivo, priorizar legibilidad y objetivos táctiles de 48 px; informar capacidad real sin escalar toda la interfaz. |
| Regresiones por mover template de 726 líneas. | Pruebas de comportamiento y estructura por incremento; no tocar estado ni servicios en los primeros cortes. |
