# Administración central: negocio y conexiones

La administración central usa una cuenta propia de **cada empresa central**. No abre turnos ni reemplaza al administrador o al POS de cada sucursal. Tras ingresar, aparecen las pestañas **Negocio** y **Conexiones a sucursales**. El SuperAdmin del portal central pertenece a Recursos Informáticos y crea empresas/administradores; no entra como administrador de una empresa ni configura las conexiones cotidianas.

En **Negocio en vivo**, el sistema consulta inicialmente **Hoy**: desde las 00:00 de Buenos Aires hasta el momento de la consulta. **Actualizar** vuelve a pedir los datos. Para otro período, elegir **Desde** y **Hasta** como fechas inclusivas de Argentina y pulsar **Consultar período**; se admiten como máximo 31 días. **Hoy** vuelve al día corriente.

Cuando todas las sucursales activas responden, el panel muestra el total de ventas, tickets y pagos aplicados, seguido del detalle por sucursal. El **saldo de turnos abiertos** y la cantidad de turnos aparecen únicamente en cada sucursal: son una fotografía al consultar y no se suman a las ventas del período. También se indica cuándo se generó cada respuesta.

Si una sucursal no responde o entrega datos incompatibles, aparece **Datos incompletos**. Las sucursales disponibles siguen visibles, pero **no se muestra un total general**; los valores de la sucursal faltante figuran como no disponibles, no como cero. Si no hay sucursales registradas, el panel lo indica. Ante sesión vencida, se vuelve al ingreso. Esta pantalla es de solo lectura: no cobra, factura ni altera stock o caja.

## Conexiones a sucursales

Antes de agregar una conexión, pedir al equipo de implementación los tres identificadores (`installationId`, empresa remota y sucursal remota), URL raíz aprobada y token de servicio de esa sucursal. Implementación debe haber habilitado el contrato de lectura de la sucursal, aprobado esa URL/IP para **esta empresa** y configurado la clave de cifrado de la central. El administrador **no puede** aprobar destinos nuevos desde la pantalla.

1. Abrir **Conexiones a sucursales** → **Agregar conexión**. Ingresar etiqueta, URL raíz, los tres UUID y el token hexadecimal de 64 caracteres. Pulsar **Verificar y agregar**. La central comprueba la identidad remota antes de guardar. El token se borra del formulario al enviarlo, no aparece en el listado y no puede recuperarse desde el navegador.
2. En la fila, **Comprobar** actualiza el resultado y la fecha de una prueba puntual. *Disponible* significa que respondió en esa prueba, **no** que estará siempre en línea. *No disponible* tampoco equivale a ventas cero: en **Negocio** se muestra un agregado incompleto si falta una sucursal activa.
3. **Editar** permite cambiar el nombre sin contactar a la sucursal. Cambiar URL o introducir **Nuevo token** obliga a verificar destino, credencial y los tres UUID. Si falla, la configuración anterior queda intacta. Los identificadores no se editan.
4. **Desactivar** pide confirmación, conserva registro e historial y excluye esa sucursal del dashboard. **Reactivar** comprueba nuevamente la sucursal antes de incluirla. Si la comprobación falla, permanece inactiva.

Para rotar una credencial, implementación prepara primero el nuevo verificador como `NextCredentialSha256` en la API de sucursal; luego el administrador carga el nuevo token en **Editar** y, tras comprobarlo, implementación retira el anterior. Para conexiones antiguas creadas por CLI, también es obligatorio introducir un token nuevo al rotar: el anterior nunca se muestra.

Si aparece **No se pudo verificar la sucursal**, revisar con implementación destino aprobado, conectividad/TLS, token y UUID; no enviar el token por capturas, chat ni tickets. Si falta el almacén de credenciales, implementación debe revisar la clave protegida de la central. Si se corta la respuesta mientras se guarda, **actualizar el listado antes de volver a enviar**, para evitar un alta o cambio duplicado. El administrador solo ve conexiones de su empresa; no puede seleccionar otra.
