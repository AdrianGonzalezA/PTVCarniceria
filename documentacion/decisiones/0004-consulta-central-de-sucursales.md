# 0004 — Consulta central de sucursales autónomas

**Estado:** aceptada para el primer contrato de lectura (14/10/2026); despliegue multisucursal pendiente de workshop.

## Contexto

La [decisión 0002](0002-instalacion-por-sucursal.md) fijó un servidor, API y PostgreSQL por sucursal para que varias cajas vendan por LAN sin depender de Internet. La empresa también necesita consultar el negocio desde una central. El laboratorio actual aún contiene varias empresas y sucursales en `carnicerias_test_visual`; no representa una instalación aislada final.

## Decisión

- Agregar a la API local un contrato opcional `GET /api/central/v1/installation` y `GET /api/central/v1/summary`, exclusivo de lectura. No interviene en confirmar ventas, caja, stock, impresión o facturación. La sucursal conserva la fuente de verdad; la central consulta en vivo y debe mostrar cuándo una sucursal no responde.
- Fijar `installationId`, `companyId` y `branchId` fuera de Git en el servidor. Las rutas no reciben un selector de empresa/sucursal. Antes de leer se comprueba que la empresa y sucursal siguen activas y vinculadas; un vínculo inválido falla cerrado.
- Usar una credencial de servicio propia, distinta de cookies humanas y credenciales Electron, y un verificador SHA-256 en configuración protegida. Dos verificadores permiten rotación con una fuente recargable sin interrumpir el POS. La identidad fijada no cambia al recargar credenciales.
- Limitar la exposición a VPN/red privada con HTTPS y controlar frecuencia, período de consulta y tiempo de respuesta. El primer resumen agrega únicamente ventas, pagos y efectivo de turnos abiertos de esa sucursal en una transacción PostgreSQL de solo lectura con instantánea consistente; no envía clientes ni detalles de tickets.
- No crear ni clonar otra base de aplicación para este desarrollo, ni añadir migraciones al contrato.

## Alternativas y consecuencias

Una base central única pondría la conectividad externa en el camino crítico de cada venta. Replicar tablas completas introduciría conflictos y una segunda fuente de verdad. Un proxy general a `/api/admin` heredaría permisos de sesiones humanas y abriría consultas o mutaciones no previstas. Se descartan para esta etapa en favor de dos consultas versionadas y acotadas.

La consulta central puede quedar incompleta si una sucursal está desconectada; la futura vista central deberá mostrar ese estado y no presentar totales parciales como totales generales. El limitador actual es en memoria porque el despliegue objetivo tiene una API por sucursal; si una sucursal usa varios procesos, la cuota debe pasar a un almacén compartido. La rotación sin reinicio exige que el proveedor de configuración protegida recargue cambios; valores fijados solo como variables de entorno no ofrecen esa propiedad. La publicación real por VPN/HTTPS y las contingencias se validarán en el workshop.

Véanse [la especificación](../../SPEC-branch-service-contract.md), [el plan](../../tasks/branch-service-contract-plan.md) y [la guía de activación](../despliegue/integracion-central-sucursal.md).
