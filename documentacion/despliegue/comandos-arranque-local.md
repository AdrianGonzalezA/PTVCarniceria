# Arranque local: API, administración y Caja 1

Estos comandos son para **PowerShell**: primero en este equipo y, al final, en otras PC de caja. Los comandos de desarrollo parten de `C:\Proyectos\Carnicerias`. Abrir una consola distinta para cada proceso. Usar únicamente la base de desarrollo existente `carnicerias_test_visual` en PostgreSQL Docker (`127.0.0.1:55433`); estos pasos no crean ni reinician bases.

> **Identidad por empresa (13/10/2026):** las migraciones ya están aplicadas en la única base local. `visual-admin` pertenece solo a Empresa Visual; `rustica-admin` y `prueba-admin` son cuentas separadas. La empresa de prueba continúa inactiva, por lo que su login se rechaza. Para el navegador usar el código de empresa indicado en la [auditoría](auditoria-identidad-por-empresa-20261013.md); el POS Electron obtiene la empresa de la credencial de caja.

## 1. API (consola 1)

Comprobar que Docker Desktop y el contenedor PostgreSQL estén activos:

```powershell
docker ps
```

Antes de iniciar otra API, comprobar si la actual responde:

```powershell
Invoke-RestMethod http://localhost:5197/api/health
```

Si devuelve `status: healthy`, **ya está levantada**: dejarla abierta y pasar al paso 2. Para ejecutar código nuevo, detener primero la instancia anterior con `Ctrl+C` en su consola. No intentar abrir dos API en el puerto 5197.

Si no responde, configurar la cadena de conexión **en la consola de la API**, sin escribir la contraseña en el archivo ni en el historial de comandos:

```powershell
cd C:\Proyectos\Carnicerias
$cadenaPos = Read-Host 'Cadena PostgreSQL (Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=...;Password=...)' -AsSecureString
$env:CARNICERIAS_CONNECTION_STRING = [System.Net.NetworkCredential]::new('', $cadenaPos).Password
Remove-Variable cadenaPos
dotnet run --project src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release -- --urls http://localhost:5197
```

Dejar esa consola abierta. En otra consola, verificar nuevamente `/api/health` con el comando anterior. La cadena ingresada debe apuntar **solo** a `carnicerias_test_visual`.

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

La API y PostgreSQL corren **solo en la PC servidora**; las otras PC llevan únicamente Electron. La API debe escuchar en una IP LAN accesible, no solo en `localhost`. Si hoy está levantada en `localhost:5197`, detenerla en su consola y volver a iniciarla allí con la misma cadena de conexión a `carnicerias_test_visual`, sustituyendo la IP de ejemplo por la real:

```powershell
cd C:\Proyectos\Carnicerias
dotnet run --project src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release -- --urls http://192.168.10.20:5197
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
