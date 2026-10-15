# Administración central: migración controlada en laboratorio

La API central y la API POS son procesos separados. Durante este desarrollo comparten **únicamente** la base existente `carnicerias_test_visual` del PostgreSQL Docker, publicada en `127.0.0.1:55433`. Las tablas centrales usan el esquema `central_access` y su propio `__EFMigrationsHistory`. La API central **no migra al arrancar**. No crear otra base ni ejecutar la suite de integración que la provisione.

## Antes de migrar

1. Confirmar con `docker ps` que el contenedor esperado publica `127.0.0.1:55433->5432/tcp`. Confirmar con `psql` que `current_database()` es `carnicerias_test_visual` y existen `platform_access` y `pos_sales`.
2. Generar un nombre **nuevo** de respaldo `.dump` y ejecutar `docker exec <contenedor> pg_dump -U postgres -d carnicerias_test_visual -Fc -f /tmp/<nombre>.dump`. El archivo nunca debe sobreescribir un respaldo anterior.
3. Verificar `docker exec <contenedor> pg_restore --list /tmp/<nombre>.dump`: debe terminar con código cero e indicar `dbname: carnicerias_test_visual` y formato `CUSTOM`.
4. Copiar con `docker cp <contenedor>:/tmp/<nombre>.dump .local-backups/<nombre>.dump`. Comparar `docker exec <contenedor> sha256sum /tmp/<nombre>.dump` con `Get-FileHash -Algorithm SHA256 .local-backups/<nombre>.dump`. `.local-backups/` está excluido de Git y contiene el respaldo completo de datos ficticios; mantenerlo protegido.
5. Revisar el SQL idempotente con `dotnet ef migrations script --project src/Carnicerias.Central.Infrastructure --startup-project src/Carnicerias.Central.Api --context CentralAccessDbContext --idempotent`. Debe afectar exclusivamente `central_access`.

La cadena de conexión se proporciona **solo** mediante `CARNICERIAS_CENTRAL_CONNECTION_STRING` desde la configuración protegida de la máquina. No pasar la contraseña como argumento ni guardarla en Git o en el historial de comandos. El comando de laboratorio rechaza cualquier host, puerto o base distintos de `127.0.0.1:55433/carnicerias_test_visual`, y exige un respaldo reciente en formato custom cuyo SHA-256 coincida:

```powershell
dotnet run --project tools/Carnicerias.Central.Bootstrap/Carnicerias.Central.Bootstrap.csproj --configuration Release -- migrate --backup-file <ruta-absoluta-al-dump> --backup-sha256 <SHA256> --apply
```

La herramienta valida el destino antes de abrir la conexión, confirma la identidad de la base y la presencia de los esquemas POS, y aplica **solo** las migraciones de `CentralAccessDbContext`. Una ampliación a otro servidor o base requerirá una decisión y un guardia nuevos; no aflojar esta validación silenciosamente.

## Alta y recuperación inicial de cuentas centrales

Con la misma variable de entorno protegida y las migraciones centrales aplicadas, ejecutar desde una terminal **interactiva**:

```powershell
dotnet run --project tools/Carnicerias.Central.Bootstrap/Carnicerias.Central.Bootstrap.csproj --configuration Release -- company-create --code <codigo-central> --name <nombre> --username <administrador>
dotnet run --project tools/Carnicerias.Central.Bootstrap/Carnicerias.Central.Bootstrap.csproj --configuration Release -- password-reset --code <codigo-central> --username <administrador>
```

La herramienta pide la contraseña dos veces sin eco y rechaza la entrada redirigida. El CLI actual verifica antes de conectar que su destino es exclusivamente `127.0.0.1:55433/carnicerias_test_visual`; **solo para esa base de laboratorio** admite seis caracteres. El generador central predeterminado mantiene un mínimo de 12 caracteres para cualquier otro uso. El formulario y el endpoint de *ingreso* aceptan una contraseña no vacía para poder verificar cuentas existentes, sin alterar la política de alta. No pasar claves como argumento, variable de entorno ni archivo. El alta graba empresa, administrador y auditoría en una única operación atómica; el código de empresa es único. El reseteo invalida las sesiones existentes y registra el evento en la misma operación. Son cuentas distintas de las cuentas de cada sucursal. No hay contraseña predeterminada ni ABM HTTP de estas altas.

## Evidencia del 14/10/2026

- Se respaldó la misma base visual con `pg_dump` 16 en archivo custom. `pg_restore --list` verificó 421 entradas y `dbname: carnicerias_test_visual`; el SHA-256 de la copia local coincidió con el archivo del contenedor. El respaldo local permanece en `.local-backups/`, fuera de Git.
- Se aplicó `20261014221626_InitialCentralAccess` mediante el comando protegido. La comprobación posterior mostró `central_access` con `companies`, `administrators`, `sessions`, `audit_events` y su historial propio; `platform_access` y `pos_sales` siguieron presentes con 3 empresas y 54 ventas confirmadas respectivamente. El historial EF local permanece en `public`.
- No se creó, clonó ni reinició ninguna base. La restauración del respaldo **no** se ejecutó; cualquier restauración futura requiere una decisión explícita y puede sustituir datos de prueba posteriores.
- Las pruebas puras de alta, hash y reseteo pasan; el alta real desde una terminal interactiva queda pendiente antes del smoke de login del portal. No se inventó ni se expuso una contraseña de administrador central.
- La segunda migración, `20261014224615_AddCentralInstallations`, se aplicó después de otro respaldo custom verificado (`pg_restore --list`: 445 entradas, SHA-256 local idéntico al del contenedor). Solo agregó `central_access.installations` y su índice; el historial central pasó a 2 entradas, el historial local siguió en 2, y permanecieron las 3 empresas y 54 ventas POS de prueba.

La prueba de restauración física en un servidor separado y el procedimiento de producción quedan pendientes del workshop. El encabezado y SHA-256 del archivo no sustituyen una prueba de restauración completa.

## Registro protegido de una sucursal

La central registra conexiones únicamente con el CLI, nunca mediante el navegador. Antes de ejecutarlo, el equipo de implementación configura en el entorno protegido del proceso:

- `CARNICERIAS_CENTRAL_CONNECTION_STRING`: la misma base de desarrollo admitida por el guardia actual.
- `CARNICERIAS_CENTRAL_APPROVED_BRANCHES`: JSON de destinos exactos y **todas** las IP que puede devolver DNS, por ejemplo `[ { "url": "https://sucursal.interna:8443/", "addresses": ["10.10.1.25"] } ]`. El destino del comando debe coincidir; la central fija la IP validada al conectar y mantiene la validación TLS del nombre.
- `CARNICERIAS_CENTRAL_BRANCH_SECRET_<REFERENCIA>`: token de servicio de 32 bytes en hexadecimal (64 caracteres). La referencia se normaliza a mayúsculas y solo ella se graba en `central_access.installations`. Guardar el valor mediante el administrador de secretos/configuración protegida del servidor, fuera de Git y sin pasarlo como argumento. La API de sucursal almacena solo el SHA-256 de los **bytes** del token en `Federation:CredentialSha256`.

```powershell
dotnet run --project tools/Carnicerias.Central.Bootstrap/Carnicerias.Central.Bootstrap.csproj --configuration Release -- installation-register --company-code <codigo-central> --installation-id <uuid-estable> --remote-company-id <uuid-empresa-sucursal> --remote-branch-id <uuid-sucursal> --label <etiqueta> --url <url-aprobada> --secret-ref <REFERENCIA>
```

La sucursal debe responder `GET /api/central/v1/installation` con la credencial y los tres UUID exactos. Un duplicado, destino/credencial ausente, error TLS, redirección o discrepancia no crea instalación; se audita el rechazo sin token. El listado web informa *registrada*, no *conectada*. Para HTTP de laboratorio se requiere **además** `--allow-loopback-http`, URL literal `http://127.0.0.1:<puerto>/` y aprobación de `127.0.0.1`. No usarlo para la red real. `AllowedHosts` de la API de sucursal debe incluir exactamente el host aprobado; una variable de entorno heredada puede prevalecer sobre `appsettings.json` y producir `400 Invalid Hostname` aunque el puerto esté abierto.

El sitio Angular central y `/api/v1/*` deben publicarse bajo **el mismo origen HTTPS** mediante un proxy reverso. La cookie es `Secure`; un cliente HTTP de PowerShell no la reenvía automáticamente en el laboratorio. Para acceder por el proxy de desarrollo `localhost:4300`, la API central local debe admitir tanto `localhost` como `127.0.0.1` en `AllowedHosts`; con solo `127.0.0.1`, el proxy devuelve `400 Invalid Hostname`. Ese proxy solo sirve para probar la API, no sustituye la revisión de un navegador ni una publicación HTTPS. La API central no ejecuta migraciones al arrancar.

## Consulta de negocio en vivo

La misma configuración protegida de destinos y credenciales usada para registrar una instalación debe estar disponible **también para el proceso de la API central** al consultar `/api/v1/business/summary`. Si falta, la instalación se informa no disponible; el registro por sí solo no comprueba conectividad. Solo para laboratorio local, `CARNICERIAS_CENTRAL_ALLOW_LOOPBACK_HTTP=true` habilita HTTP literal `127.0.0.1` ya incluido en los destinos aprobados. No habilitarlo en un despliegue por VPN: allí se usa HTTPS con nombre y certificado válidos.

La central obtiene las instalaciones activas de la empresa de la cookie, aprueba de nuevo el destino y lee `/api/central/v1/summary` sin enviar la cookie al POS. `GET /api/v1/business/summary` consulta el día corriente de Buenos Aires desde las 00:00 hasta el instante actual. Para un período distinto, pasar juntos `fromDate` y `toDate` (`yyyy-MM-dd`, fechas locales inclusivas) o juntos `fromUtc` y `toUtc` (instantes UTC). El máximo son 31 días. La respuesta no contiene URLs, referencias de secreto ni datos de tickets; el saldo de turnos abiertos se informa **por sucursal**, como fotografía separada de ventas. Una sucursal no disponible anula el agregado general, no los resultados individuales. Si dos instalaciones activas apuntan a la misma empresa/sucursal remota, ambas se marcan no disponibles para evitar doble conteo; el equipo de implementación debe corregir el registro. La fase remota tiene plazo global de 10 segundos, 4 conexiones simultáneas y 30 consultas/minuto por IP (`429` al excederlo).

El navegador sirve la pantalla desde el mismo origen que la API central; no abrir la ruta API como si fuera una página. No se requiere otra base ni se copian métricas de negocio a `central_access`. El sitio central puede revisarse en navegador; el POS sigue revisándose **solo en Electron**.

## Smoke de negocio del 15/10/2026

- La API central se inició con la única base `carnicerias_test_visual`, la instalación `Sucursal Visual` aprobada en `127.0.0.1:5246` y su credencial protegida. No se migró ni se creó base.
- Un `GET /api/central/v1/summary` autenticado respondió versión 1, sin ventas nuevas del día y con un turno abierto. La consulta central autenticada respondió `completeness=complete`, una sucursal `ok`, agregado de cero ventas del día y un turno abierto por sucursal; el inicio UTC de “hoy” fue `2026-10-15T03:00:00Z`. El mismo resultado se obtuvo a través del proxy Angular `localhost:4300` tras login central, sin inspeccionar ni exponer la cookie en la salida.
- Se verificaron 66 pruebas .NET de la central, 15 pruebas Angular y el build del portal. La revisión visual autenticada queda pendiente: las herramientas de navegador aislado no ofrecieron una superficie en esta sesión; no se interpreta el build como revisión de diseño.

## Smoke de laboratorio del 14/10/2026

- Se creó la empresa central ficticia `VISUAL-CENTRAL` con el administrador independiente `admin-central`, usando contraseña aleatoria ingresada sin eco por terminal interactiva. La contraseña no está en este documento ni en Git; entregarla y rotarla por un canal separado antes de cualquier despliegue.
- Una instancia temporal de la API de sucursal respondió en `127.0.0.1:5246` con `contractVersion=1` para `Sucursal Visual`. La API POS habitual conservó el puerto `localhost:5197`; ambas apuntan **a la misma** `carnicerias_test_visual`.
- El CLI registró esa instalación con referencia `VISUAL_BRANCH`. Un intento posterior con `branchId` erróneo devolvió rechazo y no creó otra. El conteo posterior fue: 1 empresa central, 1 instalación y 54 ventas POS ficticias preservadas.
- A través del proxy Angular/API se comprobó login `200`, listado paginado con esa sucursal `200`, logout `204` y cookie revocada `401`. En el smoke HTTP se reenvió explícitamente la cookie `Secure`; queda pendiente probar persistencia de cookie y diseño visual en navegador bajo HTTPS.
- Ninguna base nueva fue creada ni reiniciada. Queda pendiente el workshop con dos servidores, VPN/HTTPS y sucursales físicas; no se afirma conectividad permanente a partir del registro.

## Laboratorio Visual de tres sucursales del 15/10/2026

- Se mantuvo exclusivamente `carnicerias_test_visual`. Bajo **Empresa Visual** se crearon `Sucursal Visual Norte` (`b108c7d4-9805-432a-92e7-246e4418c83d`) y `Sucursal Visual Sur` (`d7f87213-287a-4b91-b4fc-726d74c717db`). LA Rústica no se modificó. Cada sucursal recibió su propia caja, un cajero asignado solo a ella y la lista ficticia `Mostrador`; ningún COM de impresora o balanza fue asignado.
- Los perfiles Electron `caja-visual-norte` y `caja-visual-sur` se abrieron simultáneamente. Capturas de las **ventanas Electron** mostraron respectivamente `Empresa Visual · Sucursal Visual Norte · Caja Visual Norte` y `Empresa Visual · Sucursal Visual Sur · Caja Visual Sur` en el login. La credencial de Norte no pudo seleccionar Sur (`POS_TERMINAL_BRANCH_MISMATCH`, HTTP 403). No se atribuye a estas capturas la prueba del flujo poslogin, que se hizo por API.
- Se ingresaron 5 unidades ficticias de `Pan rallado 1 kg` en Norte y 7 en Sur mediante ajustes separados. Con turnos abiertos y pagos en efectivo, se confirmaron dos tickets **no fiscales**: Norte, 1 unidad y $2.400; Sur, 2 unidades y $4.800. Los saldos de caja leídos de cada turno fueron $3.400 (incluye apertura de $1.000) y $5.300 (incluye apertura de $500). El stock final quedó en 4 y 5 unidades respectivamente; el stock de la sucursal Visual original permaneció en 32. Las cajas quedan con sus turnos abiertos para la revisión del usuario.
- En el laboratorio, tres procesos API de sucursal (`127.0.0.1:5246`, `:5247`, `:5248`) consultan **la misma base de pruebas**, cada uno fijado a un `BranchId` y una credencial de servicio diferente. Las instalaciones Norte y Sur se registraron en `VISUAL-CENTRAL` después de que `/api/central/v1/installation` devolviera los IDs esperados. El servicio central en `127.0.0.1:5245` respondió saludable. Para el día calendario argentino del 15/10, las consultas remotas devolvieron 0 ventas en Visual original, 1 por $2.400 en Norte y 1 por $4.800 en Sur; un turno abierto por sucursal. Pasaron las 66 pruebas puras de la central y las 86 pruebas de Electron; el build Angular y Electron se completó.
- Después de restablecer por CLI únicamente `admin-central` a la clave de laboratorio indicada por el usuario (sin registrarla en este documento ni en Git), un login por `localhost:4300/api/v1/sessions` respondió `200`. Se revocaron las sesiones anteriores. La consulta autenticada mostró las tres sucursales `ok`, `completeness=complete`, 2 ventas y $7.200 en el agregado; los saldos de caja permanecieron separados por sucursal. Al detener temporalmente **solo** el servicio de Sucursal Visual Sur, la consulta mostró `completeness=incomplete`, Sur `unavailable` y agregado `null`, sin convertir la ausencia en cero. Se reinició el servicio Sur y la consulta volvió a `complete` con 2 ventas y $7.200. El endpoint central sin sesión respondió `401`, como corresponde.
- **Pendiente:** recorrer el POS después del login en Electron y revisar visualmente el sitio central en navegador bajo HTTPS. Las claves de servicio de esta corrida se generaron en memoria y se inyectaron solo a procesos locales; no están en Git, en el documento ni en un almacén persistente. Si estos procesos se reinician, habrá que rotar esas claves de forma controlada antes de repetir la consulta. Los puertos de diagnóstico de Electron (`9223` y `9224`) se cerraron después de las capturas; las dos cajas quedaron abiertas en modo normal. Esto **no** modela todavía tres bases/servidores independientes del workshop físico.
