# Plan: instalación autónoma por sucursal

**Contrato:** [SPEC-despliegue-sucursal.md](../SPEC-despliegue-sucursal.md). Este plan específico no reemplaza `tasks/plan.md`, que conserva el plan histórico de platform access.

## Orden

1. Configuración de destino API por Electron, con validación estricta y prueba de la credencial de terminal. Sin esto no hay caja remota.
2. Configuración local de balanza e impresora y textos de UI independientes de COM1/COM6. Sin esto no se puede asignar hardware diferente a cada PC.
3. Publicación de Angular junto a la API y preparación del paquete Electron. Sin esto el administrador dependería del servidor Angular de desarrollo.
4. Guía de instalación y verificación del workshop, sin tocar la base de desarrollo ni exponer secretos.

## Riesgos y decisiones abiertas

- La LAN del laboratorio podrá usar HTTP privado solo con opt-in explícito y datos ficticios. Para instalación real se exige HTTPS y un certificado confiable por las cajas.
- La salida a Internet de la sucursal para ARCA/pasarelas no equivale a publicar la API. Callbacks y webhooks externos requieren un diseño de exposición mínima posterior.
- No se promete continuidad si cae el servidor local. Respaldos, UPS y recuperación se probarán antes de pasar de laboratorio a uso real.
- La integración futura entre sucursales no condiciona el flujo de venta local, pero deberá diseñarse antes de consolidar datos.
