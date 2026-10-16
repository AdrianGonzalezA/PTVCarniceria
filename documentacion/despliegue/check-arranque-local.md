# Arranque local (PowerShell)

1. API — consola 1 (Docker Desktop iniciado)

   ```powershell
   cd C:\Proyectos\Carnicerias
   .\tools\Start-LocalPosApi.ps1
   ```

   El script usa las credenciales del contenedor existente sin pedir datos ni mostrarlas. Si el puerto `5197` está ocupado, cerrar primero la API anterior con `Ctrl+C` en su consola. Para comprobar que la conexión a la base funciona (sin sesión, resultado esperado `401`):

   ```powershell
   curl.exe -s -o NUL -w "%{http_code}" http://localhost:5197/api/sessions/current
   ```

2. Admin — consola 2

   ```powershell
   cd C:\Proyectos\Carnicerias
   npm run start --prefix src/Carnicerias.Web
   ```

3. POS Rústica (cajas A, B y C) — consola 3

   ```powershell
   cd C:\Proyectos\Carnicerias\src\Carnicerias.Pos
   npm run build:web
   npm run build:electron
   npm run stage:web
   $credenciales = Get-Content 'C:\Proyectos\Carnicerias\documentacion\info.txt' -Encoding UTF8
   foreach ($caja in 'A','B','C') {
     $linea = $credenciales | Where-Object { $_ -like "Caja $caja *" } | Select-Object -First 1
     $dato = [regex]::Match($linea, "^Caja $caja ([A-Za-z0-9_-]{43})$")
     if (-not $dato.Success) { throw "Falta la credencial de Caja $caja en documentacion/info.txt" }
     $env:CARNICERIAS_POS_TERMINAL_TOKEN = $dato.Groups[1].Value
     try { Start-Process -FilePath '.\node_modules\electron\dist\electron.exe' -ArgumentList @('.', "--pos-profile=caja-rustica-$($caja.ToLowerInvariant())") -WorkingDirectory (Get-Location).Path }
     finally { Remove-Item Env:\CARNICERIAS_POS_TERMINAL_TOKEN -ErrorAction SilentlyContinue }
   }
   ```

4. Laboratorio Visual: tres APIs de sucursal (`5246`–`5248`) y dos APIs centrales (`5245`, `5250`) — consola 4. Ejecutar `Start` **solo si los cinco puertos están libres**; si los cinco servicios ya están activos, ejecutar únicamente `Verify`.

   ```powershell
   cd C:\Proyectos\Carnicerias
   .\tools\Start-VisualCentralLab.ps1 -Action Check
   .\tools\Start-VisualCentralLab.ps1 -Action Start
   .\tools\Start-VisualCentralLab.ps1 -Action Verify
   ```

5. Laboratorio La Rústica: API de Alta Córdoba (`5249`), API central aislada (`5251`) y portal (`4302`) — consola 5. Ejecutar `Start` **solo si los tres puertos están libres**; si los tres servicios ya están activos, ejecutar únicamente `Verify`.

   ```powershell
   cd C:\Proyectos\Carnicerias
   .\tools\Start-RusticaManagedLab.ps1 -Action Check
   .\tools\Start-RusticaManagedLab.ps1 -Action Start
   .\tools\Start-RusticaManagedLab.ps1 -Action Verify
   ```

6. Portal central habitual (`4300`) y alternativo (`4301`) — una consola por portal. El primero usa la API `5245`; el segundo, `5250`. El portal aislado `4302` ya lo inicia el paso 5.

   ```powershell
   cd C:\Proyectos\Carnicerias
   npm run start --prefix src/Carnicerias.Central.Web -- --host localhost --port 4300
   ```

   ```powershell
   cd C:\Proyectos\Carnicerias
   npm run start --prefix src/Carnicerias.Central.Web -- --host localhost --port 4301 --proxy-config proxy.superadmin.local.json
   ```

7. URLs locales (abrir portales en navegador; revisar el POS solo en Electron):

   | Servicio | URL |
   | --- | --- |
   | Administración POS compartida | `http://localhost:4200/` · `http://localhost:4200/admin` |
   | API POS compartida | `http://localhost:5197/api/health` |
   | Central habitual / SuperAdmin | `http://localhost:4300/` · `http://localhost:4300/superadmin` |
   | Central alternativa / SuperAdmin | `http://localhost:4301/` · `http://localhost:4301/superadmin` |
   | Central aislada La Rústica | `http://localhost:4302/` |
   | API central habitual / alternativa / aislada | `http://127.0.0.1:5245/api/health` · `http://127.0.0.1:5250/api/health` · `http://127.0.0.1:5251/api/health` |
   | APIs sucursal Visual, Norte y Sur | `http://127.0.0.1:5246/api/health` · `http://127.0.0.1:5247/api/health` · `http://127.0.0.1:5248/api/health` |
   | Administración / API sucursal Alta Córdoba | `http://localhost:5249/` · `http://127.0.0.1:5249/api/health` |

Los pasos 4 y 5 usan los binarios y secretos DPAPI **ya aprovisionados en esta computadora** y la misma base `carnicerias_test_visual`; no ejecutar `Provision` para un arranque normal. No volver a lanzar una API o portal si su puerto está ocupado. Para reiniciar solo las APIs centrales `5245`/`5250`, con esos dos puertos libres: `.\tools\Start-VisualCentralLab.ps1 -Action StartCentral`. Para reiniciar solo Alta Córdoba `5249`, con ese puerto libre: `.\tools\Start-RusticaManagedLab.ps1 -Action StartBranch`.
