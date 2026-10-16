# Checklist del workshop por sucursal

- [x] Electron acepta origen API por puesto, seguro por defecto; pruebas de rechazo y reenvío de credencial; `npm run electron:test --prefix src/Carnicerias.Pos` y build de Electron.
- [x] Balanza/impresora asignables a cada caja desde Administración, sin hardware predeterminado; pruebas seriales y Angular sin COM fijo; `npm run electron:test --prefix src/Carnicerias.Pos` y `npm test --prefix src/Carnicerias.Web -- --watch=false`.
- [x] API sirve build Angular en LAN y `/api/health`; build .NET y Angular, verificación HTTP no destructiva (`200` salud/administración, `404` API desconocida).
- [x] Guía de empaquetado, provisión, respaldo y checklist de prueba de dos cajas; paquete Windows x64 generado y revisado en Electron.
- [ ] Workshop físico: dos o más PC, caja/credencial propias, ventas concurrentes, stock, turnos, reinicio y corte de red. Pendiente hasta disponer de esas máquinas; no usar otra base de aplicación sin autorización explícita.
- [ ] Antes de producción: HTTPS interno, ACL de configuración, servicio automático, respaldo/restauración y claves de Data Protection, fiscalidad y periféricos reales, callback externo mínimo.

## Instaladores finales (posteriores al workshop)

- [ ] D1. Contrato de instalación: inventario de parámetros y secretos, cuenta de servicio, rutas y dependencias; decidir PostgreSQL incluido o prerrequisito. Verificar que servidor, administración y cajas tienen entradas separadas y ninguna credencial va en Git o en los paquetes.
- [ ] D2. Claves persistentes: fijar almacén y respaldo de ASP.NET Data Protection con ACL; probar que ARCA/Mercado Pago/Clover siguen descifrables tras reinicio y restauración autorizada, sin imprimir secretos. Depende de D1.
- [ ] D3. Instalador de servidor: API + administración web, servicio Windows, cadena PostgreSQL protegida, HTTPS y firewall LAN, migraciones con respaldo y SuperAdmin inicial. Verificar instalación limpia, reinicio y fallo seguro sin crear otra base de desarrollo. Depende de D1-D2.
- [ ] D4. Instalador de caja: Electron, URL API y credencial individual provisionada una vez en perfil cifrado. COM/baud se asignan desde Administración por caja; sin asignación no se abre el dispositivo. Verificar dos PC/cajas distintas y rechazo de credencial o servidor equivocados dentro de Electron. Depende de D1 y D3.
- [ ] D5. Mantenimiento: actualización, reversión, rotación de credencial y desinstalación conservan datos y secretos salvo confirmación explícita. Verificar recuperación con backup y checklist por sucursal. Depende de D3-D4.
- [ ] Checkpoint final: instalación repetible con dos paquetes, navegador para administración y dos cajas Electron; venta, stock y turnos compartidos; reinicio/restauración comprobados en laboratorio autorizado. Depende de D1-D5 y del workshop físico.
