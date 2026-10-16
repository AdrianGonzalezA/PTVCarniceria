# Arranque local: POS, sucursales y central

Estos comandos son para **PowerShell**: primero en este equipo y, al final, en otras PC de caja. Los comandos de desarrollo parten de `C:\Proyectos\Carnicerias`. Abrir una consola distinta para cada proceso. Usar únicamente la base de desarrollo existente `carnicerias_test_visual` en PostgreSQL Docker (`127.0.0.1:55433`); estos pasos no crean ni reinician bases.

> **Identidad por empresa (13/10/2026):** las migraciones ya están aplicadas en la única base local. `visual-admin` pertenece solo a Empresa Visual; `rustica-admin` y `prueba-admin` son cuentas separadas. La empresa de prueba continúa inactiva, por lo que su login se rechaza. Para el navegador usar el código de empresa indicado en la [auditoría](auditoria-identidad-por-empresa-20261013.md); el POS Electron obtiene la empresa de la credencial de caja.

## 1. API (consola 1)

Comprobar que Docker Desktop y el contenedor PostgreSQL estén activos:

```powershell
docker ps
```

Si ya hay una API en el puerto 5197, detenerla primero con `Ctrl+C` en su consola. La respuesta de `/api/health` por sí sola no comprueba la conexión a PostgreSQL. Iniciar la API con este comando, sin completar usuarios, contraseñas ni cadenas de conexión:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-LocalPosApi.ps1
```

El script toma la contraseña del contenedor Docker existente, valida el puerto `55433` y la única base `carnicerias_test_visual`, y le pasa la conexión a la API sin mostrarla ni guardarla en Git. Dejar la consola abierta. En otra consola, verificar la conexión real con:

```powershell
curl.exe -s -o NUL -w "%{http_code}" http://localhost:5197/api/sessions/current
```

Sin sesión iniciada, debe responder `401`. Si responde `500`, la API no quedó bien conectada; cerrar esa instancia y volver a ejecutar el comando de este apartado. No iniciar la API con `dotnet run` directamente.

## 2. Administración Angular (consola 2)

```powershell
cd C:\Proyectos\Carnicerias
npm run start --prefix src/Carnicerias.Web
```

Abrir `http://localhost:4200/admin` en un navegador común e ingresar con el usuario administrador. Angular recarga los cambios de desarrollo y su proxy envía `/api` a `http://localhost:5197`. Si faltan dependencias, ejecutar una vez `npm ci --prefix src/Carnicerias.Web` antes de iniciar.

## 3. Caja 1 en Electron (consola 3)

```powershell
cd C:\Proyectos\Carnicerias
npm run electron:start --prefix src/Carnicerias.Pos
```

El comando compila Angular y Electron, prepara los archivos web y abre el perfil local `caja-1`. Debe conservar su credencial de terminal vigente; además la caja debe estar activa y el cajero debe abrir su turno. Si faltan dependencias, ejecutar una vez `npm ci --prefix src/Carnicerias.Pos` (y `npm ci --prefix src/Carnicerias.Web`).

La caja **no usa** el servidor Angular de `4200`: carga su propia compilación. Para ver cambios de interfaz en el POS, cerrar Electron con normalidad y volver a ejecutar el comando de Caja 1. Revisar visualmente el POS siempre dentro de Electron; el navegador del paso 2 es solo para Administración.

## 4. Dos o tres cajas en esta misma PC

Mantener **una sola API y una sola base**, compartidas por las cajas. En Administración → Configuración → Organización → Cajas, comprobar que Caja 1 y Caja 2 estén activas; para una tercera, crear **Caja 3** en la misma sucursal. Cada caja necesita su **propia credencial de terminal**, entregada al provisionar su perfil local. El nombre `caja-3` no crea la caja en la base ni sustituye esa credencial. Usar un cajero/turno responsable por caja para comprobar ventas y cierres separados.

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
.\node_modules\.bin\electron.cmd . --pos-profile=caja-1
```

```powershell
cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
.\node_modules\.bin\electron.cmd . --pos-profile=caja-2
```

```powershell
cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
.\node_modules\.bin\electron.cmd . --pos-profile=caja-3
```

Si el perfil de Caja 2 o Caja 3 todavía **no tiene credencial**, crear/obtener la credencial desde Administración (solo se muestra al crearla o rotarla) y hacer **su primer arranque** así, cambiando `caja-3` por el perfil correspondiente. No pegar el valor en este documento ni en el comando:

```powershell
cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
$credencialCaja = Read-Host 'Credencial de esta caja' -AsSecureString
$env:CARNICERIAS_POS_TERMINAL_TOKEN = [System.Net.NetworkCredential]::new('', $credencialCaja).Password
Remove-Variable credencialCaja
try { .\node_modules\.bin\electron.cmd . --pos-profile=caja-3 }
finally { Remove-Item Env:\CARNICERIAS_POS_TERMINAL_TOKEN -ErrorAction SilentlyContinue }
```

Los siguientes arranques usan la línea normal del perfil, sin volver a ingresar la credencial. No copiar perfiles ni usar la credencial de Caja 1 en Caja 2/3. Un perfil ya vinculado a otra credencial no se puede reutilizar para una caja diferente.

**Periféricos en una misma PC:** ninguna caja tiene balanza ni impresora por defecto. En Administración → Organización → Cajas → Dispositivos, asignar a cada caja sus propios puertos y velocidades. Windows no permite compartir un COM abierto entre cajas: para pruebas simultáneas, usar pares virtuales distintos. Si una caja queda sin balanza se ingresa el peso manualmente; si queda sin impresora, se guarda la venta sin intentar imprimir. `pos-config.json` solo define la URL de la API.

## 5. Cajas en dos o tres computadoras distintas

La API y PostgreSQL corren **solo en la PC servidora**; las otras PC llevan únicamente Electron. La API debe escuchar en una IP LAN accesible, no solo en `localhost`. Si hoy está levantada en `localhost:5197`, detenerla en su consola y volver a iniciarla con el mismo script, sustituyendo la IP de ejemplo por la IP LAN real de esa PC:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-LocalPosApi.ps1 -BindAddress '192.168.10.20'
```

Habilitar el acceso al puerto 5197 **solo desde la LAN de prueba** en el firewall del servidor. En cada PC de caja, comprobar `Invoke-RestMethod 'http://192.168.10.20:5197/api/health'`. No exponer PostgreSQL (`55433`) a la LAN ni crear una base por caja.

Preparar el paquete de Electron según la [guía de workshop](guia-workshop-por-sucursal.md#3-construir-y-copiar-los-artefactos). En **cada PC**, copiar la carpeta completa de Electron y `Start-PosTerminal.ps1`; crear su propio `C:\Carnicerias\Caja\pos-config.json` con `apiOrigin` apuntando a la IP LAN real y `allowInsecureLan: true` **solo para este laboratorio aislado**. Configurar los COM desde Administración para la caja correspondiente. Desde `C:\Carnicerias\Caja`, ejecutar en cada equipo (ajustar rutas):

```powershell
cd C:\Carnicerias\Caja

# PC de Caja 1
.\Start-PosTerminal.ps1 -ExecutablePath '.\Carnicerias POS-win32-x64\Carnicerias.Pos.exe' -ConfigPath '.\pos-config.json' -TerminalProfile caja-1 -Provision

# PC de Caja 2
.\Start-PosTerminal.ps1 -ExecutablePath '.\Carnicerias POS-win32-x64\Carnicerias.Pos.exe' -ConfigPath '.\pos-config.json' -TerminalProfile caja-2 -Provision

# PC de Caja 3, si corresponde
.\Start-PosTerminal.ps1 -ExecutablePath '.\Carnicerias POS-win32-x64\Carnicerias.Pos.exe' -ConfigPath '.\pos-config.json' -TerminalProfile caja-3 -Provision
```

Ejecutar **solo la línea de esa PC**. `-Provision` solicita sin eco la credencial **distinta** de su caja; se usa únicamente en la primera vinculación. Para los siguientes arranques, repetir la misma línea **sin** `-Provision`. Cada PC puede usar sus propios COM1/COM6, porque sus puertos son independientes. Para la configuración de red, firewall y archivos de caja, seguir la [guía completa](guia-workshop-por-sucursal.md#7-preparar-cada-pc-de-caja).

Para detener API o Angular, usar `Ctrl+C` en su consola. Para detener una caja, cerrar normalmente su ventana de Electron.

## 6. Laboratorio local de sucursales y central

Estos comandos complementan el POS compartido `:5197`; no lo reemplazan ni crean otra base. Funcionan **solo en esta computadora**, con los binarios publicados y el almacén DPAPI de la misma cuenta Windows. Los scripts comprueban el contenedor, la base `carnicerias_test_visual` y las identidades esperadas antes de arrancar. `Provision` no es un comando de arranque: no repetirlo para resolver un puerto ocupado o una API caída.

Para iniciar las tres APIs de sucursal Visual (`5246`, `5247`, `5248`) y las dos APIs centrales habituales (`5245`, `5250`), con esos cinco puertos libres:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-VisualCentralLab.ps1 -Action Check
.\tools\Start-VisualCentralLab.ps1 -Action Start
.\tools\Start-VisualCentralLab.ps1 -Action Verify
```

Para iniciar solo las centrales `5245` y `5250`, si las sucursales ya están activas y **ambos puertos centrales están libres**, usar `-Action StartCentral` en lugar de `Start`. Las APIs Visual `5246`–`5248` no incluyen la interfaz Angular de administración: sus URLs son endpoints de API.

Para iniciar Alta Córdoba de La Rústica (`5249`), su central aislada (`5251`) y el portal `4302`, con esos tres puertos libres:

```powershell
cd C:\Proyectos\Carnicerias
.\tools\Start-RusticaManagedLab.ps1 -Action Check
.\tools\Start-RusticaManagedLab.ps1 -Action Start
.\tools\Start-RusticaManagedLab.ps1 -Action Verify
```

Si únicamente hay que reiniciar la API de Alta Córdoba y el puerto `5249` está libre, usar `.\tools\Start-RusticaManagedLab.ps1 -Action StartBranch`. El script no detiene ni reemplaza procesos existentes. Si todos los servicios de un grupo ya responden, omitir su `Start` y ejecutar `Verify`; si solo responde una parte, diagnosticar qué puerto falta antes de intentar reiniciar. No iniciar duplicados. La guía de [administración central](administracion-central.md) detalla el alcance del laboratorio y del almacén protegido.

## 7. Portales centrales y URLs

El portal central habitual y el alternativo son dos servidores Angular. Abrir una consola por portal; no ejecutar una línea si su puerto ya está ocupado. El portal aislado `4302` se inicia con el `Start` de La Rústica del apartado anterior.

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
| Administración POS compartida | `http://localhost:4200/` · `http://localhost:4200/admin` | API POS `5197` |
| API POS compartida | `http://localhost:5197/api/health` | PostgreSQL `127.0.0.1:55433/carnicerias_test_visual` |
| Portal central habitual / SuperAdmin | `http://localhost:4300/` · `http://localhost:4300/superadmin` | API central `5245` |
| Portal central alternativo / SuperAdmin | `http://localhost:4301/` · `http://localhost:4301/superadmin` | API central `5250` |
| Portal central aislado La Rústica | `http://localhost:4302/` | API central `5251` |
| APIs centrales | `http://127.0.0.1:5245/api/health` · `http://127.0.0.1:5250/api/health` · `http://127.0.0.1:5251/api/health` | Misma base PostgreSQL |
| APIs sucursal Visual, Norte y Sur | `http://127.0.0.1:5246/api/health` · `http://127.0.0.1:5247/api/health` · `http://127.0.0.1:5248/api/health` | Misma base PostgreSQL de laboratorio |
| Administración / API local Alta Córdoba | `http://localhost:5249/` · `http://127.0.0.1:5249/api/health` | API sucursal `5249` |

En `4300` y `4301`, `/` es el acceso del administrador de empresa central y `/superadmin` es el del SuperAdmin central; no son cuentas del POS local. El portal `4302` es un entorno aislado de prueba, no el acceso central habitual. Todas estas URLs son loopback de laboratorio, no destinos de despliegue. El POS se revisa visualmente únicamente en Electron.
