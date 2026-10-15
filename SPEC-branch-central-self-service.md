# Información de conexión a central desde el administrador de sucursal

**Estado:** aprobada e implementada funcionalmente el 15/10/2026; revisión visual autenticada pendiente por falta de una superficie de navegador en esta sesión. Complementa [el contrato de lectura de sucursal](SPEC-branch-service-contract.md) y [el alta desde el administrador central](SPEC-central-branch-connections.md). La presente especificación sustituye **solo el aprovisionamiento manual de los datos que se copian a la central**; no amplía el acceso remoto a escritura ni cambia la fuente de verdad de ventas. Decisión: [ADR 0008](documentacion/decisiones/0008-credencial-de-central-desde-sucursal.md).

## Objetivo y autoridad

El administrador de una sucursal debe poder obtener **desde Configuración de esa sucursal todos los datos que necesita el formulario de conexión de la central**, sin consultar la base, editar archivos, ejecutar comandos ni pedirle UUID o token a Recursos Informáticos. La conexión completa debe poder demostrarse con **La Rústica / Alta Córdoba** en el laboratorio actual, aunque todavía no exista un despliegue de producción.

La operación exige sesión local con permiso `organization.manage` para la empresa y sucursal activas. No pertenece al cajero, a un administrador de otra empresa ni al SuperAdmin del portal central. El administrador central de La Rústica registra y comprueba únicamente las sucursales de su propia empresa. Recursos Informáticos conserva el control del despliegue, de la identidad fija del proceso de API y de la lista central de destinos de red permitidos.

## Flujo visible de punta a punta

1. En **Administrador de sucursal → Configuración → Conexión con central** se muestran la empresa y sucursal actuales, sus IDs reales, el nombre de sucursal sugerido como etiqueta, el ID estable de instalación y el estado de la integración. El servidor obtiene la identidad de la sesión y de su vinculación de despliegue; el navegador no envía IDs para elegir otra empresa o sucursal. Si la API local está vinculada a otra sucursal, se muestra el conflicto y se impide generar datos engañosos.
2. El administrador configura en esa misma pantalla la **URL raíz por la que la central puede alcanzar la API de esta sucursal**. La pantalla muestra la URL guardada y permite modificarla; no afirma que una URL detectada desde el navegador sea alcanzable desde la central. Se valida su forma. HTTPS es obligatorio fuera del laboratorio; HTTP de loopback se admite únicamente cuando el despliegue de laboratorio lo habilita de forma expresa. La pantalla distingue *URL configurada localmente* de *conexión comprobada desde central*.
3. **Obtener información para conectar a central** crea, si todavía no existe, un `installationId` estable y una credencial aleatoria de 32 bytes. Muestra un paquete copiable con **etiqueta, URL, installationId, remoteCompanyId, remoteBranchId y token**: son todos los campos requeridos por el alta central actual. La etiqueta y los IDs provienen de la sucursal; la URL proviene de su configuración; el token lo genera el servidor. El token se muestra **solo en esta emisión**. Una recarga posterior muestra los metadatos y el estado, nunca recupera el token.
4. El administrador central de La Rústica pega el paquete en **Conexiones a sucursales**. La pantalla central rellena los campos, permite revisar los valores y usa su comprobación existente antes de activar. La central compara la identidad remota exacta y la URL con su política de destinos permitidos **para La Rústica**. La etiqueta se puede ajustar sin cambiar la identidad.
5. Si se pierde la credencial, el administrador de sucursal usa **Rotar credencial**. La interfaz advierte que la central deberá actualizar su conexión. La rotación conserva el `installationId` y la identidad de empresa/sucursal. Se admite una transición acotada con token anterior y nuevo para evitar un corte accidental; el administrador puede finalizarla y revocar el anterior. Desactivar la integración revoca el acceso remoto sin borrar ventas ni impedir operar el POS.
6. La central muestra el resultado y la fecha de su comprobación; si la sucursal no responde, aparece como no disponible, nunca como ventas cero. La venta local sigue funcionando si la central está apagada o la credencial se revoca.

## Persistencia, límites de confianza y compatibilidad

- La única base de datos de aplicación de este desarrollo sigue siendo `carnicerias_test_visual`. Una migración agrega la configuración de federación administrada para la **pareja de empresa/sucursal vinculada al proceso**; no crea otra base ni duplica ventas. Se guarda el `installationId`, URL, estado, fechas y **solo verificadores criptográficos de credenciales**, no el token en claro. La generación usa un generador criptográfico del servidor. Las mutaciones son atómicas y auditadas sin valores secretos.
- La API de cada instalación conserva una vinculación fija a una empresa y una sucursal mediante configuración protegida del despliegue. La pantalla solo administra la conexión que coincide con esa vinculación; no transforma una instalación en un proxy multiempresa/multisucursal. La ruta remota `/api/central/v1/*` continúa siendo de solo lectura y queda cerrada cuando la integración administrada está inactiva. No se confunden cookies humanas, credenciales de caja y token de central.
- La configuración protegida heredada de federación sigue funcionando para instalaciones ya aprovisionadas. El modo administrado y el modo heredado son explícitos: un error de lectura o de verificador **no** provoca una caída silenciosa al otro modo. Durante la migración se actualizan el contrato, el ADR y la guía de despliegue para explicar qué fuente tiene prioridad y cómo se pasa de una a otra sin perder el acceso local.
- El navegador no persiste el token en `localStorage`, `sessionStorage`, URL, historial ni logs. La respuesta de emisión es `no-store`; solo una acción explícita copia el paquete al portapapeles, con aviso de que contiene una credencial. Las escrituras exigen sesión, autorización de empresa/sucursal y protección CSRF; se limitan los intentos. Las lecturas posteriores no entregan token ni hash. La central conserva su cifrado de credenciales y su política anti-SSRF existentes.
- La URL y la credencial sirven para la conexión central, no para identificar por sí solas la sucursal. La comprobación central exige coincidencia de los tres IDs y una respuesta autenticada de la API de sucursal. Un destino configurado localmente no se anuncia como conectado hasta que la central lo compruebe.

## Estructura y estilo de implementación

- `src/Carnicerias.Api/` e infraestructura local: modelo y migración de configuración administrada, generación/rotación/revocación, lectura del vínculo fijo del proceso y endpoints de administración local. Mantener DTO JSON en `camelCase` y los errores estructurados existentes; ningún endpoint acepta `companyId` o `branchId` de un formulario para cambiar el alcance.
- `src/Carnicerias.Web/src/app/features/admin/`: sección accesible de Configuración, estados claros, advertencias de token de una sola exhibición y acción de copiar. Respetar el estilo de la maqueta y de los demás módulos administrativos; formularios/acciones en pantalla o modal, sin obligar a buscar resultados debajo de una grilla.
- `src/Carnicerias.Central.Web/src/app/`: entrada **Pegar datos de sucursal** que complete el formulario actual y permita comprobación antes de guardar. El parseo del paquete valida versión, campos y formato; nunca conserva el token tras el envío.
- `tests/` y `documentacion/`: pruebas de lógica, contrato, permisos, interfaz y guía operativa con capturas/pasos de la demo. Los ejemplos documentales usan IDs y tokens ficticios, nunca el secreto real del laboratorio.

Ejemplo de límite correcto: `var branchId = deploymentBinding.BranchId;` debe coincidir con la sucursal activa autorizada antes de leer o emitir una credencial. No se usa `request.BranchId` como fuente de autoridad.

## Comandos y verificación

```powershell
dotnet build src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release --no-restore
dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --no-restore
npm --prefix src/Carnicerias.Web run build
npm --prefix src/Carnicerias.Web test -- --watch=false
npm --prefix src/Carnicerias.Central.Web run build
npm --prefix src/Carnicerias.Central.Web test -- --watch=false
git diff --check
```

Pruebas: primer aprovisionamiento y relectura sin secreto, generación concurrente idempotente, rotación/revocación, autorización cruzada entre empresas y sucursales, CSRF, URL inválida, vinculación local incorrecta, token rechazado tras revocación, legado sin regresión, y fallo de central sin afectar una venta. La prueba end-to-end usa **solo** la base `carnicerias_test_visual`, con respaldo antes de migrar y sin ejecutar suites que creen o reinicien otra base. La revisión visual del POS, si se toca, se hace **dentro de Electron**.

## Límites y criterios de aceptación

1. El administrador de **La Rústica / Alta Córdoba** puede obtener desde la Configuración de esa sucursal un paquete completo, sin valores copiados de SQL, consola ni archivos. La URL de laboratorio se establece y se ve allí. Un administrador de otra empresa o de otra sucursal no puede obtenerlo ni rotarlo.
2. El administrador central de La Rústica pega ese paquete, comprueba la identidad y ve **Alta Córdoba conectada** y su resumen de negocio en la central. Se demuestra la persistencia tras reiniciar ambas APIs y la diferencia entre configurado, conectado y no disponible.
3. El token se entrega una vez; no aparece en respuestas posteriores, base en claro, Git ni logs. La rotación permite actualizar la central y la revocación corta su acceso sin bloquear caja/ventas.
4. La lista permitida de destinos sigue siendo una política de instalación **por empresa**. Una URL no autorizada, una identidad remota discordante o un token incorrecto no crean una conexión activa. La API de sucursal no ofrece escritura remota.
5. Código, migración, pruebas, ADR y manual de instalación/uso quedan sincronizados. No se crea otra base de datos de Carnicerías.

## Supuestos a validar

- El administrador local con `organization.manage` es quien emite y rota la credencial de **su sucursal activa**; Recursos Informáticos prepara el vínculo fijo del proceso y la política de red, pero **ninguno de los seis datos que se copian al administrador central se obtiene fuera de la pantalla de sucursal**.
- Para la demo se usará la instalación de Alta Córdoba de La Rústica y una URL HTTP de loopback aprobada expresamente para laboratorio. En un despliegue real se requerirán red privada/VPN y HTTPS, y la URL se configurará desde esa misma pantalla.
- El paquete copiable se puede pegar de una vez en la central, además de permitir copiar campos individualmente. La credencial no se recupera después; si se perdió, se rota.

Estos supuestos forman parte de la aprobación de esta especificación; no hay otra regla de negocio pendiente para empezar a implementarla.

## Evidencia de implementación del laboratorio

La única base `carnicerias_test_visual` fue respaldada antes de aplicar las dos migraciones locales; conservó 56 ventas ficticias. El admin local de Alta Córdoba guardó la URL, emitió el paquete una sola vez y la central `LARUSTICA` registró la conexión. Después de reiniciar ambas APIs, la central comprobó `available` y obtuvo resumen `complete`; un token incorrecto dio `401`, la central sin sesión `401`, la emisión local sin CSRF `403` y el admin de otra empresa recibió `409` por vínculo distinto. La lectura posterior de configuración no expuso token. Pasaron 88 pruebas de arquitectura, 102 centrales, 155 del admin local y 30 del portal central, además de sus builds. No se ejecutaron las pruebas de integración que crean otra base. La inspección visual autenticada queda pendiente porque esta sesión no ofreció superficie de navegador; ver [recorrido de demo](documentacion/despliegue/administracion-central.md).
