# Plan de implementación de platform access

## Estado

**Especificación:** `SPEC-platform-access.md` versión 1.0 aprobada  
**Estado del plan:** aprobado  
**Fecha de aprobación:** 29 de septiembre de 2026  
**Lista ejecutable:** `tasks/todo.md`

## Objetivo

Construir el primer módulo vertical del sistema: autenticación local, sesión segura, selección explícita de empresa y sucursal, administración web de usuarios y roles, recuperación por correo y políticas parametrizables. La instalación será independiente por cliente, admitirá múltiples empresas y sucursales y evitará dependencias que impidan una migración posterior a cloud.

## Decisiones de arquitectura

- Backend modular en .NET 10 LTS con PostgreSQL.
- Frontend Angular 22, componentes standalone y modo estricto.
- Administración en Angular web y puesto de venta con Angular empaquetado en Electron.
- Electron mantiene `contextIsolation` y sandbox; un preload expone métodos tipados y mínimos. Angular no recibe acceso general a Node, IPC, shell ni sistema de archivos.
- API HTTP JSON bajo `/api`, documentada con OpenAPI y errores uniformes.
- Sesión mediante cookie opaca `HttpOnly`, `Secure` y `SameSite`; estado revocable en servidor.
- Base independiente por cliente; aislamiento adicional por empresa y sucursal dentro de cada instalación.
- Configuración externa para secretos y parámetros de infraestructura.
- Parámetros funcionales tipados y auditables; secretos fuera del mecanismo de parametrización.
- Contratos internos para eventos de auditoría y bloqueos operativos, sin acceso directo entre tablas de módulos.
- Migraciones explícitas y datos iniciales mínimos; el primer Administrador se crea mediante herramienta segura.

## Grafo de dependencias

```text
Base de solución y contratos
    ├── Persistencia e instalación
    │     ├── Administrador inicial
    │     └── Sesiones persistentes
    ├── Aplicación Angular
    │     └── Contenedor Electron POS seguro
    │
    └── Login completo
          ├── Selección de contexto
          │     └── Bloqueos de cambio de contexto
          ├── Administración de usuarios
          │     └── Roles y asignaciones
          ├── Recuperación por correo
          └── Políticas de sesión
                └── Concurrencia de sesiones

Todos los flujos ──> hardening, observabilidad, pruebas E2E y documentación
```

## Fases y orden

### Fase 1 Base verificable

1. Crear solución .NET, convenciones, verificación y contratos compartidos.
2. Crear aplicación Angular y cliente HTTP base.
2A. Crear el contenedor Electron del POS y el puente nativo mínimo.
3. Incorporar PostgreSQL, migraciones y modelo mínimo de instalación.
4. Crear herramienta segura para el primer Administrador.

**Checkpoint:** API, Angular y Electron compilan; migración funciona desde una base vacía; existe un Administrador inicial sin secretos expuestos.

### Fase 2 Acceso y contexto

5. Implementar login y logout de extremo a extremo en web y POS.
6. Implementar consulta, selección y confirmación explícita del contexto.
7. Incorporar autorización por permisos y aislamiento por empresa y sucursal.
8. Incorporar contrato de bloqueos para impedir cambios con operaciones pendientes.

**Checkpoint:** un usuario inicia sesión, confirma contexto y no puede cruzar datos o cambiarlo ante un bloqueo.

### Fase 3 Administración

9. Implementar consulta y alta de usuarios desde Angular.
10. Implementar edición, activación, inactivación y revocación de sesiones.
11. Implementar administración de roles, permisos y asignaciones.
12. Proteger al último Administrador operativo.

**Checkpoint:** un Administrador gestiona accesos; un usuario común no puede invocar esas operaciones.

### Fase 4 Recuperación y políticas

13. Implementar solicitud y consumo de recuperación de contraseña por correo.
14. Implementar administración de políticas de sesión tipadas.
15. Implementar concurrencia de sesiones de manera atómica.

**Checkpoint:** recuperación, expiración y concurrencia cumplen la política configurada y quedan trazadas.

### Fase 5 Cierre de calidad

16. Completar hardening, métricas y eventos de seguridad.
17. Completar pruebas E2E, documentación operativa y verificación de migración a configuración cloud-compatible.

**Checkpoint final:** todos los criterios de `SPEC-platform-access.md` y la Definition of Done están satisfechos.

## Paralelización segura

- Las tareas 1 y 2 pueden avanzar en paralelo una vez acordados nombres y puertos.
- La tarea 2A puede avanzar después de congelar el contrato mínimo entre Angular y Electron; la lógica real de dispositivos pertenece a `devices-printing`.
- La interfaz de las tareas 9 a 11 puede prepararse después de congelar sus contratos API, mientras se implementa el backend correspondiente.
- Las pruebas de abuso de la tarea 16 pueden prepararse desde que login y autorización estén disponibles.
- Las migraciones, cambios de contratos compartidos y políticas de sesión deben ejecutarse secuencialmente.

## Verificación por incremento

Cada tarea debe ejecutar sus pruebas enfocadas y, antes de cada checkpoint:

```powershell
dotnet restore Carnicerias.sln
dotnet build Carnicerias.sln --configuration Release --no-restore
dotnet test Carnicerias.sln --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet format Carnicerias.sln --verify-no-changes
npm ci --prefix src/Carnicerias.Web
npm run lint --prefix src/Carnicerias.Web
npm test --prefix src/Carnicerias.Web -- --watch=false
npm run build --prefix src/Carnicerias.Web
npm run electron:build --prefix src/Carnicerias.Pos
npm run electron:test --prefix src/Carnicerias.Pos
```

Además, cada checkpoint requiere una prueba manual o E2E del flujo entregado y revisión humana antes de continuar.

## Riesgos y mitigaciones

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Sesiones sin vencimiento por inactividad | Alto | Revocación por eventos, duración absoluta parametrizable, reautenticación sensible, cierre operativo y alertas |
| Sin bloqueo ni espera por intentos fallidos | Alto | Errores genéricos, contraseñas robustas, límite técnico alto contra automatización, métricas y alertas |
| Administrador se quita su propio acceso | Alto | Invariante transaccional que conserva al menos un Administrador operativo |
| Cruce de empresa o sucursal | Alto | Contexto construido en servidor, filtros obligatorios y pruebas negativas de aislamiento |
| Carrera al limitar sesiones concurrentes | Alto | Reclamo atómico en PostgreSQL y pruebas concurrentes |
| Correo no disponible | Medio | Token persistido, resultado observable, reintentos controlados y operación administrativa alternativa |
| Configuración local dificulta migración a cloud | Medio | Procesos sin estado local obligatorio, configuración externa y adaptadores de infraestructura |
| Contrato prematuro con módulos futuros | Medio | Interfaz mínima para bloqueos y auditoría, dobles de prueba y evolución aditiva |
| Electron amplía la superficie de ataque | Alto | Sandbox, aislamiento de contexto, navegación restringida, CSP y puente mínimo validado |
| Distribución y actualización por terminal | Medio | Empaquetado reproducible, firma y canal de actualización controlado definidos antes del despliegue |

## Fuentes técnicas verificadas

- Angular releases y soporte: `https://angular.dev/reference/releases`
- Compatibilidad Angular, Node.js y TypeScript: `https://angular.dev/reference/versions`
- Configuración local y Angular CLI: `https://angular.dev/tools/cli/setup-local`
- Ciclo de soporte de .NET: `https://learn.microsoft.com/dotnet/core/releases-and-support`
- Dispositivos en Electron: `https://www.electronjs.org/docs/latest/tutorial/devices`
- Modelo de procesos e IPC de Electron: `https://www.electronjs.org/docs/latest/tutorial/process-model` y `https://www.electronjs.org/docs/latest/tutorial/ipc`
- Seguridad y aislamiento de contexto: `https://www.electronjs.org/docs/latest/tutorial/security` y `https://www.electronjs.org/docs/latest/tutorial/context-isolation`

## Preguntas abiertas

- Seleccionar el adaptador de correo concreto durante la tarea 13 según la infraestructura de cada cliente; el dominio no dependerá del proveedor.
- Definir valores predeterminados de las políticas de sesión durante la validación operativa, sin impedir que permanezcan parametrizables.

Estas preguntas no cambian el orden del plan, pero sus decisiones deben cerrarse antes de completar las tareas afectadas.

## Aprobación

El plan técnico y la lista de tareas fueron aprobados. La implementación comenzará únicamente cuando sea solicitada de forma explícita y avanzará tarea por tarea, respetando los checkpoints humanos.
