# Mapa de capacidades: temas visuales por equipo

**Estado:** aprobado por el usuario el 17/10/2026. Se continúa en `feature/ui-v2`; agregar un tema nuevo no requiere abrir una rama por estilo. Complementa `tasks/ui-v2-plan.md` sin cambiar el flujo de venta.

## Alcance confirmado

- La elección del tema es **por equipo/perfil local**, no por empresa ni por sucursal. Electron y un navegador común conservan preferencias separadas incluso en la misma PC; el usuario confirmó este límite.
- **Original** recupera la apariencia **y la distribución** anteriores del POS. **Nocturno** conserva la interfaz UI2 actual. El administrador conserva su aspecto anterior en Original y recibe una variante Nocturno en todas sus pantallas.
- Debe ser posible registrar temas adicionales después, sin duplicar la lógica de negocio, sin crear una rama por tema y sin agregar campos a la base de la aplicación para esta preferencia local.

| Módulo | Responsabilidad verificable | Depende de |
|---|---|---|
| `visual-theme-selection` | Catálogo de IDs de tema, selector en Configuración, aplicación inmediata y persistencia por perfil local con valor seguro ante una preferencia desconocida. | — |
| `pos-visual-themes` | Renderizar Original previo y Nocturno UI2 en la misma caja, compartiendo sesión, tickets A–D, venta, stock y cobro. | `visual-theme-selection` |
| `admin-visual-themes` | Conservar Original y aplicar Nocturno a Negocio, Configuración, ABM, Usuarios y SuperAdmin, incluidos diálogos y estados de error. | `visual-theme-selection` |

Orden: `visual-theme-selection` → `pos-visual-themes` → `admin-visual-themes`. Cada módulo tendrá una especificación y pruebas propias. La pantalla de inicio de sesión no es una pantalla administrativa y queda fuera de la adaptación visual inicial; la preferencia local puede leerse antes del inicio de sesión para evitar un cambio brusco al navegar.

## Límites del laboratorio

Se conserva exclusivamente `carnicerias_test_visual` en PostgreSQL Docker local 55433; estos temas no requieren migración ni una segunda base. No se ejecutarán pruebas de integración que creen o reinicien bases. El POS se revisa visualmente **dentro de Electron**. El archivo de comandos locales ya modificado fuera de este trabajo se conserva sin alteraciones.
