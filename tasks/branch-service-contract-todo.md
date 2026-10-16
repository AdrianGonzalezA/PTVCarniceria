# Tareas: contrato de lectura sucursal–central

**Estado:** implementadas y verificadas localmente el 14/10/2026; falta el workshop VPN/HTTPS multiservidor. El usuario aprobó [el plan](branch-service-contract-plan.md) y la [especificación](../SPEC-branch-service-contract.md). No reemplaza `tasks/todo.md`.

## Tarea 1. Configuración y credencial de servicio

**Descripción:** cargar la identidad fija y los verificadores desde configuración protegida, deshabilitados por defecto. Exponer un validador puro que falle cerrado y admita dos verificadores durante la rotación.

**Aceptación:**
- [x] Valores ausentes, GUID vacíos o verificadores mal formados no habilitan rutas y no impiden iniciar el POS.
- [x] Un token aleatorio válido de al menos 256 bits pasa; un token incorrecto no, con comparación de tiempo constante y sin guardar/loguear el secreto.
- [x] La configuración no acepta elegir empresa/sucursal a través de HTTP.

**Verificación:** `dotnet test tests/Carnicerias.ArchitectureTests/Carnicerias.ArchitectureTests.csproj --filter FullyQualifiedName~BranchServiceSettingsTests` y build API Release.

**Dependencias:** ninguna. **Archivos previstos (2):** `src/Carnicerias.Api/Federation/BranchServiceSettings.cs`, `tests/Carnicerias.ArchitectureTests/BranchServiceSettingsTests.cs`. **Tamaño:** S.

## Tarea 2. Identidad de instalación protegida

**Descripción:** registrar condicionalmente el grupo `/api/central/v1` y su `GET /installation`, con credencial de servicio, límite de frecuencia, `no-store` y validación de empresa/sucursal activas.

**Aceptación:**
- [x] Deshabilitado o incompleto devuelve `404`; credencial ausente o incorrecta devuelve `401`; vínculo inactivo/no válido devuelve `503` con el formato de error acordado.
- [x] Con credencial válida se devuelve solo identidad/capacidades de la pareja configurada, sin parámetros extra ni datos personales.
- [x] Cookie admin, credencial de caja y token de servicio no cruzan sus respectivos límites de autorización.

**Verificación:** prueba HTTP enfocada que no aprovisione base, más build API Release; comprobación positiva de lectura sobre la única base visual en la tarea 5.

**Dependencias:** tarea 1. **Archivos previstos (3):** `src/Carnicerias.Api/Federation/BranchServiceEndpoints.cs`, `src/Carnicerias.Api/Program.cs`, `tests/Carnicerias.IntegrationTests/BranchServiceEndpointTests.cs`. **Tamaño:** M.

### Checkpoint A — después de tareas 1–2

- [x] Pasan pruebas puras y HTTP negativas, sin crear ni reiniciar bases.
- [x] Compila API Release y las rutas no existen con integración deshabilitada.
- [x] La edición de `Program.cs` conserva los cambios ajenos y no cambia permisos del POS.

## Tarea 3. Ventana y agregados de sucursal

**Descripción:** construir un parser UTC estricto de hasta 31 días y una proyección EF de ventas, pagos por medio y turnos abiertos de la empresa/sucursal fijadas, sin reutilizar métricas de cuenta corriente de alcance empresarial.

**Aceptación:**
- [x] Se rechazan ventanas nulas, invertidas, no UTC o mayores a 31 días; `fromUtc` es inclusivo y `toUtc` exclusivo.
- [x] Todas las consultas de ventas, pagos y caja filtran empresa y sucursal configuradas y usan `AsNoTracking`.
- [x] La respuesta tipada solo puede contener los campos agregados de la especificación.

**Verificación:** pruebas puras de límites de ventana, inspección de la proyección y build API Release; comparación read-only con la base visual en la tarea 5.

**Dependencias:** tareas 1–2. **Archivos usados (4):** `src/Carnicerias.Api/Federation/BranchSummaryWindow.cs`, `src/Carnicerias.Api/Federation/BranchSummaryQuery.cs`, `tests/Carnicerias.ArchitectureTests/BranchSummaryWindowTests.cs`, `tests/Carnicerias.ArchitectureTests/BranchSummaryQueryTests.cs`. **Tamaño:** M.

## Tarea 4. Resumen HTTP v1

**Descripción:** conectar `GET /summary` con la consulta acotada y completar sus pruebas de contrato y abuso.

**Aceptación:**
- [x] Requiere el mismo token que `/installation`; parámetros faltantes, extras o inválidos devuelven `400` y nunca cambian el alcance.
- [x] Entrega campos, importes, zona UTC, `generatedAtUtc`, versión y `Cache-Control: no-store` establecidos en la especificación.
- [x] Errores, respuestas y logs no muestran token, cadena de conexión, excepciones ni información personal.

**Verificación:** `dotnet test tests/Carnicerias.IntegrationTests/Carnicerias.IntegrationTests.csproj --filter FullyQualifiedName~BranchServiceEndpointTests`, después de comprobar que esa clase es no destructiva; build API Release.

**Dependencias:** tarea 3. **Archivos previstos (2):** `src/Carnicerias.Api/Federation/BranchServiceEndpoints.cs`, `tests/Carnicerias.IntegrationTests/BranchServiceEndpointTests.cs`. **Tamaño:** S.

### Checkpoint B — después de tareas 3–4

- [x] Pasan las pruebas enfocadas y compila la API.
- [x] Revisión de seguridad de los dos endpoints, filtros de alcance y límites de consulta.
- [x] No hay migraciones ni acceso de escritura nuevo.

## Tarea 5. Operación y comprobación no destructiva

**Descripción:** registrar la ampliación de la topología en un ADR, documentar activación, rotación, VPN/HTTPS y fallos, y contrastar las lecturas con la base local si el entorno está disponible.

**Aceptación:**
- [x] La guía no contiene credenciales y explica que el POS sigue operando sin central y que la integración inicia apagada.
- [x] Una lectura autorizada concuerda con consultas de solo lectura de `carnicerias_test_visual`; no se crean ni reinician bases.
- [x] Se registra la revisión de alcance, `no-store`, ausencia de PII y ausencia de cambios en ventas/stock/turnos.

**Verificación:** comandos del plan, `git diff --check`, diff acotado y smoke HTTP de solo lectura si hay configuración local; revisar POS únicamente en Electron si hace falta inspección visual.

**Dependencias:** tareas 1–4. **Archivos usados (5):** `documentacion/decisiones/0004-consulta-central-de-sucursales.md`, `documentacion/despliegue/integracion-central-sucursal.md`, `tests/Carnicerias.IntegrationTests/BranchServiceVisualDatabaseTests.cs`, `.gitignore`, `tasks/branch-service-contract-todo.md`. **Tamaño:** M. El número 0003 ya corresponde a identidad por empresa.

### Checkpoint final

- [x] Se cumplen los cuatro criterios de aceptación de `SPEC-branch-service-contract.md` en laboratorio local; queda pendiente verificar VPN/HTTPS y caída de una central real en el workshop multiservidor.
- [x] Se confirma que sigue existiendo una sola base de aplicación de desarrollo y que no se ejecutaron pruebas destructivas.
- [x] El plan se revisó con el usuario antes de comenzar la implementación.
