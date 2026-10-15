# ADR 0005 — Identidad y registro independientes de la central

**Estado:** aceptada para el laboratorio, 14/10/2026. Relacionada con [ADR 0004](0004-consulta-central-de-sucursales.md), [especificación](../../SPEC-central-access.md) y [guía](../despliegue/administracion-central.md).

## Contexto

Cada sucursal conserva sus cajas, usuarios, ventas y PostgreSQL local. Un administrador de empresa debe consultar varias sucursales desde otro proceso sin convertir una cuenta POS o un token de servicio en acceso central. Durante el desarrollo existe una sola base de aplicación autorizada: `carnicerias_test_visual`.

## Decisión

- La API y el Angular centrales son artefactos separados del POS. La identidad central tiene empresa, administrador, sesión y cookie propios. Ni el cajero, ni el administrador local, ni el superadmin local, ni la credencial entre servidores se aceptan como sesión central.
- El esquema `central_access` vive **en la misma base visual de desarrollo**, con `CentralAccessDbContext`, migraciones e historial EF propios. Esto es aislamiento lógico de laboratorio, no una promesa de aislamiento físico de base en producción. La API central no mapea tablas POS ni migra al arrancar. Cualquier otra base durante este desarrollo requiere nueva autorización.
- El equipo de implementación crea la empresa, primer administrador y conexiones mediante un CLI protegido. El navegador no puede proponer destinos ni tokens. Una instalación guarda `centralCompanyId`, los tres UUID remotos, etiqueta, URL y **referencia** de secreto, nunca el secreto. Los UUID de empresa central y empresa remota no se presumen iguales.
- Antes de guardar, la central coteja destino exacto e IPs aprobadas, fija la IP para la conexión, conserva TLS estándar, no usa proxy ni sigue redirecciones, y verifica versión e identidad de `/api/central/v1/installation`. HTTP se permite únicamente para `127.0.0.1` con opt-in de laboratorio.
- `GET /api/v1/installations` deriva la empresa de la sesión validada en el servidor y devuelve solo instalaciones activas registradas, paginadas, sin URL ni referencia. *Registrada* no significa *conectada* ni implica ventas cero. El estado vivo y los agregados son responsabilidad de `central-business-view`.

## Consecuencias y límites

Una caída de la central no impide vender en la sucursal. La central no copia clientes, tickets, stock ni pagos. Debe publicarse bajo HTTPS y mismo origen web/API; en multiproceso o exposición pública se necesita limitación de intentos compartida/en el borde, VPN y una política de egreso. La rotación física de secretos, la separación de base de datos y la consulta viva de negocio se decidirán y probarán en etapas posteriores. Las pruebas de este ADR conservaron la única base y las 54 ventas ficticias; el workshop multiservidor sigue pendiente.
