# Carnicerías

Sistema de punto de venta para carnicerías: API .NET 10 y PostgreSQL, interfaz Angular y puesto de venta dentro de Electron. La prioridad actual es el flujo de venta; la administración de usuarios permanece pausada. Las pantallas del POS se revisan en Electron y se alinean con `Documentos/maqueta`.

## Estado del POS

La lista real permite guardar un ticket, reservar stock, abrir un turno por cajero, registrar una venta con pagos y convertir la reserva en egreso físico dentro de una transacción. El cierre del turno conserva el saldo contable. Los datos actuales del catálogo son ficticios y la base visual es no productiva; no hay comprobante fiscal ni arqueo físico.

El estado detallado y los pendientes están en `tasks/pos-todo.md`. El contrato de cobro está en `SPEC-pos-checkout.md`; la evidencia de prueba en Electron y sus límites están en `tasks/pos-checkout-plan.md`.

## Desarrollo local

Requisitos: SDK .NET 10, PostgreSQL, Node.js compatible con `src/Carnicerias.Web/package.json` y npm 11.19.0. Con una base local previamente inicializada, configurar `CARNICERIAS_CONNECTION_STRING` en el entorno y ejecutar el API en `http://localhost:5197`:

Durante el desarrollo se usa **una única base de la aplicación**, `carnicerias_test_visual`, en el PostgreSQL de Docker expuesto en `127.0.0.1:55433`. No crear otra base (tampoco una de pruebas) sin autorización explícita. Para volver a empezar se limpian los datos en esta misma base y se reaplican los scripts del repositorio. La base de sistema `postgres` del servidor no es una base del POS. Esta instalación es ficticia y no productiva; el despliegue final será en un servidor PostgreSQL cuando el producto esté terminado.

El set actual contiene `Empresa Visual`, `Sucursal Visual`, los usuarios `visual-admin` y `visual-cashier`, dos terminales (`Caja 1` y `Caja 2`), nueve productos con existencias y una lista de precios. Las contraseñas y credenciales de terminal se entregan fuera de Git. `tools/SeedPosVisualData.sql` conserva el catálogo y stock ficticio; `seed-pos-visual` del proyecto `tools/Carnicerias.Bootstrap` inicializa identidades y terminales solo cuando la base está vacía.

```powershell
dotnet run --project src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release -- --urls http://localhost:5197
```

En otra consola, con dependencias instaladas mediante `npm ci` en `src/Carnicerias.Web` y `src/Carnicerias.Pos`:

```powershell
npm run electron:start --prefix src/Carnicerias.Pos
```

Ese comando compila Angular, prepara los archivos del POS y abre Electron. Para pruebas automatizadas:

```powershell
dotnet test Carnicerias.sln --configuration Release
npm test --prefix src/Carnicerias.Web -- --watch=false
npm run lint --prefix src/Carnicerias.Web
npm run electron:test --prefix src/Carnicerias.Pos
```

Las pruebas de integración PostgreSQL actualmente requieren `CARNICERIAS_TEST_CONNECTION_STRING` apuntando a una base desechable cuyo nombre comience con `carnicerias_test_`. **No ejecutarlas bajo la regla de base única vigente**: podrían crear o limpiar otra base, y nunca deben apuntarse a `carnicerias_test_visual`. Hasta que el usuario autorice una estrategia compatible, ejecutar solo las pruebas que no necesitan esa base y dejar explícita la cobertura de integración pendiente.

## Documentación

- `REQUERIMIENTOS_MVP.md` y `CAPABILITY_MAP.md`: alcance y módulos.
- `SPEC-pos-sales.md`, `SPEC-pos-checkout.md` y `SPEC-catalog-pricing.md`: contratos del POS.
- `SPEC-platform-access.md` y `tasks/todo.md`: acceso y administración, actualmente secundarios frente al POS.
- `tasks/pos-todo.md`: estado vigente y próximos incrementos.
