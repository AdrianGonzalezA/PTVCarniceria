# Arranque local para demo: La Rústica / Alta Córdoba

Estos comandos son para **PowerShell** y parten de `C:\Proyectos\Carnicerias`. La demo habitual usa **La Rústica / Alta Córdoba**, no Empresa Visual: API POS de código actual `5197`, administrador Angular `4200` y perfiles Electron `caja-rustica-a`, `caja-rustica-b` y `caja-rustica-c`. Abrir una consola distinta para cada proceso. Usar únicamente la base de desarrollo existente `carnicerias_test_visual` en PostgreSQL Docker (`127.0.0.1:55433`); estos pasos no crean ni reinician bases.

La API `5197` es **compartida** en este laboratorio: La Rústica queda determinada por la credencial del perfil Electron y el acceso del administrador, no por el puerto. Si la demo incluye el portal central, arrancar además el conjunto La Rústica `5249`/`5251`/`4302` del apartado 6. Ambos conjuntos leen la **misma base de prueba**; no equivalen todavía a dos instalaciones físicas separadas. El conjunto central usa binarios publicados previamente; no recompila automáticamente la rama actual UI2. Para revisar la interfaz nueva del administrador usar `4200`; para el POS, Electron.

> **Identidad por empresa:** `rustica-admin` pertenece a La Rústica y el código POS local es `C-102EE26253E148ED97567C2C84BCCB44`. El portal central usa `LARUSTICA` y otra sesión. El POS Electron obtiene la empresa de la credencial de caja, no de un selector de empresa. Véase la [auditoría de identidad](auditoria-identidad-por-empresa-20261013.md).

## Apagado local antes o después de la demo

Para apagar **solo todas las APIs de Carnicerías** en los puertos `5197` y `5245`–`5251`, dejando Angular y PostgreSQL activos:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Stop-LocalServices.ps1 -Group Apis
```

Si también se desea detener los Angular locales (`4200`, `4300`, `4301`, `4302`), usar el apagado completo:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Stop-LocalServices.ps1 -Group All
```

También se puede detener un grupo sin afectar los demás:

```powershell
.\tools\Stop-LocalServices.ps1 -Group Pos
.\tools\Stop-LocalServices.ps1 -Group Portals
.\tools\Start-VisualCentralLab.ps1 -Action Stop
.\tools\Start-RusticaManagedLab.ps1 -Action Stop
```

`Pos` comprende `5197` y `4200`; `Portals`, `4300` y `4301`. `-Action Stop` de Visual detiene `5245`–`5248` y `5250`; el de La Rústica detiene `5249`, `5251` y `4302`. `Apis` conserva los Angular, incluso `4302`. Los comandos de parada funcionan aunque PostgreSQL no esté activo, verifican que cada puerto pertenezca al servicio esperado y no detienen procesos ajenos. Se pueden repetir si los puertos ya están libres. No detienen Docker, PostgreSQL ni ventanas Electron. El apagado de la API POS por IP LAN no está contemplado en este comando local: cerrarla con `Ctrl+C` en su consola.

## 1. API POS para vender con La Rústica (consola 1)

Comprobar que Docker Desktop y el contenedor PostgreSQL estén activos:

```powershell
docker ps
```

Si ya hay una API en el puerto 5197, detenerla primero con `Ctrl+C` en su consola o usar `-Group Apis` del apartado de apagado. La respuesta de `/api/health` por sí sola no comprueba la conexión a PostgreSQL. Iniciar la API con este comando, sin completar usuarios, contraseñas ni cadenas de conexión:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-LocalPosApi.ps1
```

El script toma la contraseña del contenedor Docker existente, valida el puerto `55433` y la única base `carnicerias_test_visual`, y le pasa la conexión a la API sin mostrarla ni guardarla en Git. Dejar la consola abierta. En otra consola, verificar la conexión real con:

```powershell
curl.exe -s -o NUL -w "%{http_code}" http://localhost:5197/api/sessions/current
```

Sin sesión iniciada, debe responder `401`. Si responde `500`, la API no quedó bien conectada; cerrar esa instancia y volver a ejecutar el comando de este apartado. No iniciar la API con `dotnet run` directamente.

## 2. Administración Angular de La Rústica (consola 2)

Si `http://localhost:4200/admin` ya abre, **reutilizar ese Angular** y omitir el comando siguiente; detener solo las APIs no lo cierra. No iniciar dos `ng serve` en el mismo puerto.

```powershell
cd C:\Proyectos\Carnicerias
npm run start --prefix src/Carnicerias.Web
```

Abrir `http://localhost:4200/admin` en un navegador común e ingresar con el código de empresa local `C-102EE26253E148ED97567C2C84BCCB44` y un administrador de La Rústica, por ejemplo `rustica-admin`; seleccionar Alta Córdoba si la API compartida lo solicita. **No** usar `LARUSTICA` en este acceso: ese código pertenece al portal central. Angular recarga los cambios de desarrollo y su proxy envía `/api` a `http://localhost:5197`. Si faltan dependencias, ejecutar una vez `npm ci --prefix src/Carnicerias.Web` antes de iniciar.

## 3. Caja A de La Rústica en Electron (consola 3)

```powershell
cd C:\Proyectos\Carnicerias
npm run build:web --prefix src/Carnicerias.Pos
npm run build:electron --prefix src/Carnicerias.Pos
npm run stage:web --prefix src/Carnicerias.Pos
cd .\src\Carnicerias.Pos
.\node_modules\.bin\electron.cmd . --pos-profile=caja-rustica-a
```

Los tres comandos `build`/`stage` preparan el código actual y la última línea abre el perfil local **Caja A de La Rústica**. Debe conservar su credencial de terminal vigente; además la caja debe estar activa y el cajero debe abrir su turno. Si faltan dependencias, ejecutar una vez `npm ci --prefix src/Carnicerias.Pos` (y `npm ci --prefix src/Carnicerias.Web`). `npm run electron:start --prefix src/Carnicerias.Pos` abre el perfil genérico `caja-1` y **no** es el comando de esta demo.

La caja **no usa** el servidor Angular de `4200`: carga su propia compilación. Para ver cambios de interfaz en el POS, cerrar Electron con normalidad y volver a ejecutar el comando de Caja A. Revisar visualmente el POS siempre dentro de Electron; el navegador del paso 2 es solo para Administración.

## 4. Cajas A, B y C de La Rústica en esta misma PC

Mantener **una sola API y una sola base**, compartidas por las cajas. En Administración → Configuración → Organización → Cajas, comprobar que las cajas A, B y C de **Alta Córdoba** estén activas. Cada caja necesita su **propia credencial de terminal**, entregada al provisionar su perfil local. El nombre `caja-rustica-c` no crea la caja en la base ni sustituye esa credencial. Usar un cajero/turno responsable por caja para comprobar ventas y cierres separados.

Para abrir las tres ventanas sin recompilar los mismos archivos mientras otra caja está abierta, preparar la compilación **una sola vez** desde la raíz:

```powershell
cd C:\Proyectos\Carnicerias
npm run build:web --prefix src/Carnicerias.Pos
npm run build:electron --prefix src/Carnicerias.Pos
npm run stage:web --prefix src/Carnicerias.Pos
```

`stage:web` conserva los archivos de versiones anteriores que puedan seguir usando las cajas Electron abiertas. La copia nueva se carga al cerrar y volver a abrir cada caja; no hace falta volver a vincular su perfil. Para armar un paquete de entrega, cerrar primero todas las cajas de desarrollo: `electron:build` y `Publish-BranchLab.ps1` usan una preparación limpia (`--clean`) para no incluir archivos obsoletos.

Después, abrir una consola PowerShell **por caja** y ejecutar solo su línea. Se pueden abrir dos o tres a la vez:

```powershell
cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
.\node_modules\.bin\electron.cmd . --pos-profile=caja-rustica-a
```

```powershell
cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
.\node_modules\.bin\electron.cmd . --pos-profile=caja-rustica-b
```

```powershell
cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
.\node_modules\.bin\electron.cmd . --pos-profile=caja-rustica-c
```

Si el perfil de Caja B o C todavía **no tiene credencial**, crear/obtener la credencial desde Administración (solo se muestra al crearla o rotarla) y hacer **su primer arranque** así, cambiando la letra del perfil cuando corresponda. No pegar el valor en este documento ni en el comando:

```powershell
cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
$credencialCaja = Read-Host 'Credencial de esta caja' -AsSecureString
$env:CARNICERIAS_POS_TERMINAL_TOKEN = [System.Net.NetworkCredential]::new('', $credencialCaja).Password
Remove-Variable credencialCaja
try { .\node_modules\.bin\electron.cmd . --pos-profile=caja-rustica-c }
finally { Remove-Item Env:\CARNICERIAS_POS_TERMINAL_TOKEN -ErrorAction SilentlyContinue }
```

Los siguientes arranques usan la línea normal del perfil, sin volver a ingresar la credencial. No copiar perfiles ni usar la credencial de Caja A en B/C. Un perfil ya vinculado a otra credencial no se puede reutilizar para una caja diferente. No tomar las credenciales de `documentacion/info.txt` ni dejarlas en un script de arranque.

**Periféricos en una misma PC:** ninguna caja tiene balanza ni impresora por defecto. En Administración → Organización → Cajas → Dispositivos, asignar a cada caja sus propios puertos y velocidades. Windows no permite compartir un COM abierto entre cajas: para pruebas simultáneas, usar pares virtuales distintos. Si una caja queda sin balanza se ingresa el peso manualmente; si queda sin impresora, se guarda la venta sin intentar imprimir. `pos-config.json` solo define la URL de la API.

## 5. Cajas de La Rústica en dos o tres computadoras distintas

La API y PostgreSQL corren **solo en la PC servidora**; las otras PC llevan únicamente Electron. La API debe escuchar en una IP LAN accesible, no solo en `localhost`. Si hoy está levantada en `localhost:5197`, detenerla en su consola y volver a iniciarla con el mismo script, sustituyendo la IP de ejemplo por la IP LAN real de esa PC:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-LocalPosApi.ps1 -BindAddress '192.168.10.20'
```

Habilitar el acceso al puerto 5197 **solo desde la LAN de prueba** en el firewall del servidor. En cada PC de caja, comprobar `Invoke-RestMethod 'http://192.168.10.20:5197/api/health'`. No exponer PostgreSQL (`55433`) a la LAN ni crear una base por caja.

Preparar el paquete de Electron según la [guía de workshop](guia-workshop-por-sucursal.md#3-construir-y-copiar-los-artefactos). En **cada PC**, copiar la carpeta completa de Electron y `Start-PosTerminal.ps1`; crear su propio `C:\Carnicerias\Caja\pos-config.json` con `apiOrigin` apuntando a la IP LAN real y `allowInsecureLan: true` **solo para este laboratorio aislado**. Configurar los COM desde Administración para la caja correspondiente. Desde `C:\Carnicerias\Caja`, ejecutar en cada equipo (ajustar rutas):

```powershell
cd C:\Carnicerias\Caja

# PC de Caja A
.\Start-PosTerminal.ps1 -ExecutablePath '.\Carnicerias POS-win32-x64\Carnicerias.Pos.exe' -ConfigPath '.\pos-config.json' -TerminalProfile caja-rustica-a -Provision

# PC de Caja B
.\Start-PosTerminal.ps1 -ExecutablePath '.\Carnicerias POS-win32-x64\Carnicerias.Pos.exe' -ConfigPath '.\pos-config.json' -TerminalProfile caja-rustica-b -Provision

# PC de Caja C, si corresponde
.\Start-PosTerminal.ps1 -ExecutablePath '.\Carnicerias POS-win32-x64\Carnicerias.Pos.exe' -ConfigPath '.\pos-config.json' -TerminalProfile caja-rustica-c -Provision
```

Ejecutar **solo la línea de esa PC**. `-Provision` solicita sin eco la credencial **distinta** de su caja; se usa únicamente en la primera vinculación. Para los siguientes arranques, repetir la misma línea **sin** `-Provision`. Cada PC puede usar sus propios COM1/COM6, porque sus puertos son independientes. Para la configuración de red, firewall y archivos de caja, seguir la [guía completa](guia-workshop-por-sucursal.md#7-preparar-cada-pc-de-caja).

Para detener la API o Angular de esta computadora, usar los comandos de [apagado local](#apagado-local-antes-o-después-de-la-demo) o `Ctrl+C` en su consola. Para detener una caja, cerrar normalmente su ventana de Electron.

## 6. Central y administración de sucursal para La Rústica

Estos comandos complementan el POS compartido `:5197`; no lo reemplazan ni crean otra base. Funcionan **solo en esta computadora**, con los binarios publicados y el almacén DPAPI de la misma cuenta Windows. El script comprueba el contenedor, la base `carnicerias_test_visual` y las identidades de **La Rústica / Alta Córdoba** antes de arrancar. `Provision` no es un comando de arranque: no repetirlo para resolver un puerto ocupado o una API caída.

Para iniciar la API de Alta Córdoba (`5249`), su API central (`5251`) y su portal Angular (`4302`), con esos tres puertos libres:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-RusticaManagedLab.ps1 -Action Check
.\tools\Start-RusticaManagedLab.ps1 -Action Start
.\tools\Start-RusticaManagedLab.ps1 -Action Verify
```

`Verify` espera hasta 90 segundos a que los tres servicios respondan; no hace falta introducir una pausa entre `Start` y `Verify`. Si vence ese plazo, informa la URL que no responde. No repetir `Start` si los puertos siguen ocupados: revisar el servicio indicado y ejecutar solo `Verify` otra vez.

Si se usó `-Group Apis` pero quedó abierto el Angular `4302` de una corrida anterior, `-Action Start` no puede ocupar ese puerto. En ese caso detener **el conjunto La Rústica** con `.\tools\Stop-LocalServices.ps1 -Group Rustica` (también cierra `4302`) y repetir `Check`, `Start`, `Verify`. No usar `Provision` para resolverlo.

Abrir `http://localhost:5249/admin` para la administración de sucursal servida por el paquete publicado, y `http://localhost:4302/` para el administrador central de La Rústica. El código de empresa central es `LARUSTICA`, distinto del código del login POS local. La API central lee en vivo a Alta Córdoba por `5249`; el POS de los pasos 1–4 usa `5197`. La demo funciona porque ambos procesos ven la misma base de laboratorio, no porque exista una sincronización de instalaciones físicas.

Si únicamente hay que reiniciar la API de Alta Córdoba y el puerto `5249` está libre, usar `.\tools\Start-RusticaManagedLab.ps1 -Action StartBranch`. El script no detiene ni reemplaza procesos existentes. Si todos los servicios de un grupo ya responden, omitir `Start` y ejecutar `Verify`; si solo responde una parte, diagnosticar qué puerto falta. La guía de [administración central](administracion-central.md) detalla el almacén protegido.

### Otros laboratorios históricos (no forman parte de la demo La Rústica)

Para iniciar las tres APIs Visual (`5246`–`5248`) y las centrales Visual (`5245`, `5250`), usar solo cuando se necesite probar específicamente Empresa Visual:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-VisualCentralLab.ps1 -Action Check
.\tools\Start-VisualCentralLab.ps1 -Action Start
.\tools\Start-VisualCentralLab.ps1 -Action Verify
```

Para iniciar solo las centrales Visual, si ambos puertos están libres, existe `-Action StartCentral`. Las APIs Visual no incluyen el administrador Angular. No iniciar este conjunto para una demo común de La Rústica.

## 7. URLs de la demo y portales opcionales

Para La Rústica, el portal central `4302` se inicia **con** `Start-RusticaManagedLab.ps1 -Action Start`; no abrir otro `ng serve` en ese puerto. Los portales `4300` y `4301` pertenecen a pruebas Visual históricas y **no hacen falta** para esta demo. Si se necesitan por separado, abrir una consola por portal y comprobar que el puerto esté libre:

```powershell
cd C:\Proyectos\Carnicerias
npm run start --prefix src/Carnicerias.Central.Web -- --host localhost --port 4300
```

```powershell
cd C:\Proyectos\Carnicerias
npm run start --prefix src/Carnicerias.Central.Web -- --host localhost --port 4301 --proxy-config proxy.superadmin.local.json
```

| Destino | URL local | Se conecta a |
| --- | --- | --- |
| Administración POS actual de La Rústica | `http://localhost:4200/admin` | API compartida `5197`; ingresar código de empresa local y Alta Córdoba |
| Caja A/B/C La Rústica | Electron `caja-rustica-a/b/c` | API compartida `5197` y una credencial propia por caja |
| API POS compartida | `http://localhost:5197/api/health` | PostgreSQL `127.0.0.1:55433/carnicerias_test_visual` |
| Administración publicada Alta Córdoba | `http://localhost:5249/admin` | API de sucursal vinculada `5249`; paquete publicado previamente |
| Portal central La Rústica | `http://localhost:4302/` · `http://localhost:4302/superadmin` | API central `5251` |
| API central La Rústica | `http://127.0.0.1:5251/api/health` | Misma base PostgreSQL de laboratorio |
| API sucursal Alta Córdoba | `http://127.0.0.1:5249/api/health` | Misma base PostgreSQL de laboratorio |
| Portal central Visual / SuperAdmin, opcional | `http://localhost:4300/` · `http://localhost:4300/superadmin` | API central Visual `5245` |
| Portal central Visual alternativo, opcional | `http://localhost:4301/` · `http://localhost:4301/superadmin` | API central Visual `5250` |
| APIs centrales Visual, opcionales | `http://127.0.0.1:5245/api/health` · `http://127.0.0.1:5250/api/health` | Misma base PostgreSQL de laboratorio |
| APIs sucursal Visual, Norte y Sur | `http://127.0.0.1:5246/api/health` · `http://127.0.0.1:5247/api/health` · `http://127.0.0.1:5248/api/health` | Misma base PostgreSQL de laboratorio |

En los portales centrales, `/` es el acceso del administrador de empresa y `/superadmin` es el del SuperAdmin central; no son cuentas del POS local. Todas las URLs de esta tabla son loopback de laboratorio, no destinos de despliegue. El POS se revisa visualmente únicamente en Electron.




Lista hecha por Adrian

primer terminal
cd C:\Proyectos\Carnicerias
.\tools\Stop-LocalServices.ps1 -Group Apis

.\tools\Start-LocalPosApi.ps1

segunda terminal
cd C:\Proyectos\Carnicerias
npm run start --prefix src/Carnicerias.Web
admin sucursal
http://localhost:4200/
admin Instalacion
http://localhost:4200/superadmin


tercer teminal cajas
cd C:\Proyectos\Carnicerias
npm run build:web --prefix src/Carnicerias.Pos
npm run build:electron --prefix src/Carnicerias.Pos
npm run stage:web --prefix src/Carnicerias.Pos
cd .\src\Carnicerias.Pos
.\node_modules\.bin\electron.cmd . --pos-profile=caja-rustica-a



central

cd C:\Proyectos\Carnicerias
.\tools\Start-RusticaManagedLab.ps1 -Action Check
.\tools\Start-RusticaManagedLab.ps1 -Action Start
.\tools\Start-RusticaManagedLab.ps1 -Action Verify

http://localhost:4302/
