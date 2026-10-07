# Revisión del relevamiento del sistema de carnicerías

## Resultado

El material describe un sistema de punto de venta y gestión para carnicerías con facturación electrónica, caja, stock mixto, trazabilidad individual, productos a granel, periféricos e integración con frigorífico o abastecedor. `REQUERIMIENTOS_MVP.md` reúne correctamente la mayor parte de las fuentes y es una buena base funcional, pero todavía es un borrador: contiene decisiones provisionales, ampliaciones inferidas desde las maquetas y preguntas que bloquean arquitectura, estimación y aceptación.

No corresponde convertirlo todavía en una especificación de implementación monolítica. El alcance reúne capacidades independientes y debe dividirse según `CAPABILITY_MAP.md`. Después de aprobar ese mapa, cada módulo podrá recibir su propia especificación, criterios de aceptación, plan y tareas.

## Fuentes revisadas

| Fuente | Contenido relevante | Evaluación |
|---|---|---|
| `REQUERIMIENTOS_MVP.md` | Consolidación de alcance, 19 requerimientos funcionales, no funcionales, aceptación, contradicciones y 35 preguntas abiertas | Base principal, aún no aprobada |
| `Especificacion_Sistema_Carnicerias.pdf` | Definición del PO, etapas, trazabilidad, stock, caja, fiscalidad e integración | Fuente funcional de mayor autoridad disponible |
| `RI-P-529_25_ ANÁLISIS FUNCIONAL_ CARNICERÍAS - Etapa 1.docx` | Alcance inicial, tecnología, dependencias y criterios generales | Complementa al PDF; presenta diferencias de plataforma y tenancy |
| ERS de autenticación | Login, logout, sesión y errores | Útil, pero incompleta en seguridad, recuperación y ciclo de sesión |
| ERS de ABM de productos | Campos, validaciones, historial, permisos y eliminación | Útil; el borrado físico contradice trazabilidad e historia |
| ERS de selección de productos | Categorías, búsqueda, balanza, lector y carga rápida | Útil; la carga rápida queda semánticamente indefinida |
| ERS de detalle de venta | Cliente, comprobante, ítems, edición, descuentos y cancelación | Útil; carece de criterios de aceptación explícitos |
| ERS de finalización de compra | Medios combinados, vuelto, auditoría y fiscalidad | Útil; contiene una contradicción sobre sobrepago y vuelto |
| `protoripo.txt` | Enlace a prototipo Figma | Referencia externa; las capturas locales permiten revisar solo estados estáticos |
| Once PNG de maqueta | Siete pantallas funcionales y cuatro recursos gráficos | Evidencia de intención UX, no de reglas ni alcance aprobado |

## Cobertura confirmada

Las fuentes coinciden en los siguientes objetivos para Etapa 1:

- autenticación y selección de empresa o sucursal autorizada;
- catálogo de productos con y sin trazabilidad, categorías y listas de precios;
- venta táctil mediante botonera, búsqueda, códigos y balanza;
- facturación electrónica y puntos manuales o no fiscales;
- pagos combinados, vuelto y movimientos de caja;
- ingresos y egresos de stock por remitos y ventas;
- clientes eventuales y cuentas corrientes sin límite de crédito;
- IVA y percepciones por ítem;
- impresión y envío de comprobantes;
- reportes de cierre de caja y stock, más estadísticas de venta;
- integración con frigorífico o abastecedor, al menos para detalle de facturación destinado al Libro IVA;
- desarme de productos fuera de Etapa 1.

## Decisiones correctas del consolidado

El documento consolidado resuelve razonablemente varias contradicciones, aunque deben ser ratificadas:

- inactivar productos con historia en lugar de borrarlos físicamente;
- separar listas de precios del maestro de productos y congelar el precio usado en la venta;
- admitir sobrepago únicamente en efectivo para calcular vuelto;
- evitar altas implícitas de productos desde el puesto de venta;
- tratar descuentos manuales con permiso y tope, dejando promociones para una etapa posterior;
- conservar estados recuperables e idempotencia ante fallas de ARCA o sincronización;
- expresar el cierre de stock en la unidad base y usar kilos solo cuando corresponda, en lugar de forzar todos los productos a kilos.

## Ampliaciones de alcance que requieren aprobación

Tres capacidades aparecen en `REQUERIMIENTOS_MVP.md` principalmente por interpretación de las maquetas, no por una definición funcional explícita del PO:

1. **Ventas simultáneas por terminal.** Las pestañas `Ticket A` a `Ticket D` sugieren cuatro borradores paralelos, pero no definen límite, persistencia, propiedad, recuperación ni comportamiento al cerrar sesión o caja.
2. **Notas de crédito.** La maqueta muestra una pantalla, pero no existe una ERS original ni una definición del PO sobre alcance total o parcial, restitución de stock, reintegro, permisos o documentos fiscales admitidos.
3. **Administración de balanzas.** La maqueta muestra varias balanzas, pero no define protocolo, asignación, prueba de conexión, credenciales, selección por defecto ni responsabilidad operativa.

Estas capacidades pueden ser necesarias para producción, pero deben confirmarse como parte del MVP antes de convertirse en compromisos de entrega.

## Contradicciones y riesgos todavía abiertos

| Tema | Evidencia | Riesgo si no se resuelve |
|---|---|---|
| Web o Electron | El PDF del PO pide web; el análisis funcional propone Electron para POS | La estrategia de periféricos, actualización y soporte cambia materialmente |
| Tenancy y despliegue | Resuelto: instalación y base independiente por cliente, con múltiples empresas y sucursales, preparada para migrar servicios a cloud | Decisión incorporada en `SPEC-platform-access.md` |
| Operación sin conectividad | Se exige cola local y tiempo real, pero no se define qué puede seguir operando | Puede prometerse una continuidad fiscal técnicamente o legalmente inviable |
| Dueño del stock trazable | No está decidido si el frigorífico o la carnicería gobierna el estado real | Riesgo de divergencia de correlativos y doble venta |
| Caja | No se define si abre por sucursal, punto, terminal, turno o cajero | Los saldos y cierres no tienen una identidad estable |
| Cuenta corriente | Se la menciona como modalidad o medio de pago, sin recibos, saldos ni cancelaciones definidos | El ciclo financiero queda incompleto |
| Stock inicial y negativo | Ambos mecanismos están abiertos | No se puede implantar ni definir la validación final de venta |
| Etiquetas y balanzas | Faltan modelos, protocolos y estructuras reales de códigos | No es posible estimar ni certificar la integración |
| Fiscalidad | Faltan comprobantes exactos, contingencia y reglas de rechazo o timeout | Bloquea el diseño transaccional y la homologación |
| Nota de crédito | Solo existe intención visual | Puede afectar simultáneamente fiscalidad, pagos, caja y stock sin reglas consistentes |

## Brechas para una especificación implementable

El relevamiento funcional todavía no cubre las seis áreas mínimas exigidas para comenzar implementación:

- **Objetivo y alcance:** existen, pero requieren aprobación y partición por módulo.
- **Stack tecnológico:** hay una orientación (.NET, PostgreSQL, web o Electron), pero la plataforma POS está abierta.
- **Comandos ejecutables:** no existen porque todavía no hay una base de código definida.
- **Estructura del proyecto:** no está definida.
- **Estilo de código:** no está definido.
- **Estrategia de pruebas:** faltan niveles, ambientes, datos representativos, homologación ARCA, hardware certificado y objetivos de cobertura.
- **Límites operativos:** hay reglas funcionales, pero falta convertirlas en `Siempre`, `Consultar antes` y `Nunca` por módulo.

## Decisiones necesarias para aprobar el mapa

Antes de redactar especificaciones por módulo se necesita confirmar:

1. si `credit-notes` pertenece efectivamente al MVP;
2. si `pos-sales` debe incluir ventas simultáneas por terminal;
3. si la configuración de balanzas pertenece al MVP o a la puesta en marcha;
4. si `customers-credit` incluye el ciclo completo de cuenta corriente o solo la imputación de una venta;
5. si `reporting` incluye exportaciones CSV, XLSX y PDF o un subconjunto;
6. si `integrations-sync` debe abarcar stock, facturación y caja desde la primera salida o solo facturación para Libro IVA.
