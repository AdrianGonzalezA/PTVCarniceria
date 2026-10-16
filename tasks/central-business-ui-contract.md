# Contrato visual breve · negocio central

- **Trabajo:** leer el negocio de varias sucursales en vivo, con período claro y estado de completitud. No administrar cajas desde aquí.
- **Acción primaria:** consultar el día argentino actual. Acción secundaria: elegir fechas locales inclusivas (máximo 31 días) y actualizar manualmente.
- **Jerarquía:** advertencia de datos parciales antes de cualquier cifra, total de ventas y pagos únicamente completo, tabla/lista de sucursales con saldo de caja por sucursal y hora de generación.
- **Estados:** carga, error general, sin instalaciones, completo, incompleto, fila no disponible, sesión vencida y rango inválido. Ninguna ausencia se convierte en cero.
- **Referencia visual:** colores y controles de `tasks/central-access-ui-contract.md`; fondo claro, encabezados teal y acción lima. Densidad de información comercial, sin tarjetas decorativas ni tema oscuro.
- **Adaptación y acceso:** fecha con etiqueta visible, botones nativos, estado por texto y no solo color, tabla desplazable o filas apiladas a 320 px, `role="status"` para carga y `role="alert"` para errores/incompletitud.
