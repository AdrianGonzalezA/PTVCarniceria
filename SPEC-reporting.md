# Área Negocio del administrador: decisiones y puntos a revisar

**Estado:** separación y clasificación aprobadas por el usuario el 8/10/2026. Primer resumen comercial implementado; indicadores posteriores sujetos a revisión durante las pruebas. Complementa `SPEC-admin.md` y corresponde al módulo `reporting` del `CAPABILITY_MAP.md`.

## Objetivo

Separar las tareas de mantenimiento del sistema de la lectura de la operación comercial. El mismo administrador, sesión, empresa y base PostgreSQL tendrá dos pestañas principales:

| Pestaña | Propósito | Contenido propuesto |
|---|---|---|
| **Negocio** | Saber qué pasó y qué queda pendiente | Resumen de ventas, medios cobrados, caja/turnos, cuentas corrientes y acceso al historial de operaciones. |
| **Configuración** | Definir cómo funciona el POS | ABM de organización, usuarios, catálogo, clientes, precios, cajas, dispositivos y reglas de etiquetas. |

No habrá un segundo sitio ni otro login. Una pestaña no concede permisos: cada página y API seguirá validando el permiso del rol y la empresa del contexto.

## Navegación y alcance inicial

- `/admin` abre **Negocio**; **Configuración** conserva los enlaces existentes sin cambiar sus URLs públicas. Ambas pestañas permanecen visibles al recorrer páginas administrativas y muestran cuál está activa.
- El **Historial** actual se ubica en Negocio, con sus filtros y detalle de tickets. Los ABM actuales quedan en Configuración. **Existencias** y **Recepción de piezas** pertenecen a Negocio porque registran movimientos reales; la fórmula de lectura de etiquetas permanece en Configuración.
- Primer dashboard: período de hoy en hora `America/Argentina/Buenos_Aires`, empresa del contexto y selector de una sucursal o todas. Sin datos, mostrar ceros y un estado vacío claro, nunca cifras demostrativas. La API admite además un intervalo UTC explícito `[fromUtc, toUtc)` de hasta 366 días; el filtro de fechas en la interfaz queda para el siguiente corte.
- Indicadores iniciales: importe y cantidad de ventas confirmadas del período; cobros inmediatos aplicados a esas ventas por medio; importe de nuevas ventas cargado a cuenta; cargos a cuenta brutos acumulados. Aún no se muestra deuda neta porque recibos, imputaciones y anticipos no están persistidos. Las futuras cobranzas de deuda se mostrarán **separadas** de ventas y de cobros de venta. El fondo inicial de caja se muestra separado de ingresos; los saldos de caja pertenecen a los turnos correspondientes.
- En **Cuentas corrientes**, listar clientes con deuda/saldo a favor y abrir un estado de cuenta por cliente: ventas cargadas, cobranzas e imputaciones, anticipos aplicados y correcciones, por fecha y sucursal. Solo se presentarán movimientos que ya estén persistidos; las secciones aún no implementadas se rotularán como pendientes o no se mostrarán.
- Los listados se paginan y permiten filtrar período y sucursal. El dashboard resume; no modifica ventas, caja ni saldos. Las acciones de cobranza pertenecen al flujo transaccional aprobado en `SPEC-customers-credit.md`, no a un botón que altere cifras desde un reporte.

## Definiciones para evitar dobles conteos

- **Venta**: total de tickets confirmados, incluso la parte a cuenta. No equivale a dinero ingresado.
- **Cobro de venta**: pagos inmediatos aplicados al ticket. El vuelto reduce el efectivo neto; otros medios no producen vuelto.
- **Nueva deuda**: cargo a cuenta originado en la venta del período. No es ingreso de caja.
- **Cobranza de deuda**: dinero recibido después por una cuenta corriente; ingresa a la caja/turno que lo cobró y disminuye deuda o genera anticipo, pero **no crea una nueva venta**.
- **Deuda pendiente**: cargos menos imputaciones válidas, calculada desde movimientos. El saldo a favor se informa aparte. Hasta que existan recibos e imputaciones, el sistema solo puede mostrar cargos originales, rotulados como tales.
- Fechas: el filtro que el usuario ve en hora local se convierte a intervalo UTC `[inicio, fin)` en API. Toda consulta se limita a la empresa autorizada; “todas las sucursales” nunca incluye otra empresa.

## Tecnología, estructura y comandos

- Angular 22 en `src/Carnicerias.Web`: shell administrativo con rutas protegidas, pestañas accesibles y páginas de Negocio separadas de los ABM. Seguir las señales, `@if`/`@for` y los tokens SCSS ya presentes.
- .NET 10 Minimal API y EF Core en `src/Carnicerias.Api`/`src/Carnicerias.Infrastructure`: consultas agregadas y paginadas de solo lectura; no crear tablas de totales editables para el primer corte.
- Compilar: `dotnet build Carnicerias.sln --configuration Debug --no-restore`; `npm run build --prefix src/Carnicerias.Web`.
- Probar: `dotnet test Carnicerias.sln --no-restore`; `npm test --prefix src/Carnicerias.Web -- --watch=false`; `npm run lint --prefix src/Carnicerias.Web`. No ejecutar pruebas que creen o reinicien otra base.
- Revisar la interfaz del POS y la administración **dentro de Electron**, alineando cabecera, colores y jerarquía con `Documentos/maqueta/Configuración - Desktop.png`. El diseño de los reportes se deriva de ese lenguaje visual, no de datos ficticios ni de un tablero genérico.

## Ejemplo de contrato y estilo

Una respuesta de resumen mantiene conceptos separados, por ejemplo:

```json
{
  "saleCount": 12,
  "salesTotal": 125000.00,
  "immediateSalePayments": 90000.00,
  "newAccountCharges": 35000.00,
  "registeredAccountCharges": 51000.00
}
```

Los nombres de campo usan `camelCase`, los importes son decimales monetarios del servidor y ninguna cifra se calcula sumando tarjetas, efectivo, deuda y ventas como si fueran magnitudes intercambiables. El ejemplo expresa el contrato, no datos para cargar en la base.

El endpoint implementado es `GET /api/admin/history/summary`, con permiso `organization.manage` y empresa obligatoria del contexto. Devuelve además `fromUtc`, `toUtc`, `branchId` y `paymentsByMethod`. Sin fechas recibe el día actual argentino; con fechas exige ambas en UTC, ordenadas y dentro de 366 días. `branchId` debe pertenecer a la empresa; un identificador vacío es inválido y una sucursal ajena o inexistente no expone datos. No modifica la base.

## Límites y aceptación

- Siempre: filtros y agregaciones en el servidor; autorización por empresa y permiso; estados de carga, vacío y error; filtros visibles; accesibilidad por teclado; consultas no destructivas; pruebas y revisión visual en Electron.
- Pedir validación antes de: cambiar la asignación de secciones entre pestañas, definir indicadores fiscales, publicar exportaciones oficiales o presentar la deuda bruta como saldo neto una vez que existan cobranzas.
- Nunca: crear otra base, guardar credenciales en Git, alterar ventas/asientos desde el dashboard, mostrar datos de otra empresa o llamar “facturado” a una venta no fiscal.
- Aceptación del primer corte: el administrador alterna entre Negocio y Configuración sin perder contexto; los enlaces existentes siguen funcionando; un cajero no accede; el resumen usa ventas persistidas y distingue deuda de caja; se verifica en Electron y con pruebas automatizadas.

## Clasificación aprobada

El usuario confirmó que **Existencias y Recepción de piezas** pertenecen a Negocio porque son movimientos operativos. **Lectura de etiquetas** permanece en Configuración. La decisión cambia navegación, no datos ni permisos.
