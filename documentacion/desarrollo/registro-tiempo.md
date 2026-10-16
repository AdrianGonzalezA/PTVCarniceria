# Registro de tiempo por petición

Este registro distingue **tiempo activo registrado** de **estimaciones retrospectivas**. Las horas no registradas no se convierten en horas trabajadas por el solo paso del tiempo. Todas las horas se expresan en Argentina (UTC−03:00).

## Reconstrucción retrospectiva al 16/10/2026

El historial Git disponible contiene 137 commits de Codex entre el 7/10/2026 a las 17:15 y el 15/10/2026 a las 20:12. El primer commit ya incorporó la base del POS (API, Angular, Electron y documentación; 197 archivos), de modo que **no permite calcular el tiempo invertido antes del 7/10**.

Para ofrecer una referencia reproducible, se sumaron únicamente los intervalos entre commits consecutivos del mismo día separados por **90 minutos o menos**. Una separación mayor inicia otro bloque y no se suma. El resultado mide la extensión de bloques de actividad visibles, **no tiempo de trabajo comprobado**: puede incluir pausas cortas y omite trabajo anterior al primer commit o posterior al último de cada bloque.

| Fecha | Commits | Bloques visibles | Intervalos próximos sumados |
| --- | ---: | ---: | ---: |
| 07/10 | 22 | 1 | 3 h 21 min |
| 08/10 | 32 | 2 | 11 h 17 min |
| 09/10 | 32 | 3 | 8 h 17 min |
| 13/10 | 20 | 3 | 6 h 21 min |
| 14/10 | 1 | 1 | 0 min* |
| 15/10 | 30 | 2 | 7 h 53 min |
| **Total observable** | **137** | **12** | **37 h 09 min** |

\* Un único commit no ofrece un intervalo; **0 min observables no significa 0 min trabajados**. Como contraste, sumar sin descontar ninguna pausa el tiempo entre el primer y el último commit de cada día da 49 h 23 min, una cifra que también incluye inactividad. Para planificación, la estimación tentativa del período **7–15/10** es **unas 40 horas**, con incertidumbre amplia (aproximadamente **30–50 horas**). No es un total de todo el proyecto ni una medición apta para facturación. El tiempo previo al primer commit queda **desconocido**, no igual a cero.

Fuente reproducible: `git log --all --no-merges --format=%cI`. El cálculo usa fecha/hora de commit, no fecha de modificación de archivos. Si aparecen commits históricos adicionales o evidencia de sesiones anteriores, esta estimación se revisará sin reescribir los registros medidos.

## Registro desde el 16/10/2026

En cada petición del usuario, sea desarrollo, consulta, diagnóstico, documentación u operación, se anota la primera marca horaria fiable, la última, la tarea y el tiempo activo. Se descuentan pausas conocidas, esperas de respuesta del usuario e interrupciones del entorno; compilaciones y pruebas supervisadas sí cuentan. Si no hay marcas suficientes, la petición queda como **no cuantificable**, sin asignarle cero minutos ni inventar precisión. No se incluyen los períodos entre peticiones. Redondear a minutos y no mezclar estos valores con la estimación anterior.

| Fecha | Inicio | Fin | Tiempo activo | Tarea | Evidencia / observaciones |
| --- | --- | --- | ---: | --- | --- |
| 16/10/2026 | 07:49 | 07:52 | 3 min | Reconstruir actividad y crear registro de tiempo | Primera marca horaria fiable del turno; 137 commits y 12 bloques analizados. Sin pausas conocidas. |
| 16/10/2026 | 08:11 | 08:15 | 4 min | Actualizar comandos locales de sucursales y central | Se cotejaron scripts, proxies y puertos; `Check` y `Verify` de ambos laboratorios pasaron. Sin pausas conocidas. |
| 16/10/2026 | 09:29 | 09:48 | 19 min | Corregir arranque local de la API POS y documentar comando directo | Se reprodujo el HTTP 500 de `5197`, se probó el arranque en `5198` contra la base existente y `/api/sessions/current` respondió 401 sin sesión. Sin pausas conocidas. |
| 16/10/2026 | — | — | No cuantificable | Apagar las APIs de Carnicerías | Se cerraron siete procesos de API y se verificaron los puertos. No se tomaron marcas horarias de este turno. |
| 16/10/2026 | — | — | No cuantificable | Apagar los servidores Angular restantes | Se cerró el proceso de `4302` y se verificaron `4200`, `4300`, `4301` y `4302`. No se tomaron marcas horarias de este turno. |
| 16/10/2026 | — | — | No cuantificable | Solicitar registro de tiempo por petición | La petición se amplió durante el mismo turno para incluir `AGENTS.md`; no se registró una marca de inicio independiente. |
| 16/10/2026 | 10:39:50 | 10:40:50 | 1 min medido | Incorporar la regla de tiempo por petición a `AGENTS.md` | Primera marca fiable posterior al inicio del trabajo; el tramo previo no se cuantifica. Sin pausas conocidas en el intervalo medido. |
| 16/10/2026 | 10:40:50 | 10:42:28 | 2 min | Diagnosticar error de `Start-RusticaManagedLab.ps1 -Action Verify` | Tras el arranque, las tres URLs terminaron respondiendo `healthy`; el fallo ocurrió antes de que el sitio Angular aceptara conexiones. El ajuste automático de espera quedó pendiente al cambiar la prioridad a apagar servicios. Sin pausas conocidas. |
| 16/10/2026 | 10:42:28 | 10:43:35 | 1 min | Apagar APIs y Angular para prueba de arranque del usuario | Se detuvieron ocho procesos del laboratorio y se verificó que no quedan listeners ni procesos de API/Angular de Carnicerías. Sin pausas conocidas. |
| 16/10/2026 | 10:46:43 | 10:49:23 | 3 min | Corregir `Verify` del laboratorio La Rústica | Prueba de espera en rojo y luego en verde; pasaron las pruebas PowerShell y `Verify` real con `5249`, `5251` y `4302` saludables. Los comandos de arranque no cambiaron. Sin pausas conocidas. |
| 16/10/2026 | 10:54:09 | 11:05:46 | 11 min | Crear comandos Stop para APIs y Angular locales | Se detuvieron los laboratorios Visual y La Rústica, se comprobó el caso POS `5197` y quedaron libres los puertos documentados. Pruebas PowerShell en verde. Se descuenta aproximadamente 1 min de espera de aprobación. |
| 16/10/2026 | 11:14:45 | 11:17:12 | 2 min | Diagnosticar y restablecer el administrador de sucursal | El proxy Angular apuntaba a la API POS `5197`, detenida por el Stop anterior. Se reiniciaron API y Angular; `/admin` respondió 200 y `/api/sessions/current` devolvió el 401 esperado sin sesión. Se descontó la breve espera de aprobación. |
| 16/10/2026 | 11:30:06 | 11:30:29 | <1 min | Aclarar el alcance de los comandos Start | Visual y La Rústica levantan sus APIs de laboratorio; el administrador Angular `4200` requiere por separado la API POS `5197`. Sin pausas conocidas. |
| 16/10/2026 | 11:36:28 | 11:37:08 | 1 min | Explicar la topología de APIs compartidas y sucursales aisladas | Se contrastaron las decisiones de despliegue, el contrato de central y el filtro de contexto operativo. Sin cambios de arquitectura ni pausas conocidas. |
| 16/10/2026 | 11:43:21 | 11:44:19 | 1 min medido | Evaluar Electron POS en Raspberry Pi | Se revisaron versión, empaquetado y puertos seriales locales contra documentación oficial de Electron, Forge, SerialPort y Raspberry Pi. La investigación anterior a la primera marca horaria no se cuantifica. |
| 16/10/2026 | 12:40:19 | — | No cuantificable | Abrir la rama UI2 e iniciar el rediseño del POS | El turno se interrumpió antes de conservar una marca final; quedaron los commits `49de30a`, `fed06c6` y `2189782`. No se atribuye duración al intervalo. |
| 16/10/2026 | 14:45:01 | 15:03:19 | 18 min | Restaurar catálogo izquierdo y compra derecha; compactar ticket | Prueba roja/verde, 157 pruebas Angular, lint, build y mediciones/capturas en Electron. Sin pausas conocidas ni cambios de base. |
| 16/10/2026 | 15:03:19 | 15:04:39 | 1 min | Confirmar categorías horizontales y corregir el plan UI2 | Fila real verificada en Electron (`flex-direction: row`, desplazamiento horizontal automático); rama `feature/ui-v2` publicada. Sin pausas conocidas. |
| 16/10/2026 | 15:08:18 | 15:14:00 | 6 min | Ubicar categorías verticales a la izquierda y ampliar la compra | CSS, plan y tareas actualizados; Electron revisado en 1920, 1366, 800 y 560 px; 157 pruebas Angular, lint y build correctos. Sin cambios de base ni pausas conocidas. |
| 16/10/2026 | 15:17:51 | 15:20:17 | 2 min | Levantar la caja A de La Rústica en Electron | API POS existente saludable en `localhost:5197`; se inició el perfil `caja-rustica-a` y se comprobó en Electron `Alta Cordoba · Caja A · Juan`, con turno abierto y borrador recuperado. La captura del controlador de escritorio agotó el tiempo de espera; se verificó mediante el renderer de Electron. Sin cambios de base ni pausas conocidas. |
| 16/10/2026 | 15:21:56 | 15:28:07 | 6 min | Cambiar las tarjetas del detalle de venta por una tabla compacta | Prueba focalizada rojo/verde; 157 pruebas Angular, lint y build correctos. Se revisaron cinco renglones reales de La Rústica en Electron, sin crear ventas ni cambiar la base. El pedido posterior de mover A–D quedó en cola durante el cierre de este incremento. |

**Total activo registrado desde el 16/10/2026:** 81 min medidos, más tramos no cuantificables.
