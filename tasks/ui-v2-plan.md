# Plan de implementación: POS UI v2 — Nocturna Touch

**Rama:** `feature/ui-v2`, creada desde `main` después de integrar `feature/federacion-sucursales`.
**Referencia aprobada:** `UI2/RI-Nocturna-Touch/INTEGRAR-EN-CODEX.md`, `nocturna-touch.html`, `nocturna-touch.scss` y `tokens.json`.
**Lista ejecutable:** `tasks/ui-v2-todo.md`. Los planes históricos de `tasks/plan.md` y `tasks/todo.md` no se reemplazan.

## Objetivo y contrato visual

Rediseñar solamente la pantalla real del POS Angular/Electron para operar rápido en pantalla táctil: identidad grafito/cobre, categorías verticales en el extremo izquierdo, catálogo de productos a su derecha y compra/ticket amplio a la derecha (hasta 540 CSS px). Los botones de producto apuntan a 64–70 px de alto, con nombre, precio y unidad de venta cuando corresponda. El objetivo de densidad es mostrar los 35 cortes de referencia en 1366 × 768 CSS px al 100 %, con editor cerrado; es una meta que se debe medir en Electron, no una garantía tomada de la maqueta.

**Decisión de distribución (16/10/2026):** la referencia UI2 propone el ticket a la izquierda, pero el usuario prefirió expresamente conservar la distribución anterior del POS: catálogo a la izquierda y compra a la derecha. Luego precisó categorías verticales bien a la izquierda y más superficie para la compra; esta indicación reemplaza el pedido intermedio de categorías horizontales. Se mantiene la identidad, densidad y funcionalidad de UI2; sus archivos de referencia no se modifican.

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
3. Compactar búsqueda y tarjetas con productos reales, unidades y estado de disponibilidad; mantener las categorías como botones en una columna vertical desplazable a la izquierda.
4. Compactar ticket y controles fiscales; ubicar tickets A–D en una barra gris al pie del catálogo y mantener total y cobro visibles con scroll independiente.
5. Llevar el editor de cantidad/peso al ticket sin alterar la lectura de balanza o etiqueta y con teclado manual táctil solo cuando corresponde.
6. Ajustar responsive, foco, estados de error y contraste; medir el resultado real en Electron y corregir la densidad sin usar zoom CSS.
7. Recorrer el flujo de venta y cobro existente, actualizar manual de usuarios y presentar capturas/medidas y límites pendientes.

Cada etapa se entrega en un incremento compilable, con prueba focalizada antes de pasar a la siguiente. Los criterios detallados y checkpoints están en `tasks/ui-v2-todo.md`.

## Estado y medición en Electron (16/10/2026)

Se aplicaron la identidad Nocturna, las tarjetas compactas del catálogo, las categorías verticales a la izquierda y el detalle de compra como tabla semántica de cinco columnas. Conserva producto y código/pieza, cantidad o peso editable, precio por unidad, descuento informativo, importe y acción de quitar. Por pedido posterior, las pestañas A–D quedaron en una barra gris al pie del catálogo, con botones más grandes y la marca visible «Guardado» para cada borrador persistido. El cuerpo se desplaza sin mover total/cobro; el editor para agregar productos sigue en el diálogo existente.

- Caja A de La Rústica en Electron maximizado, viewport 1920 × 992 CSS px: ticket a `x=1366`, ancho 540 px; tabla de 497 px sin desplazamiento horizontal. La barra gris ocupa `x=14–1352`, `y=900–978`, debajo de las categorías; las cuatro pestañas centradas miden 134 × 60 px cada una. «Finalizar compra» permanece a la derecha en `x=1380–1892`, `y=916–964`. Cinco renglones reales ocupan 76 px de alto cada uno y se ven simultáneamente; total y cobro permanecen visibles. El catálogo mostró 35 artículos reales de la categoría Vacuno, sin ventas nuevas para esta verificación.
- La columna de categorías mantiene `flex-direction: column` y `overflow-y: auto`. La tabla tiene ancho mínimo de 470 px y desplazamiento horizontal interno si el panel cae por debajo de ese ancho. La revisión de este nuevo diseño a 1366 × 768, 800 × 768 y DPI 125 %/150 % sigue pendiente; las mediciones anteriores correspondían al detalle en tarjetas.
- Prueba focalizada primero en rojo y después en verde; suite Angular: 157/157; lint y build: correctos. Las capturas se revisaron dentro de Electron. No se crearon bases ni ventas para esta verificación.

## Validación

- `npm test --prefix src/Carnicerias.Web -- --watch=false`, secuencial para evitar presión de memoria.
- `npm run lint --prefix src/Carnicerias.Web` y `npm run build --prefix src/Carnicerias.Web`.
- `npm run electron:test --prefix src/Carnicerias.Pos` si se toca el empaquetado o el puente nativo.
- Revisión visual y funcional del POS **en Electron**, en 1366 × 768 y 1920 × 1080 CSS px; registrar `window.innerWidth/innerHeight`, filas, columnas y altura de tarjeta. Comprobar Windows al 125 %/150 % si ese escalado está disponible. No usar una pestaña del navegador para validar el POS.
- Probar tickets A–D, borrador recuperado, producto por kg y por unidad, código común y trazable, stock insuficiente, balanza, descuentos, tipo de cliente/comprobante y cobro sin alterar la base por pruebas de integración destructivas.

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| La tabla necesita más ancho que el panel en ventanas estrechas. | Mantener el desplazamiento horizontal dentro del detalle, sin desbordar la página ni quitar edición, eliminación o información de pieza. |
| El editor inline compite con renglones y controles fiscales. | Scroll independiente del cuerpo del ticket; total/cobro fijos; controles contextuales colapsables sin ocultar validaciones. |
| Menos texto en tarjetas puede confundir productos semejantes o por unidad. | Mantener nombre distintivo y unidad visible, con código/stock en editor y estado explícito de no disponibilidad. |
| Densidad objetivo imposible con DPI, nombres reales o múltiples controles. | Medir viewport efectivo, priorizar legibilidad y objetivos táctiles de 48 px; informar capacidad real sin escalar toda la interfaz. |
| Regresiones por mover template de 726 líneas. | Pruebas de comportamiento y estructura por incremento; no tocar estado ni servicios en los primeros cortes. |
