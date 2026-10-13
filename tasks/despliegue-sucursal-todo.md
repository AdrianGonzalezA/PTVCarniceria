# Checklist del workshop por sucursal

- [x] Electron acepta origen API por puesto, seguro por defecto; pruebas de rechazo y reenvío de credencial; `npm run electron:test --prefix src/Carnicerias.Pos` y build de Electron.
- [x] Balanza/impresora configurables en cada puesto; pruebas seriales y Angular sin COM fijo; `npm run electron:test --prefix src/Carnicerias.Pos` y `npm test --prefix src/Carnicerias.Web -- --watch=false`.
- [x] API sirve build Angular en LAN y `/api/health`; build .NET y Angular, verificación HTTP no destructiva (`200` salud/administración, `404` API desconocida).
- [x] Guía de empaquetado, provisión, respaldo y checklist de prueba de dos cajas; paquete Windows x64 generado y revisado en Electron.
- [ ] Workshop físico: dos o más PC, caja/credencial propias, ventas concurrentes, stock, turnos, reinicio y corte de red. Pendiente hasta disponer de esas máquinas; no usar otra base de aplicación sin autorización explícita.
- [ ] Antes de producción: HTTPS interno, ACL de configuración, servicio automático, respaldo/restauración y claves de Data Protection, fiscalidad y periféricos reales, callback externo mínimo.
