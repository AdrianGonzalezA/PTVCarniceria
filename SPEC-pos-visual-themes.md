# Especificación propuesta: temas visuales del punto de venta

**Estado:** aprobado por el usuario el 17/10/2026. **Módulo:** `pos-visual-themes` del [mapa de temas](CAPABILITY-MAP-visual-themes.md). **Rama:** `feature/ui-v2`; no integrar a `main` antes de la revisión del usuario.

## Objetivo y referencias

La caja ofrece dos presentaciones del **mismo** flujo de venta. **Original** recupera el aspecto y la distribución anteriores a UI2, identificados en el commit `eb9292b`; **Nocturno** conserva la composición UI2 actual de esta rama. El tema se toma de la preferencia local ya implementada en `visual-theme-selection`. La venta, el turno, el ticket y sus dispositivos no pertenecen al tema.

## Supuestos explícitos

1. «Como antes» significa tomar `eb9292b` como referencia visual: cabecera Carnicerías, grafito/gris con verde lima y turquesa, catálogo a la izquierda, compra a la derecha, tabla de detalle de seis columnas y barra A–D al pie de toda la pantalla. Se conservan las mejoras funcionales posteriores (por ejemplo, aviso «Sin stock» y estado «Guardado»).
2. «Nocturno» significa conservar la variante hoy presente en `feature/ui-v2`: grafito/cobre, categorías verticales a la izquierda, catálogo compacto, compra a la derecha y barra gris A–D debajo del catálogo. Los diálogos pueden conservar una superficie clara si mantienen contraste y legibilidad.
3. La fidelidad se comprobará con Electron en el mismo perfil y viewport, no mediante comparación exacta de píxeles ni una pestaña del navegador.

## Criterios de aceptación

1. Con `data-visual-theme="original"` o sin preferencia, el POS muestra marca y paleta originales, barra de búsqueda turquesa, categorías grises a la izquierda, productos claros, compra gris a la derecha y pie de tickets A–D de ancho completo. El detalle vuelve a mostrar **Producto, Peso/Cant., Precio, Desc., Total y acción**, sin ocultar código o identificador de pieza.
2. Con `data-visual-theme="nocturno"`, permanece el layout actual: marca NOCTURNA, paleta cobre/grafito, tarjetas compactas, panel de compra amplio, detalle compacto y barra A–D bajo el catálogo. Los botones «Guardado» siguen legibles.
3. Un único estado de `PosPage` sirve a ambos temas. Cambiar el atributo de tema no vuelve a buscar productos, no recarga la ruta y no modifica líneas, cantidades, descuento, total, borradores A–D, selección de comprobante ni estado de cobro.
4. Solo hay un control interactivo por acción visible en cada tema. El lector mantiene foco y comportamiento; las opciones de precio, cliente y comprobante y los estados de turno cerrado, stock, error de guardado, factura y pago siguen accesibles. No se crean dos ventas o dos formularios paralelos por tema.
5. En Electron a 1366 × 768 y 1920 × 1080 CSS px, catálogo y compra son utilizables, el total y «Finalizar compra» quedan accesibles y la lista de artículos se desplaza sin esconder A–D. En anchos menores, el contenido puede desplazarse internamente sin perder acciones, foco visible ni etiquetas accesibles.
6. Los colores y reglas propias de POS no alteran las pantallas administrativas ni el inicio de sesión. Una preferencia desconocida cae en Original según `SPEC-visual-theme-selection.md`.

## Tecnología, estructura y estilo

- Angular 22.2, TypeScript y SCSS existentes, sin dependencias, API, migraciones ni archivos de imagen nuevos.
- `src/Carnicerias.Web/src/app/features/pos/pos-page.html`: una vista con la misma lógica y controles; se permite marcado de presentación específico cuando CSS solo no pueda expresar ambas estructuras, sin duplicar lógica de venta.
- `src/Carnicerias.Web/src/app/features/pos/pos-page.scss`: estilos limitados por el identificador aplicado al elemento raíz, con variables separadas por tema y reglas responsive explícitas. El SCSS previo a UI2 en `eb9292b` y el SCSS UI2 actual son las dos referencias.
- `src/Carnicerias.Web/src/app/features/pos/pos-page.spec.ts`: pruebas cercanas de estructura y comportamiento. `core/theme/visual-theme.ts` sigue siendo la fuente única de IDs; `pos-page.ts` no guarda una segunda preferencia.

Ejemplo de convención de nombres, no una segunda fuente de estado:

```scss
.pos-shell {
  color: var(--pos-theme-ink);
  background: var(--pos-theme-background);
}
```

## Comandos y pruebas

```powershell
npm test --prefix src/Carnicerias.Web -- --watch=false --include=src/app/features/pos/pos-page.spec.ts
npm test --prefix src/Carnicerias.Web -- --watch=false
npm run lint --prefix src/Carnicerias.Web
npm run build --prefix src/Carnicerias.Web
```

- Pruebas primero en rojo y después en verde: marca, tabla y ubicación única de A–D por tema; un ticket con piezas y productos por kg/unidad; cambio de atributo sin mutar el estado. Reutilizar las pruebas existentes de checkout, dispositivos, stock y persistencia.
- Revisión visual **dentro de Electron** de ambos temas con turno abierto, ticket vacío y ticket con varios renglones, 1366 × 768 y 1920 × 1080 CSS px. Registrar viewport, tamaño de paneles, scroll, foco, legibilidad de importes y estados de error. No confirmar nuevas ventas solo para probar el tema.
- No ejecutar integraciones que creen o reinicien bases. La única base de aplicación permanece `carnicerias_test_visual` en Docker, puerto local `55433`.

## Límites

- **Siempre:** conservar turno, stock, lector, balanza, precios, tickets A–D, descuentos, comprobantes, ARCA, pagos e impresión; mantener un único estado de venta; probar en Electron; conservar accesibilidad de teclado y mensajes de error.
- **Consultar antes:** cambiar reglas de negocio o persistencia, añadir dependencias, modificar dispositivos/API, o reinterpretar Original de forma distinta a la referencia `eb9292b`.
- **Nunca:** crear otra base de Carnicerías, versionar credenciales, incorporar el JavaScript demo de UI2, ni usar el navegador como validación visual del POS.

## Preguntas abiertas

Ninguna bloqueante. La revisión del usuario en Electron decidirá si algún detalle de la referencia Original necesita un ajuste visual adicional antes de habilitar el selector y fusionar la rama.
