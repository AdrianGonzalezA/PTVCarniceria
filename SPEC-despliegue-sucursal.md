# Despliegue autónomo por sucursal — contrato del laboratorio

## Objetivo y alcance

Cada sucursal opera una instalación independiente: una API .NET, un PostgreSQL de la aplicación y una o más PC de caja con Electron. Las cajas de esa sucursal comparten catálogo, stock, tickets y turnos mediante su API local. Administración se abre en un navegador de la red de la sucursal. No se publica una API central ni se sincronizan sucursales en esta etapa.

“Sin nada en Internet” significa sin hosting central ni exposición pública de la API de venta. Se asume que puede existir salida a Internet para ARCA o pasarelas; si no existe, esas funciones externas no están disponibles. Esta interpretación se confirmará antes de la instalación de pruebas.

## Topología y datos

```text
PC caja 1 (Electron, lector, balanza, impresora) ─┐
PC caja N (Electron, lector, balanza, impresora) ─┼─ LAN ─ API local ─ PostgreSQL local
Administración (navegador) ────────────────────────┘
```

- Cada despliegue real tendrá su propia base de datos en su servidor. Esto no autoriza crear otra base de desarrollo o laboratorio ahora: `carnicerias_test_visual` sigue siendo la única base de aplicación en este entorno hasta nueva autorización.
- El modelo y la administración aún permiten definir más de una sucursal en una base. Si la API se vincula a una empresa/sucursal, las sesiones y rutas operativas quedan limitadas a esa identidad y el administrador web no elige sucursal. Una restricción de “exactamente una sucursal almacenada por instancia” sigue pendiente antes de distribuir instalaciones reales.
- La API es el único cliente de PostgreSQL; las cajas no acceden a la base directamente.
- Una caja se identifica con su credencial de terminal y el cajero abre su turno. Varios puestos de la sucursal pueden operar simultáneamente.
- Datos y configuración de una sucursal no se mezclan con los de otra. La futura publicación/consolidación empresarial requerirá un contrato de integración, identificadores de origen e idempotencia; no se presume replicación directa de tablas ni se implementa ahora.

## Capacidades exigidas para el workshop

1. El paquete Electron acepta una URL de API por puesto sin recompilar y no envía la credencial de terminal a un origen arbitrario. HTTP por LAN se admite solo como opción explícita de laboratorio; HTTPS es el objetivo de instalación real.
2. Los puertos de balanza e impresora son configurables por puesto. La balanza de prueba conserva el protocolo `ST,peso,kg`; otros modelos requieren adaptación y prueba específica.
3. La misma API sirve la administración Angular desde la LAN, sin exigir un servidor de desarrollo Angular en cada caja.
4. El empaquetado de Electron y la publicación de API/Angular tienen pasos reproducibles; configuración local, credenciales y certificados quedan fuera de Git.
5. El laboratorio verifica dos o más cajas en la misma sucursal, con credenciales diferentes, stock compartido y recuperación de tickets guardados. No se ejecutan pruebas que creen o reinicien otra base de aplicación.

## Límites operativos

- Sin API/LAN no se confirman ventas ni se promete recuperar ediciones aún no guardadas. No hay modo offline con sincronización posterior.
- Con LAN y servidor activos pero sin Internet, las funciones que requieren ARCA, Mercado Pago o Clover no pueden darse por operativas. La posibilidad legal de emitir comprobantes en contingencia se definirá con el responsable fiscal antes de producción.
- Mercado Pago puede consultar pagos salientes mientras el POS esté abierto, pero su webhook de conciliación requiere una solución de ingreso HTTPS público aún no aprobada. Clover OAuth también requiere callback HTTPS; no se abre por ello toda la API local a Internet.
- El entorno actual de ARCA y las pasarelas es de homologación/prueba. No se traslada la base ficticia como histórico comercial.

## Verificación

- Pruebas unitarias de validación de configuración, destino API y serialización de periféricos.
- Build de Angular, API y Electron; prueba HTTP local de `/api/health` y `/admin` sobre el mismo host de la API.
- Inspección visual del POS exclusivamente dentro de Electron. La prueba física de varias PC, hardware real, corte de red y restauración de respaldo pertenece al workshop.
