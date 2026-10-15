# Plan: vista central de negocio en vivo

Base: [SPEC-central-business-view.md](../SPEC-central-business-view.md), aprobada el 2026-10-14. La autorización previa de avanzar sin pausas salvo reglas de negocio permite ejecutar este plan; no supone permiso para crear otra base o publicar a Internet.

## Cortes verificables

1. **Ventana y contrato de salida.** Servidor calcula "hoy" en Buenos Aires con reloj inyectable y valida rangos explícitos UTC de hasta 31 días. Pruebas de medianoche, parámetros incompletos y rango excesivo. Sin consultas remotas aún.
2. **Consulta protegida de una instalación.** Selección por empresa de la sesión, revalidación de destino, bearer por referencia, HTTP fijado sin proxy/redirección y respuesta remota limitada. Pruebas para identidad, ventana, moneda, importes, token ausente y fallos. Una sucursal caída produce estado no disponible sin secretos.
3. **Composición de varias sucursales.** Paralelismo acotado, filas en orden estable, suma solo si todas responden. Caja abierta solo por sucursal. Pruebas de cero, una y varias instalaciones, parcial e incompatibilidad.
4. **Pantalla central.** Vista responsive con "hoy" inicial, rango editable, estados carga/vacío/parcial/error, actualización manual y protección frente a respuestas tardías tras cerrar sesión/cambiar de empresa. Pruebas Angular y revisión visual del sitio central si hay navegador disponible. No se altera el POS.
5. **Verificación y documentación.** Tests .NET/Angular, builds, smoke read-only contra la única base `carnicerias_test_visual`, revisión de seguridad y actualización de guía central. No correr suites que creen otra base. Si la revisión visual está impedida por herramientas, dejarlo pendiente sin afirmar que pasó.

## Dependencias y límites

- La configuración de destinos y secretos se lee en el servidor; si está ausente, falla cerrado por sucursal.
- El HTTP local `127.0.0.1` se permite solo con configuración explícita de laboratorio; despliegue por VPN/HTTPS.
- Ninguna métrica comercial se persiste en la central. No hay cambio de esquema.
- La operación de venta local no consulta a la central.

## Laboratorio de tres sucursales Visual (15/10/2026)

Este corte amplía la verificación del contrato central con dos sucursales nuevas de **Empresa Visual** en `carnicerias_test_visual`. No modifica LA Rústica ni crea otra base.

1. **Alta y aislamiento.** Confirmar IDs y estado existentes; crear Sucursal Visual Norte y Sucursal Visual Sur por la API administrativa, una caja distinta en cada una y conservar sus credenciales solo en perfiles locales protegidos. Comprobar empresa/sucursal/caja por consulta independiente. Ningún dispositivo serial se asigna por defecto.
2. **Operación de cajas.** Dar acceso de cajero y lista de precios a ambas sucursales; cargar stock ficticio solo si la venta de prueba lo requiere. Levantar un Electron por caja con perfil separado y verificar identificación, turno y venta no fiscal en efectivo sin usar ARCA ni pasarelas externas. Comprobar que tickets, movimientos y turnos queden en su sucursal.
3. **Lectura central.** Iniciar un servicio de sucursal por cada nueva sucursal con identidad y secreto distintos, registrarlos en la empresa central de laboratorio y verificar tres filas, agregación completa, aislamiento de LA Rústica y estado parcial al dejar una sucursal fuera de servicio. No persistir métricas en la central.
4. **Cierre.** Documentar resultados y límites; dejar cajas y servicios levantados para revisión si es seguro. No fusionar esta rama a `develop` ni `main` antes de la prueba del usuario.

**Checkpoint:** IDs, perfiles y credenciales no se cruzan; tres sucursales aparecen en la central; ninguna consulta ni venta toca LA Rústica; el POS se inspecciona solo en Electron. Si faltan credenciales de administrador o conectividad del laboratorio, registrar el bloqueo sin insertar datos manualmente por SQL.
