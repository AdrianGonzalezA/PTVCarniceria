$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$container = 'carnicerias-pos-test-20261007'
$database = 'carnicerias_test_visual'

$inspection = @(docker inspect $container | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $inspection.Count -ne 1 -or
    $inspection[0].Name -ne "/$container" -or
    -not $inspection[0].State.Running -or
    $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostIp -ne '127.0.0.1' -or
    $inspection[0].NetworkSettings.Ports.'5432/tcp'[0].HostPort -ne '55433') {
    throw 'El PostgreSQL del laboratorio no corresponde al contenedor autorizado.'
}
$actual = docker exec $container psql -U postgres -d $database -At -c 'select current_database();'
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $database) {
    throw 'La unica base de aplicacion autorizada no existe o no esta disponible.'
}
$passwords = @($inspection[0].Config.Env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' })
if ($passwords.Count -ne 1) { throw 'Falta la credencial del contenedor autorizado.' }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 55433
$connection['Database'] = $database
$connection['Username'] = 'postgres'
$connection['Password'] = $passwords[0].Substring('POSTGRES_PASSWORD='.Length)
$previous = [Environment]::GetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING',
        $connection.ConnectionString, 'Process')
    & dotnet ef database update --project (Join-Path $root 'src\Carnicerias.Infrastructure') `
        --startup-project (Join-Path $root 'src\Carnicerias.Infrastructure') `
        --context PlatformAccessDbContext
    if ($LASTEXITCODE -ne 0) { throw "Fallaron las migraciones con codigo $LASTEXITCODE." }
    Write-Output "Migraciones aplicadas en $database."
}
finally {
    [Environment]::SetEnvironmentVariable('CARNICERIAS_CONNECTION_STRING', $previous, 'Process')
    $connection['Password'] = ''
}
