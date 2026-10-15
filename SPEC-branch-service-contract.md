# Contrato de lectura sucursal–central (`branch-service-contract`)

**Estado:** aprobado por el usuario, 14/10/2026. **Mapa aprobado:** [sucursal–central](CAPABILITY-MAP-sucursal-central.md). Este documento especifica solo la API que ofrece una sucursal; no implementa todavía la central.

## Objetivo

Permitir que la API central de una empresa consulte la identidad y un resumen comercial de una sucursal autónoma. La sucursal sigue siendo la fuente de verdad y confirma ventas aunque la central no exista, esté desconectada o falle. El primer contrato es de **solo lectura** y no replica ventas, pagos, stock ni clientes.

El contrato amplía el despliegue de [una sucursal autónoma](SPEC-despliegue-sucursal.md). La decisión histórica `documentacion/decisiones/0002-instalacion-por-sucursal.md` sigue describiendo el workshop sin central; al implementar este módulo se registrará una decisión nueva que explique la ampliación.

## Alcance y topología

- La sucursal expone únicamente `/api/central/v1/*` a la central por una red privada/VPN con HTTPS confiable. La administración local, el POS y los callbacks de ARCA/pasarelas no pasan por la central.
- La integración está **deshabilitada por defecto**. Habilitarla exige fijar fuera de Git un `installationId` estable, `companyId`, `branchId` y una credencial de servicio distinta de las credenciales de caja y de las sesiones humanas.
- Esos IDs se validan contra la empresa y sucursal activas de la base local. El laboratorio puede contener otras empresas/sucursales en la misma base; este contrato solo permite leer la pareja fijada en la configuración del servidor. No acepta IDs de empresa o sucursal enviados por la central.
- La restricción de una sola sucursal operativa por instalación completa sigue siendo una tarea separada. Este módulo no cambia los ABM locales ni aplica migraciones.

## API v1

Las respuestas de las rutas habilitadas son JSON con `Cache-Control: no-store`, incluidos los errores y el límite de frecuencia. Los importes son decimales en ARS; `fromUtc` es inclusivo y `toUtc` exclusivo. Las fechas usan ISO 8601 con zona UTC. No se envían datos personales ni el detalle de tickets.

| Método y ruta | Entrada | Resultado |
|---|---|---|
| `GET /api/central/v1/installation` | Encabezado `Authorization: Bearer <credencial-de-servicio>`; sin parámetros de consulta. | `contractVersion`, `installationId`, `companyId`, `companyName`, `branchId`, `branchName`, `capabilities`. |
| `GET /api/central/v1/summary` | Mismo encabezado; `fromUtc` y `toUtc` obligatorios, ambos UTC, rango mayor que cero y no mayor a 31 días. | Identidad de origen, ventana, `generatedAtUtc`, `currency`, cantidad e importe de ventas, pagos aplicados por medio y cantidad/saldo en efectivo de turnos abiertos. |

Respuesta de `summary` (forma del contrato, importes ilustrativos):

```json
{
  "contractVersion": 1,
  "installationId": "11111111-1111-4111-8111-111111111111",
  "companyId": "22222222-2222-4222-8222-222222222222",
  "branchId": "33333333-3333-4333-8333-333333333333",
  "fromUtc": "2026-10-14T03:00:00Z",
  "toUtc": "2026-10-15T03:00:00Z",
  "generatedAtUtc": "2026-10-14T18:00:00Z",
  "currency": "ARS",
  "saleCount": 8,
  "salesTotal": 150000.00,
  "salePaymentsTotal": 140000.00,
  "paymentsByMethod": [{ "method": "cash", "amount": 90000.00 }, { "method": "mercadoPago", "amount": 50000.00 }],
  "openShiftCount": 2,
  "openShiftCashBalance": 95000.00
}
```

`salesTotal` incluye el total confirmado de las ventas de esa sucursal en la ventana; `salePaymentsTotal` suma pagos aplicados en esas ventas y puede ser menor por cuenta corriente/saldo a favor. `openShiftCashBalance` es el saldo de efectivo de los turnos abiertos **al momento de consultar**, no un movimiento dentro de la ventana. Los campos de cuenta corriente que hoy se calculan para toda una empresa no se reutilizan como si fueran específicos de una sucursal.

Errores usan el formato actual `{ "error": { "code", "message", "details" } }`: `400 VALIDATION_ERROR` para parámetros inválidos o adicionales; `401 NOT_AUTHENTICATED` para credencial ausente o inválida, sin revelar cuál; `503 BRANCH_BINDING_UNAVAILABLE` si la pareja configurada no está activa o no puede validarse; `503 SUMMARY_UNAVAILABLE` si vence el tiempo de consulta o falla su lectura; `429 RATE_LIMITED` al superar la cuota. Si la integración está deshabilitada, las rutas no se registran (`404`). Un fallo interno no incluye credenciales, cadena de conexión ni excepciones en la respuesta.

## Autenticación y límites de confianza

- La credencial es aleatoria de al menos 256 bits, única por instalación y solo autoriza lectura en estas rutas. Su formato inicial es hexadecimal de 32 bytes (64 caracteres). La sucursal conserva únicamente el SHA-256 de esos bytes en su configuración protegida; la central conserva el secreto correspondiente fuera de Git. La comparación es de tiempo constante. Los verificadores actual y de relevo se releen por solicitud desde una fuente de configuración que admita recarga, de modo que la rotación planificada no requiera reiniciar la API ni detener el POS; la identidad de instalación/empresa/sucursal queda fijada hasta reiniciar el servidor.
- Una cookie de administrador local, una cookie de SuperAdmin o una credencial de terminal Electron **no** autorizan estas rutas. La credencial de integración tampoco autoriza `/api/admin`, `/api/pos`, ventas, pagos ni mutaciones.
- La central solo recibe un destino de sucursal previamente registrado por un administrador autorizado, no una URL arbitraria enviada por el navegador. La validación de destino, resolución DNS, redirecciones y TLS pertenecen a `central-access`; así se evita usar la central como proxy hacia servicios internos.
- La red privada/VPN y HTTPS son controles adicionales, no sustitutos de la autenticación. Ni el token ni los encabezados `Authorization` se registran en logs. Límites de tiempo, frecuencia y tamaño de respuesta protegen la API local.
- La consulta y sus errores jamás participan en la transacción de venta, apertura/cierre de turno, stock o impresión.

## Tecnología, estructura y estilo

- Sucursal: .NET 10 Minimal API y EF Core/Npgsql existentes, en `src/Carnicerias.Api/Federation/`. Reutilizar las entidades y el cálculo de ventana de negocio sin exponer directamente el endpoint de administrador basado en cookie.
- Contratos con tipos explícitos y propiedades estables, por ejemplo `record BranchSummaryResponse(Guid InstallationId, Guid BranchId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, decimal SalesTotal);`. La respuesta completa se define en este documento; la versión `v1` cambia solo de forma aditiva.
- Pruebas puras/de arquitectura en `tests/Carnicerias.ArchitectureTests/`; pruebas HTTP no destructivas pueden apuntar exclusivamente a `carnicerias_test_visual` si la configuración local está disponible.

## Comandos y estrategia de verificación

```powershell
dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj
dotnet build src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release
git diff --check
```

Las pruebas nuevas cubrirán contrato, rangos, configuración incompleta, credencial incorrecta, intento de seleccionar otra empresa/sucursal, autorización inversa (token de integración contra rutas operativas) y ausencia de efectos sobre ventas. No se ejecutarán pruebas que creen o reinicien otra base. Si una prueba HTTP requiere la base visual, será de solo lectura y quedará documentada; la prueba multiservidor real espera el workshop autorizado.

## Criterios de aceptación

1. Con integración deshabilitada o configuración incompleta, la venta local funciona igual y no existe una API central abierta por defecto.
2. Con configuración válida, la credencial de esa instalación lee solo su identidad y resumen de la sucursal fijada; ninguna entrada HTTP puede cambiar el alcance, y el resultado coincide con consultas de solo lectura de la base local.
3. La ausencia, revocación o error de la central no cambia stock, ventas, pagos, turnos ni el resultado de confirmar una venta.
4. Las respuestas y logs no contienen credenciales ni datos personales; errores y límites del contrato se prueban sin crear otra base de aplicación.

## Límites y decisiones posteriores

- `central-access` definirá el inicio de sesión del **administrador de empresa** en la central. El usuario aprobó ese rol, pero aún no decidió si compartirá identidad con administradores locales; no se copiarán contraseñas por inferencia.
- `central-business-view` consumirá este contrato y distinguirá resultados completos de parciales cuando una sucursal no responda. El catálogo detallado de stock/ventas y la administración remota de solo lectura requieren contratos adicionales; no forman parte de estos dos endpoints iniciales.
- `branch-change-signals` incorporará después avisos salientes idempotentes. El pull seguirá funcionando aunque no lleguen avisos.
- Ningún despliegue de este módulo autoriza una segunda base de datos en el entorno de desarrollo actual.
