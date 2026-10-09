# 01 · Acceso, sucursal y turno de caja

## Ingresar

1. En **Iniciar sesión**, escribí tu usuario o correo y contraseña.
2. Elegí la **empresa** y la **sucursal** habilitadas y pulsá **Confirmar sucursal**, incluso si solo aparece una opción.
3. Desde la pantalla de contexto, abrí **Punto de venta** o **Administración**, según tus permisos. También podés usar **Cambiar empresa o sucursal** y **Cerrar sesión**.

El POS requiere una caja activa y una credencial de esa caja guardada en el perfil local de Electron. Que la caja figure activa en Administración no reemplaza esa credencial. Si aparece **Caja no disponible**, un administrador debe revisar o renovar la vinculación de la terminal; el cajero no debe intentar cobrar desde otra caja por su cuenta.

## Abrir un turno

El POS no permite operar tickets ni cobrar sin turno abierto. Después de ingresar con tu usuario, pulsá **Abrir turno**, registrá el **fondo inicial en efectivo** (puede ser $0) y confirmá. La cabecera mostrará la sucursal, caja, cajero y **Turno abierto**.

El turno atribuye ventas, cobros y movimientos al cajero responsable de esa caja. Otras cajas de la misma sucursal pueden estar abiertas al mismo tiempo; sus turnos y tickets no se mezclan.

## Consultar y cerrar el turno

Pulsá **Turno abierto** para ver el resumen: ventas, efectivo de esas ventas, cargos a cuenta, cobranzas de cuenta, otros medios y **efectivo registrado en caja**. Este último es un saldo contable que incluye el fondo inicial; **no es un arqueo físico**.

Antes de pulsar **Cerrar turno**, confirmá o cancelá todos los tickets A–D pendientes. El sistema bloquea el cierre si quedan borradores. Al cerrar, se conserva el resumen y el último turno cerrado puede consultarse nuevamente. El siguiente responsable debe identificarse con su propio usuario y abrir un turno nuevo; no debe continuar con la sesión del cajero anterior.

## Si no aparece la sucursal o la caja

- Si no hay empresas o sucursales disponibles, pedí al administrador que revise tus asignaciones y el estado de la sucursal.
- Si la caja está inactiva o su credencial local falta/caducó, pedí al administrador que la reactive o rote la credencial y la vincule al perfil de Electron correspondiente.
- Si un turno ajeno sigue abierto en la caja, primero debe resolverse el relevo de responsabilidad; no compartas la sesión ni crees ventas atribuidas a otra persona.

Más detalle técnico: [acceso y cajas](../../SPEC-platform-access.md) y [cierre de venta/turno](../../SPEC-pos-checkout.md).
