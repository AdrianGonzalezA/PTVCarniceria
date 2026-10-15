# Guía de implementación — una sucursal autónoma y varias cajas

**Estado:** procedimiento de laboratorio, 13/10/2026. No certifica aptitud productiva. Se basa en [el contrato de despliegue](../../SPEC-despliegue-sucursal.md) y la [decisión de arquitectura](../decisiones/0002-instalacion-por-sucursal.md). Cada sucursal desplegada tendrá su propio servidor y sus propios datos. No hay centralización ni sincronización entre sucursales.

## 1. Relevar antes de instalar

Completar una hoja de instalación, guardada fuera de Git, con:

| Dato | Qué registrar |
| --- | --- |
| Sucursal | Nombre, empresa, responsable y dirección. |
| Servidor | Nombre, IP LAN fija o reserva DHCP, sistema operativo, capacidad de disco y respaldo. |
| Red | Subred de cajas, switch, router, DNS local y salida a Internet disponible o no. |
| Cajas | Una fila por PC: nombre, usuario Windows, número de caja, cajero(s), lector, balanza e impresora. |
| Periféricos | Modelo, driver, puerto COM de Windows, velocidad y protocolo de cada dispositivo. |
| Datos iniciales | Catálogo, listas de precio, impuestos, clientes, stock físico contado y piezas trazables. |
| Externos | Si se probarán ARCA, Mercado Pago o Clover; credenciales y certificados siempre por canal seguro. |

Usar, por ejemplo, `192.168.10.20` para el servidor y `192.168.10.0/24` para el laboratorio **solo si coinciden con la red real**. Los comandos de esta guía que usan esas direcciones son ejemplos a sustituir. Una sucursal puede tener varias cajas; asignar una identidad/credencial de terminal distinta a cada una. El nombre local `caja-1` del perfil Electron no reemplaza esa vinculación.

## 2. Preparar la red y el servidor

1. Conectar servidor y cajas a la misma LAN. Fijar o reservar la IP del servidor y comprobar desde cada caja que puede resolverlo y alcanzarlo. Dar hora correcta a todos los equipos.
2. Instalar PostgreSQL en el servidor. Para el workshop se puede mantener PostgreSQL Docker existente en `127.0.0.1:55433`, siempre usando **solo** `carnicerias_test_visual`. No abrir el puerto de PostgreSQL a las cajas. La API se conecta localmente a PostgreSQL.
3. Definir una carpeta persistente para el servidor y una ubicación de respaldos distinta del disco del servidor. Proteger servidor, switch y router con UPS si el laboratorio incluye simulaciones de corte de energía.
4. Para laboratorio aislado se admite HTTP por LAN con la opción explícita `allowInsecureLan: true` en las cajas. No usar esta excepción con credenciales/datos reales. Para una instalación real se deberá configurar HTTPS con certificado confiable para cada PC y revisar el proxy/cabeceras reenviadas de ASP.NET Core. Referencia: [Microsoft, proxy y encabezados](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

No exponer el servidor POS a Internet. Si se precisa acceso administrativo remoto, diseñar VPN o un acceso acotado aparte. Dar salida a Internet al servidor solo para servicios externos que se decida habilitar; “sin central en Internet” no significa que ARCA o las pasarelas funcionen sin conectividad.

## 3. Construir y copiar los artefactos

En una máquina de compilación con .NET 10 SDK, Node compatible, npm 11.19.0 y las dependencias ya instaladas en `src/Carnicerias.Web` y `src/Carnicerias.Pos`, desde la raíz del repositorio:

Antes de generar el paquete, cerrar las ventanas Electron de desarrollo. La publicación limpia la copia web anterior para no incluir JavaScript obsoleto en la entrega; la preparación habitual de desarrollo conserva esos archivos para no interrumpir cajas que siguen abiertas.

```powershell
.\tools\Publish-BranchLab.ps1 -OutputDirectory 'C:\Entregas\Carnicerias-Workshop-001'
```

La carpeta de salida debe estar vacía; el script no pisa una entrega anterior. Genera `servidor` (API y Angular), `inicializacion` (alta del superadmin del fabricante) y `cajas` (carpeta completa del Electron Windows x64, `Start-PosTerminal.ps1` y ejemplo de configuración). **Copiar la carpeta completa de Electron**, no solo el `.exe`. El paquete sigue siendo portable; aún no hay instalador MSI ni actualización automática. Electron Forge distingue el paquete de un instalador, según su [documentación de distribución](https://www.electronjs.org/docs/latest/tutorial/tutorial-packaging).

Copiar `servidor` e `inicializacion` al servidor local. No copiar contraseñas, PFX, claves, perfiles de Electron ni la base ficticia dentro del paquete. Conservar el código, migraciones y herramientas de bootstrap del repositorio para reconstruir la misma versión.

## 4. Elegir la ruta de datos

**Workshop con el entorno de desarrollo actual:** utilizar exclusivamente `carnicerias_test_visual` en `127.0.0.1:55433`. Ya contiene usuarios, empresas, sucursales y datos ficticios; **no ejecutar** `create-admin` ni `seed-pos-visual` y no crear otra base. La migración de identidad por empresa y el alta de cuentas independientes ya se aplicaron con respaldo fuera de Git. Véase [auditoría de identidad](auditoria-identidad-por-empresa-20261013.md).

**Primera instalación nueva de una sucursal, más adelante:** requiere autorización explícita antes de crear su única base de aplicación en el servidor de esa sucursal. El operador de PostgreSQL crea el usuario y la base con permisos mínimos y registra sus credenciales fuera de Git. Configurar `CARNICERIAS_CONNECTION_STRING` en la consola de inicialización y luego en el proceso API, nunca en las cajas. Aplicar las migraciones de la versión desde el repositorio sobre esa base ya existente y después de verificar la cadena de conexión. El comando `create-superadmin` **no aplica migraciones automáticamente**. Con la base migrada, ejecutar una sola vez desde una consola interactiva del servidor:

```powershell
dotnet .\inicializacion\Carnicerias.Bootstrap.dll create-superadmin --username superadmin --email superadmin@instalacion.local
```

La herramienta pide la contraseña sin mostrarla y rechaza un segundo superadmin inicial. En una instalación nueva exige al menos 12 caracteres; la excepción de seis caracteres solo existe para la base visual local. Abrir `/superadmin` en un navegador de esa instalación y crear empresa, código de acceso, primera sucursal y administrador inicial. Para agregar otro administrador después, abrir **Editar** en esa empresa y completar **Agregar administrador**; no reemplaza al inicial. Si la empresa está inactiva, la nueva cuenta no podrá ingresar hasta activarla. El antiguo `create-admin` quedó deshabilitado. No usar el seed visual como datos comerciales ni transportar ventas de desarrollo. Si cambió la versión, respaldar antes de migrar. No ejecutar pruebas de integración que creen o reinicien otra base de aplicación en este entorno.

Para recuperar **una empresa existente sin administrador** después de una migración controlada, el operador del servidor puede ejecutar `dotnet .\inicializacion\Carnicerias.Bootstrap.dll create-company-admin --company-id <uuid> --username <usuario> --email <correo>` con la misma variable de conexión. El comando solicita la contraseña oculta y se niega si la empresa ya tiene una asignación administradora. No es el alta normal de empresas ni debe entregarse a una caja o a un administrador cliente. La empresa inactiva puede recibir administrador, pero solo podrá iniciar sesión después de que el fabricante la active.

## 5. Configurar y levantar la API local

En la consola del servidor, establecer la cadena de conexión recibida por canal seguro en la variable `CARNICERIAS_CONNECTION_STRING` del proceso. No escribirla en un script, captura, ticket o repositorio. Si contiene claves de ARCA/pasarelas cifradas, conservar además las claves de ASP.NET Data Protection del mismo usuario Windows; el mecanismo persistente y respaldo formal de esas claves todavía requiere preparación antes de producción.

Ubicarse en la carpeta `servidor` y lanzar la API escuchando **solo** en la IP LAN elegida:

```powershell
dotnet .\Carnicerias.Api.dll --urls http://192.168.10.20:5197
```

Para el workshop, limitar el firewall del servidor a la subred de cajas, reemplazando direcciones y ejecutando una consola administrativa:

```powershell
New-NetFirewallRule -DisplayName 'Carnicerias POS laboratorio' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5197 -RemoteAddress 192.168.10.0/24
```

Desde el servidor y **cada caja**, verificar `http://192.168.10.20:5197/api/health` (JSON con estado saludable). En un navegador de administración de esa LAN, abrir `http://192.168.10.20:5197/admin`; Angular y la API comparten origen. Una ruta API inexistente debe devolver 404, no la pantalla Angular. No abrir el puerto 55433 de PostgreSQL en el firewall. Al reiniciar Windows la API **no se inicia automáticamente todavía**: definir servicio y cuenta de ejecución fija antes de una instalación desatendida.

Después del alta inicial de empresa y sucursal, fijar en la configuración protegida del proceso `Federation:Enabled=true`, `Federation:Mode=managed`, `Federation:CompanyId=<ID de la empresa>` y `Federation:BranchId=<ID de la sucursal>`; reiniciar la API y verificar que los IDs correspondan a esta instalación. Son identificadores de vínculo, no una selección del navegador. Sin este vínculo el servidor se comporta como compartido y seguirá ofreciendo el selector de sucursal. El procedimiento de integración con central detalla la [configuración del vínculo](integracion-central-sucursal.md); no es necesario activar una conexión central para que el vínculo local limite la sesión.

## 6. Configurar el negocio desde Administración

1. En el navegador, ingresar código de empresa, usuario y contraseña del administrador inicial. Si la API ya está vinculada a esta sucursal, el acceso la confirma automáticamente y no muestra selector; si todavía opera en modo compartido, habrá que elegirla. En Electron, la credencial de terminal determina la empresa y el cajero no escribe el código.
2. En **Configuración → Organización**, revisar o crear la sucursal y una caja activa por PC. Generar/vincular la credencial de cada terminal de forma individual; se entrega una sola vez por canal seguro. Registrar en la hoja de instalación qué PC corresponde a cada caja, sin anotar la credencial.
   La interfaz aún permite crear otras sucursales en la misma base; en este workshop **no hacerlo**. El vínculo de API restringe las sesiones a la sucursal configurada, pero todavía no impide almacenar otra sucursal.
3. En **Usuarios**, crear cajeros para las sucursales o administradores adicionales de esa empresa. Cada persona usa su propio usuario y abre/cierra su turno; no compartir una sesión entre responsables.
4. Cargar categorías, artículos (con unidad de venta, impuestos y códigos), listas de precios habilitadas en la sucursal y clientes. Las plantillas Excel de la administración sirven para esos maestros. Revisar precios e IVA antes de vender.
5. Contar e ingresar stock inicial mediante las operaciones disponibles. Para piezas ciclo 2, configurar el perfil de lectura y registrar piezas; no asumir que ya existe importación masiva del frigorífico. Una venta sin stock no debe completarse.
6. Configurar en **Organización → Cajas → Dispositivos** la balanza y/o impresora de cada puesto, o dejar explícitamente «Sin balanza» y «Sin impresora». Usar COM distintos por caja cuando varias ventanas corren en la misma PC. Configurar ARCA/pasarelas solo si el laboratorio las incluye. Las credenciales actuales son de homologación/sandbox. Clover no está habilitado aún para cobros POS sin dispositivo/protocolo verificados.

## 7. Preparar cada PC de caja

1. Instalar drivers del lector, balanza e impresora. En Administrador de dispositivos de Windows, confirmar el COM real de cada uno. El lector de códigos tipo teclado debe entregar la lectura al POS; no se le asigna COM en el archivo de ejemplo.
2. Copiar la carpeta `cajas\Carnicerias POS-win32-x64` completa a la PC. Copiar `Start-PosTerminal.ps1` fuera de la carpeta del ejecutable si se desea. Crear **en esa PC** `C:\Carnicerias\Caja\pos-config.json` a partir de `pos-config.ejemplo.json`, sustituyendo la IP:

```json
{
  "apiOrigin": "http://192.168.10.20:5197",
  "allowInsecureLan": true
}
```

   El archivo no contiene secretos ni asignaciones de dispositivos. En Administración, elegir para esa caja «Sin impresora», impresora serial de texto o Hasar SMH/P-441F **desafectada en laboratorio**; y «Sin balanza» o balanza serial. La Hasar usa 9600 baudios y no puede compartir COM con la balanza. Los puertos admitidos son `COM1`–`COM999` y las velocidades admitidas son 1200, 2400, 4800, 9600, 19200, 38400, 57600 y 115200 baudios. Sin asignación no se abre el COM ni se intenta imprimir. El protocolo actual de balanza es **8N1** con tramas `ST,0.750,kg` terminadas en CR/LF; una balanza real distinta requiere adaptación, no solo cambiar el COM. Para una instalación real, usar `https://...` y quitar `allowInsecureLan`; un HTTP remoto sin opt-in, una URL con usuario/contraseña o una ruta adicional se rechazan.
   Restringir escritura del archivo y de la carpeta de instalación a los administradores del equipo: cambiar `apiOrigin` podría desviar credenciales. El cajero solo necesita lectura y ejecución. No usar el opt-in HTTP fuera de una LAN de prueba aislada.
3. La primera vez, tomar la credencial individual de la caja desde Administración y ejecutar en PowerShell en la PC de esa caja (ajustar rutas y perfil):

```powershell
.\Start-PosTerminal.ps1 -ExecutablePath 'C:\Carnicerias\Caja\Carnicerias POS-win32-x64\Carnicerias.Pos.exe' -ConfigPath 'C:\Carnicerias\Caja\pos-config.json' -TerminalProfile caja-1 -Provision
```

   El script la solicita sin eco, la pasa solo al primer arranque y la retira del entorno de la consola. Electron la guarda cifrada en el perfil Windows local. En arranques posteriores ejecutar lo mismo **sin** `-Provision`. No copiar el perfil de Electron de una PC a otra. Si se rota una credencial, provisionar un perfil nuevo y documentar el cambio; no intentar reutilizar un perfil vinculado a otra caja.
4. Repetir en la segunda PC con su credencial y su propio nombre de perfil, por ejemplo `caja-2`. Abrir un turno distinto con el cajero responsable. La activación de la caja en Administración no sustituye la credencial local. Tras cambiar dispositivos, recargar Electron para actualizar los controles visibles.

La impresora serial actual recibe texto UTF-8 por 8N1; probar acentos, ancho y corte con el modelo real. Una impresión fallida no revierte la venta y repetirla puede duplicar papel. Los PDF se guardan en la PC de caja. El envío por COM no convierte una impresora común en controladora fiscal.

## 8. Prueba de aceptación con dos o más cajas

Hacer la revisión visual del POS **dentro de Electron** en cada PC; Administración sí puede usarse en navegador. Registrar hora, caja, usuario y resultado de cada prueba, sin claves ni datos sensibles.

- [ ] Desde todas las PC responden `/api/health` y la pantalla de inicio de Electron; Administración abre en navegador.
- [ ] Caja 1 y Caja 2 usan credenciales diferentes, abren turnos separados y muestran la misma lista de precios/stock de la sucursal.
- [ ] Escanear un código de producto; probar artículos por unidad y por kilo, stock insuficiente y una pieza trazable con kilos incorporados.
- [ ] Dejar tickets A y B abiertos, reiniciar **solo** Electron y confirmar que regresan los borradores que habían terminado de guardarse.
- [ ] Confirmar una venta de prueba en Caja 1; comprobar en Caja 2 y Administración que stock, venta, pago y turno coinciden. Repetir con ventas simultáneas de las dos cajas, sin exceder el stock.
- [ ] Leer una trama estable de balanza real/virtual, imprimir un ticket de prueba y verificar recepción física; probar que un fallo de periférico no duplica la venta.
- [ ] Quitar la asignación de balanza e impresora a una caja: no debe abrir sus COM, ofrecer «Leer balanza» ni intentar imprimir al confirmar la venta; el ingreso manual y el PDF siguen disponibles.
- [ ] Cerrar ambos turnos y conciliar el efectivo por caja. No usar ventas ficticias como histórico real.
- [ ] Cortar la red de **una caja**: no debe confirmar una venta ni prometer recuperar una edición que no se guardó. Restituir la red y verificar los borradores persistidos. Repetir con caída del servidor solo en un entorno de prueba controlado.
- [ ] Si se habilitan integraciones externas, verificar por separado los requisitos de Internet, homologación y estado real de cada pago/CAE; no inferir que “sin central” equivale a funcionamiento offline de esos servicios.

**Resultado actual:** el paquete, la configuración local, el inicio de Electron y las rutas HTTP se verificaron en una sola computadora. La prueba física de varias PC y dispositivos reales sigue pendiente para el workshop.

## 9. Respaldos, actualización y contingencia

Antes del workshop tomar un respaldo de la **única base autorizada** y conservarlo fuera del servidor; después programar respaldo periódico de cada sucursal. Registrar versión de API/Electron, hora del respaldo y responsable. PostgreSQL documenta [copias lógicas y restauración](https://www.postgresql.org/docs/current/backup.html); no considerar un backup confiable hasta restaurarlo y verificarlo en un entorno autorizado. En este desarrollo no crear otra base para probar restauración sin permiso explícito.

Para actualizar una sucursal: cerrar turnos y tickets pendientes, detener API, respaldar base y claves de Data Protection, conservar el paquete anterior, aplicar migraciones controladas y reemplazar API/Electron por la misma versión de entrega. Verificar salud, login, stock y una venta controlada antes de reabrir. Una reversión de binarios **no** revierte automáticamente una migración de base; planificar cada cambio de esquema.

Si cae la LAN o el servidor, suspender el cobro en POS: hoy no existe cola offline ni sincronización posterior. Si solo cae Internet y la LAN sigue activa, no asumir que ARCA/pasarelas están disponibles; la contingencia fiscal y comercial debe aprobarla la empresa con su asesoría contable. Antes de producción quedan pendientes HTTPS interno, servicio automático, respaldo/restauración y protección persistente de claves, monitoreo, periféricos reales, facturación productiva y el diseño de callbacks/webhooks externos mínimos.
