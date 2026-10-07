# Reglas de desarrollo del POS

- Mantener **una sola base de datos de la aplicación** durante este desarrollo: `carnicerias_test_visual` en el PostgreSQL de Docker (puerto local `55433`). La base de mantenimiento `postgres` es propia del servidor y no cuenta como base de la aplicación.
- No crear, clonar ni provisionar otra base de datos de Carnicerías, ni siquiera para pruebas, sin autorización explícita del usuario. Si hace falta empezar de cero, limpiar y volver a cargar los datos **en la misma base**.
- No ejecutar pruebas de integración que creen o reinicien una base distinta. Las pruebas unitarias, de frontend y las verificaciones no destructivas sí pueden ejecutarse; documentar qué integración quedó pendiente.
- Conservar en el repositorio el código, las migraciones y los scripts de inicialización. Nunca guardar contraseñas ni credenciales de terminal en Git.
- La base actual contiene datos ficticios para probar el flujo de venta. Más adelante se desplegará en un servidor PostgreSQL; no tratar esta base local como productiva ni usar su contenido como histórico real.
- Revisar visualmente el POS siempre dentro de Electron, no en una pestaña del navegador.
