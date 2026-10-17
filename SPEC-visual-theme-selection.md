# Especificación propuesta: selección local del tema visual

**Estado:** aprobado por el usuario el 17/10/2026; el trabajo permanece en `feature/ui-v2` hasta su prueba y aceptación. **Módulo:** `visual-theme-selection` de [CAPABILITY-MAP-visual-themes.md](CAPABILITY-MAP-visual-themes.md).

## Objetivo

Un administrador elige **Original** o **Nocturno** en Configuración del equipo. El cambio se aplica al instante a la aplicación Angular del perfil actual y persiste al navegar, recargar y reiniciar Electron o el navegador. La elección no se sincroniza con otros equipos ni entre Electron y un navegador común en la misma PC. Nuevos temas se podrán añadir al catálogo de temas y sus estilos sin bifurcar la lógica de venta ni abrir una rama por cada estilo.

## Reglas y criterios de aceptación

1. Configuración muestra un control «Tema visual» con opciones **Original** y **Nocturno**, la opción activa y una explicación breve de su alcance local. Solo un administrador con acceso a Configuración puede cambiarlo; el cajero conserva el tema ya elegido en su perfil de Electron.
2. Un perfil sin preferencia guardada usa **Original**, para conservar la apariencia anterior como valor seguro. Una preferencia desconocida o almacenamiento local inaccesible también usa Original y no impide iniciar sesión ni vender.
3. El cambio actualiza la apariencia inmediatamente y no recarga la ruta, altera el ticket activo, borra borradores ni genera solicitudes de venta. El mismo perfil conserva la selección tras recarga y reinicio.
4. Electron y Chrome/Edge usan sus almacenes de perfil independientes. Elegir Nocturno en el administrador abierto en navegador no cambia automáticamente la caja Electron de esa PC; para ella se entra a Configuración desde ese perfil de Electron.
5. Los identificadores estables de tema se centralizan en un registro tipado. Agregar un tercero requiere un nuevo identificador, etiqueta y estilos, pero no otra rama, otra base, un nuevo estado de venta ni controles duplicados.
6. El módulo solo selecciona y aplica el identificador. La fidelidad visual del POS y de todas las pantallas administrativas se verifica en los módulos `pos-visual-themes` y `admin-visual-themes` del mapa.

## Tecnología y estructura

- Angular 22.2, TypeScript y SCSS existentes; sin dependencia nueva, endpoint nuevo ni migración.
- Registro y servicio de preferencia en `src/Carnicerias.Web/src/app/core/theme/`, con pruebas cercanas. La aplicación pone el identificador en el elemento raíz antes del primer render para evitar un destello del tema incorrecto.
- Selector en `src/Carnicerias.Web/src/app/features/admin/`, dentro de Configuración. No se usa la base PostgreSQL para esta preferencia.
- Persistencia local por origen y perfil de Electron/navegador. La clave contiene solo el ID del tema, sin usuario, empresa, credenciales ni datos de venta.

Estilo de contrato propuesto:

```ts
export const visualThemes = [
  { id: 'original', label: 'Original' },
  { id: 'nocturno', label: 'Nocturno' },
] as const;
export type VisualThemeId = typeof visualThemes[number]['id'];
```

## Comandos y estrategia de pruebas

- Prueba focalizada del selector y la preferencia: `npm test --prefix src/Carnicerias.Web -- --watch=false --include=src/app/core/theme/*.spec.ts` y prueba de la pantalla de Configuración.
- Regresión Angular completa: `npm test --prefix src/Carnicerias.Web -- --watch=false`.
- Calidad y compilación: `npm run lint --prefix src/Carnicerias.Web` y `npm run build --prefix src/Carnicerias.Web`.
- Revisión visual y de persistencia del POS **en Electron** con un perfil de caja; administración puede revisarse allí o en navegador. No se confirma una venta para probar el selector.
- Pruebas con preferencia ausente, válida, desconocida y almacenamiento que lanza error; cambio en una ruta con ticket abierto sin pérdida de estado; recarga y navegación en el mismo perfil.

## Límites

- **Siempre:** conservar la única base `carnicerias_test_visual` y las funciones de venta; mantener pruebas no destructivas y accesibilidad de teclado, contraste y foco del selector.
- **Consultar antes:** cambiar el alcance local por sincronización entre perfiles/equipos, agregar API o esquema, instalar dependencias o hacer que un cajero pueda modificar la preferencia sin acceso administrativo.
- **Nunca:** crear otra base de Carnicerías, ejecutar pruebas de integración que la creen/reinicien, guardar secretos en Git, ni duplicar estado de ventas para representar un tema.

## Preguntas abiertas

Ninguna para el selector. La reconstrucción exacta del POS Original y el alcance visual de cada pantalla administrativa se detallarán y aprobarán en las especificaciones de sus módulos, tomando la versión anterior a UI2 como referencia de Original.
