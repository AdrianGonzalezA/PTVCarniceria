# RI POS — Integración de Nocturna Touch

## Decisión aprobada por Adrián

Aplicar al POS de carnicerías existente, desarrollado en Angular + Electron para Windows, la propuesta **Nocturna Touch compacta**. Identidad grafito/cobre, ticket a la izquierda y catálogo denso a la derecha. El objetivo principal es mostrar más productos de una sola mirada y operar rápido en pantalla táctil.

Esta entrega sustituye como referencia de diseño a las variantes anteriores y a Nocturna con tarjetas grandes. No volver a pedir elección de variante, paleta o distribución: ya están definidas.

## Instrucción de ejecución

Inspeccioná el repositorio y sus instrucciones, identificá la pantalla real del POS, sus componentes, estilos y servicios. Implementá este rediseño usando los modelos, eventos, reglas, permisos y mecanismos de persistencia que ya existen. No te quedes en una propuesta o un plan: realizá los cambios de interfaz y ejecutá la validación disponible.

El HTML es una referencia visual e interactiva, no código de producción para incrustar en un iframe ni una nueva aplicación. Trasladá la composición al template Angular, adaptá el SCSS al alcance del componente y sustituí todo el estado demo por bindings reales. Usá el sistema de iconos existente.

Cualquier cambio de arquitectura debe consultarse con Adrián. Reorganizar el template y los estilos o extraer componentes visuales siguiendo las convenciones actuales está dentro del trabajo autorizado.

## Archivos

- `nocturna-touch.html`: maqueta autónoma, abre con doble clic en Edge o Chrome. Contiene 35 productos, búsqueda, favoritos demo, cuatro tickets y teclado de peso táctil.
- `nocturna-touch.scss`: estilos de referencia extraídos de la maqueta. No importarlos globalmente sin adaptar el selector raíz y el alcance Angular.
- `tokens.json`: colores, dimensiones y restricciones de densidad.
- `PROMPT-PARA-CODEX.txt`: instrucción breve para iniciar la integración.

No se entregan componentes Angular acoplados a una versión o librería desconocida. Generarlos dentro del repositorio con sus convenciones reales.

## Diseño funcional

### Composición

Encabezado de aproximadamente 68 px como máximo: marca compacta, sucursal, caja, operador, turno, cobrar cuenta y menú. Sin saludo, banner, imágenes decorativas ni navegación lateral permanente.

Cuerpo dividido en ticket izquierdo (270–310 CSS px orientativos) y catálogo ocupando el resto. Ajustar ticket solo si textos o controles reales lo requieren. Mantener independiente el scroll de renglones y catálogo; el total y Cobrar deben estar visibles.

El catálogo tiene un buscador/lector compacto, categorías reales y una grilla de botones rectangulares. El selector de categorías debe permitir acceder a todas las categorías existentes, aunque el demo muestre solo Vacuno y Favoritos.

En cada botón mostrar únicamente nombre del producto y precio, con unidad si no es inequívoca. La cabecera del demo indica precio/kg porque todos sus cortes se venden por peso. En producción, si conviven kg, unidades u otras magnitudes, mostrar la unidad correcta en cada producto. No ocultar información necesaria para distinguir artículos similares.

Retirar de las tarjetas el código interno, etiquetas de frecuencia, stock repetido, iconos y márgenes grandes. Conservar código y disponibilidad en el detalle/editor; los productos sin disponibilidad deben tener un estado visible y respetar la política de venta actual.

### Densidad táctil

- Altura objetivo del producto: **64 px**, ampliable hasta 70 px cuando lo exijan nombres y legibilidad.
- Ancho orientativo: 120–145 px; separación: 7–8 px.
- Nombre: 13–15 px, hasta dos líneas. Precio: 12–14 px. No recortar nombres esenciales ni depender de hover.
- Acciones principales y teclado: al menos 48 × 48 CSS px; cobro: 55 px de alto orientativo.
- No usar escala CSS/zoom para achicar todo. Ganar capacidad quitando decoración y utilizando el ancho disponible.
- Grilla orientativa: `repeat(auto-fit, minmax(120px, 1fr))`; ajustar con el ancho real y nombres del catálogo.
- Mantener orden estable de productos por categoría. No reordenar por frecuencia después de cada venta. Favoritos debe ser una selección explícita y persistida si ya existe; no fijar los favoritos inventados del demo.
- Objetivo verificable: los 35 cortes de referencia deben entrar simultáneamente en un escritorio de **1366 × 768 CSS px a zoom 100 %**, sin abrir el editor de peso y con encabezados compactos. Es un objetivo de integración que debe medirse, no una certificación del prototipo.
- En 1920 × 1080, aprovechar el espacio para más productos y lectura cómoda. En Windows a 125 % o 150 %, medir `window.innerWidth/innerHeight`; no suponer que la resolución física equivale al espacio CSS.
- Para catálogos más grandes, conservar búsqueda, categorías y desplazamiento táctil fluido. No encoger botones indefinidamente para meter todos los productos.

### Interacción rápida

1. Tocar un producto selecciona el corte y abre el editor de peso en el área del ticket; no navega a otra pantalla.
2. Si la balanza real está integrada, conservar la lectura y confirmación existentes. No agregar un teclado ni un toque obligatorio cuando el flujo actual ya resuelve el pesaje más rápido.
3. Para carga manual, teclado numérico grande, coma decimal, borrar y Agregar. No mostrar el teclado manual automáticamente por encima del catálogo. Evitar activar teclado virtual de Windows y teclado propio simultáneamente.
4. Tras confirmar, agregar el renglón, cerrar el editor y dejar el catálogo listo para el siguiente corte.
5. Al cancelar o cambiar de ticket, descartar solo la entrada de peso no confirmada; conservar los renglones según las reglas actuales.
6. Evitar agregar un producto con peso vacío, inválido, inestable o perteneciente a una lectura anterior. Usar reglas y límites del dominio; el máximo de 1000 kg del demo es ilustrativo.
7. Conservar lector, etiquetas con peso/precio embebido y parser existentes. Los códigos 1001–1035 del demo son ficticios.
8. Mantener accesible edición y eliminación de renglones según permisos. El demo solo muestra eliminación; no autoriza a quitar edición existente.
9. Conservar los atajos actuales. Si el proyecto permite F2 búsqueda y F8 cobro, usarlos sin conflictos. No interceptar caracteres del peso, importes o datos del cliente.

## Funciones originales que deben mantenerse

| Función | Integración requerida |
|---|---|
| Sucursal/caja/operador/turno | Datos y acciones reales en encabezado |
| Cobrar cuenta | Acceso al flujo actual; independiente de cobrar venta |
| Menú | Mismas opciones, permisos y operaciones de turno |
| Buscar/escanear | Buscar por campos y códigos admitidos actualmente |
| Catálogo de sucursal | Mantener acceso al módulo existente desde menú o acción compacta |
| Categorías | Todas las categorías reales, no solo Vacuno |
| Lista de precios | Mismas listas, reglas de selección y recálculo |
| Tipo de comprobante | Tipos admitidos según emisor, operación y configuración |
| Tipo de cliente y datos fiscales | Mantener selector, edición y validaciones; no dejar “Consumidor final” fijo |
| Tickets A–D | Estado independiente, reanudación y persistencia reales |
| Cancelar venta | Mantener acción y confirmación; puede estar en menú contextual del ticket |
| Borrador/stock sin descontar | Mostrar estado real; no afirmar guardado antes de confirmarlo |
| Cantidades/precios/descuentos/totales | Conservar funcionalidades y cálculos del sistema |
| Finalizar compra | Abrir cobro existente y su lógica completa |
| Balanza/impresora/conectividad | Mostrar señales reales, con texto además de color |

La maqueta compacta omite algunos controles para mostrar jerarquía. Esa omisión no permite eliminar funcionalidad existente: ubicarlos de forma compacta o en el menú contextual correspondiente, sin agregar pasos a la venta habitual.

## Componentes orientativos

Adaptar a la estructura del repositorio: cabecera de caja, contenedor POS, selector de tickets, detalle de venta, editor de peso, buscador/lector, categorías, botón de producto y pie de cobro. Evitar un segundo store de venta o servicios nuevos para duplicar los existentes.

Usar `trackBy` o el mecanismo equivalente de la versión Angular instalada para identidad estable del producto. Revisar que cada cambio de peso no vuelva a construir toda la grilla. Conservar virtualización si ya existe; si hace falta cambiar estrategia por rendimiento, justificarlo con medición y consultar cuando afecte arquitectura.

Los estilos están delimitados por `#ri-touch-pos`. Sustituir por la clase/host apropiado y declarar variables CSS con prefijo propio; no invadir otros módulos. No agregar fuentes, imágenes, CDN, bibliotecas UI o dependencias de red para este diseño.

## Reglas y límites de la referencia

- Todos los artículos, códigos, stock, precios y tickets son demostrativos. El demo inicia con dos renglones ficticios para mostrar el ticket ocupado; la aplicación real debe cargar su estado real.
- La maqueta no persiste ventas ni confirma pagos. “Simular cobro” borra el ticket solo como demostración; en producción nunca borrar, descontar stock, imprimir o marcar pagado por el clic del usuario.
- El flujo real debe distinguir pago pendiente, aprobado, rechazado e incierto, evitar doble procesamiento y preservar el detalle ante fallos.
- La moneda, precisión y redondeos deben provenir de la implementación actual, sin copiar aritmética JavaScript del demo.
- No copiar las selecciones fiscales limitadas del ejemplo ni deducir reglas tributarias del HTML.
- No cambiar backend, base de datos, contratos, pasarelas, fiscalización, permisos, balanzas ni impresoras como parte de este rediseño.

## Validación y entrega del agente

Ejecutar build y pruebas existentes relevantes. Verificar manualmente en Electron/Windows:

- 35 cortes simultáneos en 1366 × 768 CSS px, zoom 100 %, editor cerrado; registrar cantidad real de columnas/filas y altura de tarjeta.
- 1920 × 1080 y escalado Windows 125 % y 150 %, registrando viewport CSS efectivo. Si no entra la misma cantidad, priorizar toque y lectura y documentar capacidad real.
- Nombres largos, productos por unidad, importes altos y distintas categorías.
- Operación con touch: sin hover necesario, objetivos separados, desplazamiento fluido, feedback de selección y ausencia de doble activación.
- Balanza real o simulación controlada existente: peso estable/inestable, desconexión, tara y carga manual conforme al dominio.
- Lector real o mecanismo de prueba actual: código común, etiqueta con peso/precio y foco del editor.
- Agregar/editar/quitar, lista de precios, cliente, comprobante, cancelar venta y tickets A–D independientes.
- Recuperar borrador y probar errores sin perder datos.
- Flujo actual de pago, cobro de cuenta e impresión sin cambios de comportamiento.
- Recorrido por teclado, foco visible, contraste y lectura de errores.

Entregar resumen de archivos modificados, validaciones realizadas, capturas en resoluciones verificadas y límites pendientes. No afirmar pruebas de hardware o navegador no realizadas.

## Verificación de este paquete

Se comprobó la sintaxis JavaScript de la referencia y la integridad del ZIP. No se ejecutó el proyecto Angular ni se verificó visualmente en navegador, Electron o hardware touch. Esas comprobaciones corresponden a la integración en el repositorio real.
