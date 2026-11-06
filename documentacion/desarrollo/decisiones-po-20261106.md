# Decisiones del PO — 06/11/2026

Respuestas a las siete preguntas de cierre del 05/11. La pregunta 5 no fue respondida y permanece pendiente.

1. **I18b, archivo offline:** por ahora no se firma. El JSON exportado es un medio opcional de carga, no una prueba de autenticidad del origen. La importación puede validar estructura, empresa/destino y duplicados conocidos, pero no puede afirmar que un tercero no alteró un archivo. La recepción requiere revisión y aprobación humana; no se implementará una supuesta verificación criptográfica.
2. **I18b, pieza recibida manualmente:** no exigir ID interno de la sucursal de origen. Registrar remito, origen declarado y código físico; si el origen usa nuestro POS y aporta ID de pieza/remito, conservar ese vínculo verificable. No inventar un vínculo por coincidencia.
3. **I19, ubicación:** la sucursal es la única ubicación de stock por ahora. No crear depósitos internos.
4. **I20, códigos:** aprobado un catálogo único de códigos por empresa, con tipo alternativo de venta o bulto de compra y factor de unidades. Un código no puede tener ambos usos. La conversión de bulto sigue limitada a la recepción.
5. **I21, aprobación de ajustes:** sin respuesta. Falta definir si también alcanza los ajustes manuales y Excel actuales o solo las diferencias de conteo.
6. **I21, saldo teórico:** clasificación por categoría y por artículo. Propuesta técnica: valor por defecto de categoría y excepción explícita por artículo, preservando la operatoria actual de los artículos existentes hasta configurarlos.
7. **I21, conteo:** las cajas siguen operando; el bloqueo global de las cajas no forma parte de esta etapa. El recuento cuando cambia el saldo sigue como propuesta técnica pendiente de confirmación. La mención del PO a una posible definición futura «por proceso» queda para precisar sin introducir una opción global de POS por inferencia.
