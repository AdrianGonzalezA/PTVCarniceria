# SuperAdmin del portal central

Esta pantalla administra **empresas centrales y sus administradores**. La cuenta SuperAdmin central es distinta de los usuarios del POS y de los administradores de cada empresa. No permite abrir cajas ni crear una conexión a sucursal desde el navegador.

1. Abrir `/superadmin` en el sitio de administración central e ingresar con el usuario y contraseña central entregados por implementación. No se pide elegir empresa.
2. En **Empresas centrales**, pulsar **Nueva empresa**. Indicar un código único (de 2 a 40 letras, números o guiones), el nombre, el usuario del primer administrador y su contraseña inicial. Pulsar **Crear empresa**. La empresa y ese administrador se crean juntos; no se muestran ni guardan contraseñas en la grilla.
3. Para consultar una empresa existente, pulsar **Administrar** en su fila. Se abre otra vista con sus administradores; allí se puede agregar uno nuevo. El usuario queda asociado exclusivamente a esa empresa central.
4. Usar **Volver a empresas** para regresar al listado y **Cerrar sesión** al terminar.

El administrador creado ingresa por la página principal del sitio central (`/`), indicando **código de empresa**, usuario y contraseña. Una empresa nueva puede aparecer sin sucursales: el equipo de implementación debe registrar cada conexión por separado mediante su procedimiento protegido. Esta pantalla no pide URLs ni tokens de sucursal. Si una clave central se pierde, solicitar al equipo de implementación el restablecimiento; no crear otra empresa para recuperar el acceso.
