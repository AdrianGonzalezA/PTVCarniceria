# Seguimiento: vista central de negocio

- [x] Reglas de negocio confirmadas: "hoy" calendario argentino y caja solo por sucursal.
- [x] Ventana predeterminada, rango UTC y fechas locales con pruebas.
- [x] Lector protegido de resúmenes de sucursales con pruebas.
- [x] Agregación completa/parcial y endpoint autenticado con pruebas.
- [x] Panel Angular y pruebas de sesión/estados.
- [x] Smoke no destructivo en `carnicerias_test_visual` y documentación operativa.
- [ ] Revisión visual del sitio central si hay superficie de navegador disponible.
- [ ] Pruebas del taller físico por VPN/HTTPS (pendiente de infraestructura de taller).

## Laboratorio de tres sucursales Visual

- [x] Confirmar estado inicial y empresa Visual sin tocar LA Rústica.
- [x] Crear dos sucursales Visual y una caja provisionada en cada una; verificar identidad y dispositivos vacíos.
- [x] Configurar cajero, precios y stock ficticio acotado; abrir ambas cajas en perfiles Electron independientes. Se verificaron visualmente las pantallas de login en Electron.
- [x] Probar turno y venta no fiscal de cada sucursal por API y verificar persistencia aislada; la operación visual posterior al login en Electron sigue pendiente.
- [x] Registrar ambas instalaciones adicionales; sus contratos remotos devolvieron empresa y sucursal correctas y los resúmenes del día mostraron 0, 1 y 1 ventas.
- [x] Verificar agregado central autenticado completo y parcial: tres sucursales `ok` con 2 ventas y $7.200; Sur `unavailable` anuló el agregado; tras reiniciar su servicio se recuperó `complete`.
- [x] Registrar evidencia y pendientes antes de fusionar ramas. No se hizo commit, push ni merge.
