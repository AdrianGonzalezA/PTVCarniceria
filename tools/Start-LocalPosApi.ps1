param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 5197,
    [string]$BindAddress = 'localhost'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$containerName = 'carnicerias-pos-test-20261007'
$databaseName = 'carnicerias_test_visual'
$parsedAddress = $null
if ($BindAddress -ne 'localhost' -and
    (-not [Net.IPAddress]::TryParse($BindAddress, [ref]$parsedAddress) -or
     $parsedAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork)) {
    throw 'BindAddress debe ser localhost o una dirección IPv4 de esta computadora.'
}

function New-LocalConnectionString($inspection, [string]$expectedContainer, [string]$expectedDatabase) {
    $endpoint = @($inspection.NetworkSettings.Ports.'5432/tcp')
    if ($inspection.Name -ne "/$expectedContainer" -or
        -not $inspection.State.Running -or
        $endpoint.Count -ne 1 -or
        $endpoint[0].HostIp -ne '127.0.0.1' -or
        $endpoint[0].HostPort -ne '55433') {
        throw 'El contenedor PostgreSQL no corresponde al laboratorio autorizado.'
    }

    $users = @($inspection.Config.Env | Where-Object { $_ -like 'POSTGRES_USER=*' })
    $passwords = @($inspection.Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
    if ($users.Count -gt 1 -or ($users.Count -eq 1 -and $users[0] -ne 'POSTGRES_USER=postgres') -or
        $passwords.Count -ne 1 -or $passwords[0].Length -le 'POSTGRES_PASSWORD='.Length) {
        throw 'Faltan las credenciales PostgreSQL del contenedor autorizado.'
    }

    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = 55433
    $connection['Database'] = $expectedDatabase
    $connection['Username'] = 'postgres'
    $connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
    return $connection.ConnectionString
}

if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "El puerto $Port ya está ocupado. Cerrá primero la API anterior en su consola con Ctrl+C."
}

$inspection = @(docker inspect $containerName | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1) {
    throw 'El contenedor PostgreSQL autorizado no está disponible.'
}
$connectionString = New-LocalConnectionString $inspection[0] $containerName $databaseName
$actualDatabase = docker exec $containerName psql -U postgres -d $databaseName -At -c 'select current_database();'
if ($LASTEXITCODE -ne 0 -or $actualDatabase.Trim() -ne $databaseName) {
    throw 'La única base de aplicación autorizada no está disponible.'
}

$previousPosConnection = [Environment]::GetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', 'Process')
$previousEfConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__PlatformAccess', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', $connectionString, 'Process')
    [Environment]::SetEnvironmentVariable('ConnectionStrings__PlatformAccess', $connectionString, 'Process')
    Write-Output "API POS en http://$($BindAddress):$Port con $databaseName. La contraseña no se muestra."
    & dotnet run --no-launch-profile --project (Join-Path $root 'src\Carnicerias.Api\Carnicerias.Api.csproj') `
        --configuration Release -- --urls "http://$($BindAddress):$Port"
    if ($LASTEXITCODE -ne 0) { throw "La API terminó con código $LASTEXITCODE." }
}
finally {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', $previousPosConnection, 'Process')
    [Environment]::SetEnvironmentVariable('ConnectionStrings__PlatformAccess', $previousEfConnection, 'Process')
}
