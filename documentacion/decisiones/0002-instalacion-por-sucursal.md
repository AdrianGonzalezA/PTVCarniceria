# 0002 — Instalación autónoma por sucursal

**Estado:** aceptada como topología objetivo para el laboratorio (13/10/2026); instalación física y contingencias aún no verificadas.

## Contexto

El primer despliegue se probará como workshop con varias PC de caja. El usuario decidió que cada sucursal sea independiente y que no exista por ahora un servidor central publicado en Internet. La caja necesita un servidor para persistir tickets, pagos y stock; los periféricos pertenecen al puesto.

## Decisión

- Una API y una base PostgreSQL por sucursal desplegada, ambas en un servidor local; varias cajas Electron se conectan por LAN. Administración usa navegador en esa LAN.
- La API es la única que conecta a PostgreSQL. Cada caja conserva identidad de terminal y cada cajero abre su turno.
- Se prepara configuración del destino API y de periféricos por puesto, sin recompilar Electron y sin guardar credenciales en Git.
- No se crea ahora ninguna base nueva: la restricción de una sola base de desarrollo `carnicerias_test_visual` continúa vigente.
- No hay sincronización entre sucursales. Un futuro servicio empresarial consumirá contratos deliberados de cada sucursal, no replicación improvisada de bases.

## Alternativas y consecuencias

Un servidor central en Internet simplificaría la administración multisucursal, pero haría depender todas las ventas de la conectividad externa y no coincide con la decisión actual. Una base en cada caja crearía conflictos de stock y conciliación; por eso la persistencia se concentra en el servidor de la sucursal.

La LAN y el servidor local pasan a ser críticos: una caída de cualquiera impide confirmar ventas; los borradores solo se recuperan si llegaron a PostgreSQL. ARCA y pasarelas aún requieren salida a Internet y algunos callbacks requieren un punto HTTPS de entrada acotado, que se diseñará aparte. Antes de uso real hacen falta HTTPS interno, respaldo/restauración probados, alimentación protegida y validación de hardware y fiscalidad.
