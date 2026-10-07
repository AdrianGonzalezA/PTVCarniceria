# Mapa de capacidades del sistema de carnicerías

## Propósito

Este mapa divide el MVP relevado en módulos con resultados verificables y dependencias explícitas. Los identificadores son estables y se usarán para nombrar las futuras especificaciones como `SPEC-<module-id>.md`.

**Estado:** aprobado  
**Fecha de aprobación:** 29 de septiembre de 2026  
**Aprobado por:** usuario responsable del relevamiento

## Módulos propuestos

| Module id | Responsabilidad | Depende de |
|---|---|---|
| `platform-access` | Identidad, sesión, roles, permisos, empresas, sucursales, terminales y contexto operativo | — |
| `audit-operations` | Registro inmutable y consulta autorizada de acciones críticas, errores y correlación operativa | `platform-access` |
| `catalog-pricing` | Productos, categorías, unidades, códigos, impuestos configurables, listas de precios, vigencias y favoritos | `platform-access`, `audit-operations` |
| `customers-credit` | Clientes eventuales o registrados, datos fiscales y cuenta corriente dentro del alcance aprobado | `platform-access`, `audit-operations` |
| `inventory-traceability` | Stock agregado, correlativos, ubicaciones, movimientos, remitos internos o externos y carga inicial | `platform-access`, `catalog-pricing`, `audit-operations` |
| `devices-printing` | Contenedor Electron del POS, lectores, balanzas, configuración técnica, captura de peso, comandera y estado de periféricos | `platform-access`, `catalog-pricing`, `audit-operations` |
| `pos-sales` | Borradores de venta, ventas paralelas si se aprueban, selección de productos, detalle, reservas, descuentos y cancelación | `catalog-pricing`, `customers-credit`, `inventory-traceability`, `devices-printing`, `audit-operations` |
| `payments-cash` | Medios de pago combinados, vuelto, caja, aperturas, retiros, arqueo, cierre y reapertura | `platform-access`, `pos-sales`, `audit-operations` |
| `fiscal-billing` | Validación fiscal, ARCA, numeración, comprobantes, contingencias, impresión, correo y consulta | `customers-credit`, `pos-sales`, `payments-cash`, `audit-operations` |
| `credit-notes` | Notas de crédito totales o parciales y efectos coordinados en fiscalidad, pagos, caja y stock | `fiscal-billing`, `payments-cash`, `inventory-traceability`, `audit-operations` |
| `integrations-sync` | Contratos con frigorífico o abastecedor, colas, idempotencia, reintentos y conciliación | `inventory-traceability`, `fiscal-billing`, `payments-cash`, `audit-operations` |
| `reporting` | Cierres de caja y stock, estadísticas, consultas y exportaciones con permisos | `inventory-traceability`, `payments-cash`, `fiscal-billing`, `credit-notes`, `audit-operations` |

## Orden de construcción propuesto

1. `platform-access`
2. `audit-operations`
3. `catalog-pricing` y `customers-credit`
4. `inventory-traceability` y `devices-printing`
5. `pos-sales`
6. `payments-cash`
7. `fiscal-billing`
8. `credit-notes`
9. `integrations-sync`
10. `reporting`

`catalog-pricing` y `customers-credit` pueden avanzar en paralelo después de la base. `inventory-traceability` y `devices-printing` también pueden avanzar en paralelo una vez fijados los contratos de producto. El resto debe integrarse en el orden indicado para evitar definir pagos, fiscalidad, devoluciones o reportes sobre estados todavía inestables.

## Límites del mapa

- El desarme de piezas y la generación de nueva trazabilidad pertenecen a Etapa 2 y no forman parte de estos módulos del MVP.
- Promociones automáticas, fidelización, límites de crédito e integración directa con adquirentes quedan fuera.
- Seguridad, observabilidad, rendimiento, disponibilidad, respaldo, accesibilidad e idempotencia son atributos transversales; cada especificación modular debe incluir criterios medibles para ellos.
- La parametrización será un mecanismo transversal: cada módulo declarará parámetros tipados, alcance, valores predeterminados y validaciones; `platform-access` proveerá la administración y auditoría básica del mecanismo sin apropiarse de las reglas de otros módulos.
- La administración y las consultas usarán Angular web. El puesto de venta reutilizará Angular dentro de Electron para integrar hardware local mediante contratos IPC restringidos.
- Las fronteras de este mapa no deciden la arquitectura de despliegue ni implican microservicios. Son límites funcionales y de especificación.

## Aprobación

El mapa fue aprobado como índice de especificaciones y orden de trabajo. La inclusión definitiva de `credit-notes`, ventas paralelas dentro de `pos-sales`, administración de balanzas dentro de `devices-printing` y el alcance inicial de `integrations-sync` se resolverán en la especificación de cada módulo.
