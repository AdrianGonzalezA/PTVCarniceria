# Carnicerías

Sistema de punto de venta para carnicerías: API .NET 10 y PostgreSQL, interfaz Angular y puesto de venta dentro de Electron. El flujo de venta persiste tickets, pagos y stock; el sitio administrativo mantiene el catálogo y la organización. Las pantallas se revisan en Electron y se alinean con `Documentos/maqueta`.

**Despliegue objetivo:** una instalación autónoma por sucursal (API y PostgreSQL locales, varias cajas Electron por LAN, administración en navegador); no hay sincronización entre sucursales. Contrato y pasos de laboratorio: [SPEC-despliegue-sucursal.md](SPEC-despliegue-sucursal.md) y [guía de implementación](documentacion/despliegue/guia-workshop-por-sucursal.md). El paquete de laboratorio se construye con `tools/Publish-BranchLab.ps1`; la prueba de varias PC físicas y los requisitos productivos siguen pendientes. La regla de **una sola base de desarrollo** indicada más abajo no cambia.

## Estado del POS

La lista real permite guardar hasta cuatro tickets A–D independientes por caja, cajero y turno, reservar stock por cada uno y recuperar los borradores confirmados por PostgreSQL al reiniciar Electron. La pestaña activa se recuerda localmente para ese turno. Solo se puede cambiar de ticket cuando el último guardado terminó; una edición aún no enviada no se promete recuperable ante un corte. El POS permite registrar una venta con pagos y convertir su reserva en egreso físico dentro de una transacción. El cierre del turno conserva el saldo contable y se bloquea si aún quedan tickets abiertos. Los datos actuales del catálogo son ficticios y la base visual es no productiva; no hay arqueo físico. Tras confirmar una venta no fiscal, Electron puede guardar un PDF de prueba en la carpeta `tickets` de su perfil y, solo si esa caja tiene impresora asignada, imprimir por el COM configurado en Administración. Sin impresora asignada no se intenta imprimir. Si se solicita factura electrónica o ticket fiscal de prueba, el API consulta ARCA homologación y solo después de obtener CAE habilita la salida correspondiente rotulada «SIN VALIDEZ FISCAL». Ninguna salida vuelve a cobrar ni modifica stock. El detalle, configuración y límites están en [tasks/arca-homologacion.md](tasks/arca-homologacion.md). `npm run electron:test:pdf --prefix src/Carnicerias.Pos` prueba el motor PDF nativo sin registrar una venta ni crear un archivo.

El estado detallado y los pendientes están en `tasks/pos-todo.md`. El contrato de cobro está en `SPEC-pos-checkout.md`; la evidencia de prueba en Electron y sus límites están en `tasks/pos-checkout-plan.md`.

Mercado Pago admite QR dinámico, QR fijo del puesto y Point por caja desde el cierre de venta. El fijo reutiliza la imagen del QR de un puesto activo/integrado, pero cada cobro crea una orden por el importe de esa venta; requiere el `external_id` alfanumérico del puesto, no su ID numérico. El POS consulta qué modos están disponibles para la caja, cajero y turno actuales y solo ofrece los configurados; si esa consulta falla, no habilita una orden nueva, aunque mantiene visible una orden previa para conciliarla. Una orden pendiente no cierra el ticket: mientras el cierre está abierto, el POS consulta automáticamente su estado cada 5 segundos; el cajero también puede consultar o solicitar cancelación. Solo la acreditación verificada por la API permite registrar el pago, y el cajero aún debe confirmar la venta. La configuración de prueba se carga en **Configuración → Pasarelas de cobro** y sus requisitos están en `SPEC-mercado-pago.md`. Point virtual se probó de punta a punta en Electron con acreditación simulada en sandbox, venta y stock persistidos; no hubo dinero real. La sucursal/caja QR de prueba ya está vinculada a Caja 1 y se verificó desde el móvil una orden acreditada; falta una prueba de cobro completo por QR fijo, configurar un webhook HTTPS público y conciliar en segundo plano.

## Administración

`visual-admin` ingresa y elige empresa/sucursal antes de abrir `/admin`. Allí puede mantener categorías, artículos y códigos alternativos, listas/precios, empresas, sucursales, cajas, existencias y cajeros. En Configuración → Impuestos por artículo administra un catálogo por empresa: IVA operativo y otros gravámenes asignables a uno, varios o todos los artículos existentes. Los otros gravámenes permanecen «no calculados» y no alteran ventas ni ARCA; el 21 % de Empresa Visual es un dato ficticio de homologación. Puede restablecer la contraseña de cualquier usuario de la empresa, incluida la propia cuenta; al hacerlo se revocan todas las sesiones de esa persona. Por ahora no se crean otros administradores ni roles desde la interfaz. El historial de ventas, pagos, turnos y movimientos es de solo lectura y permite filtros por sucursal, caja (cuando corresponde) y fecha. El cajero no tiene acceso a estas API. Contrato y pendientes: `SPEC-admin.md`, `SPEC-tax-catalog.md` y `tasks/admin-plan.md`.

Los ABM de categorías, artículos y códigos alternativos, listas de precios con habilitación por sucursal, y clientes tienen **Descargar plantilla** e **Importar Excel**. Cada `.xlsx` trae hojas de carga vacías y una hoja `Ejemplos` que no se importa. El modal exige previsualizar y confirmar; informa altas, cambios, filas no importadas y errores de hoja/fila. Un artículo existente no se sobrescribe, aunque se pueden agregarle códigos alternativos nuevos. Los cambios de precio y de costo estadístico conservan vigencias; el precio puede quedar por debajo del costo. Ver `SPEC-admin-excel-import.md` para columnas y reglas. La carga de prueba existente en `carnicerias_test_visual` es ficticia y no debe interpretarse como histórico comercial.

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

Para simular la balanza, asignar COM6 a la caja en **Administración → Organización → Cajas → Dispositivos**, abrir PuTTY en su puerto emparejado COM5 a 9600/8N1 y escribir `ST,0.750,kg` seguido de Enter. Si no se ve lo escrito en PuTTY, usar `Change Settings…` → `Terminal` → `Local echo: Force on`; opcionalmente, `Local line editing: Force on` permite corregir antes de enviar con Enter. No usar `Ctrl+V` para pegar: PuTTY puede transmitir el byte de control 22 en lugar del texto; escribir a mano o usar clic derecho / `Shift+Insert`. Solo con balanza asignada, **Leer balanza** copia una lectura estable de menos de 30 segundos al campo de kilos de un producto por peso. `US,0.750,kg` indica peso inestable y no se acepta. Sin balanza asignada se ingresa el peso manualmente; la pieza trazable conserva el peso de su etiqueta. Al cerrar una venta, el POS imprime automáticamente solo si la caja tiene una impresora de texto o Hasar asignada; de lo contrario, no intenta abrir un puerto. El ticket fiscal espera un CAE confirmado de ARCA homologación; la factura electrónica conserva su PDF. Una falla de impresión no repite el cobro. Contrato y límites: `SPEC-device-emulation.md`.

La Hasar SMH/P-441F del laboratorio se comunica por **COM8**, separada de COM1. El enlace y un ticket físico de prueba quedaron verificados (N.º 5160). El menú de prueba fue retirado: ahora la impresión toma el detalle de la venta confirmada, no un importe fijo. Ticket no fiscal y ticket fiscal B para consumidor final usan comandos distintos; en el segundo caso se espera el CAE de ARCA homologación y se identifica como tal en el papel. Hay una marca local por venta para evitar duplicados ante respuestas inciertas. La aceptación física del cierre de venta con el nuevo binario aún está pendiente: [alcance y pasos](tasks/hasar-441f-evaluacion.md).

Para comprobar el puerto sin vender ni tocar stock: `npm run serial:smoke --prefix src/Carnicerias.Pos`. PuTTY en COM2 debe mostrar el identificador `SERIAL-SMOKE`.

Para repetir la prueba visual de tickets A/B en esta instalación de desarrollo, iniciar Caja 1 con `--remote-debugging-port=9224` solo en la máquina local y ejecutar `node src/Carnicerias.Pos/tests/ticket-slots-smoke.cjs --prepare` con A y B vacíos. El script agrega una Gaseosa a A y un Pan rallado a B, dejando reservas ficticias. Reiniciar el proceso de Electron y ejecutar `node src/Carnicerias.Pos/tests/ticket-slots-smoke.cjs --verify`: comprueba que ambos detalles vuelven separados. No usar la depuración remota en un despliegue ni apuntar esta prueba a datos reales.

Para inspeccionar visualmente el POS en ese mismo Electron de desarrollo, `node src/Carnicerias.Pos/tests/inspect-electron.mjs --screenshot <ruta-local.png>` captura su ventana; el mismo helper acepta una expresión DOM para diagnósticos puntuales. Borrar las capturas después de revisarlas y nunca dejar habilitado el puerto de depuración en una caja desplegada.

```powershell
dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --configuration Debug --no-restore
dotnet test tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~SaleTicketSlotsTests"
npm test --prefix src/Carnicerias.Web -- --watch=false
npm run lint --prefix src/Carnicerias.Web
npm run electron:test --prefix src/Carnicerias.Pos
```

Las pruebas de integración PostgreSQL actualmente requieren `CARNICERIAS_TEST_CONNECTION_STRING` apuntando a una base desechable cuyo nombre comience con `carnicerias_test_`. **No ejecutarlas bajo la regla de base única vigente**: podrían crear o limpiar otra base, y nunca deben apuntarse a `carnicerias_test_visual`. Hasta que el usuario autorice una estrategia compatible, ejecutar solo las pruebas que no necesitan esa base y dejar explícita la cobertura de integración pendiente.

### Clover sandbox (vinculación OAuth)

La administración → **Pasarelas de cobro** permite cargar App ID, Merchant ID y App Secret, y generar un enlace OAuth v2 de diez minutos. El secreto y el par access/refresh token se protegen en la API y no se devuelven al navegador. La autorización **no habilita todavía cobros Clover en el POS**: faltan la asignación del dispositivo por caja y Cloud Pay Display.

Con el comercio conectado, **Probar conexión Clover** consulta el [comercio configurado en la API oficial de sandbox](https://docs.clover.com/dev/reference/merchantgetmerchant). Si venció el access token, el servidor usa el [refresh token de un solo uso](https://docs.clover.com/dev/docs/use-refresh-token-to-generate-new-expiring-token) y guarda el nuevo par antes de consultar. El botón envía un POST autenticado; no expone tokens ni registra pagos. Una renovación ambigua queda bloqueada para no repetir el token: hay que reconectar el comercio mediante OAuth.

**Consultar dispositivos Clover** usa la [lista oficial de dispositivos provisionados del comercio sandbox](https://docs.clover.com/dev/reference/devicegetmerchantdevices) y muestra solo nombre, modelo e ID, nunca serial ni token. El 13/10/2026 la consulta real respondió sin dispositivos para el comercio de prueba. Esta lista no demuestra que [Cloud Pay Display](https://docs.clover.com/dev/docs/configuring-a-cloud-connection) esté instalado o ejecutándose; para probar cobros presenciales sigue haciendo falta un [Dev Kit asociado al comercio sandbox](https://docs.clover.com/dev/docs/devices-and-dev-kits-faqs).

El retorno público usa `https://clover-sandbox.nuestromundo.ar/clover/callback`. Cloudflare Tunnel entrega únicamente las rutas GET `/clover`, `/clover/status`, `/clover/launch` y `/clover/callback` al proceso separado `http://127.0.0.1:5198`; `/api/*` responde 404 en ese proceso. Para ejecutarlo, configurar la **misma** `CARNICERIAS_CONNECTION_STRING` de la API local, apuntando solo a `carnicerias_test_visual`, y usar `dotnet run --project src/Carnicerias.Api --launch-profile clover-tunnel`. El túnel y la API deben ejecutarse bajo el mismo usuario de Windows para compartir las claves locales de Data Protection. En un servidor, configurar un repositorio de claves compartido y persistente antes de usar OAuth; no copiar secretos ni claves al repositorio.

Las migraciones aditivas `20261013124143_AddCloverOAuth` y `20261013143200_AddCloverRefreshReservation` se aplicaron el 13/10/2026 solo a `carnicerias_test_visual`. Se completó un callback OAuth real y después se renovó el token vencido con una consulta autenticada al comercio sandbox que respondió `verified`; la nueva vigencia quedó guardada. Esto **no habilita aún cobros Clover**. Sin Dev Kit no podemos verificar asociación física ni Cloud Pay Display; esas pruebas y la asignación de dispositivo por caja siguen pendientes. Si Docker/WSL no responde, no crear otra base ni utilizar un callback simulado como prueba de vinculación. Referencias: [OAuth v2 de Clover](https://docs.clover.com/dev/docs/oauth-flows-in-clover) y [generación de tokens con vencimiento](https://docs.clover.com/dev/docs/generate-expiring-tokens-using-v2-oauth-flow).

## Documentación

- [Arquitectura de la solución — estado actual](documentacion/arquitectura/arquitectura-solucion-estado-actual.md): componentes, fronteras, despliegue por sucursal, central, flujos y mapa para documentación derivada.
- [Anexo técnico de arquitectura](documentacion/arquitectura/anexo-tecnico-metodologia-conectividad-seguridad.md): metodología, tecnologías, conexiones y seguridad implementada y pendiente.
- [Base del manual de usuarios](documentacion/manual-usuarios/README.md): acceso, POS, cobros, negocio, configuración y límites vigentes.
- `REQUERIMIENTOS_MVP.md` y `CAPABILITY_MAP.md`: alcance y módulos.
- `SPEC-pos-sales.md`, `SPEC-pos-checkout.md` y `SPEC-catalog-pricing.md`: contratos del POS.
- `SPEC-platform-access.md`, `SPEC-admin.md` y `tasks/admin-plan.md`: acceso y administración.
- `tasks/pos-todo.md`: estado vigente y próximos incrementos.
