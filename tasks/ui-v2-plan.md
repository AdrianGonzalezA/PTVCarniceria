# Plan de implementación: POS UI v2 — Nocturna Touch

**Rama:** `feature/ui-v2`, creada desde `main` después de integrar `feature/federacion-sucursales`.
**Referencia aprobada:** `UI2/RI-Nocturna-Touch/INTEGRAR-EN-CODEX.md`, `nocturna-touch.html`, `nocturna-touch.scss` y `tokens.json`.
**Lista ejecutable:** `tasks/ui-v2-todo.md`. Los planes históricos de `tasks/plan.md` y `tasks/todo.md` no se reemplazan.

## Objetivo y contrato visual

Rediseñar solamente la pantalla real del POS Angular/Electron para operar rápido en pantalla táctil: identidad grafito/cobre, encabezado compacto, ticket a la izquierda (270–310 CSS px) y catálogo denso a la derecha. Los botones de producto apuntan a 64–70 px de alto, con nombre, precio y unidad de venta cuando corresponda. El objetivo de densidad es mostrar los 35 cortes de referencia en 1366 × 768 CSS px al 100 %, con editor cerrado; es una meta que se debe medir en Electron, no una garantía tomada de la maqueta.

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
3. Compactar búsqueda, categorías y tarjetas con productos reales, unidades y estado de disponibilidad.
4. Compactar ticket, controles fiscales, tickets A–D y scroll independiente; mantener total y cobro visibles.
5. Llevar el editor de cantidad/peso al ticket sin alterar la lectura de balanza o etiqueta y con teclado manual táctil solo cuando corresponde.
6. Ajustar responsive, foco, estados de error y contraste; medir el resultado real en Electron y corregir la densidad sin usar zoom CSS.
7. Recorrer el flujo de venta y cobro existente, actualizar manual de usuarios y presentar capturas/medidas y límites pendientes.

Cada etapa se entrega en un incremento compilable, con prueba focalizada antes de pasar a la siguiente. Los criterios detallados y checkpoints están en `tasks/ui-v2-todo.md`.

## Validación

- `npm test --prefix src/Carnicerias.Web -- --watch=false`, secuencial para evitar presión de memoria.
- `npm run lint --prefix src/Carnicerias.Web` y `npm run build --prefix src/Carnicerias.Web`.
- `npm run electron:test --prefix src/Carnicerias.Pos` si se toca el empaquetado o el puente nativo.
- Revisión visual y funcional del POS **en Electron**, en 1366 × 768 y 1920 × 1080 CSS px; registrar `window.innerWidth/innerHeight`, filas, columnas y altura de tarjeta. Comprobar Windows al 125 %/150 % si ese escalado está disponible. No usar una pestaña del navegador para validar el POS.
- Probar tickets A–D, borrador recuperado, producto por kg y por unidad, código común y trazable, stock insuficiente, balanza, descuentos, tipo de cliente/comprobante y cobro sin alterar la base por pruebas de integración destructivas.

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| El ticket de 270 px no admite la tabla actual de seis columnas. | Convertir renglones a filas compactas sin quitar edición, eliminación ni información de pieza. |
| El editor inline compite con renglones y controles fiscales. | Scroll independiente del detalle; total/cobro fijos; controles contextuales colapsables sin ocultar validaciones. |
| Menos texto en tarjetas puede confundir productos semejantes o por unidad. | Mantener nombre distintivo y unidad visible, con código/stock en editor y estado explícito de no disponibilidad. |
| Densidad objetivo imposible con DPI, nombres reales o múltiples controles. | Medir viewport efectivo, priorizar legibilidad y objetivos táctiles de 48 px; informar capacidad real sin escalar toda la interfaz. |
| Regresiones por mover template de 726 líneas. | Pruebas de comportamiento y estructura por incremento; no tocar estado ni servicios en los primeros cortes. |
