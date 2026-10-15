# Plan: instalación autónoma por sucursal

**Contrato:** [SPEC-despliegue-sucursal.md](../SPEC-despliegue-sucursal.md). Este plan específico no reemplaza `tasks/plan.md`, que conserva el plan histórico de platform access.

## Orden

1. Configuración de destino API por Electron, con validación estricta y prueba de la credencial de terminal. Sin esto no hay caja remota.
2. Asignación de balanza e impresora por caja desde Administración, con «sin dispositivo» como valor inicial y textos de UI independientes de COM1/COM6. La configuración local conserva únicamente el destino de la API.
3. Publicación de Angular junto a la API y preparación del paquete Electron. Sin esto el administrador dependería del servidor Angular de desarrollo.
4. Guía de instalación y verificación del workshop, sin tocar la base de desarrollo ni exponer secretos.

## Fase posterior al workshop: instaladores de sucursal

**Decisión aprobada:** entregar dos instaladores Windows: **Servidor de sucursal** (API y administración web) y **Caja** (Electron). Administración se usa desde el navegador y no requiere un tercer instalador. Los paquetes y scripts actuales son artefactos de laboratorio, no instaladores terminados. El seguimiento ejecutable está en [despliegue-sucursal-todo.md](despliegue-sucursal-todo.md).

5. Definir contrato de instalación y secretos: identidad de la sucursal, destino PostgreSQL, cuenta de servicio, rutas persistentes, URL HTTPS, certificados, respaldo, parámetros COM y credencial distinta por caja. Decidir si PostgreSQL lo instala el paquete de servidor o es un prerrequisito administrado; no trasladar datos ficticios ni claves del laboratorio.
6. Preparar persistencia y recuperación de claves ASP.NET Data Protection antes de cargar ARCA o pasarelas. Resguardar base y claves juntos, con acceso limitado a la cuenta de servicio; probar restauración sin exponer secretos. La cadena de conexión debe venir de configuración protegida del servidor, nunca del paquete, Git ni las cajas.
7. Construir instalador de servidor: instalar API + Angular publicado, configurar servicio Windows de inicio automático, HTTPS y firewall LAN, validar PostgreSQL, aplicar migraciones con respaldo y habilitar inicialización única del SuperAdmin. Fallar sin modificar datos ante configuración inválida. No crear ni reiniciar otra base en el entorno de desarrollo actual.
8. Construir instalador de caja: instalar Electron y pedir URL HTTPS de la API e identidad del puesto. Asignar puertos/velocidades de periféricos a esa caja desde Administración; sin asignación, Electron no abre balanza ni imprime. Provisionar una credencial de terminal por PC una sola vez y conservarla cifrada en su perfil Windows; no copiar perfiles ni incluir tokens en el instalador.
9. Verificar ciclo de vida: instalación nueva en laboratorio autorizado, varias cajas, reinicio, actualización, recuperación, rotación de credencial y desinstalación sin borrar base, claves, respaldos ni perfiles por accidente. Mantener la revisión visual del POS dentro de Electron.

**Checkpoint:** una instalación nueva se puede completar desde los dos paquetes siguiendo un procedimiento repetible, sin código fuente ni comandos de desarrollo en destino; se comprueba venta entre dos cajas y recuperación de configuración/secretos después de reiniciar y restaurar.

## Riesgos y decisiones abiertas

- La LAN del laboratorio podrá usar HTTP privado solo con opt-in explícito y datos ficticios. Para instalación real se exige HTTPS y un certificado confiable por las cajas.
- La salida a Internet de la sucursal para ARCA/pasarelas no equivale a publicar la API. Callbacks y webhooks externos requieren un diseño de exposición mínima posterior.
- No se promete continuidad si cae el servidor local. Respaldos, UPS y recuperación se probarán antes de pasar de laboratorio a uso real.
- La integración futura entre sucursales no condiciona el flujo de venta local, pero deberá diseñarse antes de consolidar datos.
- Falta decidir el formato de los instaladores y si PostgreSQL será un prerrequisito o parte del instalador de servidor. No fijar esa elección hasta probar instalación, actualización y recuperación.
- El checklist local que extrae la contraseña de Docker y las credenciales de `documentacion/info.txt` es solo de desarrollo. No trasladarlo al procedimiento de instalación ni publicar esas credenciales.
