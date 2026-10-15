# Contrato visual breve · acceso central

- **Trabajo de la pantalla:** identificar al administrador de una empresa central y dejar visible el destino de sucursales registradas, sin fingir que es una caja ni mezclar permisos locales.
- **Acción primaria:** ingresar con código de empresa, usuario y contraseña. Acción secundaria luego de entrar: cerrar sesión.
- **Estados obligatorios:** verificación inicial, formulario, envío ocupado, credenciales inválidas genéricas, exceso de intentos, error de conexión y sesión activa. Ningún estado presenta datos comerciales ficticios como reales.
- **Referencia del producto:** cabecera `Carnicerías`, fondo `#f3f6f4`, panel blanco, teal `#005e60`, botón lima `#d3ff00`, bordes discretos y densidad de la administración SuperAdmin existente. La central se distingue por el rótulo `Administración central`.
- **Adaptación:** una columna y controles de ancho completo a 320 px; panel de ingreso de ancho acotado en escritorio. Etiquetas visibles, foco claro, errores con `role="alert"`, carga con `role="status"`, orden natural de tabulación.
- **No hacer:** tema oscuro, gradientes decorativos, tarjetas de métricas inventadas, enlaces a caja Electron ni almacenamiento local de credenciales.
