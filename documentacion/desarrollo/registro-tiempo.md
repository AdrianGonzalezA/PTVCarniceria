# Registro de tiempo de desarrollo

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

En cada turno de desarrollo se anota la primera marca horaria fiable, la última, la tarea y el tiempo activo. Se descuentan pausas conocidas, esperas de respuesta del usuario e interrupciones del entorno; compilaciones y pruebas supervisadas sí cuentan. Una interrupción no documentada se señala como incertidumbre, sin inventar precisión. No se incluyen los períodos entre turnos. Redondear a minutos y no mezclar estos valores con la estimación anterior.

| Fecha | Inicio | Fin | Tiempo activo | Tarea | Evidencia / observaciones |
| --- | --- | --- | ---: | --- | --- |
| 16/10/2026 | 07:49 | 07:52 | 3 min | Reconstruir actividad y crear registro de tiempo | Primera marca horaria fiable del turno; 137 commits y 12 bloques analizados. Sin pausas conocidas. |
| 16/10/2026 | 08:11 | 08:15 | 4 min | Actualizar comandos locales de sucursales y central | Se cotejaron scripts, proxies y puertos; `Check` y `Verify` de ambos laboratorios pasaron. Sin pausas conocidas. |
| 16/10/2026 | 09:29 | 09:48 | 19 min | Corregir arranque local de la API POS y documentar comando directo | Se reprodujo el HTTP 500 de `5197`, se probó el arranque en `5198` contra la base existente y `/api/sessions/current` respondió 401 sin sesión. Sin pausas conocidas. |

**Total activo registrado desde el 16/10/2026:** 26 min.
