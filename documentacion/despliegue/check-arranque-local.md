# Arranque local (PowerShell)

1. API — consola 1 (Docker Desktop iniciado)

   ```powershell
   cd C:\Proyectos\Carnicerias
   $pgContainer = docker ps -q --filter 'publish=55433' | Select-Object -First 1
   if (-not $pgContainer) { throw 'PostgreSQL en Docker (puerto 55433) no está activo' }
   $pgEnv = docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' $pgContainer
   $pgUser = ($pgEnv | Where-Object { $_ -like 'POSTGRES_USER=*' } | Select-Object -First 1) -replace '^POSTGRES_USER=', ''
   if (-not $pgUser) { $pgUser = 'postgres' }
   $pgPasswordEntry = $pgEnv | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' } | Select-Object -First 1
   if (-not $pgPasswordEntry) { throw 'No se encontró POSTGRES_PASSWORD en el contenedor' }
   $pgPassword = $pgPasswordEntry.Substring('POSTGRES_PASSWORD='.Length)
   $env:CARNICERIAS_CONNECTION_STRING = "Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=$pgUser;Password=$pgPassword"
   dotnet run --project src/Carnicerias.Api/Carnicerias.Api.csproj --configuration Release -- --urls http://localhost:5197
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
