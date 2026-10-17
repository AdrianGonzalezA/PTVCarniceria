# Especificación propuesta: temas visuales de administración

**Estado:** aprobado por el usuario el 17/10/2026. **Módulo:** `admin-visual-themes` del [mapa de temas](CAPABILITY-MAP-visual-themes.md). **Rama:** `feature/ui-v2`; no integrar a `main` antes de revisar ambas variantes.

## Objetivo y supuestos explícitos

Los administradores de empresa y el SuperAdmin local deben poder recorrer las mismas funciones de administración con el tema Original o Nocturno, según la preferencia de su equipo. Se modifica la presentación, no el contenido ni los permisos.

1. **Original** conserva la distribución y los controles administrativos actuales: cabecera grafito, fondo oscuro, paneles claros, acentos turquesa/lima, tablas y formularios existentes.
2. **Nocturno** usa la identidad grafito/cobre de UI2 de forma coherente en navegación, paneles, tablas, campos, botones y diálogos. No debe trasladar el JavaScript ni los datos demostrativos de UI2.
3. La preferencia sigue siendo **local al perfil**. La administra `visual-theme-selection`; este módulo solo presenta el tema ya elegido. El selector no se publica hasta completar también esta adaptación.
4. El alcance aprobado es `src/Carnicerias.Web` (administración de sucursal/empresa y `/superadmin`). `src/Carnicerias.Central.Web` es otra aplicación y tendrá una especificación posterior.
5. La pantalla general de inicio de sesión (`/`) queda fuera del cambio visual, conforme al mapa aprobado. El formulario de acceso que vive dentro de `/superadmin` sí debe resultar legible en Nocturno.

## Pantallas y elementos cubiertos

| Familia | Rutas o componentes |
| --- | --- |
| Inicio y navegación | `/admin` (Negocio), `/admin/configuracion`, pestañas Negocio/Configuración y tarjetas de módulos. |
| Catálogos y configuración | `/admin/categories`, `/admin/products`, `/admin/taxes`, `/admin/price-lists`, `/admin/organization`, `/admin/central-connection`, `/admin/arca`, `/admin/payment-gateways`, `/admin/customers`, `/admin/barcode-layouts`. |
| Operación y consulta | `/admin/stock`, `/admin/pieces`, `/admin/history`, `/admin/accounts`. |
| Accesos | `/users` y `/superadmin`, incluidos sus formularios y tablas. |
| Componentes transversales | `AdminAreaTabs`, `AdminDetailDialog`, importación Excel y paneles de impuestos; estados de carga, vacío, éxito, advertencia y error. |

## Criterios de aceptación

1. En **Original**, las 18 entradas anteriores mantienen aspecto, distribución y comportamiento actuales. Ninguna pantalla hereda colores Nocturno por accidente; los mensajes y el foco siguen visibles.
2. En **Nocturno**, cada ruta listada tiene cabecera, navegación, superficie de contenido y controles legibles con la paleta grafito/cobre. No quedan grandes paneles claros sin intención visual ni texto oscuro sobre superficie oscura. Los colores de estado conservan significado y contraste.
3. Tablas, búsqueda, paginación, filtros, botones de fila, formularios e importación Excel mantienen su función. La tabla puede desplazarse horizontalmente dentro de su contenedor en anchos estrechos, sin perder acciones ni desbordar toda la página.
4. Todos los diálogos de detalle, alta, edición y confirmación se tematizan también cuando están abiertos. Mantienen título accesible, cierre, foco visible y desplazamiento interno; los estados de error y validación se ven sin depender solo del color.
5. Cambiar `data-visual-theme` en la raíz actualiza la pantalla y un diálogo abierto **sin recargar ni reiniciar el formulario**. No cambia sesión, empresa, sucursal, permisos, datos ingresados ni llamadas de negocio.
6. El diseño es utilizable a 1920 × 992, 1366 × 768 y 800 × 768 CSS px; a 560 px no hay controles inaccesibles ni desplazamiento horizontal de la página completa. Texto normal apunta a contraste WCAG AA de 4,5:1 y el foco de teclado se distingue en ambos temas.
7. La pantalla administrativa puede verificarse en navegador común y en Electron. El POS, si se revisa durante esta etapa, se inspecciona **solo dentro de Electron**. No se usan pruebas que creen o reinicien bases.
8. Los estilos Nocturno no alteran el POS, la pantalla `/` ni el portal Central fuera del alcance inicial. Un futuro tema se podrá agregar con nuevos tokens/variantes, sin duplicar componentes ni lógica de administración.

## Tecnología, estructura y estilo

- Angular 22.2, TypeScript y SCSS existentes; sin API, migración, dependencia ni imágenes nuevas.
- Estilos compartidos: `src/Carnicerias.Web/src/app/features/admin/admin-page.scss` y `categories-page.scss`, importados por muchas pantallas; navegación `admin-area-tabs.ts`, diálogo `admin-detail-dialog.scss`, importación y paneles especializados. Usuarios y SuperAdmin poseen hojas propias que también deben cubrirse.
- Tokens visuales por tema y overrides acotados a administración. Las reglas de tema deben estar bajo el identificador raíz o el host administrativo; evitar selectores globales sin prefijo que afecten la caja. Revisar estilos compilados de Angular antes de elegir la técnica concreta de alcance.
- Mantener HTML semántico y un solo formulario/tabla por función. No crear una segunda vista administrativa con estado paralelo.

Ejemplo de convención para estilos compartidos, no una segunda fuente de preferencia:

```scss
.category-panel {
  color: var(--admin-ink);
  background: var(--admin-surface);
}

.category-panel button:focus-visible {
  outline: 3px solid var(--admin-focus);
}
```

## Comandos y estrategia de pruebas

```powershell
npm test --prefix src/Carnicerias.Web -- --watch=false
npm run lint --prefix src/Carnicerias.Web
npm run build --prefix src/Carnicerias.Web
npm run start --prefix src/Carnicerias.Web
```

- Pruebas focalizadas de tema para navegación compartida, tabla, formulario y diálogo; pruebas existentes de cada página deben seguir pasando. Verificar cambio de atributo sin pérdida de valores de formulario ni mutación de estado.
- Inspección visual de **cada ruta** en ambos temas con datos del entorno existente; cubrir al menos un modal, tabla poblada, tabla vacía, validación y error en cada familia. Registrar viewport y contraste de combinaciones principales.
- La verificación en navegador del administrador no sustituye la revisión del POS en Electron. No confirmar ventas, alterar usuarios reales ni ejecutar integraciones destructivas para comprobar colores.

## Límites

- **Siempre:** preservar permisos, navegación, etiquetas, foco, validaciones, importación y estados; mantener Original disponible; revisar capturas y consola; trabajar en `feature/ui-v2` y conservar la única base `carnicerias_test_visual`.
- **Consultar antes:** extender el alcance al portal Central independiente, cambiar una interacción o un flujo de negocio, agregar dependencias o modificar contratos/API.
- **Nunca:** crear otra base de Carnicerías, versionar credenciales, ocultar errores o acciones para ajustar la maqueta, duplicar formularios/estado, ni fusionar a `main` antes de la aceptación del usuario.

## Decisión de alcance

El usuario confirmó el 17/10/2026 que este corte cubre **la aplicación de sucursal/empresa y su SuperAdmin local**. El portal Central independiente queda para una especificación posterior; no es un pendiente bloqueante de T3.
