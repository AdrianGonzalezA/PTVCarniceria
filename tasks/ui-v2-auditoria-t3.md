# Auditoría T3 · administración Original/Nocturno

**Fecha:** 17/10/2026. **Rama:** `feature/ui-v2`. **Alcance:** `Carnicerias.Web` de sucursal/empresa y SuperAdmin local; el portal Central independiente queda fuera. Se usó únicamente la base existente `carnicerias_test_visual`, sin crear bases ni registrar ventas, cobros, ajustes, piezas, usuarios o empresas.

## Matriz de rutas

Cada entrada se revisó en Original y Nocturno a **1920 × 992**, **1366 × 768**, **800 × 768** y **560 × 768 CSS px**. Son 72 recorridos de ruta/tamaño y 144 estados de ruta/tamaño/tema. `tools/inspect-admin-theme.mjs` capturó las pantallas en un perfil Chrome aislado, sin versionar capturas ni credenciales. La inspección del POS se hizo aparte en Electron.

| Ruta | 1920 | 1366 | 800 | 560 |
| --- | :---: | :---: | :---: | :---: |
| `/admin` | ✓ | ✓ | ✓ | ✓ |
| `/admin/configuracion` | ✓ | ✓ | ✓ | ✓ |
| `/admin/categories` | ✓ | ✓ | ✓ | ✓ |
| `/admin/products` | ✓ | ✓ | ✓ | ✓ |
| `/admin/taxes` | ✓ | ✓ | ✓ | ✓ |
| `/admin/price-lists` | ✓ | ✓ | ✓ | ✓ |
| `/admin/organization` | ✓ | ✓ | ✓ | ✓ |
| `/admin/central-connection` | ✓ | ✓ | ✓ | ✓ |
| `/admin/arca` | ✓ | ✓ | ✓ | ✓ |
| `/admin/payment-gateways` | ✓ | ✓ | ✓ | ✓ |
| `/admin/customers` | ✓ | ✓ | ✓ | ✓ |
| `/admin/barcode-layouts` | ✓ | ✓ | ✓ | ✓ |
| `/admin/stock` | ✓ | ✓ | ✓ | ✓ |
| `/admin/pieces` | ✓ | ✓ | ✓ | ✓ |
| `/admin/history` | ✓ | ✓ | ✓ | ✓ |
| `/admin/accounts` | ✓ | ✓ | ✓ | ✓ |
| `/users` | ✓ | ✓ | ✓ | ✓ |
| `/superadmin` | ✓ | ✓ | ✓ | ✓ |

El barrido automatizado encontró **0 excepciones de JavaScript, 0 errores de consola, 0 desbordes horizontales del documento y 0 fallas de contraste** entre los nodos de texto visibles muestreados sobre su fondo opaco más próximo. Las acciones de tablas que se desplazan horizontalmente fueron alcanzables a 560 px. El foco de teclado de las pestañas administrativas y del acceso SuperAdmin se distinguió en ambos temas. Este muestreo no reemplaza una auditoría WCAG completa ni verifica todos los estados posibles de la aplicación.

Se corrigió un hallazgo real: el texto blanco sobre el turquesa Original `#008b8b` daba aproximadamente **4,15:1** en cabeceras de tablas y en la pestaña activa de Historial. Se oscureció solo ese fondo a `#008080` (**4,77:1**), conservando el aspecto de Original. El campo de fecha «Desde» de Historial también se amplió a 560 px para que pudiera usarse.

## Estados y alcance

- Se abrieron sin confirmar cambios los diálogos representativos de catálogos, impuestos, artículos, precios, clientes, existencias, historial, cuentas, usuarios y SuperAdmin; también se revisaron la importación Excel y los formularios de ARCA, pasarelas y recepción de piezas. Las capturas manualmente inspeccionadas cubren Original/Nocturno en tamaños amplio y estrecho.
- En `/superadmin` se verificaron acceso independiente, tabla de cuatro empresas y diálogos de alta, edición/administrador y estado a 1366 y 560 px, sin crear ni inactivar nada. La matriz general de esa ruta capturó el estado previo al acceso.
- La empresa Visual no tiene una conexión Central asociada en este entorno; se verificó el error real y, en T3.10, la presentación configurada mediante una respuesta GET sintética, sin emitir credenciales ni alterar la instalación.
- El selector de tema sigue oculto hasta T4. No se modificaron la pantalla general `/` ni los archivos del portal Central.
- El POS se capturó **dentro de Electron** a 1272 × 744 CSS px con cinco líneas del borrador A y el mismo total en Original y Nocturno. La variante temporal se restauró a Original y no se confirmó la venta ni se alteró la preferencia guardada.

## Verificación y pendientes

La suite Angular terminó **191/191**, Electron **88/88**, lint y build correctos. No se ejecutaron pruebas de integración que creen o reinicien bases. Los avisos de presupuesto de `admin-page.scss` y `categories-page.scss`, y el de `qrcode` CommonJS, preexistían a esta auditoría y no se silenciaron ni se elevaron los umbrales. T4 habilitará el selector local para aceptación visual del usuario; T3 no fusiona esta rama a `main`.
