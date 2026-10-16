# Vista de negocio central en vivo (`central-business-view`)

**Estado:** aprobado para implementación el 2026-10-14. **Módulo:** `central-business-view` del [mapa aprobado](CAPABILITY-MAP-sucursal-central.md). Depende de [acceso central](SPEC-central-access.md) y del [contrato de lectura de sucursal](SPEC-branch-service-contract.md).

## Supuestos a validar

1. El primer dashboard usa exclusivamente resúmenes **en vivo** de `/api/central/v1/summary`; no replica tickets, clientes, stock ni movimientos en tablas centrales.
2. Un total general se muestra solo cuando **todas** las sucursales activas registradas de la empresa responden con datos compatibles. Con una caída, se muestran las filas disponibles, pero el total general queda ausente, no en cero ni como suma parcial silenciosa.
3. Los importes de ventas y pagos pertenecen a la ventana consultada. El saldo de efectivo de turnos abiertos es una **fotografía al consultar**, y se presenta aparte de las ventas, nunca sumado a ellas.
4. La consulta no abre ni cierra caja, no factura, no cobra y no cambia stock. El POS local sigue operando sin central.

## Objetivo y alcance

Un administrador central autenticado elige un período y consulta, en una sola pantalla, ventas y pagos de las sucursales **registradas en su empresa**. Ve hora de generación y estado de cada respuesta. Si alguna sucursal no responde, la pantalla indica datos incompletos y evita un total general engañoso. El alcance inicial es el resumen ya definido por la API de sucursal; detalle de tickets, clientes, stock y administración remota quedan fuera.

No se agrega base ni esquema: en desarrollo se usa únicamente `carnicerias_test_visual` (`127.0.0.1:55433`) y `central_access.installations` para identificar destinos. La central consulta a cada sucursal por VPN/red privada con HTTPS en despliegue; HTTP literal `127.0.0.1` requiere habilitación explícita solo en laboratorio. Una instalación *registrada* no se declara *conectada* por sí misma.

## Contrato de API y consistencia

`GET /api/v1/business/summary` requiere la cookie central vigente. Sin parámetros, el servidor selecciona **hoy de Buenos Aires**, desde las 00:00 locales hasta el instante actual (consulta acumulada del día), y devuelve los límites UTC efectivos. Para un período elegido se admiten **ambos** parámetros UTC (`fromUtc`, `toUtc`) o **ambas** fechas de calendario argentino (`fromDate`, `toDate`, formato `yyyy-MM-dd`, extremos inclusivos). Las fechas locales se convierten en la central a un límite superior UTC exclusivo. Toda ventana debe ser válida y de máximo 31 días, igual al contrato de sucursal. Un solo límite, la mezcla de formatos o cualquier parámetro ajeno es inválido. No acepta `companyId`, `branchId`, URL ni selector de instalación desde el navegador. La empresa se obtiene de la sesión y la lista de instalaciones activas se filtra en el servidor.

La central vuelve a validar el destino contra su lista/IP aprobada antes de cada conexión, resuelve la credencial por referencia fuera de la base y usa un cliente HTTP sin proxy ni redirecciones, con IP fijada, TLS estándar, respuesta limitada y timeout. Nunca envía cookie central a una sucursal. Verifica en cada respuesta `contractVersion=1`, los tres UUID, ventana solicitada y moneda `ARS`. No acepta importes inválidos ni métricas de otra sucursal como propias. Los errores y logs omiten token, destino y datos personales.

Respuesta propuesta (números ilustrativos, no históricos):

```json
{
  "window": { "fromUtc": "2026-10-14T03:00:00Z", "toUtc": "2026-10-14T18:00:00Z" },
  "retrievedAtUtc": "2026-10-14T18:00:00Z",
  "completeness": "complete",
  "aggregate": { "currency": "ARS", "saleCount": 8, "salesTotal": 150000.00, "salePaymentsTotal": 140000.00 },
  "branches": [
    {
      "installationId": "11111111-1111-4111-8111-111111111111",
      "label": "Sucursal Centro",
      "status": "ok",
      "generatedAtUtc": "2026-10-14T17:59:59Z",
      "saleCount": 8,
      "salesTotal": 150000.00,
      "salePaymentsTotal": 140000.00,
      "openShiftCount": 1,
      "openShiftCashBalance": 50000.00,
      "paymentsByMethod": [{ "method": "cash", "amount": 90000.00 }]
    }
  ]
}
```

Si una sucursal falla, su fila queda con `status: "unavailable"` y métricas `null`; `completeness: "incomplete"` y `aggregate: null`. Si no hay instalaciones activas, `completeness: "empty"`, `branches: []` y `aggregate: null`. No se sustituye ausencia por cero. `generatedAtUtc` pertenece a la respuesta remota; `retrievedAtUtc` pertenece a la central. El motivo técnico detallado no se devuelve al navegador. El endpoint central devuelve `200` para una consulta parcial representable, `400` por ventana inválida y `401` por falta de sesión; no convierte la caída de una sola sucursal en error global. Respuestas llevan `Cache-Control: no-store`.

La API suma `saleCount`, `salesTotal` y `salePaymentsTotal` solamente si todas las filas son `ok` y compatibles. Dos registros activos que apunten a la misma pareja `remoteCompanyId`/`remoteBranchId` quedan no disponibles para esta consulta: no se les pide el resumen ni se cuenta esa sucursal dos veces. `paymentsByMethod`, `openShiftCount` y `openShiftCashBalance` se muestran por sucursal en este primer corte; no se mezclan saldos instantáneos con importes de la ventana. La pantalla muestra explícitamente cuándo se consultó cada fila. Cada HTTP de sucursal vence a los 5 segundos; la **fase de consulta remota** tiene un plazo global de 10 segundos, máximo 4 conexiones simultáneas y un límite de 100 instalaciones activas. El endpoint admite hasta 30 consultas por minuto por IP, sin cola; luego responde `429 QUERY_RATE_LIMITED`. El resultado tardío se marca no disponible.

## Estructura, estilo y comandos

- API central: `src/Carnicerias.Central.Api/` (ruta autenticada) y `src/Carnicerias.Central.Infrastructure/` (lector de registro y cliente de sucursal). No incorporar `PlatformAccessDbContext` ni consultar tablas operativas POS desde la central.
- Sitio central: `src/Carnicerias.Central.Web/src/app/` (cliente tipado y panel). Conserva la identidad visual actual, estados accesibles de carga/vacío/incompleto y diseño responsive. El POS se revisa visualmente solo en Electron; el sitio central puede revisarse en navegador.
- Pruebas sin aprovisionar otra base: `tests/Carnicerias.Central.Tests/` y pruebas Angular del sitio central. Un smoke no destructivo puede usar la sucursal de laboratorio en la única base visual.

Ejemplo de límite de alcance en código:

```csharp
var installations = await registry.ForCompanyAsync(session.Company.Id, cancellationToken);
// Ningún companyId recibido por HTTP decide la consulta.
```

Comandos de verificación previstos:

```powershell
dotnet build src/Carnicerias.Central.Api/Carnicerias.Central.Api.csproj --configuration Release --no-restore
dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --configuration Release --no-restore
npm --prefix src/Carnicerias.Central.Web test -- --watch=false
npm --prefix src/Carnicerias.Central.Web run build
git diff --check
```

## Estrategia de pruebas y límites

Pruebas puras y HTTP deben cubrir empresa A/B con UUIDs manipulados, ventana malformada o mayor a 31 días, sucursal caída/lenta, redirección, TLS inválido, token ausente, respuesta con UUID/moneda/ventana incompatibles, total completo y parcial sin agregado. Angular debe probar estado vacío, incompleto, sesión vencida y ausencia de valores cero inventados. No ejecutar suites que crean o reinician bases.

- **Siempre:** autorización por empresa de la sesión, validación estricta del contrato remoto y del destino, tiempos/límites de respuesta, logs y errores sin secretos, regresión de venta local sin dependencia central.
- **Requiere nueva aprobación:** otra base, replicación o persistencia de métricas, un endpoint de detalle, nuevas métricas fiscales o monetarias, publicación a Internet y cambios de alcance entre sucursales.
- **Nunca:** enviar la cookie central a la sucursal, aceptar una URL del navegador, copiar datos comerciales al esquema central, mostrar suma parcial como total general o bloquear ventas por falta de central.

## Criterios de aceptación

1. Una sesión de Empresa A solo provoca consultas a sus instalaciones activas; no puede leer B con parámetros o UUIDs conocidos.
2. Con todas las sucursales respondiendo versión/identidad/ventana/moneda correctas, el agregado coincide exactamente con las sumas de ventas y pagos del período, y cada fila indica su hora remota.
3. Con una sucursal inaccesible o incompatible, esa fila no muestra ceros; el resultado dice `incomplete` y **no** publica `aggregate`.
4. El endpoint y la pantalla no devuelven URL, token, referencia de secreto, nombres de clientes ni detalle de tickets. La venta de sucursal sigue funcionando con la central detenida.
5. Pruebas y smoke conservan `carnicerias_test_visual` y sus ventas ficticias; el workshop real por VPN/HTTPS queda pendiente.

## Reglas de negocio confirmadas

1. **Período inicial al abrir el dashboard:** "hoy" del calendario de `America/Argentina/Buenos_Aires`, convertido a UTC para consultar. Se entiende como acumulado desde la medianoche hasta el momento de la consulta, no como últimas 24 horas. La pantalla permitirá elegir otro rango de hasta 31 días.
2. **Saldo de caja instantáneo:** solo por sucursal en v1, sin total general. Cada respuesta puede tomarse en segundos distintos y el saldo no pertenece al período de ventas. Un futuro consolidado requeriría regla de tolerancia temporal y etiqueta de corte.
