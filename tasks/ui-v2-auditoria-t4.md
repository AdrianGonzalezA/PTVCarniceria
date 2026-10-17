# Auditoría T4 · Selector local de tema visual

**Fecha:** 17/10/2026. **Rama:** `feature/ui-v2`. **Alcance:** administrador de sucursal/empresa de `Carnicerias.Web`; el portal Central independiente no forma parte de estos temas.

## Resultado

- Configuración muestra «Tema visual» con Original y Nocturno, indicador «En uso» y explicación del alcance por perfil. Negocio no muestra el selector. Solo se renderiza tras validar el contexto administrativo existente.
- El cambio se aplica de inmediato en la misma ruta, sin solicitudes de venta ni mutaciones del ticket. La preferencia se conserva en `localStorage`; Original es el valor seguro si falta una elección, es desconocida o el almacenamiento falla (pruebas T1).
- La prueba de pantalla comenzó en rojo antes de implementar el control y pasó después. Tras elegir Nocturno, un perfil nuevo de Chrome mantuvo la elección al recargar, pasar a Negocio y volver a Configuración. Se verificó foco visible del radio con teclado.
- Capturas inspeccionadas en Chrome a 1920 × 992, 1366 × 768, 800 × 768 y 560 × 768 CSS px para ambos temas, sin desborde horizontal. El muestreo de contraste de texto visible encontró 0 fallas entre 47 elementos por tema a 1366 px. Las capturas de prueba quedan en la carpeta temporal local `carnicerias-admin-theme`, no en Git.
- Mientras Chrome estaba en Nocturno, la caja POS existente se consultó **solo dentro de Electron**: antes y después siguió en Original, en `/pos`, con sus cinco líneas de borrador intactas. No se confirmó venta, se modificó stock ni se creó otra base.
- El build nuevo se copió al bundle local de Electron y se recargó la caja abierta: pasó a la versión nueva y recuperó las mismas cinco líneas en Original. La sesión activa era la del cajero Juan, sin acceso a Configuración; no se cerró su sesión para forzar una prueba de selección dentro de ese perfil. La elección directa se verificó en el navegador administrativo y el aislamiento entre perfiles se midió contra este Electron.

## Comprobaciones automáticas

- `admin-area-navigation.spec.ts`: cuatro pruebas, incluidas visibilidad por área y selección local sin navegación ni solicitudes de negocio.
- Angular: **192/192**; Electron: **88/88**; lint y build correctos. El build conserva únicamente advertencias previas del presupuesto SCSS de `admin-page.scss`/`categories-page.scss` y CommonJS de `qrcode`; el selector tiene su propio SCSS y no aumentó el exceso del primero.
- La inicialización T1 prueba también preferencia guardada, ID inválido y almacenamiento inaccesible; se aplica antes del primer render. La verificación de runtime de T4 cubrió recarga del navegador, no un apagado físico de la PC.

## Observación de entorno

El servidor Angular abierto desde las 12:50 no había actualizado el bundle tras los cambios de archivos, aunque build y pruebas usaban el código nuevo. Se reprodujo la ausencia del selector en el DOM, se reinició **solo** `ng serve` y el control apareció; la prueba de runtime pasó. La API, PostgreSQL y el Electron de la caja no se reiniciaron.

La aceptación visual del usuario y los pendientes generales de UI2 —incluida la prueba de DPI físico— siguen antes de integrar la rama a `main`.
