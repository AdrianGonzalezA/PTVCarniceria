# Plan de implementación: POS UI v2 — Nocturna Touch

**Rama:** `feature/ui-v2`, creada desde `main` después de integrar `feature/federacion-sucursales`.
**Referencia aprobada:** `UI2/RI-Nocturna-Touch/INTEGRAR-EN-CODEX.md`, `nocturna-touch.html`, `nocturna-touch.scss` y `tokens.json`.
**Lista ejecutable:** `tasks/ui-v2-todo.md`. Los planes históricos de `tasks/plan.md` y `tasks/todo.md` no se reemplazan.

## Objetivo y contrato visual

El primer alcance rediseña la pantalla real del POS Angular/Electron para operar rápido en pantalla táctil: identidad grafito/cobre, categorías verticales en el extremo izquierdo, catálogo de productos a su derecha y compra/ticket amplio a la derecha (hasta 540 CSS px). Los botones de producto apuntan a 64–70 px de alto, con nombre, precio y unidad de venta cuando corresponda. El objetivo de densidad es mostrar los 35 cortes de referencia en 1366 × 768 CSS px al 100 %, con editor cerrado; es una meta que se debe medir en Electron, no una garantía tomada de la maqueta. El alcance posterior aprobado incorpora temas visuales por perfil local y la variante Nocturno de todas las pantallas administrativas, sin crear otra rama por estilo.

**Decisión de distribución (16/10/2026):** la referencia UI2 propone el ticket a la izquierda, pero el usuario prefirió expresamente conservar la distribución anterior del POS: catálogo a la izquierda y compra a la derecha. Luego precisó categorías verticales bien a la izquierda y más superficie para la compra; esta indicación reemplaza el pedido intermedio de categorías horizontales. Se mantiene la identidad, densidad y funcionalidad de UI2; sus archivos de referencia no se modifican.

**Ampliación aprobada (17/10/2026):** [mapa de temas](../CAPABILITY-MAP-visual-themes.md) y [selector local](../SPEC-visual-theme-selection.md). Original debe recuperar aspecto **y distribución** anteriores; Nocturno es la versión UI2 actual. Cada perfil Electron o navegador elige por separado. Se trabajará íntegramente en `feature/ui-v2`; no se integra a `main` hasta probar ambas variantes y recibir acuerdo del usuario. Los temas siguientes reutilizarán el registro sin ramas por estilo.

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

## Extensión: temas visuales configurables (plan propuesto)

1. Construir y probar el registro tipado y la preferencia local con valor predeterminado Original, tratamiento de IDs desconocidos y almacenamiento inaccesible. Preparar la aplicación temprana del identificador sin exponer todavía un selector que prometa una apariencia no implementada.
2. Especificar y construir `pos-visual-themes` sobre un único estado de venta. Verificar Original frente a la versión anterior a UI2 y Nocturno frente a la caja actual, ambos en Electron, incluyendo borrador A–D y tamaños de pantalla.
3. Especificar y construir `admin-visual-themes`: estilos Nocturno para Negocio, Configuración, ABM, Usuarios y SuperAdmin, incluidos modales, tablas, estados vacíos/error y navegación; Original sigue disponible.
4. Habilitar «Tema visual» en Configuración solo cuando ambas variantes estén listas. El cambio debe ser inmediato y persistente por perfil, sin recargar ni alterar el ticket. Probar Electron y navegador como perfiles independientes.
5. Ejecutar regresión completa y revisión visual por ruta; corregir contraste, foco y responsive; actualizar manual. Mantener la rama sin integrar a `main` hasta la aceptación del usuario.

El riesgo principal es que Original requiere estructura previa, no solo otra paleta. Se reutilizarán señales y servicios del POS y se variará únicamente presentación. El selector no será visible en una versión intermedia que muestre «Original» con aspecto Nocturno. No hay migración ni otra base.

**Plan detallado de T2, aprobado como alcance el 17/10/2026:** `SPEC-pos-visual-themes.md` toma `eb9292b` como referencia Original. Primero se separan los estilos en una base Original y una capa Nocturno limitada por el atributo de tema; `styles.scss` importa el SCSS del POS globalmente, por lo que el selector usa `html[data-visual-theme='nocturno'] app-pos-page` y no `:host-context`. El prefijo del componente se añadió en T3.1 al comprobar que un selector global del POS ocultaba la marca administrativa. Después se recuperan marca, información de stock y sexta columna del ticket en Original con una sola tabla y un solo control visible A–D. Por último se verifica responsive, estados y flujo en Electron. Cada corte conserva pruebas y build correctos; no se toca `pos-page.ts`, API o base salvo que una prueba evidencie una necesidad real. La ubicación de A–D es el mayor riesgo de layout porque Original usa pie completo y Nocturno pie del catálogo; se medirá en ambos tamaños antes de considerar T2 cerrado.

## Plan técnico aprobado de T3: administración Original/Nocturno

**Especificación, orden y lista ejecutable aprobados por el usuario el 17/10/2026:** [`SPEC-admin-visual-themes.md`](../SPEC-admin-visual-themes.md), limitada a `Carnicerias.Web`. El portal `Carnicerias.Central.Web` queda fuera de este corte. La lista T3 está en `tasks/ui-v2-todo.md`. No se modifica `tasks/plan.md` ni `tasks/todo.md`, que conservan pendientes históricos de *platform access*.

**Dependencias:** el atributo raíz y la preferencia local de T1 ya funcionan; el POS T2 ya interpreta ese atributo. La administración no tendrá otra fuente de estado. El grafo de presentación es:

```text
visual-theme-selection (atributo raíz)
    └── estilos de host compartidos (admin-page.scss)
          ├── navegación, paneles, tablas, formularios y diálogo común
          ├── páginas específicas de Configuración y Negocio
          └── Usuarios y SuperAdmin local
                └── revisión visual por ruta y habilitación posterior del selector (T4)
```

**Decisión técnica:** conservar Original como estilos base. Introducir tokens `--admin-*` para superficies, texto, bordes, acento, estados y foco; aplicar la variante Nocturno solo a componentes administrativos. En los SCSS declarados en `styleUrl`/`styleUrls`, Angular usa encapsulación emulada y compila `:host-context()`; [la documentación oficial](https://angular.dev/guide/components/styling#viewencapsulationemulated) advierte que el pseudoselector nativo está deprecado, pero confirma soporte del compilador. T3.1 verificó en runtime los colores calculados de `/admin` y `/admin/configuracion` antes de extender el patrón. No usar `::ng-deep`, un selector global sin prefijo ni una segunda vista por tema. El POS es distinto: su SCSS está importado globalmente y la variante se restringe a `html[data-visual-theme='nocturno'] app-pos-page`, para no filtrar estilos al administrador.

**Orden de cortes verificables (cada corte de implementación se dividirá en tareas de hasta cinco archivos):**

1. **Prueba del alcance y base común.** Capturar Original en `/admin` y `/admin/configuracion`; aplicar el atributo Nocturno al host compartido, comprobar tokens y estilo computado, y probar que POS, `/` y Original no cambian. Priorizar este riesgo de encapsulación antes de invertir en páginas específicas.
2. **Controles reutilizados.** Adaptar pestañas de áreas, paneles/listas/tablas/formularios comunes, `AdminDetailDialog`, importación Excel y paneles de impuestos. Verificar un diálogo abierto durante el cambio de tema, foco, estados de carga/error y un formulario con valores sin pérdida de estado. Separar navegación, superficies y diálogo en incrementos pequeños.
3. **Configuración por familias.** Recorrer catálogos y precios; luego impuestos/ARCA/pasarelas; después organización, conexión central, clientes y códigos de barras. Ajustar solo estilos especializados que no hereden correctamente los tokens compartidos. Cada familia se valida con tabla poblada, estado vacío y modal representativo.
4. **Negocio por familias.** Cubrir existencias, recepción de piezas, historial y cuentas corrientes; revisar filtros, paginación, importes, alertas y acciones de fila. Los colores de estado no dependen solo del color para transmitir significado.
5. **Accesos locales.** Cubrir `/users` y `/superadmin`, que tienen SCSS propios; incluir el formulario de acceso contenido en SuperAdmin, tablas y modales sin cambiar roles, permisos o manejo de sesiones.
6. **Auditoría final.** Revisar las 18 entradas de `SPEC-admin-visual-themes.md` en Original y Nocturno, navegador administrativo y Electron cuando corresponda. Medir 1920 × 992, 1366 × 768, 800 × 768 y 560 px; verificar contraste, foco, scroll horizontal solo dentro de tablas, consola, suite Angular, lint y build. Mantener selector oculto hasta T4 y `main` intacto hasta aceptación del usuario.

**Checkpoints:** tras 1–2, confirmar tokens/encapsulación y controles comunes con pruebas/build; tras 3–4, confirmar Configuración y Negocio por ruta sin regresión Original; tras 5–6, confirmar accesos, estados, tamaños y ausencia de cambios en POS. Los cortes de páginas especializadas pueden avanzar independientemente **después** de estabilizar la base compartida, pero cualquier cambio al mismo SCSS común se hace secuencialmente. No se prevén migraciones, API, dependencias ni otra base.

**Riesgos y mitigación:** los muchos colores literales pueden dejar islas claras u ocultar estados en Nocturno (inventario por ruta y estilo calculado); las hojas compartidas importadas por numerosas páginas pueden filtrar cambios a Original (pruebas y capturas de ambos temas en cada checkpoint); los diálogos nativos se dibujan en capa superior (probarlos abiertos en runtime); las tablas amplias pueden desbordar a 560 px (scroll interno y acciones accesibles). Si el patrón de encapsulación no se confirma en el primer corte, detener la propagación y ajustar el plan antes de tematizar las demás páginas.

**T3 implementado y auditado el 17/10/2026:** las 18 rutas previstas se recorrieron en Original/Nocturno a los cuatro tamaños aprobados, con contraste muestreado, foco, consola, scroll y estados representativos. Los hallazgos de contraste de cabeceras Original y del filtro de fecha móvil se corrigieron. La [auditoría T3](ui-v2-auditoria-t3.md) conserva la matriz y sus límites. El selector permanece oculto; T4 y la aceptación visual del usuario aún están pendientes antes de fusionar a `main`.

## Estado y medición en Electron (16/10/2026)

Se aplicaron la identidad Nocturna, las tarjetas compactas del catálogo, las categorías verticales a la izquierda y el detalle de compra como tabla semántica de cinco columnas. Conserva producto y código/pieza, cantidad o peso editable, precio por unidad, descuento informativo, importe y acción de quitar. Por pedido posterior, las pestañas A–D quedaron en una barra gris al pie del catálogo, con botones más grandes y la marca visible «Guardado» para cada borrador persistido. El cuerpo se desplaza sin mover total/cobro; el editor para agregar productos sigue en el diálogo existente.

- Caja A de La Rústica en Electron maximizado, viewport 1920 × 992 CSS px: ticket a `x=1366`, ancho 540 px; tabla de 497 px sin desplazamiento horizontal. La barra gris ocupa `x=14–1352`, `y=900–978`, debajo de las categorías; las cuatro pestañas centradas miden 134 × 60 px cada una. «Finalizar compra» permanece a la derecha en `x=1380–1892`, `y=916–964`. Cinco renglones reales ocupan 76 px de alto cada uno y se ven simultáneamente; total y cobro permanecen visibles. El catálogo mostró 35 artículos reales de la categoría Vacuno, sin ventas nuevas para esta verificación.
- La columna de categorías mantiene `flex-direction: column` y `overflow-y: auto`. La tabla tiene ancho mínimo de 470 px y desplazamiento horizontal interno si el panel cae por debajo de ese ancho. La revisión de este nuevo diseño a 1366 × 768, 800 × 768 y DPI 125 %/150 % sigue pendiente; las mediciones anteriores correspondían al detalle en tarjetas.
- Prueba focalizada primero en rojo y después en verde; suite Angular: 157/157; lint y build: correctos. Las capturas se revisaron dentro de Electron. No se crearon bases ni ventas para esta verificación.

## Temas del POS verificados en Electron (17/10/2026)

La caja A de La Rústica, con turno abierto, se revisó en Electron usando exclusivamente `carnicerias_test_visual`. En un borrador de prueba se agregaron cinco artículos por kilogramo sin cerrar ni cobrar la venta. El total fue $ 47.554,00 en ambos temas; A–B–A recuperó las cinco líneas y otra recarga recuperó el mismo borrador. La elección visual del perfil quedó en Original y el selector continúa oculto hasta completar administración.

- A 1366 × 768 CSS px, Original: catálogo `x=12`, ancho 839; compra `x=867`, ancho 488; barra A–D de ancho completo `y=710`, alto 58; total y cobro visibles. El catálogo y la tabla desplazan su contenido internamente y el documento mide exactamente 768 px de alto, sin scroll de página.
- A 1366 × 768 CSS px, Nocturno: catálogo `x=14`, ancho 805; compra `x=833`, ancho 519; barra gris A–D bajo el catálogo `y=676`, alto 78; total/cobro visibles. La sexta columna de descuento se oculta solo en esta vista y su información permanece junto al precio.
- A 1920 × 992 CSS px, Original: catálogo de 1189 px, compra de 691 px y barra completa al pie; Nocturno: catálogo de 1338 px, compra de 540 px y barra bajo el catálogo. Las cinco líneas y la indicación «Guardado» resultaron legibles en ambas vistas. Se corrigió el icono de menú Nocturno al observarlo colapsado en la captura real.
- A 800 × 768 y 560 × 768 CSS px, ambos temas apilan catálogo y compra: el catálogo mide aproximadamente 461 px de alto y desplaza productos internamente; A–D queda entre catálogo y ticket. El ticket ocupa el ancho disponible, con sus importes, acciones y cobro visibles al desplazarse por la página. Esto reemplaza la columna de compra de 300 px que recortaba el detalle Nocturno. Original muestra «Disponible» para stock positivo y «Sin stock» cuando no se puede agregar el producto.
- Pruebas: 39/39 focalizadas de POS, 166/166 Angular, 88/88 Electron; lint y build correctos. No se ejecutaron integraciones que creen/reinicien bases. Sigue pendiente la revisión de DPI físico 125 %/150 %, el resto de la administración y la aceptación visual del usuario antes de fusionar la rama.

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
