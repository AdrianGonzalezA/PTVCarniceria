# Plan de implementación: conexiones a sucursales desde la central

**Estado:** aprobado por el usuario el 15/10/2026. **Especificación aprobada:** [SPEC-central-branch-connections.md](../SPEC-central-branch-connections.md). **Módulo:** `central-access` del [mapa aprobado](../CAPABILITY-MAP-sucursal-central.md). **Lista detallada para revisión:** [central-branch-connections-todo.md](central-branch-connections-todo.md).

Este plan específico conserva intactos `tasks/plan.md` y `tasks/todo.md`, que tienen trabajo abierto de `platform-access`, y no reescribe el plan histórico `central-access-plan.md`. Las tareas pequeñas están en `central-branch-connections-todo.md`, siguiendo la convención por funcionalidad del repositorio.

## Resultado y alcance

El administrador central de una empresa gestionará en su portal las conexiones de sus sucursales: listado administrativo, alta verificada, edición, comprobación, desactivación y reactivación. La API central será la única que contacte a la sucursal; cada operación tomará el `centralCompanyId` de la sesión. El SuperAdmin de Recursos Informáticos conserva la creación de empresas y administradores, pero no opera las conexiones de una empresa. No se modifica el contrato de venta ni la API de negocio local.

## Decisiones técnicas

1. **Destinos permitidos por empresa.** Extender la configuración protegida de destinos exactos para asociar cada URL/IP aprobada a una empresa central. Los registros CLI existentes siguen siendo legibles; las nuevas altas web exigen una aprobación específica de empresa. Reutilizar la validación DNS, fijación de IP, TLS, ausencia de redirecciones y límites de respuesta ya implementados, sin aceptar una URL de formulario como autorización de red.
2. **Credencial cifrada en la misma base.** Agregar solo a `central_access` una tabla de credenciales cifradas con clave maestra versionada externa a la base/Git y cifrado autenticado ligado a empresa+instalación. Conservar la resolución por variable de entorno para instalaciones anteriores. No hacer fallback de una credencial cifrada fallida a una variable homónima. La API no migra al arrancar.
3. **Autorización por sesión central.** Endpoints `/api/v1/branch-connections` comparten cookie, origen y CSRF del admin de empresa, pero no aceptan `centralCompanyId` del cliente. Cada consulta y cambio filtra por empresa; un UUID ajeno responde como inexistente. Crear y editar destino/token comprueban identidad remota antes de confirmar una transacción; un fallo conserva el estado anterior.
4. **UI separada del dashboard.** Sección de conexiones en el sitio central existente, con formulario accesible y campo de token de uso único; el listado nunca muestra secreto ni referencia. Estados *registrada*, *comprobada* y *no disponible* tienen significados distintos. No se añade esta pantalla al POS Electron.
5. **Compatibilidad y despliegue.** La lectura de negocio existente debe seguir consultando instalaciones CLI y nuevas sin duplicar ventas. La migración real se aplica únicamente a `carnicerias_test_visual` tras preflight, respaldo verificado y revisión del SQL. La documentación de operación describirá provisión local de identidad/token, destinos permitidos por empresa, clave maestra/backup y rotación.

## Grafo de dependencias

```text
Contrato y pruebas de abuso por empresa
    ├── Política de destinos por empresa ──────────────┐
    └── Credencial cifrada + migración central ────────┤
                                                      ▼
                                    Alta/listado API + UI
                                                      │
                                                      ▼
                                    Comprobación y edición API + UI
                                                      │
                                                      ▼
                                    Desactivación/reactivación API + UI
                                                      │
                                                      ▼
                            Respaldo/migración controlada, smoke y docs
```

La política de destino y el almacén de secretos son los riesgos técnicos más altos; se prueban antes de incorporar un formulario que los ejercite. Cada corte funcional mantiene compilables la API y el portal y deja funcionando los registros CLI anteriores.

## Orden de entregas y puntos de control

| Orden | Incremento verificable | Prueba de aceptación |
|---|---|---|
| 1 | Contrato HTTP y modelo de amenazas | DTOs, errores y permisos fijados; pruebas negativas de alcance A/B, origen y URL no autorizada diseñadas antes del handler. |
| 2 | Política de red por empresa | A no puede sondear un destino aprobado solo para B; DNS/IP/redirect/TLS se rechazan sin enviar token. Los destinos heredados siguen operativos. |
| 3 | Almacén cifrado y migración no aplicada | Cifrar/descifrar con AAD empresa+instalación; manipulación o falta de clave falla cerrado; SQL generado toca solo `central_access`. Fuente heredada conserva compatibilidad. |
| 4 | Alta y listado por API | Registro atómico tras comprobar tres UUID y token, rechazo de duplicados/carreras, paginación por empresa y respuestas sin secreto. Pruebas HTTP con dobles, sin nueva base. |
| 5 | Alta y listado en Angular | Admin A registra desde la sección de conexiones; el token desaparece del formulario tras enviar; B no ve A. Estados y errores son accesibles. |
| 6 | Comprobación y edición | Test puntual con límite de frecuencia; nueva URL/token requiere verificar; error deja la conexión previa intacta. UI actualiza el resultado sin afirmar conectividad permanente. |
| 7 | Desactivación y reactivación | Desactivada no contribuye al agregado y conserva registro/auditoría; reactivación comprueba identidad, destino y token. |
| 8 | Despliegue y documentación | Respaldo de la única base, migración central controlada, smoke real de una conexión ficticia, revisión visual del portal, ADR que sustituye la decisión CLI-only y guías/manual actualizados. |

**Checkpoint A (1–3):** pruebas de política y cifrado, build .NET y revisión SQL sin aplicar migración. Si la compatibilidad de secretos heredados falla, no se abre el alta HTTP.

**Checkpoint B (4–5):** alta completa API→UI con pruebas de aislamiento A/B y respuesta sin secreto. Un timeout obliga a releer el listado, no a repetir ciegamente un POST.

**Checkpoint C (6–7):** edición fallida conserva configuración anterior; desactivar/reactivar afectan solo a la empresa de la sesión y el dashboard refleja el estado.

**Checkpoint final (8):** migración y smoke en `carnicerias_test_visual` sin crear otra base; builds/pruebas de central verdes, revisión de secretos en diff y recorrido de la UI central. El workshop por VPN/HTTPS real y la restauración completa de backups quedan marcados como pendientes si no existe ese entorno.

## Riesgos y mitigaciones

| Riesgo | Efecto | Mitigación y evidencia |
|---|---|---|
| SSRF y robo de token hacia una URL influida por el admin | Alto | Destino exacto aprobado **para la empresa**, validar todas las IP, fijar conexión, no redirigir; pruebas con URL/IP/DNS/tenant maliciosos. |
| Exposición o pérdida de la clave maestra | Alto | Secreto externo versionado y respaldado, nunca en Git/log/DB; cifrado autenticado, sin fallback silencioso. Documentar recuperación mediante reprovisión. |
| Alta parcial por caída entre comprobación y persistencia | Medio | Transacción para registro+credencial+auditoría, unicidad en DB, consulta tras timeout. |
| Rotación que deja dashboard inaccesible | Medio | Verificador siguiente en sucursal, comprobación antes de sustituir el token central y conservación de configuración previa al fallar. |
| Migración afecta datos POS de laboratorio | Alto | Revisar SQL, respaldo custom+SHA, guardia de base existente, migración exclusiva de `central_access`; no ejecutar suites que creen bases. |
| Historial contradice el nuevo flujo | Medio | Nuevo ADR que sustituye la regla CLI-only y conciliación de spec, plan, guía de despliegue y manual tras el smoke. |

## Verificación prevista

```powershell
dotnet build src/Carnicerias.Central.Api/Carnicerias.Central.Api.csproj --configuration Release --no-restore
dotnet test tests/Carnicerias.Central.Tests/Carnicerias.Central.Tests.csproj --no-restore
npm --prefix src/Carnicerias.Central.Web run build
npm --prefix src/Carnicerias.Central.Web test -- --watch=false
git diff --check
```

No se ejecutará `dotnet test Carnicerias.sln` ni otra integración que pueda crear/reiniciar una base. La UI central se puede revisar en navegador; cualquier revisión visual del POS será dentro de Electron.

## Preguntas abiertas

No quedan reglas de negocio abiertas. El usuario aprobó este plan; la lista de tareas detallada requiere revisión antes de empezar la implementación.
