# Mapa de capacidades: trazabilidad, periféricos e integraciones del POS

**Estado:** orden y límites aprobados por el usuario el 8 de octubre de 2026. Los formatos reales de etiquetas de ciclo 2 se incorporarán cuando estén disponibles.

| Módulo | Responsabilidad | Depende de |
|---|---|---|
| `inventory-traceability` | Registrar piezas individuales con producto, peso, origen y estado; recibirlas por lote o lectura manual sin perder el stock agregado existente. | Catálogo, sucursal y stock actual |
| `barcode-input` | Interpretar perfiles de código configurables por implementación y capturar lecturas por teclado/lector en recepción y venta. | `inventory-traceability`, POS |
| `receipt-printing` | Generar desde Electron un ticket no fiscal de una venta confirmada, inicialmente como archivo en disco. | Confirmación de venta |
| `scale-input` | Incorporar una medición desde balanza USB/serial o puerto virtual, con estado y origen del peso visibles. | POS, identidad de terminal |
| `fiscal-integration` | Preparar y luego homologar emisión, consulta y contingencias de comprobantes con ARCA; separado del ticket no fiscal. | Venta confirmada, reglas fiscales |
| `payment-gateways` | Preparar y luego conectar proveedores de cobro de Argentina sin confundir registro manual con cobro autorizado. | Cobro y caja |

Orden de trabajo: `inventory-traceability` → `barcode-input`; `receipt-printing` y `scale-input` pueden avanzar sin conocer el formato de ciclo 2; las conexiones reales de `fiscal-integration` y `payment-gateways` requieren contratos, credenciales y entornos de homologación propios. La lectura de venta no crea stock: recepción y venta son operaciones diferentes.

El documento `tasks/nuevasfuncionalidades.md` contiene otras ideas de la instalación y no forma parte de este mapa; se conserva sin modificaciones.
