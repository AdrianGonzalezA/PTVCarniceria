# Carnicerías

Sistema de punto de venta para carnicerías: API .NET 10 y PostgreSQL, interfaz Angular y puesto de venta dentro de Electron. El flujo de venta persiste tickets, pagos y stock; el sitio administrativo mantiene el catálogo y la organización. Las pantallas se revisan en Electron y se alinean con `Documentos/maqueta`.

## Estado del POS

La lista real permite guardar hasta cuatro tickets A–D independientes por caja, cajero y turno, reservar stock por cada uno y recuperar los borradores confirmados por PostgreSQL al reiniciar Electron. La pestaña activa se recuerda localmente para ese turno. Solo se puede cambiar de ticket cuando el último guardado terminó; una edición aún no enviada no se promete recuperable ante un corte. El POS permite registrar una venta con pagos y convertir su reserva en egreso físico dentro de una transacción. El cierre del turno conserva el saldo contable y se bloquea si aún quedan tickets abiertos. Los datos actuales del catálogo son ficticios y la base visual es no productiva; no hay arqueo físico. Tras confirmar una venta no fiscal, Electron puede guardar un PDF de prueba en la carpeta `tickets` de su perfil o enviar el detalle por COM1 a 9600 baudios. Si se solicita factura electrónica o ticket fiscal de prueba, el API consulta ARCA homologación y solo después de obtener CAE habilita el PDF/COM1 rotulados «SIN VALIDEZ FISCAL». Ninguna salida vuelve a cobrar ni modifica stock. El detalle, configuración y límites están en [tasks/arca-homologacion.md](tasks/arca-homologacion.md). `npm run electron:test:pdf --prefix src/Carnicerias.Pos` prueba el motor PDF nativo sin registrar una venta ni crear un archivo.

El estado detallado y los pendientes están en `tasks/pos-todo.md`. El contrato de cobro está en `SPEC-pos-checkout.md`; la evidencia de prueba en Electron y sus límites están en `tasks/pos-checkout-plan.md`.

## Administración

`visual-admin` ingresa y elige empresa/sucursal antes de abrir `/admin`. Allí puede mantener categorías, artículos y códigos alternativos, listas/precios, empresas, sucursales, cajas, existencias y cajeros. En Configuración → Impuestos por artículo administra un catálogo por empresa: IVA operativo y otros gravámenes asignables a uno, varios o todos los artículos existentes. Los otros gravámenes permanecen «no calculados» y no alteran ventas ni ARCA; el 21 % de Empresa Visual es un dato ficticio de homologación. Puede restablecer la contraseña de cualquier usuario de la empresa, incluida la propia cuenta; al hacerlo se revocan todas las sesiones de esa persona. Por ahora no se crean otros administradores ni roles desde la interfaz. El historial de ventas, pagos, turnos y movimientos es de solo lectura y permite filtros por sucursal, caja (cuando corresponde) y fecha. El cajero no tiene acceso a estas API. Contrato y pendientes: `SPEC-admin.md`, `SPEC-tax-catalog.md` y `tasks/admin-plan.md`.

La sección **Lectura de etiquetas** permite guardar revisiones inmutables de perfiles de campos fijos por empresa (por ejemplo `pro_numero(5) pro_item(3) peso(4)`), y probarlas dentro de Electron con un código escrito y decimales de peso configurables. El resultado separa campos y kilos, pero todavía no ingresa piezas al stock ni agrega productos al ticket. No hay ningún formato real de ciclo 2 activado por defecto; el diseño aprobado y los puntos pendientes están en `CAPABILITY-MAP-pos-peripherals.md` y `SPEC-inventory-traceability.md`.

## Desarrollo local

Requisitos: SDK .NET 10, PostgreSQL, Node.js compatible con `src/Carnicerias.Web/package.json` y npm 11.19.0. Con una base local previamente inicializada, configurar `CARNICERIAS_CONNECTION_STRING` en el entorno y ejecutar el API en `http://localhost:5197`:

Durante el desarrollo se usa **una única base de la aplicación**, `carnicerias_test_visual`, en el PostgreSQL de Docker expuesto en `127.0.0.1:55433`. No crear otra base (tampoco una de pruebas) sin autorización explícita. Para volver a empezar se limpian los datos en esta misma base y se reaplican los scripts del repositorio. La base de sistema `postgres` del servidor no es una base del POS. Esta instalación es ficticia y no productiva; el despliegue final será en un servidor PostgreSQL cuando el producto esté terminado.

El set inicial contiene `Empresa Visual`, `Sucursal Visual`, los usuarios `visual-admin` y `visual-cashier`, dos terminales (`Caja 1` y `Caja 2`), nueve productos con existencias y una lista de precios. Durante el desarrollo se agregaron datos ficticios adicionales; no se vuelve a ejecutar el seed sobre una base con usuarios. Las contraseñas y credenciales de terminal se entregan fuera de Git. `tools/SeedPosVisualData.sql` conserva el catálogo y stock ficticio; `seed-pos-visual` del proyecto `tools/Carnicerias.Bootstrap` inicializa identidades y terminales solo cuando la base está vacía.

Para unificar temporalmente las contraseñas de todos los usuarios existentes en esa base local, ejecutar `dotnet run --project tools/Carnicerias.Bootstrap -- reset-visual-passwords` con `CARNICERIAS_CONNECTION_STRING` apuntando a `127.0.0.1:55433/carnicerias_test_visual` y `CARNICERIAS_VISUAL_PASSWORD` configurada solo en el entorno de esa consola. El comando usa Argon2id, revoca las sesiones activas y no modifica ventas ni stock. No registrar la clave en Git. Solo para esa conexión local, el alta y restablecimiento de contraseñas desde la administración admiten un mínimo de 6 caracteres; en cualquier otra conexión el mínimo sigue siendo 12. La interfaz consulta ese mínimo al API.

```powershell
dotnet run --project src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release -- --urls http://localhost:5197
```

En otra consola, con dependencias instaladas mediante `npm ci` en `src/Carnicerias.Web` y `src/Carnicerias.Pos`:

```powershell
npm run electron:start --prefix src/Carnicerias.Pos
```

Los perfiles `caja-1` y `caja-2` abren directamente el POS aunque conserven una sesión administrativa. El Electron sin perfil mantiene el ingreso habitual.

Ese comando compila Angular, prepara los archivos del POS y abre Electron con el perfil local ya provisionado de `Caja 1`. Para abrir otra terminal, usar `npm run electron:start:caja2 --prefix src/Carnicerias.Pos`; para revisar únicamente la administración sin credencial de caja, `npm run electron:start:admin --prefix src/Carnicerias.Pos`. La activación de la caja en administración y la credencial guardada en el perfil de Electron son requisitos distintos: un perfil sin credencial vigente no puede operar el POS aunque la caja esté activa. Para pruebas automatizadas:

Para simular la balanza, abrir PuTTY en COM5 a 9600/8N1 y escribir `ST,0.750,kg` seguido de Enter. Si no se ve lo escrito en PuTTY, usar `Change Settings…` → `Terminal` → `Local echo: Force on`; opcionalmente, `Local line editing: Force on` permite corregir antes de enviar con Enter. No usar `Ctrl+V` para pegar: PuTTY puede transmitir el byte de control 22 en lugar del texto; escribir a mano o usar clic derecho / `Shift+Insert`. El POS escucha permanentemente el extremo emparejado COM6; **Leer balanza COM6** copia una lectura estable de menos de 30 segundos al campo de kilos de un producto por peso. `US,0.750,kg` indica peso inestable y no se acepta. No hace falta abrir otro Electron; la pieza trazable conserva el peso de su etiqueta. Tras cobrar, **Imprimir en COM1** envía el ticket en texto UTF-8 por COM1 a 9600/8N1 con saltos CRLF; el formato fiscal de prueba solo se habilita con CAE confirmado. En el par virtual COM1–COM2, PuTTY debe escuchar COM2. El controlador HHD acepta la escritura pero no confirma `FlushFileBuffers`, por lo que el POS indica que verifiques la recepción en PuTTY antes de reimprimir. **Guardar ticket PDF** sigue siendo independiente. Una falla serial no repite el cobro ni impide ingresar peso manualmente. Contrato y límites: `SPEC-device-emulation.md`.

Para comprobar el puerto sin vender ni tocar stock: `npm run serial:smoke --prefix src/Carnicerias.Pos`. PuTTY en COM2 debe mostrar el identificador `SERIAL-SMOKE`.

Para repetir la prueba visual de tickets A/B en esta instalación de desarrollo, iniciar Caja 1 con `--remote-debugging-port=9224` solo en la máquina local y ejecutar `node src/Carnicerias.Pos/tests/ticket-slots-smoke.cjs --prepare` con A y B vacíos. El script agrega una Gaseosa a A y un Pan rallado a B, dejando reservas ficticias. Reiniciar el proceso de Electron y ejecutar `node src/Carnicerias.Pos/tests/ticket-slots-smoke.cjs --verify`: comprueba que ambos detalles vuelven separados. No usar la depuración remota en un despliegue ni apuntar esta prueba a datos reales.

```powershell
dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --configuration Debug --no-restore
dotnet test tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~SaleTicketSlotsTests"
npm test --prefix src/Carnicerias.Web -- --watch=false
npm run lint --prefix src/Carnicerias.Web
npm run electron:test --prefix src/Carnicerias.Pos
```

Las pruebas de integración PostgreSQL actualmente requieren `CARNICERIAS_TEST_CONNECTION_STRING` apuntando a una base desechable cuyo nombre comience con `carnicerias_test_`. **No ejecutarlas bajo la regla de base única vigente**: podrían crear o limpiar otra base, y nunca deben apuntarse a `carnicerias_test_visual`. Hasta que el usuario autorice una estrategia compatible, ejecutar solo las pruebas que no necesitan esa base y dejar explícita la cobertura de integración pendiente.

## Documentación

- `REQUERIMIENTOS_MVP.md` y `CAPABILITY_MAP.md`: alcance y módulos.
- `SPEC-pos-sales.md`, `SPEC-pos-checkout.md` y `SPEC-catalog-pricing.md`: contratos del POS.
- `SPEC-platform-access.md`, `SPEC-admin.md` y `tasks/admin-plan.md`: acceso y administración.
- `tasks/pos-todo.md`: estado vigente y próximos incrementos.
