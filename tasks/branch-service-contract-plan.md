# Plan de implementación: contrato de lectura sucursal–central

**Estado:** aprobado por el usuario e implementado con verificación local (14/10/2026). **Módulo:** `branch-service-contract` del [mapa de capacidades](../CAPABILITY-MAP-sucursal-central.md). **Especificación aprobada:** [SPEC-branch-service-contract.md](../SPEC-branch-service-contract.md). **Lista ejecutable:** [branch-service-contract-todo.md](branch-service-contract-todo.md).

Este par de archivos es específico del módulo y no reemplaza `tasks/plan.md` ni `tasks/todo.md`, que conservan tareas abiertas de `platform-access`, siguiendo la convención de los demás módulos del repositorio.

## Resultado y límites

La API de cada sucursal podrá responder `GET /api/central/v1/installation` y `GET /api/central/v1/summary` a una central autorizada, con identidad y alcance fijos en la configuración protegida del servidor. El módulo no crea una API central, no agrega pantallas, no replica entidades, no modifica el flujo de venta y no aplica migraciones. La integración estará deshabilitada por defecto. La única base de aplicación de este desarrollo sigue siendo `carnicerias_test_visual` en el PostgreSQL de Docker del puerto local 55433.

## Decisiones técnicas y grafo de dependencias

```text
Configuración protegida y verificador de credencial
                    │
                    ▼
Grupo HTTP de servicio, límite de frecuencia y endpoint de identidad
                    │
                    ▼
Ventana UTC de 31 días y proyección agregada por sucursal fija
                    │
                    ▼
Endpoint de resumen y pruebas del contrato
                    │
                    ▼
Procedimiento de activación, verificación y ADR
```

- La configuración de `Federation` contiene `enabled`, `installationId`, `companyId`, `branchId` y verificadores de credencial actual y de relevo. Se parsea una sola vez al iniciar. Sin configuración completa no se mapean las rutas; se informa el problema sin registrar secretos ni impedir el POS. El servicio nunca obtiene empresa o sucursal de la solicitud.
- La credencial es un token aleatorio de al menos 256 bits. La configuración de la sucursal conserva solo su hash/verificador; la comprobación es de tiempo constante. La coexistencia temporal de dos verificadores permite rotar sin detener las cajas. No se reutilizan sesiones humanas, cookies, credenciales de terminal ni el filtro de permisos operativos.
- Se agregará un grupo Minimal API específico con respuestas tipadas, formato de error existente y `Cache-Control: no-store` también en errores. El limitador se aplica solo al grupo central; no se cambia la cuota del POS. El despliegue expone estas rutas únicamente por VPN/red privada con HTTPS confiable; la configuración de ingress queda fuera del endpoint.
- La identidad se valida contra `Company` y `Branch` activas en la base antes de responder. Un vínculo inválido produce `503`, nunca una consulta a otra sucursal. La ventana exige parámetros `fromUtc`/`toUtc` explícitos, UTC, ordenados y de hasta 31 días. La proyección EF usa `AsNoTracking`, una transacción PostgreSQL de solo lectura con instantánea consistente, filtros simultáneos por empresa y sucursal y campos permitidos; no devuelve tickets, clientes ni métricas de cuenta corriente empresariales como si fueran de la sucursal.
- El endpoint de resumen puede reutilizar las definiciones funcionales de `AdminHistoryEndpoints`, pero no su filtro de cookie ni su respuesta con datos de alcance empresarial. Si se extrae lógica compartida, se preserva sin cambios el contrato de administración existente.

## Modelo breve de amenazas

| Abuso posible | Control y prueba prevista |
|---|---|
| Usar cookie admin/credencial Electron o token inválido para leer la API central | Autenticación de servicio aislada; pruebas HTTP de `401`, sin revelar qué credencial falló. |
| Usar token central para invocar venta, caja o administración | No registrar el token como sesión operativa; prueba negativa de autorización inversa. |
| Consultar otra empresa/sucursal por query, ruta o encabezado | IDs exclusivamente de configuración; rechazar parámetros adicionales; aserción de alcance en consulta y respuesta. |
| Extraer datos personales o saturar la sucursal con períodos grandes | Proyección de campos permitidos, ventana máxima, límite de frecuencia y cancelación/timeout; pruebas de respuesta y validación. |
| Filtrar secretos por configuración, respuesta o logs | Verificador fuera de Git, errores genéricos y revisión de logs/diff antes de entregar. |

## Tareas y puntos de control

1. Configuración cerrada por defecto y verificador de credencial, con pruebas puras.
2. Endpoint de identidad protegido y pruebas HTTP negativas, sin dependencia de una base nueva.
   - **Checkpoint A:** pruebas de seguridad iniciales y build; integración deshabilitada no cambia el POS ni registra rutas centrales.
3. Ventana UTC y consulta agregada de solo lectura acotada a la pareja configurada.
4. Endpoint de resumen y pruebas de contrato, incluyendo parámetros extra y ausencia de datos personales.
   - **Checkpoint B:** pruebas enfocadas, build y revisión de consulta/errores antes de usar datos locales.
5. ADR, guía de activación/rotación y comprobación no destructiva contra la base visual existente, si hay configuración local; registrar lo no verificado.
   - **Checkpoint final:** comparar los agregados con consultas de solo lectura, comprobar que no hubo migración ni nueva base y que una falla de la central no participa en las ventas.

Cada tarea afecta como máximo cinco archivos y deja el proyecto compilable. Las tareas 1–4 son secuenciales por el contrato compartido; la documentación de la tarea 5 puede prepararse aparte, pero se valida al final. No se delegará trabajo en agentes paralelos.

## Verificación permitida

```powershell
dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj
dotnet test tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj --filter FullyQualifiedName~BranchServiceEndpointTests
dotnet build src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release
git diff --check
```

El segundo comando solo será válido si la nueva clase HTTP no crea, reinicia ni escribe en otra base; se inspeccionará antes de ejecutarlo. La comprobación positiva con datos reales de laboratorio será únicamente de lectura en `carnicerias_test_visual`. No se ejecutará el conjunto completo de integración, porque contiene pruebas que crean o reinician bases. La revisión visual del POS, si resulta necesaria, se hará solo en Electron.

**Verificación ejecutada:** 75 pruebas de arquitectura, 10 pruebas HTTP negativas/de límites y 1 prueba positiva de solo lectura contra la base visual; todas pasaron. El build Release común encontró bloqueado `bin/Release/net10.0/Carnicerias.Api.exe` por la API local en ejecución (PID 14576). Se compiló el mismo proyecto en Release, con 0 advertencias y 0 errores, hacia `bin/BranchVerification/` (ignorado por Git), sin detener esa instancia. El workshop VPN/HTTPS con servidores separados sigue pendiente.

## Riesgos, mitigaciones y decisiones diferidas

| Riesgo | Mitigación |
|---|---|
| El laboratorio contiene más de una empresa/sucursal aunque el despliegue objetivo sea autónomo | Vínculo inmutable por configuración de servidor y verificación de pertenencia/actividad en cada lectura; no inferir la primera sucursal. |
| Un resumen largo compite con el POS por PostgreSQL | Ventana de 31 días, agregados SQL, límite de frecuencia, cancelación/timeout y medición antes de ampliar el contrato. |
| El `Program.cs` y otros archivos tienen cambios no relacionados sin confirmar | Editar solo el punto de mapeo necesario, revisar diff acotado y preservar las modificaciones existentes. |
| La prueba positiva necesita una instalación configurada | Usar la única base visual con consultas no destructivas; si no hay credencial/configuración local, dejar esa comprobación pendiente sin inventar secretos ni crear una base. |

No hay decisiones de negocio pendientes para este módulo. La identidad del administrador de empresa en la futura central, sus credenciales y el almacén central pertenecen a `central-access`; no se decidirán aquí. La publicación de VPN/HTTPS y una instalación multiservidor real se validarán en el workshop posterior.
